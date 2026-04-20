using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Provides a flattened, end-user-friendly configuration surface for Excel-backed <c>workbook_*</c> tools.
/// </summary>
/// <remarks>
/// This type combines the most common <see cref="ExcelWorkbookHandlerOptions"/> and <see cref="WorkbookToolsOptions"/>
/// settings so hosts can configure local-file support, hosted Microsoft 365 support, logging, and generic workbook-tool
/// limits from one place.
/// </remarks>
public sealed class ExcelWorkbookToolSetOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative local workbook paths.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets an optional additional resolver that can recognize workbook URLs, IDs, or other non-path references.
    /// </summary>
    public IWorkbookReferenceResolver? ReferenceResolver { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of WorkbookDoc lines returned when no explicit read limit is provided.
    /// </summary>
    public int MaxReadLines { get; init; } = 2_000;

    /// <summary>
    /// Gets or sets the maximum file size allowed for exact WorkbookDoc edits.
    /// </summary>
    public long MaxEditFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of results returned by workbook grep searches.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether direct local Excel paths are enabled.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical WorkbookDoc should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedWorkbookDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external Excel workbooks.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the hosted Microsoft 365 Excel settings. Leave <see langword="null"/> to disable hosted M365 support.
    /// </summary>
    public ExcelWorkbookM365Options? M365 { get; init; }

    /// <summary>
    /// Gets or sets an optional logger factory used when workbook tool invocations should be logged.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether WorkbookDoc payload parameters should be included in logs.
    /// </summary>
    /// <remarks>
    /// This is disabled by default so workbook content is not logged unless the host opts in.
    /// </remarks>
    public bool LogContentParameters { get; init; }

    /// <summary>
    /// Gets or sets optional additional workbook handlers appended after the Excel handler.
    /// </summary>
    public IEnumerable<IWorkbookHandler>? AdditionalHandlers { get; init; }

    /// <summary>
    /// Gets or sets optional additional prompt providers appended after the Excel prompt provider.
    /// </summary>
    public IEnumerable<IWorkbookToolPromptProvider>? AdditionalPromptProviders { get; init; }
}
