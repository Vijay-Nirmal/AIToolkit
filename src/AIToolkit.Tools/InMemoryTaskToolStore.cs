using System.Collections.Concurrent;
using System.Diagnostics;

namespace AIToolkit.Tools;

/// <summary>
/// Provides the built-in in-memory <see cref="ITaskToolStore"/> implementation.
/// </summary>
public sealed class InMemoryTaskToolStore(int maxOutputCharacters = 64_000) : ITaskToolStore
{
    private readonly ConcurrentDictionary<string, TaskEntry> _tasks = new(StringComparer.Ordinal);
    private readonly int _maxOutputCharacters = Math.Max(1, maxOutputCharacters);

    public WorkspaceTaskSnapshot CreateManualTask(
        string subject,
        string description,
        string? activeForm,
        string? owner,
        Dictionary<string, string?> metadata)
    {
        var entry = TaskEntry.CreateManual(subject, description, activeForm, owner, metadata, _maxOutputCharacters);
        _tasks[entry.Id] = entry;
        return entry.CreateSnapshot();
    }

    public WorkspaceTaskSnapshot CreateProcessTask(
        string subject,
        string description,
        string shellKind,
        string command,
        string workingDirectory)
    {
        var entry = TaskEntry.CreateProcess(subject, description, shellKind, command, workingDirectory, _maxOutputCharacters);
        _tasks[entry.Id] = entry;
        return entry.CreateSnapshot();
    }

    public void AttachProcess(string taskId, Process process)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            throw new InvalidOperationException($"Task '{taskId}' was not found.");
        }

        entry.AttachProcess(process);
    }

    public void AppendStandardOutput(string taskId, string value)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.AppendStandardOutput(value);
        }
    }

    public void AppendStandardError(string taskId, string value)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.AppendStandardError(value);
        }
    }

    public void CompleteProcess(string taskId, int? exitCode)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.CompleteProcess(exitCode);
        }
    }

    public WorkspaceTaskSnapshot? GetTask(string taskId) =>
        _tasks.TryGetValue(taskId, out var entry) ? entry.CreateSnapshot() : null;

    public IReadOnlyList<WorkspaceTaskSnapshot> ListTasks(WorkspaceTaskStatus? status, int maxResults, out bool truncated)
    {
        var snapshots = _tasks.Values
            .Select(static entry => entry.CreateSnapshot())
            .OrderByDescending(static task => task.UpdatedAt)
            .ToArray();

        if (status is not null)
        {
            snapshots = snapshots.Where(task => task.Status == status.Value).ToArray();
        }

        truncated = snapshots.Length > maxResults;
        return snapshots.Take(maxResults).ToArray();
    }

    public WorkspaceTaskSnapshot? UpdateTask(
        string taskId,
        string? subject,
        string? description,
        string? activeForm,
        string? owner,
        WorkspaceTaskStatus? status,
        Dictionary<string, string?>? metadata,
        out string[] updatedFields)
    {
        updatedFields = [];
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return null;
        }

        updatedFields = entry.Update(subject, description, activeForm, owner, status, metadata);
        return entry.CreateSnapshot();
    }

    public async Task<WorkspaceTaskSnapshot?> StopTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return null;
        }

        await entry.StopAsync(cancellationToken).ConfigureAwait(false);
        return entry.CreateSnapshot();
    }

    /// <summary>
    /// Tracks the mutable state for one manual or process-backed task.
    /// </summary>
    internal sealed class TaskEntry
    {
        private readonly object _gate = new();
        private readonly ToolOutputBuffer _stdout;
        private readonly ToolOutputBuffer _stderr;
        private Process? _process;
        private bool _stopRequested;

        private TaskEntry(
            string subject,
            string description,
            WorkspaceTaskStatus status,
            bool isBackgroundProcess,
            string? shellKind,
            string? command,
            string? workingDirectory,
            string? activeForm,
            string? owner,
            Dictionary<string, string?> metadata,
            int maxOutputCharacters)
        {
            Id = Guid.NewGuid().ToString("N");
            Subject = subject;
            Description = description;
            Status = status;
            IsBackgroundProcess = isBackgroundProcess;
            ShellKind = shellKind;
            Command = command;
            WorkingDirectory = workingDirectory;
            ActiveForm = activeForm;
            Owner = owner;
            Metadata = new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase);
            CreatedAt = DateTimeOffset.UtcNow;
            UpdatedAt = CreatedAt;
            _stdout = new ToolOutputBuffer(maxOutputCharacters);
            _stderr = new ToolOutputBuffer(maxOutputCharacters);
        }

        public string Id { get; }
        public string Subject { get; private set; }
        public string Description { get; private set; }
        public WorkspaceTaskStatus Status { get; private set; }
        public bool IsBackgroundProcess { get; }
        public string? ShellKind { get; }
        public string? Command { get; }
        public string? WorkingDirectory { get; }
        public int? ExitCode { get; private set; }
        public string? ActiveForm { get; private set; }
        public string? Owner { get; private set; }
        public Dictionary<string, string?> Metadata { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        public static TaskEntry CreateManual(
            string subject,
            string description,
            string? activeForm,
            string? owner,
            Dictionary<string, string?> metadata,
            int maxOutputCharacters) =>
            new(subject, description, WorkspaceTaskStatus.Pending, false, null, null, null, activeForm, owner, metadata, maxOutputCharacters);

        public static TaskEntry CreateProcess(
            string subject,
            string description,
            string shellKind,
            string command,
            string workingDirectory,
            int maxOutputCharacters) =>
            new(subject, description, WorkspaceTaskStatus.Running, true, shellKind, command, workingDirectory, null, null, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), maxOutputCharacters);

        public void AttachProcess(Process process)
        {
            lock (_gate)
            {
                _process = process;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void AppendStandardOutput(string value)
        {
            lock (_gate)
            {
                _stdout.Append(value);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void AppendStandardError(string value)
        {
            lock (_gate)
            {
                _stderr.Append(value);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void CompleteProcess(int? exitCode)
        {
            lock (_gate)
            {
                ExitCode = exitCode;
                Status = _stopRequested
                    ? WorkspaceTaskStatus.Canceled
                    : exitCode.GetValueOrDefault() == 0 ? WorkspaceTaskStatus.Completed : WorkspaceTaskStatus.Failed;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public string[] Update(
            string? subject,
            string? description,
            string? activeForm,
            string? owner,
            WorkspaceTaskStatus? status,
            Dictionary<string, string?>? metadata)
        {
            var updatedFields = new List<string>();

            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(subject) && !string.Equals(Subject, subject, StringComparison.Ordinal))
                {
                    Subject = subject;
                    updatedFields.Add(nameof(subject));
                }

                if (description is not null && !string.Equals(Description, description, StringComparison.Ordinal))
                {
                    Description = description;
                    updatedFields.Add(nameof(description));
                }

                if (activeForm is not null && !string.Equals(ActiveForm, activeForm, StringComparison.Ordinal))
                {
                    ActiveForm = activeForm;
                    updatedFields.Add(nameof(activeForm));
                }

                if (owner is not null && !string.Equals(Owner, owner, StringComparison.Ordinal))
                {
                    Owner = owner;
                    updatedFields.Add(nameof(owner));
                }

                if (status is not null && CanChangeStatus(status.Value))
                {
                    Status = status.Value;
                    updatedFields.Add(nameof(status));
                }

                if (metadata is not null)
                {
                    foreach (var pair in metadata)
                    {
                        Metadata[pair.Key] = pair.Value;
                    }

                    updatedFields.Add(nameof(metadata));
                }

                if (updatedFields.Count > 0)
                {
                    UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            return [.. updatedFields];
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Process? process;
            lock (_gate)
            {
                _stopRequested = true;
                process = _process;

                if (!IsBackgroundProcess || process is null || process.HasExited)
                {
                    Status = WorkspaceTaskStatus.Canceled;
                    UpdatedAt = DateTimeOffset.UtcNow;
                    return;
                }
            }

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
                process.Kill();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    Status = WorkspaceTaskStatus.Canceled;
                    UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        public WorkspaceTaskSnapshot CreateSnapshot()
        {
            lock (_gate)
            {
                return new WorkspaceTaskSnapshot(
                    Id,
                    Subject,
                    Description,
                    Status,
                    IsBackgroundProcess,
                    ShellKind,
                    Command,
                    WorkingDirectory,
                    _stdout.ToString(),
                    _stderr.ToString(),
                    _stdout.Truncated || _stderr.Truncated,
                    ExitCode,
                    ActiveForm,
                    Owner,
                    new Dictionary<string, string?>(Metadata, StringComparer.OrdinalIgnoreCase),
                    CreatedAt,
                    UpdatedAt);
            }
        }

        private bool CanChangeStatus(WorkspaceTaskStatus newStatus) =>
            !IsBackgroundProcess || Status is not WorkspaceTaskStatus.Running || newStatus is WorkspaceTaskStatus.Canceled;
    }
}