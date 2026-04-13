using System.Text.Json.Serialization;

namespace AIToolkit.Tools;

/// <summary>
/// Represents the common success and message fields returned by workspace tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record WorkspaceToolResult(bool Success, string? Message = null);

/// <summary>
/// Describes the supported deterministic file edit operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceFileEditOperation
{
    ReplaceOnce,
    ReplaceAll,
    InsertBefore,
    InsertAfter,
    Prepend,
    Append,
}

/// <summary>
/// Describes the supported notebook cell edit operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNotebookEditOperation
{
    InsertTop,
    InsertBottom,
    InsertAfter,
    Replace,
    Delete,
}

/// <summary>
/// Identifies the logical notebook cell type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNotebookCellType
{
    Code,
    Markdown,
    Raw,
}

/// <summary>
/// Identifies the lifecycle state of an in-memory workspace task.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceTaskStatus
{
    Pending,
    InProgress,
    Running,
    Completed,
    Failed,
    Canceled,
}

/// <summary>
/// Represents the outcome of a shell or PowerShell command.
/// </summary>
public sealed record WorkspaceCommandToolResult(
    bool Success,
    string Command,
    string WorkingDirectory,
    string StandardOutput,
    string StandardError,
    int? ExitCode,
    bool TimedOut,
    bool RunningInBackground,
    bool OutputTruncated,
    string? TaskId = null,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of reading a text file.
/// </summary>
public sealed record WorkspaceReadFileToolResult(
    bool Success,
    string Path,
    string Content,
    int StartLine,
    int EndLine,
    int TotalLines,
    bool Truncated,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of writing a file.
/// </summary>
public sealed record WorkspaceWriteFileToolResult(
    bool Success,
    string Path,
    string ChangeType,
    int CharacterCount,
    string? OriginalContent,
    string? Content,
    string? Patch,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of applying a deterministic file edit.
/// </summary>
public sealed record WorkspaceEditFileToolResult(
    bool Success,
    string Path,
    int ChangesApplied,
    int CharacterCount,
    string? OriginalContent,
    string? UpdatedContent,
    string? Patch,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of a glob-style file search.
/// </summary>
public sealed record WorkspaceGlobSearchToolResult(
    bool Success,
    string RootDirectory,
    string[] Paths,
    bool Truncated,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents a single grep-style text match.
/// </summary>
public sealed record WorkspaceGrepMatch(
    string Path,
    int LineNumber,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style content search.
/// </summary>
public sealed record WorkspaceGrepSearchToolResult(
    bool Success,
    string RootDirectory,
    WorkspaceGrepMatch[] Matches,
    bool Truncated,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Summarizes a notebook cell after an edit operation.
/// </summary>
public sealed record WorkspaceNotebookCellSummary(
    string? CellId,
    int CellIndex,
    WorkspaceNotebookCellType CellType,
    int LineCount);

/// <summary>
/// Represents the outcome of a notebook edit operation.
/// </summary>
public sealed record WorkspaceNotebookEditToolResult(
    bool Success,
    string Path,
    WorkspaceNotebookEditOperation Operation,
    int CellCount,
    WorkspaceNotebookCellSummary? AffectedCell,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents a snapshot of a workspace task.
/// </summary>
public sealed record WorkspaceTaskSnapshot(
    string Id,
    string Subject,
    string Description,
    WorkspaceTaskStatus Status,
    bool IsBackgroundProcess,
    string? ShellKind,
    string? Command,
    string? WorkingDirectory,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    int? ExitCode,
    string? ActiveForm,
    string? Owner,
    Dictionary<string, string?> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents the outcome of creating a task.
/// </summary>
public sealed record WorkspaceTaskCreateToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of retrieving a task.
/// </summary>
public sealed record WorkspaceTaskGetToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of listing tasks.
/// </summary>
public sealed record WorkspaceTaskListToolResult(
    bool Success,
    WorkspaceTaskSnapshot[] Tasks,
    bool Truncated,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of updating a task.
/// </summary>
public sealed record WorkspaceTaskUpdateToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string[] UpdatedFields,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of stopping a task.
/// </summary>
public sealed record WorkspaceTaskStopToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);