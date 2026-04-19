namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Resolves Google Docs URLs and <c>gdocs://</c> references into hosted document resources.
/// </summary>
/// <remarks>
/// The resolver delegates parsing and Drive lookups to <see cref="IGoogleDocsWorkspaceClient"/>, then translates the
/// result into a generic <see cref="DocumentReferenceResolution"/> that the shared document service can use.
/// </remarks>
internal sealed class GoogleDocsDocumentReferenceResolver(
    IGoogleDocsWorkspaceClient client) : IDocumentReferenceResolver
{
    private readonly IGoogleDocsWorkspaceClient _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// Resolves a Google Docs URL or <c>gdocs://</c> reference into a stream-backed document resource.
    /// </summary>
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
