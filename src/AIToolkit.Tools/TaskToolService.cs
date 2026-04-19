using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIToolkit.Tools;

/// <summary>
/// Implements the behavior behind the public <c>task_*</c> AI functions.
/// </summary>
/// <remarks>
/// <see cref="TaskAIFunctionFactory"/> reflects over this service to create host-facing AI functions. The method
/// signatures intentionally include optional <see cref="IServiceProvider"/> and <see cref="CancellationToken"/>
/// parameters so Microsoft.Extensions.AI can flow dependency injection and cancellation through tool invocation.
/// </remarks>
/// <seealso cref="TaskTools"/>
/// <seealso cref="ITaskToolStore"/>
internal sealed class TaskToolService(TaskToolsOptions options, ITaskToolStore taskStore)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "TaskToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();
    private readonly TaskToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ITaskToolStore _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));

    /// <summary>
    /// Creates a new manual task.
    /// </summary>
    /// <param name="subject">A short title describing the task.</param>
    /// <param name="description">The detailed task description.</param>
    /// <param name="activeForm">Optional present-progress wording such as "Reviewing docs".</param>
    /// <param name="owner">The optional task owner.</param>
    /// <param name="metadata">Additional arbitrary metadata to persist with the task.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that can cancel the tool call before completion.</param>
    /// <returns>A structured result describing the created task or the validation failure.</returns>
    public Task<WorkspaceTaskCreateToolResult> CreateTaskAsync(
        string subject,
        string description,
        string? activeForm = null,
        string? owner = null,
        Dictionary<string, string?>? metadata = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "task_create",
            new Dictionary<string, object?>
            {
                ["subject"] = subject,
                ["owner"] = owner,
            });

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(new WorkspaceTaskCreateToolResult(false, null, "subject is required."));
        }

        var task = _taskStore.CreateManualTask(
            subject,
            description,
            activeForm,
            owner,
            metadata is null
                ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase));

        return Task.FromResult(new WorkspaceTaskCreateToolResult(true, task));
    }

    /// <summary>
    /// Retrieves a task by identifier.
    /// </summary>
    /// <param name="taskId">The identifier of the task to retrieve.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that can cancel the tool call before completion.</param>
    /// <returns>A structured result containing the task when it exists.</returns>
    public Task<WorkspaceTaskGetToolResult> GetTaskAsync(
        string taskId,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "task_get", new Dictionary<string, object?> { ["taskId"] = taskId });

        var task = _taskStore.GetTask(taskId);
        return Task.FromResult(
            task is null
                ? new WorkspaceTaskGetToolResult(false, null, "Task not found.")
                : new WorkspaceTaskGetToolResult(true, task));
    }

    /// <summary>
    /// Lists tracked tasks, optionally filtered by status.
    /// </summary>
    /// <param name="status">An optional task status filter.</param>
    /// <param name="maxResults">The requested maximum number of tasks to return.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that can cancel the tool call before completion.</param>
    /// <returns>A structured result containing the matching task snapshots.</returns>
    public Task<WorkspaceTaskListToolResult> ListTasksAsync(
        WorkspaceTaskStatus? status = null,
        int maxResults = 50,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "task_list",
            new Dictionary<string, object?>
            {
                ["status"] = status,
                ["maxResults"] = maxResults,
            });

        var effectiveMaxResults = Math.Clamp(maxResults, 1, Math.Max(1, _options.MaxListResults));
        var tasks = _taskStore.ListTasks(status, effectiveMaxResults, out var truncated);
        return Task.FromResult(new WorkspaceTaskListToolResult(true, [.. tasks], truncated));
    }

    /// <summary>
    /// Updates mutable task fields.
    /// </summary>
    /// <param name="taskId">The identifier of the task to update.</param>
    /// <param name="subject">The replacement subject, or <see langword="null"/> to keep the current value.</param>
    /// <param name="description">The replacement description, or <see langword="null"/> to keep the current value.</param>
    /// <param name="activeForm">The replacement active-form wording, or <see langword="null"/> to keep the current value.</param>
    /// <param name="owner">The replacement owner, or <see langword="null"/> to keep the current value.</param>
    /// <param name="status">The replacement status, or <see langword="null"/> to keep the current value.</param>
    /// <param name="metadata">Additional metadata entries to merge into the task.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that can cancel the tool call before completion.</param>
    /// <returns>A structured result containing the updated task when it exists.</returns>
    public Task<WorkspaceTaskUpdateToolResult> UpdateTaskAsync(
        string taskId,
        string? subject = null,
        string? description = null,
        string? activeForm = null,
        string? owner = null,
        WorkspaceTaskStatus? status = null,
        Dictionary<string, string?>? metadata = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "task_update",
            new Dictionary<string, object?>
            {
                ["taskId"] = taskId,
                ["status"] = status,
            });

        var task = _taskStore.UpdateTask(taskId, subject, description, activeForm, owner, status, metadata, out var updatedFields);
        return Task.FromResult(
            task is null
                ? new WorkspaceTaskUpdateToolResult(false, null, Array.Empty<string>(), "Task not found.")
                : new WorkspaceTaskUpdateToolResult(true, task, updatedFields));
    }

    /// <summary>
    /// Stops a running background task or cancels a manual task.
    /// </summary>
    /// <param name="taskId">The identifier of the task to stop.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the stop request.</param>
    /// <returns>A structured result containing the updated task when it exists.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceTaskStopToolResult> StopTaskAsync(
        string taskId,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(serviceProvider, "task_stop", new Dictionary<string, object?> { ["taskId"] = taskId });

        var task = await _taskStore.StopTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        return task is null
            ? new WorkspaceTaskStopToolResult(false, null, "Task not found.")
            : new WorkspaceTaskStopToolResult(true, task);
    }

    private static void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, Dictionary<string, object?> parameters)
    {
        if (serviceProvider?.GetService(typeof(ILoggerFactory)) is not ILoggerFactory loggerFactory)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger("AIToolkit.Tools");
        ToolInvocationLog(logger, toolName, JsonSerializer.Serialize(parameters, LogJsonOptions), null);
    }
}
