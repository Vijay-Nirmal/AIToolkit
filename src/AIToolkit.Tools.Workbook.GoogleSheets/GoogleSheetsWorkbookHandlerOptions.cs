namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Configures the Google Sheets workbook handler.
/// </summary>
/// <remarks>
/// The Google Sheets provider layers hosted-spreadsheet resolution on top of the Excel WorkbookDoc engine. These options
/// control how managed and embedded canonical payloads are discovered and when best-effort XLSX import is allowed.
/// </remarks>
public sealed class GoogleSheetsWorkbookHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the managed canonical WorkbookDoc payload stored alongside the Google Sheet should be preferred when present.
    /// </summary>
    public bool PreferManagedWorkbookDocPayload { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an embedded canonical WorkbookDoc payload found in an exported XLSX should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedWorkbookDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used when a Google Sheet does not have a managed canonical payload.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the Google Drive workspace settings used for hosted Google Sheets support.
    /// </summary>
    public GoogleSheetsWorkspaceOptions? Workspace { get; init; }
}
