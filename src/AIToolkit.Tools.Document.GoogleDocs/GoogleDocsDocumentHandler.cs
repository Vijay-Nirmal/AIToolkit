using AIToolkit.Tools.Document.Word;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Bridges Google Docs through the Word AsciiDoc engine and a managed payload sidecar.
/// </summary>
internal sealed class GoogleDocsDocumentHandler(
    GoogleDocsDocumentHandlerOptions options,
    IGoogleDocsWorkspaceClient client) : IDocumentHandler
{
    private readonly GoogleDocsDocumentHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IGoogleDocsWorkspaceClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public string ProviderName => "google-docs";

    public IReadOnlyCollection<string> SupportedExtensions => [];

    public bool CanHandle(DocumentHandlerContext context) =>
        context.State is GoogleDocsDocumentLocation;

    public async ValueTask<DocumentReadResponse> ReadAsync(DocumentHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = await ResolveAccessAsync(context, DocumentToolOperation.Read, cancellationToken).ConfigureAwait(false);
        if (_options.PreferManagedAsciiDocPayload && access.Location.Exists)
        {
            var managedPayload = await _client.TryReadManagedAsciiDocAsync(access.Location, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(managedPayload))
            {
                return new DocumentReadResponse(
                    AsciiDoc: WordAsciiDocTextUtilities.NormalizeLineEndings(managedPayload),
                    IsLosslessRoundTrip: true,
                    SourceFormat: "google-docs",
                    Message: "Read canonical AsciiDoc from the managed Google Docs payload.");
            }
        }

        await using var sourceStream = await access.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
                SourceFormat: "google-docs",
                Message: "Read canonical AsciiDoc from the exported Google Docs DOCX payload.");
        }

        if (!_options.EnableBestEffortImport)
        {
            return new DocumentReadResponse(
                AsciiDoc: string.Empty,
                IsLosslessRoundTrip: false,
                SourceFormat: "google-docs",
                Message: "This Google Doc does not contain a managed canonical AsciiDoc payload.");
        }

        var imported = WordAsciiDocImporter.Import(document);
        return new DocumentReadResponse(
            AsciiDoc: imported,
            IsLosslessRoundTrip: false,
            SourceFormat: "google-docs",
            Message: "Imported Google Docs content into best-effort AsciiDoc.");
    }

    public async ValueTask<DocumentWriteResponse> WriteAsync(DocumentHandlerContext context, string asciiDoc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = await ResolveAccessAsync(context, DocumentToolOperation.Write, cancellationToken).ConfigureAwait(false);
        await using var destinationStream = await access.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = destinationStream.CanSeek ? null : new MemoryStream();
        var writeStream = bufferedStream ?? destinationStream;
        if (writeStream.CanSeek)
        {
            writeStream.Position = 0;
            writeStream.SetLength(0);
        }

        using (var document = WordprocessingDocument.Create(writeStream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            WordAsciiDocRenderer.Write(mainPart, asciiDoc);
            WordAsciiDocPayload.Write(mainPart, asciiDoc);

            var title = WordAsciiDocRenderer.TryExtractTitle(asciiDoc);
            if (!string.IsNullOrWhiteSpace(title))
            {
                document.PackageProperties.Title = title;
            }

            document.PackageProperties.Creator = "AIToolkit.Tools.Document.GoogleDocs";
            document.PackageProperties.Description = "Canonical AsciiDoc round-trip document for Google Docs";

            if (_options.PostProcessor is not null)
            {
                await _options.PostProcessor.ProcessAsync(new WordDocumentPostProcessorContext(context, document, asciiDoc), cancellationToken).ConfigureAwait(false);
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
            OutputFormat: "google-docs",
            Message: "Stored canonical AsciiDoc in the managed Google Docs payload and refreshed the Google Doc body through Drive conversion.");
    }

    private static async ValueTask<GoogleDocsResolvedAccess> ResolveAccessAsync(
        DocumentHandlerContext context,
        DocumentToolOperation operation,
        CancellationToken cancellationToken)
    {
        if (context.State is GoogleDocsDocumentLocation directLocation)
        {
            return new GoogleDocsResolvedAccess(directLocation, context.OpenReadAsync, context.OpenWriteAsync);
        }

        throw new InvalidOperationException(
            "Google Docs operations require a direct Google Docs URL, gdocs://documents/{documentId}, or gdocs://folders/{folderId}/documents/{title} reference.");
    }

    private static async ValueTask<MemoryStream> EnsureReadableStreamAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        var bufferedStream = new MemoryStream();
        await sourceStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return bufferedStream;
    }

    private sealed record GoogleDocsResolvedAccess(
        GoogleDocsDocumentLocation Location,
        Func<CancellationToken, ValueTask<Stream>> OpenReadAsync,
        Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync);
}