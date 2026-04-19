using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Creates the generic <c>workspace_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This type is the public entry point for the file, search, notebook, and shell tools implemented by this
/// package. It keeps the host-facing API small while delegating execution to
/// <see cref="WorkspaceToolService"/> and preserving stable tool names through
/// <see cref="WorkspaceAIFunctionFactory"/>.
/// </remarks>
/// <example>
/// <code>
/// var taskStore = new InMemoryTaskToolStore();
/// IReadOnlyList&lt;AIFunction&gt; tools = WorkspaceTools.CreateFunctions(
///     new WorkspaceToolsOptions
///     {
///         WorkingDirectory = @"C:\repo",
///     },
///     taskStore);
/// </code>
/// </example>
/// <seealso cref="WorkspaceToolsOptions"/>
/// <seealso cref="TaskTools"/>
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
    /// <remarks>
    /// When <paramref name="taskStore"/> is <see langword="null"/>, the factory creates an
    /// <see cref="InMemoryTaskToolStore"/> sized by <see cref="WorkspaceToolsOptions.MaxTaskOutputCharacters"/>.
    /// </remarks>
    /// <returns>The <c>workspace_*</c> AI functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateAll();

    /// <summary>
    /// Creates the <c>workspace_run_bash</c> function.
    /// </summary>
    /// <param name="options">The options that control working directory resolution, timeouts, and output limits.</param>
    /// <param name="taskStore">The optional shared task store used when commands run in the background.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.RunBashAsync"/>.</returns>
    public static AIFunction CreateRunBashFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateRunBash();

    /// <summary>
    /// Creates the <c>workspace_run_powershell</c> function.
    /// </summary>
    /// <param name="options">The options that control working directory resolution, timeouts, and output limits.</param>
    /// <param name="taskStore">The optional shared task store used when commands run in the background.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.RunPowerShellAsync"/>.</returns>
    public static AIFunction CreateRunPowerShellFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateRunPowerShell();

    /// <summary>
    /// Creates the <c>workspace_read_file</c> function.
    /// </summary>
    /// <param name="options">The options that control path resolution, read limits, and custom file handlers.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.ReadFileAsync"/>.</returns>
    public static AIFunction CreateReadFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateReadFile();

    /// <summary>
    /// Creates the <c>workspace_write_file</c> function.
    /// </summary>
    /// <param name="options">The options that control path resolution and read-state validation.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.WriteFileAsync"/>.</returns>
    public static AIFunction CreateWriteFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateWriteFile();

    /// <summary>
    /// Creates the <c>workspace_edit_file</c> function.
    /// </summary>
    /// <param name="options">The options that control path resolution, edit-size limits, and read-state validation.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.EditFileAsync"/>.</returns>
    public static AIFunction CreateEditFileFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateEditFile();

    /// <summary>
    /// Creates the <c>workspace_glob_search</c> function.
    /// </summary>
    /// <param name="options">The options that control working directory resolution and search result limits.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.GlobSearchAsync"/>.</returns>
    public static AIFunction CreateGlobSearchFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGlobSearch();

    /// <summary>
    /// Creates the <c>workspace_grep_search</c> function.
    /// </summary>
    /// <param name="options">The options that control working directory resolution and search result limits.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.GrepSearchAsync"/>.</returns>
    public static AIFunction CreateGrepSearchFunction(WorkspaceToolsOptions? options = null, ITaskToolStore? taskStore = null) =>
        CreateFactory(options, taskStore).CreateGrepSearch();

    /// <summary>
    /// Creates the <c>workspace_edit_notebook</c> function.
    /// </summary>
    /// <param name="options">The options that control path resolution and workspace behavior.</param>
    /// <param name="taskStore">The optional shared task store used by the workspace tool service.</param>
    /// <returns>An AI function that invokes <see cref="WorkspaceToolService.EditNotebookAsync"/>.</returns>
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
