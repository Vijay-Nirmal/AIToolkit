namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Resolves Google Sheets URLs and <c>gsheets://</c> references into hosted workbook resources.
/// </summary>
internal sealed class GoogleSheetsWorkbookReferenceResolver(
    IGoogleSheetsWorkspaceClient client) : IWorkbookReferenceResolver
{
    private readonly IGoogleSheetsWorkspaceClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async ValueTask<WorkbookReferenceResolution?> ResolveAsync(
        string workbookReference,
        WorkbookReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directLocation = await _client.ResolveAsync(workbookReference, context.Operation, cancellationToken).ConfigureAwait(false);
        if (directLocation is not null)
        {
            return CreateResolution(directLocation);
        }

        return null;
    }

    private WorkbookReferenceResolution CreateResolution(GoogleSheetsWorkbookLocation location) =>
        WorkbookReferenceResolution.CreateStreamBacked(
            resolvedReference: location.ResolvedReference,
            extension: ".xlsx",
            existsAsync: cancellationToken => ValueTask.FromResult(location.Exists),
            openReadAsync: cancellationToken => _client.OpenExportReadAsync(location, cancellationToken),
            openWriteAsync: cancellationToken => _client.OpenUploadWriteAsync(location, cancellationToken),
            version: location.Version,
            length: location.Length,
            state: location,
            readStateKey: location.ReadStateKey);
}
