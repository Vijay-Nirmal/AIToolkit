using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools;

/// <summary>
/// Maps the internal workspace tool service methods to the public <c>workspace_*</c> AI function names.
/// </summary>
internal sealed class WorkspaceAIFunctionFactory(WorkspaceToolService toolService)
{
    private readonly WorkspaceToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));

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

    public AIFunction CreateRunBash() =>
        Create(nameof(WorkspaceToolService.RunBashAsync), "workspace_run_bash", ToolPromptCatalog.WorkspaceRunBashDescription);

    public AIFunction CreateRunPowerShell() =>
        Create(nameof(WorkspaceToolService.RunPowerShellAsync), "workspace_run_powershell", ToolPromptCatalog.WorkspaceRunPowerShellDescription);

    public AIFunction CreateReadFile() =>
        Create(nameof(WorkspaceToolService.ReadFileAsync), "workspace_read_file", ToolPromptCatalog.WorkspaceReadFileDescription);

    public AIFunction CreateWriteFile() =>
        Create(nameof(WorkspaceToolService.WriteFileAsync), "workspace_write_file", ToolPromptCatalog.WorkspaceWriteFileDescription);

    public AIFunction CreateEditFile() =>
        Create(nameof(WorkspaceToolService.EditFileAsync), "workspace_edit_file", ToolPromptCatalog.WorkspaceEditFileDescription);

    public AIFunction CreateGlobSearch() =>
        Create(nameof(WorkspaceToolService.GlobSearchAsync), "workspace_glob_search", ToolPromptCatalog.WorkspaceGlobSearchDescription);

    public AIFunction CreateGrepSearch() =>
        Create(nameof(WorkspaceToolService.GrepSearchAsync), "workspace_grep_search", ToolPromptCatalog.WorkspaceGrepSearchDescription);

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