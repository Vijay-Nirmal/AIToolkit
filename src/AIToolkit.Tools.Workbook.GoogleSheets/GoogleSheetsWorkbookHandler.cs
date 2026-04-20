using AIToolkit.Tools.Workbook.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Bridges Google Sheets through the Excel WorkbookDoc engine and a managed payload sidecar.
/// </summary>
internal sealed class GoogleSheetsWorkbookHandler(
    GoogleSheetsWorkbookHandlerOptions options,
    IGoogleSheetsWorkspaceClient client) : IWorkbookHandler
{
    private readonly GoogleSheetsWorkbookHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IGoogleSheetsWorkspaceClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public string ProviderName => "google-sheets";

    public IReadOnlyCollection<string> SupportedExtensions => [];

    public bool CanHandle(WorkbookHandlerContext context) =>
        context.State is GoogleSheetsWorkbookLocation;

    public async ValueTask<WorkbookReadResponse> ReadAsync(WorkbookHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = ResolveAccess(context);
        if (_options.PreferManagedWorkbookDocPayload && access.Location.Exists)
        {
            var managedPayload = await _client.TryReadManagedWorkbookDocAsync(access.Location, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(managedPayload))
            {
                return new WorkbookReadResponse(
                    WorkbookDoc: NormalizeLineEndings(managedPayload),
                    IsLosslessRoundTrip: true,
                    SourceFormat: "google-sheets",
                    Message: "Read canonical WorkbookDoc from the managed Google Sheets payload.");
            }
        }

        await using var sourceStream = await access.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var bufferedStream = sourceStream.CanSeek ? null : await EnsureReadableStreamAsync(sourceStream, cancellationToken).ConfigureAwait(false);
        var workingStream = bufferedStream ?? sourceStream;
        if (workingStream.CanSeek)
        {
            workingStream.Position = 0;
        }

        using var workbook = SpreadsheetDocument.Open(workingStream, false);
        var payload = _options.PreferEmbeddedWorkbookDoc
            ? ExcelWorkbookDocPayload.TryRead(workbook.WorkbookPart)
            : null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return new WorkbookReadResponse(
                WorkbookDoc: NormalizeLineEndings(payload),
                IsLosslessRoundTrip: true,
                SourceFormat: "google-sheets",
                Message: "Read canonical WorkbookDoc from the exported Google Sheets XLSX payload.");
        }

        if (!_options.EnableBestEffortImport)
        {
            return new WorkbookReadResponse(
                WorkbookDoc: string.Empty,
                IsLosslessRoundTrip: false,
                SourceFormat: "google-sheets",
                Message: "This Google Sheet does not contain a managed canonical WorkbookDoc payload.");
        }

        var imported = ExcelWorkbookImporter.Import(workbook, context.ResolvedReference);
        var nativeMetadata = await _client.TryReadNativeFeatureMetadataAsync(access.Location, cancellationToken).ConfigureAwait(false);
        if (nativeMetadata is not null)
        {
            imported = nativeMetadata.ApplyTo(imported);
        }

        return new WorkbookReadResponse(
            WorkbookDoc: imported,
            IsLosslessRoundTrip: false,
            SourceFormat: "google-sheets",
            Message: "Imported Google Sheets content into best-effort WorkbookDoc.");
    }

    public async ValueTask<WorkbookWriteResponse> WriteAsync(WorkbookHandlerContext context, string workbookDoc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var access = ResolveAccess(context);
        await using var destinationStream = await access.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = destinationStream.CanSeek ? null : new MemoryStream();
        var writeStream = bufferedStream ?? destinationStream;
        if (writeStream.CanSeek)
        {
            writeStream.Position = 0;
            writeStream.SetLength(0);
        }

        var model = WorkbookDocParser.Parse(workbookDoc);
        using (var spreadsheet = SpreadsheetDocument.Create(writeStream, SpreadsheetDocumentType.Workbook))
        {
            ExcelWorkbookWriter.Write(spreadsheet, model);
            ExcelWorkbookDocPayload.Write(spreadsheet.WorkbookPart!, workbookDoc);
            spreadsheet.PackageProperties.Creator = "AIToolkit.Tools.Workbook.GoogleSheets";
            spreadsheet.PackageProperties.Description = "Canonical WorkbookDoc round-trip workbook for Google Sheets";
            spreadsheet.PackageProperties.Title = model.Title;
        }

        if (bufferedStream is not null)
        {
            bufferedStream.Position = 0;
            await bufferedStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return new WorkbookWriteResponse(
            PreservesWorkbookDocRoundTrip: true,
            OutputFormat: "google-sheets",
            Message: "Stored canonical WorkbookDoc in the managed Google Sheets payload, refreshed the Google Sheet body through Drive conversion, and synced native Google Sheets features where supported.");
    }

    private static GoogleSheetsResolvedAccess ResolveAccess(WorkbookHandlerContext context)
    {
        if (context.State is GoogleSheetsWorkbookLocation directLocation)
        {
            return new GoogleSheetsResolvedAccess(directLocation, context.OpenReadAsync, context.OpenWriteAsync);
        }

        throw new InvalidOperationException(
            "Google Sheets operations require a direct Google Sheets URL, gsheets://spreadsheets/{spreadsheetId}, or gsheets://folders/{folderId}/spreadsheets/{title} reference.");
    }

    private static async ValueTask<MemoryStream> EnsureReadableStreamAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        var bufferedStream = new MemoryStream();
        await sourceStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return bufferedStream;
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed record GoogleSheetsResolvedAccess(
        GoogleSheetsWorkbookLocation Location,
        Func<CancellationToken, ValueTask<Stream>> OpenReadAsync,
        Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync);
}
