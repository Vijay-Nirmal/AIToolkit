using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Converts Open XML Excel workbooks to and from canonical WorkbookDoc.
/// </summary>
internal sealed class ExcelWorkbookHandler(ExcelWorkbookHandlerOptions options) : IWorkbookHandler
{
    internal static readonly string[] SupportedFileExtensions = [".xlsx", ".xlsm", ".xltx", ".xltm"];

    private readonly ExcelWorkbookHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string ProviderName => "excel";

    public IReadOnlyCollection<string> SupportedExtensions => SupportedFileExtensions;

    public bool CanHandle(WorkbookHandlerContext context) =>
        SupportedFileExtensions.Contains(context.Extension, StringComparer.OrdinalIgnoreCase)
        && (_options.EnableLocalFileSupport || context.FilePath is null);

    public async ValueTask<WorkbookReadResponse> ReadAsync(WorkbookHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var sourceStream = await context.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
                WorkbookDoc: payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'),
                IsLosslessRoundTrip: true,
                SourceFormat: NormalizeFormat(context.Extension));
        }

        if (!_options.EnableBestEffortImport)
        {
            return new WorkbookReadResponse(
                WorkbookDoc: string.Empty,
                IsLosslessRoundTrip: false,
                SourceFormat: NormalizeFormat(context.Extension),
                Message: "This Excel workbook does not contain embedded canonical WorkbookDoc.");
        }

        var imported = ExcelWorkbookImporter.Import(workbook, context.ResolvedReference);
        return new WorkbookReadResponse(
            WorkbookDoc: imported,
            IsLosslessRoundTrip: false,
            SourceFormat: NormalizeFormat(context.Extension),
            Message: "Imported external Excel workbook into best-effort WorkbookDoc.");
    }

    public async ValueTask<WorkbookWriteResponse> WriteAsync(WorkbookHandlerContext context, string workbookDoc, CancellationToken cancellationToken = default)
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

        var model = WorkbookDocParser.Parse(workbookDoc);
        using (var spreadsheet = SpreadsheetDocument.Create(writeStream, ResolveDocumentType(context.Extension)))
        {
            ExcelWorkbookWriter.Write(spreadsheet, model);
            ExcelWorkbookDocPayload.Write(spreadsheet.WorkbookPart!, workbookDoc);
            spreadsheet.PackageProperties.Creator = "AIToolkit.Tools.Workbook.Excel";
            spreadsheet.PackageProperties.Description = "Canonical WorkbookDoc round-trip workbook";
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
            OutputFormat: NormalizeFormat(context.Extension),
            Message: "Stored canonical WorkbookDoc inside the Excel package for lossless round-tripping.");
    }

    private static async ValueTask<MemoryStream> EnsureReadableStreamAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        var bufferedStream = new MemoryStream();
        await sourceStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return bufferedStream;
    }

    private static SpreadsheetDocumentType ResolveDocumentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".xlsm" => SpreadsheetDocumentType.MacroEnabledWorkbook,
            ".xltx" => SpreadsheetDocumentType.Template,
            ".xltm" => SpreadsheetDocumentType.MacroEnabledTemplate,
            _ => SpreadsheetDocumentType.Workbook,
        };

    private static string NormalizeFormat(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? "excel" : extension.TrimStart('.').ToLowerInvariant();
}
