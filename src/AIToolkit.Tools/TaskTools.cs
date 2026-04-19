using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Creates the generic <c>task_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This façade exposes the task-tracking tool family used to coordinate work across a session. It keeps the
/// host-facing surface stable while delegating task lifecycle behavior to <see cref="TaskToolService"/> and
/// sharing state through <see cref="ITaskToolStore"/>.
/// </remarks>
/// <example>
/// <code>
/// var taskStore = new InMemoryTaskToolStore();
/// IReadOnlyList&lt;AIFunction&gt; taskFunctions = TaskTools.CreateFunctions(
///     new TaskToolsOptions { MaxListResults = 100 },
///     taskStore);
/// </code>
/// </example>
/// <seealso cref="WorkspaceTools"/>
/// <seealso cref="ITaskToolStore"/>
public static class TaskTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>task_*</c> tools.
    /// </summary>
    /// <param name="workspaceToolsEnabled"><see langword="true"/> to include guidance that references the separate <c>workspace_*</c> tools.</param>
    /// <returns>A prompt section that can be appended to a host system prompt.</returns>
    public static string GetSystemPromptGuidance(bool workspaceToolsEnabled = false) =>
        ToolPromptCatalog.GetTaskSystemPromptGuidance(workspaceToolsEnabled);

    /// <summary>
    /// Appends task tool guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The current system prompt text.</param>
    /// <param name="workspaceToolsEnabled"><see langword="true"/> to include guidance that references the separate <c>workspace_*</c> tools.</param>
    /// <returns>The combined system prompt text.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, bool workspaceToolsEnabled = false) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance(workspaceToolsEnabled));

    /// <summary>
    /// Creates the default <c>task_*</c> tool set.
    /// </summary>
    /// <param name="options">The options that control list sizing and related task tool behavior.</param>
    /// <param name="taskStore">The shared task store that persists manual tasks and background command state.</param>
    /// <remarks>
    /// When <paramref name="taskStore"/> is <see langword="null"/>, the factory creates an
    /// <see cref="InMemoryTaskToolStore"/> instance for the returned functions.
    /// </remarks>
    /// <returns>The <c>task_*</c> AI functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null)
    {
        var factory = CreateFactory(options, taskStore);
        return factory.CreateAll();
    }

    /// <summary>
    /// Creates the <c>task_create</c> function.
    /// </summary>
    /// <param name="options">The options that control task tool behavior.</param>
    /// <param name="taskStore">The optional shared task store used to persist created tasks.</param>
    /// <returns>An AI function that invokes <see cref="TaskToolService.CreateTaskAsync"/>.</returns>
    public static AIFunction CreateCreateTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateCreateTask();

    /// <summary>
    /// Creates the <c>task_get</c> function.
    /// </summary>
    /// <param name="options">The options that control task tool behavior.</param>
    /// <param name="taskStore">The optional shared task store used to look up task state.</param>
    /// <returns>An AI function that invokes <see cref="TaskToolService.GetTaskAsync"/>.</returns>
    public static AIFunction CreateGetTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGetTask();

    /// <summary>
    /// Creates the <c>task_list</c> function.
    /// </summary>
    /// <param name="options">The options that control list sizing and related task tool behavior.</param>
    /// <param name="taskStore">The optional shared task store used to enumerate tasks.</param>
    /// <returns>An AI function that invokes <see cref="TaskToolService.ListTasksAsync"/>.</returns>
    public static AIFunction CreateListTasksFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateListTasks();

    /// <summary>
    /// Creates the <c>task_update</c> function.
    /// </summary>
    /// <param name="options">The options that control task tool behavior.</param>
    /// <param name="taskStore">The optional shared task store used to mutate existing tasks.</param>
    /// <returns>An AI function that invokes <see cref="TaskToolService.UpdateTaskAsync"/>.</returns>
    public static AIFunction CreateUpdateTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateUpdateTask();

    /// <summary>
    /// Creates the <c>task_stop</c> function.
    /// </summary>
    /// <param name="options">The options that control task tool behavior.</param>
    /// <param name="taskStore">The optional shared task store used to cancel tasks or stop background processes.</param>
    /// <returns>An AI function that invokes <see cref="TaskToolService.StopTaskAsync"/>.</returns>
    public static AIFunction CreateStopTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateStopTask();

    private static TaskAIFunctionFactory CreateFactory(TaskToolsOptions? options, ITaskToolStore? taskStore)
    {
        var normalizedOptions = options ?? new TaskToolsOptions();
        var effectiveTaskStore = taskStore ?? new InMemoryTaskToolStore();
        var toolService = new TaskToolService(normalizedOptions, effectiveTaskStore);
        return new TaskAIFunctionFactory(toolService);
    }
}
