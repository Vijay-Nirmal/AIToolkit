using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIToolkit.Tools;

/// <summary>
/// Implements the behavior behind the public <c>task_*</c> AI functions.
/// </summary>
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