using System.Text.Json.Serialization;

namespace AIToolkit.Tools;

/// <summary>
/// Represents the common success and message fields returned by workspace tool operations.
/// </summary>
/// <remarks>
/// All structured tool results in this package derive from this base record so callers can quickly check whether
/// a tool succeeded before inspecting operation-specific fields.
/// </remarks>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record WorkspaceToolResult(bool Success, string? Message = null);

/// <summary>
/// Describes the supported deterministic file edit operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceFileEditOperation
{
    /// <summary>
    /// Replaces exactly one matching occurrence of the target string.
    /// </summary>
    ReplaceOnce,

    /// <summary>
    /// Replaces every matching occurrence of the target string.
    /// </summary>
    ReplaceAll,

    /// <summary>
    /// Inserts content immediately before the target string.
    /// </summary>
    InsertBefore,

    /// <summary>
    /// Inserts content immediately after the target string.
    /// </summary>
    InsertAfter,

    /// <summary>
    /// Inserts content at the start of the file.
    /// </summary>
    Prepend,

    /// <summary>
    /// Inserts content at the end of the file.
    /// </summary>
    Append,
}

/// <summary>
/// Describes the supported notebook cell edit operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNotebookEditOperation
{
    /// <summary>
    /// Inserts a new cell at the top of the notebook.
    /// </summary>
    InsertTop,

    /// <summary>
    /// Inserts a new cell at the bottom of the notebook.
    /// </summary>
    InsertBottom,

    /// <summary>
    /// Inserts a new cell after a specified target cell.
    /// </summary>
    InsertAfter,

    /// <summary>
    /// Replaces the type and/or source of an existing cell.
    /// </summary>
    Replace,

    /// <summary>
    /// Deletes an existing cell.
    /// </summary>
    Delete,
}

/// <summary>
/// Identifies the logical notebook cell type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNotebookCellType
{
    /// <summary>
    /// A code cell.
    /// </summary>
    Code,

    /// <summary>
    /// A markdown cell.
    /// </summary>
    Markdown,

    /// <summary>
    /// A raw cell.
    /// </summary>
    Raw,
}

/// <summary>
/// Identifies the lifecycle state of an in-memory workspace task.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceTaskStatus
{
    /// <summary>
    /// The task has been created but work has not started yet.
    /// </summary>
    Pending,

    /// <summary>
    /// The task is underway but is not tied to a background process.
    /// </summary>
    InProgress,

    /// <summary>
    /// The task is currently backed by a running background process.
    /// </summary>
    Running,

    /// <summary>
    /// The task finished successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The task finished unsuccessfully.
    /// </summary>
    Failed,

    /// <summary>
    /// The task was canceled before completion.
    /// </summary>
    Canceled,
}

/// <summary>
/// Represents the outcome of a shell or PowerShell command.
/// </summary>
/// <remarks>
/// This result is returned by <c>workspace_run_bash</c> and <c>workspace_run_powershell</c>, including when the
/// command is delegated to a background task tracked by <see cref="ITaskToolStore"/>.
/// </remarks>
/// <param name="Success"><see langword="true"/> when the command completed successfully or a background task was created successfully.</param>
/// <param name="Command">The command text that was executed.</param>
/// <param name="WorkingDirectory">The resolved working directory used for execution.</param>
/// <param name="StandardOutput">Captured standard-output text.</param>
/// <param name="StandardError">Captured standard-error text.</param>
/// <param name="ExitCode">The process exit code when one is available.</param>
/// <param name="TimedOut"><see langword="true"/> when the command exceeded its timeout.</param>
/// <param name="RunningInBackground"><see langword="true"/> when the command is still running as a tracked background task.</param>
/// <param name="OutputTruncated"><see langword="true"/> when output was truncated to respect configured limits.</param>
/// <param name="TaskId">The background task identifier when <paramref name="RunningInBackground"/> is <see langword="true"/>.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="Success"><see langword="true"/> when the file was read successfully.</param>
/// <param name="Path">The resolved file path.</param>
/// <param name="Content">The returned file content.</param>
/// <param name="StartLine">The first line number included in <paramref name="Content"/>.</param>
/// <param name="EndLine">The last line number included in <paramref name="Content"/>.</param>
/// <param name="TotalLines">The total number of lines in the file.</param>
/// <param name="Truncated"><see langword="true"/> when only part of the file was returned.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="Success"><see langword="true"/> when the file write succeeded.</param>
/// <param name="Path">The resolved file path.</param>
/// <param name="ChangeType">The logical change type, typically <c>create</c> or <c>update</c>.</param>
/// <param name="CharacterCount">The number of characters written.</param>
/// <param name="OriginalContent">The normalized original content when the file already existed.</param>
/// <param name="Content">The normalized content written to disk.</param>
/// <param name="Patch">A minimal patch representing the change when one can be produced.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="Success"><see langword="true"/> when the edit succeeded.</param>
/// <param name="Path">The resolved file path.</param>
/// <param name="ChangesApplied">The number of replacements or insertions applied.</param>
/// <param name="CharacterCount">The character count of the updated normalized file content.</param>
/// <param name="OriginalContent">The normalized original content when available.</param>
/// <param name="UpdatedContent">The normalized updated content when available.</param>
/// <param name="Patch">A minimal patch representing the change when one can be produced.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="RootDirectory">The resolved root directory used for the search.</param>
/// <param name="Paths">The matching relative file paths.</param>
/// <param name="Truncated"><see langword="true"/> when more matches existed than were returned.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="Path">The relative file path containing the match.</param>
/// <param name="LineNumber">The 1-based line number of the matching line.</param>
/// <param name="LineText">The matching line text.</param>
/// <param name="ContextBefore">The context lines that appeared before the match.</param>
/// <param name="ContextAfter">The context lines that appeared after the match.</param>
public sealed record WorkspaceGrepMatch(
    string Path,
    int LineNumber,
    string LineText,
    string[] ContextBefore,
    string[] ContextAfter);

/// <summary>
/// Represents the outcome of a grep-style content search.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="RootDirectory">The resolved root directory used for the search.</param>
/// <param name="Matches">The matching lines and their surrounding context.</param>
/// <param name="Truncated"><see langword="true"/> when more matches existed than were returned.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <param name="CellId">The notebook cell identifier when one is present.</param>
/// <param name="CellIndex">The zero-based cell index after the edit.</param>
/// <param name="CellType">The logical cell type.</param>
/// <param name="LineCount">The number of source lines in the cell.</param>
public sealed record WorkspaceNotebookCellSummary(
    string? CellId,
    int CellIndex,
    WorkspaceNotebookCellType CellType,
    int LineCount);

/// <summary>
/// Represents the outcome of a notebook edit operation.
/// </summary>
/// <param name="Success"><see langword="true"/> when the notebook edit succeeded.</param>
/// <param name="Path">The resolved notebook path.</param>
/// <param name="Operation">The notebook edit operation that was attempted.</param>
/// <param name="CellCount">The notebook's cell count after the operation.</param>
/// <param name="AffectedCell">A summary of the inserted, replaced, or deleted cell when available.</param>
/// <param name="Message">An optional error or status message.</param>
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
/// <remarks>
/// This immutable model is the currency shared between the background shell tools, the task tools, and any host
/// UI that wants to surface task status to the user.
/// </remarks>
/// <param name="Id">The stable task identifier.</param>
/// <param name="Subject">The short task subject.</param>
/// <param name="Description">The detailed task description.</param>
/// <param name="Status">The current task status.</param>
/// <param name="IsBackgroundProcess"><see langword="true"/> when the task is backed by a background process.</param>
/// <param name="ShellKind">The shell display name for a process-backed task.</param>
/// <param name="Command">The command text for a process-backed task.</param>
/// <param name="WorkingDirectory">The working directory used to launch a process-backed task.</param>
/// <param name="StandardOutput">Captured standard-output text.</param>
/// <param name="StandardError">Captured standard-error text.</param>
/// <param name="OutputTruncated"><see langword="true"/> when output was truncated to respect configured limits.</param>
/// <param name="ExitCode">The process exit code when one is available.</param>
/// <param name="ActiveForm">Optional present-progress wording such as "Reviewing docs".</param>
/// <param name="Owner">The optional task owner.</param>
/// <param name="Metadata">Additional task metadata.</param>
/// <param name="CreatedAt">The timestamp when the task was created.</param>
/// <param name="UpdatedAt">The timestamp when the task was last updated.</param>
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
/// <param name="Success"><see langword="true"/> when the task was created successfully.</param>
/// <param name="Task">The created task snapshot when available.</param>
/// <param name="Message">An optional error or status message.</param>
public sealed record WorkspaceTaskCreateToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of retrieving a task.
/// </summary>
/// <param name="Success"><see langword="true"/> when the task was found.</param>
/// <param name="Task">The retrieved task snapshot when available.</param>
/// <param name="Message">An optional error or status message.</param>
public sealed record WorkspaceTaskGetToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of listing tasks.
/// </summary>
/// <param name="Success"><see langword="true"/> when the task list was retrieved successfully.</param>
/// <param name="Tasks">The returned task snapshots.</param>
/// <param name="Truncated"><see langword="true"/> when more tasks existed than were returned.</param>
/// <param name="Message">An optional error or status message.</param>
public sealed record WorkspaceTaskListToolResult(
    bool Success,
    WorkspaceTaskSnapshot[] Tasks,
    bool Truncated,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of updating a task.
/// </summary>
/// <param name="Success"><see langword="true"/> when the task was found and updated.</param>
/// <param name="Task">The updated task snapshot when available.</param>
/// <param name="UpdatedFields">The logical fields that changed during the update.</param>
/// <param name="Message">An optional error or status message.</param>
public sealed record WorkspaceTaskUpdateToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string[] UpdatedFields,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);

/// <summary>
/// Represents the outcome of stopping a task.
/// </summary>
/// <param name="Success"><see langword="true"/> when the task was found and canceled or stopped.</param>
/// <param name="Task">The updated task snapshot when available.</param>
/// <param name="Message">An optional error or status message.</param>
public sealed record WorkspaceTaskStopToolResult(
    bool Success,
    WorkspaceTaskSnapshot? Task,
    string? Message = null)
    : WorkspaceToolResult(Success, Message);
