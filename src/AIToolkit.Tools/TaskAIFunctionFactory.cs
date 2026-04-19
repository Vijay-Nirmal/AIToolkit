using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools;

/// <summary>
/// Maps the internal task tool service methods to the public <c>task_*</c> AI function names.
/// </summary>
/// <remarks>
/// The package keeps tool naming and descriptions in this factory so <see cref="TaskToolService"/> can focus on
/// behavior. That separation makes it easier to preserve a stable public tool surface as implementation details
/// evolve.
/// </remarks>
/// <seealso cref="TaskTools"/>
internal sealed class TaskAIFunctionFactory(TaskToolService toolService)
{
    private readonly TaskToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    /// <summary>
    /// Creates the full set of task tools.
    /// </summary>
    /// <returns>The AI functions exposed by <see cref="TaskTools"/>.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateCreateTask(),
        CreateGetTask(),
        CreateListTasks(),
        CreateUpdateTask(),
        CreateStopTask(),
    ];

    /// <summary>
    /// Creates the <c>task_create</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="TaskToolService.CreateTaskAsync"/>.</returns>
    public AIFunction CreateCreateTask() =>
        Create(nameof(TaskToolService.CreateTaskAsync), "task_create", ToolPromptCatalog.TaskCreateDescription);

    /// <summary>
    /// Creates the <c>task_get</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="TaskToolService.GetTaskAsync"/>.</returns>
    public AIFunction CreateGetTask() =>
        Create(nameof(TaskToolService.GetTaskAsync), "task_get", ToolPromptCatalog.TaskGetDescription);

    /// <summary>
    /// Creates the <c>task_list</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="TaskToolService.ListTasksAsync"/>.</returns>
    public AIFunction CreateListTasks() =>
        Create(nameof(TaskToolService.ListTasksAsync), "task_list", ToolPromptCatalog.TaskListDescription);

    /// <summary>
    /// Creates the <c>task_update</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="TaskToolService.UpdateTaskAsync"/>.</returns>
    public AIFunction CreateUpdateTask() =>
        Create(nameof(TaskToolService.UpdateTaskAsync), "task_update", ToolPromptCatalog.TaskUpdateDescription);

    /// <summary>
    /// Creates the <c>task_stop</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="TaskToolService.StopTaskAsync"/>.</returns>
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
