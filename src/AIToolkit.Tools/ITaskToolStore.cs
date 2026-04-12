using System.Diagnostics;

namespace AIToolkit.Tools;

/// <summary>
/// Stores the shared task state used by background workspace commands and the public <c>task_*</c> tools.
/// </summary>
public interface ITaskToolStore
{
    WorkspaceTaskSnapshot CreateManualTask(
        string subject,
        string description,
        string? activeForm,
        string? owner,
        Dictionary<string, string?> metadata);

    WorkspaceTaskSnapshot CreateProcessTask(
        string subject,
        string description,
        string shellKind,
        string command,
        string workingDirectory);

    void AttachProcess(string taskId, Process process);

    void AppendStandardOutput(string taskId, string value);

    void AppendStandardError(string taskId, string value);

    void CompleteProcess(string taskId, int? exitCode);

    WorkspaceTaskSnapshot? GetTask(string taskId);

    IReadOnlyList<WorkspaceTaskSnapshot> ListTasks(WorkspaceTaskStatus? status, int maxResults, out bool truncated);

    WorkspaceTaskSnapshot? UpdateTask(
        string taskId,
        string? subject,
        string? description,
        string? activeForm,
        string? owner,
        WorkspaceTaskStatus? status,
        Dictionary<string, string?>? metadata,
        out string[] updatedFields);

    Task<WorkspaceTaskSnapshot?> StopTaskAsync(string taskId, CancellationToken cancellationToken);
}