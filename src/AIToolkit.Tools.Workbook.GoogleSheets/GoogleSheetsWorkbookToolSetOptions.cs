using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Provides a flattened, end-user-friendly configuration surface for Google Sheets-backed <c>workbook_*</c> tools.
/// </summary>
public sealed class GoogleSheetsWorkbookToolSetOptions
{
    /// <summary>
    /// Gets or sets the default working directory used for local workspace search and relative reference handling.
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
    /// Gets or sets a value indicating whether managed canonical WorkbookDoc payloads should be preferred when present.
    /// </summary>
    public bool PreferManagedWorkbookDocPayload { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical WorkbookDoc payloads in exported XLSX files should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedWorkbookDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external Google Sheets without canonical payloads.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the Google Workspace settings. Leave <see langword="null"/> to disable hosted Google Sheets support.
    /// </summary>
    public GoogleSheetsWorkspaceOptions? Workspace { get; init; }

    /// <summary>
    /// Gets or sets an optional logger factory used when workbook tool invocations should be logged.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether WorkbookDoc payload parameters should be included in logs.
    /// </summary>
    public bool LogContentParameters { get; init; }

    /// <summary>
    /// Gets or sets optional additional workbook handlers appended after the Google Sheets handler.
    /// </summary>
    public IEnumerable<IWorkbookHandler>? AdditionalHandlers { get; init; }

    /// <summary>
    /// Gets or sets optional additional prompt providers appended after the Google Sheets prompt provider.
    /// </summary>
    public IEnumerable<IWorkbookToolPromptProvider>? AdditionalPromptProviders { get; init; }
}
