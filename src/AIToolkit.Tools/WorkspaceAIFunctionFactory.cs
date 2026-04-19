using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools;

/// <summary>
/// Maps the internal workspace tool service methods to the public <c>workspace_*</c> AI function names.
/// </summary>
/// <remarks>
/// This factory centralizes the stable public tool names, descriptions, and serializer settings that AI hosts
/// depend on. The underlying <see cref="WorkspaceToolService"/> can therefore change implementation details
/// without changing the public function contract.
/// </remarks>
/// <seealso cref="WorkspaceTools"/>
internal sealed class WorkspaceAIFunctionFactory(WorkspaceToolService toolService)
{
    private readonly WorkspaceToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

    /// <summary>
    /// Creates the full set of workspace tools.
    /// </summary>
    /// <returns>The AI functions exposed by <see cref="WorkspaceTools"/>.</returns>
    public IReadOnlyList<AIFunction> CreateAll() =>
    [
        CreateRunBash(),
        CreateRunPowerShell(),
        CreateReadFile(),
        CreateWriteFile(),
        CreateEditFile(),
        CreateGlobSearch(),
        CreateGrepSearch(),
        CreateEditNotebook(),
    ];

    /// <summary>
    /// Creates the <c>workspace_run_bash</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.RunBashAsync"/>.</returns>
    public AIFunction CreateRunBash() =>
        Create(nameof(WorkspaceToolService.RunBashAsync), "workspace_run_bash", ToolPromptCatalog.WorkspaceRunBashDescription);

    /// <summary>
    /// Creates the <c>workspace_run_powershell</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.RunPowerShellAsync"/>.</returns>
    public AIFunction CreateRunPowerShell() =>
        Create(nameof(WorkspaceToolService.RunPowerShellAsync), "workspace_run_powershell", ToolPromptCatalog.WorkspaceRunPowerShellDescription);

    /// <summary>
    /// Creates the <c>workspace_read_file</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.ReadFileAsync"/>.</returns>
    public AIFunction CreateReadFile() =>
        Create(nameof(WorkspaceToolService.ReadFileAsync), "workspace_read_file", ToolPromptCatalog.WorkspaceReadFileDescription);

    /// <summary>
    /// Creates the <c>workspace_write_file</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.WriteFileAsync"/>.</returns>
    public AIFunction CreateWriteFile() =>
        Create(nameof(WorkspaceToolService.WriteFileAsync), "workspace_write_file", ToolPromptCatalog.WorkspaceWriteFileDescription);

    /// <summary>
    /// Creates the <c>workspace_edit_file</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.EditFileAsync"/>.</returns>
    public AIFunction CreateEditFile() =>
        Create(nameof(WorkspaceToolService.EditFileAsync), "workspace_edit_file", ToolPromptCatalog.WorkspaceEditFileDescription);

    /// <summary>
    /// Creates the <c>workspace_glob_search</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.GlobSearchAsync"/>.</returns>
    public AIFunction CreateGlobSearch() =>
        Create(nameof(WorkspaceToolService.GlobSearchAsync), "workspace_glob_search", ToolPromptCatalog.WorkspaceGlobSearchDescription);

    /// <summary>
    /// Creates the <c>workspace_grep_search</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.GrepSearchAsync"/>.</returns>
    public AIFunction CreateGrepSearch() =>
        Create(nameof(WorkspaceToolService.GrepSearchAsync), "workspace_grep_search", ToolPromptCatalog.WorkspaceGrepSearchDescription);

    /// <summary>
    /// Creates the <c>workspace_edit_notebook</c> function.
    /// </summary>
    /// <returns>An AI function bound to <see cref="WorkspaceToolService.EditNotebookAsync"/>.</returns>
    public AIFunction CreateEditNotebook() =>
        Create(nameof(WorkspaceToolService.EditNotebookAsync), "workspace_edit_notebook", ToolPromptCatalog.WorkspaceEditNotebookDescription);

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(WorkspaceToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(WorkspaceToolService)}.");

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
