using AIToolkit.Tools.Document;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Converts Open XML Word documents to and from canonical AsciiDoc.
/// </summary>
internal sealed class WordDocumentHandler(WordDocumentHandlerOptions options) : IDocumentHandler
{
    internal static readonly string[] SupportedFileExtensions = [".docx", ".docm", ".dotx", ".dotm"];

    private readonly WordDocumentHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string ProviderName => "word";

    public IReadOnlyCollection<string> SupportedExtensions => SupportedFileExtensions;

    public bool CanHandle(DocumentHandlerContext context) =>
        SupportedFileExtensions.Contains(context.Extension, StringComparer.OrdinalIgnoreCase)
        && (_options.EnableLocalFileSupport || context.FilePath is null);

    public async ValueTask<DocumentReadResponse> ReadAsync(DocumentHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var sourceStream = await context.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var bufferedStream = sourceStream.CanSeek ? null : await EnsureReadableStreamAsync(sourceStream, cancellationToken).ConfigureAwait(false);
        var workingStream = bufferedStream ?? sourceStream;
        if (workingStream.CanSeek)
        {
            workingStream.Position = 0;
        }

        using var document = WordprocessingDocument.Open(workingStream, false);
        var payload = _options.PreferEmbeddedAsciiDoc
            ? WordAsciiDocPayload.TryRead(document.MainDocumentPart)
            : null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return new DocumentReadResponse(
                AsciiDoc: WordAsciiDocTextUtilities.NormalizeLineEndings(payload),
                IsLosslessRoundTrip: true,
                SourceFormat: NormalizeFormat(context.Extension));
        }

        if (!_options.EnableBestEffortImport)
        {
            return new DocumentReadResponse(
                AsciiDoc: string.Empty,
                IsLosslessRoundTrip: false,
                SourceFormat: NormalizeFormat(context.Extension),
                Message: "This Word document does not contain embedded canonical AsciiDoc.");
        }

        var imported = WordAsciiDocImporter.Import(document);
        return new DocumentReadResponse(
            AsciiDoc: imported,
            IsLosslessRoundTrip: false,
            SourceFormat: NormalizeFormat(context.Extension),
            Message: "Imported external Word document into best-effort AsciiDoc.");
    }

    public async ValueTask<DocumentWriteResponse> WriteAsync(DocumentHandlerContext context, string asciiDoc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var destinationStream = await context.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = destinationStream.CanSeek ? null : new MemoryStream();
        var writeStream = bufferedStream ?? destinationStream;
        if (writeStream.CanSeek)
        {
            writeStream.Position = 0;
            writeStream.SetLength(0);
        }

        using (var document = WordprocessingDocument.Create(writeStream, ResolveDocumentType(context.Extension)))
        {
            var mainPart = document.AddMainDocumentPart();
            WordAsciiDocRenderer.Write(mainPart, asciiDoc);
            WordAsciiDocPayload.Write(mainPart, asciiDoc);

            var title = WordAsciiDocRenderer.TryExtractTitle(asciiDoc);
            if (!string.IsNullOrWhiteSpace(title))
            {
                document.PackageProperties.Title = title;
            }

            document.PackageProperties.Creator = "AIToolkit.Tools.Document.Word";
            document.PackageProperties.Description = "Canonical AsciiDoc round-trip document";

            var postProcessor = ResolvePostProcessor(context.Services);
            if (postProcessor is not null)
            {
                await postProcessor.ProcessAsync(new WordDocumentPostProcessorContext(context, document, asciiDoc), cancellationToken).ConfigureAwait(false);
            }

            mainPart.Document.Save();
        }

        if (bufferedStream is not null)
        {
            bufferedStream.Position = 0;
            await bufferedStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DocumentWriteResponse(
            PreservesAsciiDocRoundTrip: true,
            OutputFormat: NormalizeFormat(context.Extension),
            Message: "Stored canonical AsciiDoc inside the Word package for lossless round-tripping.");
    }

    private IWordDocumentPostProcessor? ResolvePostProcessor(IServiceProvider? services) =>
        _options.PostProcessor
        ?? services?.GetService(typeof(IWordDocumentPostProcessor)) as IWordDocumentPostProcessor;

    private static async ValueTask<MemoryStream> EnsureReadableStreamAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        var bufferedStream = new MemoryStream();
        await sourceStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return bufferedStream;
    }

    private static DocumentFormat.OpenXml.WordprocessingDocumentType ResolveDocumentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".docm" => DocumentFormat.OpenXml.WordprocessingDocumentType.MacroEnabledDocument,
            ".dotx" => DocumentFormat.OpenXml.WordprocessingDocumentType.Template,
            ".dotm" => DocumentFormat.OpenXml.WordprocessingDocumentType.MacroEnabledTemplate,
            _ => DocumentFormat.OpenXml.WordprocessingDocumentType.Document,
        };

    private static string NormalizeFormat(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? "word" : extension.TrimStart('.').ToLowerInvariant();
}