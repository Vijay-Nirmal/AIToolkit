using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools;

/// <summary>
/// Maps the internal task tool service methods to the public <c>task_*</c> AI function names.
/// </summary>
internal sealed class TaskAIFunctionFactory(TaskToolService toolService)
{
    private readonly TaskToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateCreateTask(),
        CreateGetTask(),
        CreateListTasks(),
        CreateUpdateTask(),
        CreateStopTask(),
    ];

    public AIFunction CreateCreateTask() =>
        Create(nameof(TaskToolService.CreateTaskAsync), "task_create", ToolPromptCatalog.TaskCreateDescription);

    public AIFunction CreateGetTask() =>
        Create(nameof(TaskToolService.GetTaskAsync), "task_get", ToolPromptCatalog.TaskGetDescription);

    public AIFunction CreateListTasks() =>
        Create(nameof(TaskToolService.ListTasksAsync), "task_list", ToolPromptCatalog.TaskListDescription);

    public AIFunction CreateUpdateTask() =>
        Create(nameof(TaskToolService.UpdateTaskAsync), "task_update", ToolPromptCatalog.TaskUpdateDescription);

    public AIFunction CreateStopTask() =>
        Create(nameof(TaskToolService.StopTaskAsync), "task_stop", ToolPromptCatalog.TaskStopDescription);

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(TaskToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(TaskToolService)}.");

        return AIFunctionFactory.Create(
            method,
            _toolService,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = ToolJsonSerializerOptions.CreateWeb(),
            });
    }
}