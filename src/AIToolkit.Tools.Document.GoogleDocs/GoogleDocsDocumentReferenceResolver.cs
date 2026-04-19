namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Resolves Google Docs URLs and gdocs:// references into hosted document resources.
/// </summary>
internal sealed class GoogleDocsDocumentReferenceResolver(
    IGoogleDocsWorkspaceClient client) : IDocumentReferenceResolver
{
    private readonly IGoogleDocsWorkspaceClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directLocation = await _client.ResolveAsync(documentReference, context.Operation, cancellationToken).ConfigureAwait(false);
        if (directLocation is not null)
        {
            return CreateResolution(directLocation);
        }

        return null;
    }

    private DocumentReferenceResolution CreateResolution(GoogleDocsDocumentLocation location) =>
        DocumentReferenceResolution.CreateStreamBacked(
            resolvedReference: location.ResolvedReference,
            extension: string.Empty,
            existsAsync: cancellationToken => ValueTask.FromResult(location.Exists),
            openReadAsync: cancellationToken => _client.OpenExportReadAsync(location, cancellationToken),
            openWriteAsync: cancellationToken => _client.OpenUploadWriteAsync(location, cancellationToken),
            version: location.Version,
            length: location.Length,
            state: location,
            readStateKey: location.ReadStateKey);
}