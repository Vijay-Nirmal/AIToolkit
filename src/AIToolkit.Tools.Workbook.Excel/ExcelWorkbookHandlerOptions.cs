namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Configures the Excel workbook handler.
/// </summary>
/// <remarks>
/// The Excel provider can operate on local Open XML workbook packages, hosted Microsoft 365 references, or both. These
/// options control which reference types are allowed and how reads behave when a workbook does or does not contain an
/// embedded canonical WorkbookDoc payload.
/// </remarks>
public sealed class ExcelWorkbookHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether local Excel file paths should be handled by this tool set.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical WorkbookDoc should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedWorkbookDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external Excel workbooks that do not
    /// contain embedded canonical WorkbookDoc.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional Microsoft 365 hosted-workbook settings used when ExcelWorkbookTools creates functions.
    /// </summary>
    /// <seealso cref="ExcelWorkbookM365Options"/>
    public ExcelWorkbookM365Options? M365 { get; init; }
}
