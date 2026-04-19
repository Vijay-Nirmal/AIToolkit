using System.Diagnostics;

namespace AIToolkit.Tools;

/// <summary>
/// Stores the shared task state used by background workspace commands and the public <c>task_*</c> tools.
/// </summary>
/// <remarks>
/// Implementations coordinate two related concerns: manual task tracking created through <see cref="TaskTools"/>
/// and process-backed task tracking created by <see cref="WorkspaceTools"/> when commands run in the background.
/// The same store can therefore be shared across both tool families to give an agent one consistent task view.
/// </remarks>
/// <seealso cref="InMemoryTaskToolStore"/>
/// <seealso cref="WorkspaceTaskSnapshot"/>
public interface ITaskToolStore
{
    /// <summary>
    /// Creates a manual task that is not backed by an operating-system process.
    /// </summary>
    /// <param name="subject">A short title describing the work item.</param>
    /// <param name="description">The detailed task description.</param>
    /// <param name="activeForm">Optional present-progress wording such as "Reviewing docs".</param>
    /// <param name="owner">The optional task owner.</param>
    /// <param name="metadata">Additional arbitrary metadata to persist with the task.</param>
    /// <returns>A snapshot of the created task.</returns>
    WorkspaceTaskSnapshot CreateManualTask(
        string subject,
        string description,
        string? activeForm,
        string? owner,
        Dictionary<string, string?> metadata);

    /// <summary>
    /// Creates a task that tracks a background shell process.
    /// </summary>
    /// <param name="subject">A short title describing the process-backed task.</param>
    /// <param name="description">The detailed task description, often the raw command text.</param>
    /// <param name="shellKind">The shell display name exposed to callers.</param>
    /// <param name="command">The command line that will run in the background.</param>
    /// <param name="workingDirectory">The working directory used to launch the process.</param>
    /// <returns>A snapshot of the created task.</returns>
    WorkspaceTaskSnapshot CreateProcessTask(
        string subject,
        string description,
        string shellKind,
        string command,
        string workingDirectory);

    /// <summary>
    /// Associates an already-started process with a previously created process task.
    /// </summary>
    /// <param name="taskId">The identifier of the task that should own the process.</param>
    /// <param name="process">The running process to monitor and stop later.</param>
    /// <exception cref="InvalidOperationException">Thrown when the store cannot find <paramref name="taskId"/>.</exception>
    void AttachProcess(string taskId, Process process);

    /// <summary>
    /// Appends captured standard-output text to a process-backed task.
    /// </summary>
    /// <param name="taskId">The identifier of the task receiving output.</param>
    /// <param name="value">The text to append.</param>
    void AppendStandardOutput(string taskId, string value);

    /// <summary>
    /// Appends captured standard-error text to a process-backed task.
    /// </summary>
    /// <param name="taskId">The identifier of the task receiving error output.</param>
    /// <param name="value">The text to append.</param>
    void AppendStandardError(string taskId, string value);

    /// <summary>
    /// Marks a process-backed task as finished.
    /// </summary>
    /// <param name="taskId">The identifier of the completed task.</param>
    /// <param name="exitCode">The process exit code when known.</param>
    void CompleteProcess(string taskId, int? exitCode);

    /// <summary>
    /// Retrieves the latest snapshot for a task.
    /// </summary>
    /// <param name="taskId">The identifier of the task to retrieve.</param>
    /// <returns>The task snapshot when found; otherwise, <see langword="null"/>.</returns>
    WorkspaceTaskSnapshot? GetTask(string taskId);

    /// <summary>
    /// Lists tasks in reverse update order.
    /// </summary>
    /// <param name="status">An optional status filter.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="truncated"><see langword="true"/> when more tasks matched than were returned.</param>
    /// <returns>A collection of task snapshots.</returns>
    IReadOnlyList<WorkspaceTaskSnapshot> ListTasks(WorkspaceTaskStatus? status, int maxResults, out bool truncated);

    /// <summary>
    /// Updates mutable task fields and returns the latest snapshot.
    /// </summary>
    /// <param name="taskId">The identifier of the task to update.</param>
    /// <param name="subject">The replacement subject, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="description">The replacement description, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="activeForm">The replacement active-form wording, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="owner">The replacement owner, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="status">The replacement status, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="metadata">Additional metadata entries to merge into the task, or <see langword="null"/> to leave metadata unchanged.</param>
    /// <param name="updatedFields">The names of fields that changed.</param>
    /// <returns>The updated task snapshot when found; otherwise, <see langword="null"/>.</returns>
    WorkspaceTaskSnapshot? UpdateTask(
        string taskId,
        string? subject,
        string? description,
        string? activeForm,
        string? owner,
        WorkspaceTaskStatus? status,
        Dictionary<string, string?>? metadata,
        out string[] updatedFields);

    /// <summary>
    /// Stops a running background process or cancels a manual task.
    /// </summary>
    /// <param name="taskId">The identifier of the task to stop.</param>
    /// <param name="cancellationToken">A token that cancels the stop request.</param>
    /// <returns>The updated task snapshot when found; otherwise, <see langword="null"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task<WorkspaceTaskSnapshot?> StopTaskAsync(string taskId, CancellationToken cancellationToken);
}
