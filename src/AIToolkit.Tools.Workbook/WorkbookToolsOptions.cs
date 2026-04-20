using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Configures the generic <c>workbook_*</c> tools created by <see cref="WorkbookTools"/>.
/// </summary>
/// <remarks>
/// These options define the workspace defaults, handler pipeline, resolver pipeline, and result limits shared by all
/// generic workbook operations. Provider-specific builders typically clone and extend this type instead of introducing a
/// separate option system.
/// </remarks>
public sealed class WorkbookToolsOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative workbook paths.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the optional resolver that can map a workbook reference such as a path, URL, or ID to a resolved workbook resource.
    /// </summary>
    public IWorkbookReferenceResolver? ReferenceResolver { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of WorkbookDoc lines returned by workbook reads when no explicit range is provided.
    /// </summary>
    /// <remarks>
    /// Partial reads are tracked as partial view state, so callers must perform a full read before exact-string edits or
    /// full rewrites of an existing workbook.
    /// </remarks>
    public int MaxReadLines { get; init; } = 2_000;

    /// <summary>
    /// Gets or sets the maximum file size allowed for exact WorkbookDoc edits.
    /// </summary>
    public long MaxEditFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of results returned by workbook content searches.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets the workbook handlers used to convert provider-specific files to and from canonical WorkbookDoc.
    /// </summary>
    /// <remarks>
    /// Handlers supplied here are evaluated before handlers resolved from dependency injection.
    /// </remarks>
    public IEnumerable<IWorkbookHandler>? Handlers { get; init; }

    /// <summary>
    /// Gets or sets the provider-specific prompt contributors that extend the generic workbook tool guidance.
    /// </summary>
    public IEnumerable<IWorkbookToolPromptProvider>? PromptProviders { get; init; }

    /// <summary>
    /// Gets or sets an optional logger factory used when tool invocations should be logged without relying on per-call services.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether large content parameters such as write/edit WorkbookDoc payloads should be included in tool invocation logs.
    /// </summary>
    /// <remarks>
    /// This is disabled by default so logs do not capture workbook content unless the host explicitly opts in.
    /// </remarks>
    public bool LogContentParameters { get; init; }
}
