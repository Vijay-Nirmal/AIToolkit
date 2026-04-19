using AIToolkit.Tools.Document;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Converts Open XML Word documents to and from canonical AsciiDoc.
/// </summary>
/// <remarks>
/// Reads prefer the hidden canonical payload stored by <see cref="WordAsciiDocPayload"/>, then fall back to
/// <see cref="WordAsciiDocImporter"/> for external documents when best-effort import is enabled. Writes always render a
/// fresh WordprocessingML body through <see cref="WordAsciiDocRenderer"/>, embed the canonical payload for lossless
/// round-trips, and optionally invoke an <see cref="IWordDocumentPostProcessor"/> before the package is saved. Hosted
/// references supplied through <see cref="IDocumentReferenceResolver"/> are treated the same as local files once a stream
/// has been resolved.
/// </remarks>
internal sealed class WordDocumentHandler(WordDocumentHandlerOptions options) : IDocumentHandler
{
    internal static readonly string[] SupportedFileExtensions = [".docx", ".docm", ".dotx", ".dotm"];

    private readonly WordDocumentHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Gets the stable provider name reported by generic document tool results.
    /// </summary>
    /// <remarks>
    /// The shared document tool responses use this provider name to identify Word-backed operations without changing the
    /// generic tool contract.
    /// </remarks>
    public string ProviderName => "word";

    /// <summary>
    /// Gets the local Word package extensions handled by this provider.
    /// </summary>
    /// <seealso cref="CreateHandler"/>
    public IReadOnlyCollection<string> SupportedExtensions => SupportedFileExtensions;

    /// <summary>
    /// Determines whether the resolved document context points at a supported Word package.
    /// </summary>
    /// <param name="context">The resolved document context to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the reference targets a supported Word package and local-file support rules allow it;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool CanHandle(DocumentHandlerContext context) =>
        SupportedFileExtensions.Contains(context.Extension, StringComparer.OrdinalIgnoreCase)
        && (_options.EnableLocalFileSupport || context.FilePath is null);

    /// <summary>
    /// Reads a Word package as canonical AsciiDoc.
    /// </summary>
    /// <param name="context">The resolved document context that can open the underlying Word package stream.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>
    /// A <see cref="DocumentReadResponse"/> containing canonical AsciiDoc from the embedded payload or a best-effort import
    /// from visible Word content.
    /// </returns>
    /// <exception cref="OpenXmlPackageException">The input stream does not contain a valid Wordprocessing package.</exception>
    public async ValueTask<DocumentReadResponse> ReadAsync(DocumentHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var sourceStream = await context.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        // Hosted resolvers may return non-seekable network streams. Buffer them once so Open XML can inspect the package
        // and the importer can re-read content safely.
        using var bufferedStream = sourceStream.CanSeek ? null : await EnsureReadableStreamAsync(sourceStream, cancellationToken).ConfigureAwait(false);
        var workingStream = bufferedStream ?? sourceStream;
        if (workingStream.CanSeek)
        {
            workingStream.Position = 0;
        }

        using var document = WordprocessingDocument.Open(workingStream, false);
        // Canonical payloads created by this package provide the only lossless representation. When they are present we
        // bypass best-effort import entirely and return the original AsciiDoc.
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

    /// <summary>
    /// Writes canonical AsciiDoc into a Word package and embeds a lossless payload for future reads.
    /// </summary>
    /// <param name="context">The resolved document context that can open the destination stream.</param>
    /// <param name="asciiDoc">The canonical AsciiDoc to render and embed.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A <see cref="DocumentWriteResponse"/> describing the round-trip guarantees of the generated package.</returns>
    /// <exception cref="OpenXmlPackageException">The destination stream cannot be written as a valid Wordprocessing package.</exception>
    public async ValueTask<DocumentWriteResponse> WriteAsync(DocumentHandlerContext context, string asciiDoc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var destinationStream = await context.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        // Microsoft Graph uploads use a non-seekable wrapper until disposal, so writes target an in-memory buffer first
        // when necessary and are copied back only after the package has been completed successfully.
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
