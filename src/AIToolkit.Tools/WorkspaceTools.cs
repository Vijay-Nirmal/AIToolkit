using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Creates the generic <c>workspace_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
public static class WorkspaceTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>workspace_*</c> tools.
    /// </summary>
    /// <param name="taskToolsEnabled"><see langword="true"/> to include guidance that references the separate <c>task_*</c> tools.</param>
    /// <returns>A prompt section that can be appended to a host system prompt.</returns>
    public static string GetSystemPromptGuidance(bool taskToolsEnabled = false) =>
        ToolPromptCatalog.GetWorkspaceSystemPromptGuidance(taskToolsEnabled);

    /// <summary>
    /// Appends workspace tool guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The current system prompt text.</param>
    /// <param name="taskToolsEnabled"><see langword="true"/> to include guidance that references the separate <c>task_*</c> tools.</param>
    /// <returns>The combined system prompt text.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, bool taskToolsEnabled = false) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance(taskToolsEnabled));

    /// <summary>
    /// Creates the default workspace tool set.
    /// </summary>
    /// <param name="options">The options that control path resolution, limits, and shell defaults.</param>
    /// <param name="taskStore">The shared task store used for background shell commands.</param>
    /// <returns>The <c>workspace_*</c> AI functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateAll();

    public static AIFunction CreateRunBashFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateRunBash();

    public static AIFunction CreateRunPowerShellFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateRunPowerShell();

    public static AIFunction CreateReadFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateReadFile();

    public static AIFunction CreateWriteFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateWriteFile();

    public static AIFunction CreateEditFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateEditFile();

    public static AIFunction CreateGlobSearchFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGlobSearch();

    public static AIFunction CreateGrepSearchFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGrepSearch();

    public static AIFunction CreateEditNotebookFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateEditNotebook();

    private static WorkspaceAIFunctionFactory CreateFactory(WorkspaceToolsOptions? options, ITaskToolStore? taskStore)
    {
        var normalizedOptions = options ?? new WorkspaceToolsOptions();
        var effectiveTaskStore = taskStore ?? new InMemoryTaskToolStore(normalizedOptions.MaxTaskOutputCharacters);
        var toolService = new WorkspaceToolService(normalizedOptions, effectiveTaskStore);
        return new WorkspaceAIFunctionFactory(toolService);
    }
}