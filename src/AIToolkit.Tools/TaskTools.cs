using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Creates the generic <c>task_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
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

    public static IReadOnlyList<AIFunction> CreateFunctions(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null)
    {
        var factory = CreateFactory(options, taskStore);
        return factory.CreateAll();
    }

    public static AIFunction CreateCreateTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateCreateTask();

    public static AIFunction CreateGetTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGetTask();

    public static AIFunction CreateListTasksFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateListTasks();

    public static AIFunction CreateUpdateTaskFunction(TaskToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateUpdateTask();

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