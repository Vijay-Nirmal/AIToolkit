using System.Collections.Concurrent;
using System.Diagnostics;

namespace AIToolkit.Tools;

/// <summary>
/// Provides the built-in in-memory <see cref="ITaskToolStore"/> implementation.
/// </summary>
/// <remarks>
/// This store is optimized for single-process agent hosts. It keeps task metadata in a concurrent dictionary,
/// buffers process output through <see cref="ToolOutputBuffer"/>, and produces immutable
/// <see cref="WorkspaceTaskSnapshot"/> instances whenever callers inspect task state.
/// </remarks>
/// <param name="maxOutputCharacters">The maximum number of characters retained for each task's stdout and stderr buffers.</param>
/// <seealso cref="TaskTools"/>
/// <seealso cref="WorkspaceTools"/>
public sealed class InMemoryTaskToolStore(int maxOutputCharacters = 64_000) : ITaskToolStore
{
    private readonly ConcurrentDictionary<string, TaskEntry> _tasks = new(StringComparer.Ordinal);
    private readonly int _maxOutputCharacters = Math.Max(1, maxOutputCharacters);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void AttachProcess(string taskId, Process process)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            throw new InvalidOperationException($"Task '{taskId}' was not found.");
        }

        entry.AttachProcess(process);
    }

    /// <inheritdoc />
    public void AppendStandardOutput(string taskId, string value)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.AppendStandardOutput(value);
        }
    }

    /// <inheritdoc />
    public void AppendStandardError(string taskId, string value)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.AppendStandardError(value);
        }
    }

    /// <inheritdoc />
    public void CompleteProcess(string taskId, int? exitCode)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            entry.CompleteProcess(exitCode);
        }
    }

    /// <inheritdoc />
    public WorkspaceTaskSnapshot? GetTask(string taskId) =>
        _tasks.TryGetValue(taskId, out var entry) ? entry.CreateSnapshot() : null;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
    /// <remarks>
    /// <see cref="InMemoryTaskToolStore"/> mutates this type behind a lock and publishes
    /// <see cref="WorkspaceTaskSnapshot"/> values outward. That split keeps the public model immutable while still
    /// allowing efficient incremental output capture and status transitions internally.
    /// </remarks>
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

        /// <summary>
        /// Gets the stable task identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the short task subject.
        /// </summary>
        public string Subject { get; private set; }

        /// <summary>
        /// Gets the detailed task description.
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Gets the current task status.
        /// </summary>
        public WorkspaceTaskStatus Status { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the task represents a running background process.
        /// </summary>
        public bool IsBackgroundProcess { get; }

        /// <summary>
        /// Gets the display name of the shell used for a process-backed task.
        /// </summary>
        public string? ShellKind { get; }

        /// <summary>
        /// Gets the command text for a process-backed task.
        /// </summary>
        public string? Command { get; }

        /// <summary>
        /// Gets the working directory used to launch a process-backed task.
        /// </summary>
        public string? WorkingDirectory { get; }

        /// <summary>
        /// Gets the process exit code when one is available.
        /// </summary>
        public int? ExitCode { get; private set; }

        /// <summary>
        /// Gets the optional present-progress wording shown while the task is active.
        /// </summary>
        public string? ActiveForm { get; private set; }

        /// <summary>
        /// Gets the optional task owner.
        /// </summary>
        public string? Owner { get; private set; }

        /// <summary>
        /// Gets the mutable task metadata bag.
        /// </summary>
        public Dictionary<string, string?> Metadata { get; }

        /// <summary>
        /// Gets the timestamp when the task was created.
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Gets the timestamp of the latest mutation.
        /// </summary>
        public DateTimeOffset UpdatedAt { get; private set; }

        /// <summary>
        /// Creates a new manual-task entry.
        /// </summary>
        /// <param name="subject">The task subject.</param>
        /// <param name="description">The task description.</param>
        /// <param name="activeForm">Optional active-form wording.</param>
        /// <param name="owner">The optional task owner.</param>
        /// <param name="metadata">Additional metadata to attach to the task.</param>
        /// <param name="maxOutputCharacters">The maximum number of characters retained per output stream.</param>
        /// <returns>A mutable task entry in the pending state.</returns>
        public static TaskEntry CreateManual(
            string subject,
            string description,
            string? activeForm,
            string? owner,
            Dictionary<string, string?> metadata,
            int maxOutputCharacters) =>
            new(subject, description, WorkspaceTaskStatus.Pending, false, null, null, null, activeForm, owner, metadata, maxOutputCharacters);

        /// <summary>
        /// Creates a new process-backed task entry.
        /// </summary>
        /// <param name="subject">The task subject.</param>
        /// <param name="description">The task description.</param>
        /// <param name="shellKind">The display name of the shell that will host the process.</param>
        /// <param name="command">The command text associated with the process.</param>
        /// <param name="workingDirectory">The working directory used to launch the process.</param>
        /// <param name="maxOutputCharacters">The maximum number of characters retained per output stream.</param>
        /// <returns>A mutable task entry in the running state.</returns>
        public static TaskEntry CreateProcess(
            string subject,
            string description,
            string shellKind,
            string command,
            string workingDirectory,
            int maxOutputCharacters) =>
            new(subject, description, WorkspaceTaskStatus.Running, true, shellKind, command, workingDirectory, null, null, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), maxOutputCharacters);

        /// <summary>
        /// Attaches the spawned process after the corresponding task has been created.
        /// </summary>
        /// <param name="process">The running process associated with the task.</param>
        public void AttachProcess(Process process)
        {
            lock (_gate)
            {
                _process = process;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Appends standard-output text and refreshes the task's update timestamp.
        /// </summary>
        /// <param name="value">The output text to append.</param>
        public void AppendStandardOutput(string value)
        {
            lock (_gate)
            {
                _stdout.Append(value);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Appends standard-error text and refreshes the task's update timestamp.
        /// </summary>
        /// <param name="value">The error text to append.</param>
        public void AppendStandardError(string value)
        {
            lock (_gate)
            {
                _stderr.Append(value);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Finalizes the task status once the background process exits.
        /// </summary>
        /// <param name="exitCode">The process exit code when known.</param>
        public void CompleteProcess(int? exitCode)
        {
            lock (_gate)
            {
                ExitCode = exitCode;
                // A user-requested stop always wins over the process exit code so task_* callers see cancellation,
                // not a late-arriving success or failure from the operating system.
                Status = _stopRequested
                    ? WorkspaceTaskStatus.Canceled
                    : exitCode.GetValueOrDefault() == 0 ? WorkspaceTaskStatus.Completed : WorkspaceTaskStatus.Failed;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Applies mutable field updates to the task.
        /// </summary>
        /// <param name="subject">The replacement subject, or <see langword="null"/> to keep the current value.</param>
        /// <param name="description">The replacement description, or <see langword="null"/> to keep the current value.</param>
        /// <param name="activeForm">The replacement active-form wording, or <see langword="null"/> to keep the current value.</param>
        /// <param name="owner">The replacement owner, or <see langword="null"/> to keep the current value.</param>
        /// <param name="status">The replacement status, or <see langword="null"/> to keep the current value.</param>
        /// <param name="metadata">Additional metadata entries to merge, or <see langword="null"/> to keep metadata unchanged.</param>
        /// <returns>The set of logical fields that changed.</returns>
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

        /// <summary>
        /// Cancels a manual task or stops the associated background process.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels the stop request.</param>
        /// <returns>A task that completes when the entry has reached the canceled state.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
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

        /// <summary>
        /// Creates an immutable snapshot suitable for returning from public APIs.
        /// </summary>
        /// <returns>The current task state.</returns>
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
