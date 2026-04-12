# AIToolkit.Tools

`AIToolkit.Tools` provides two generic tool families for `Microsoft.Extensions.AI` hosts:

- `workspace_*` tools for shell execution, files, search, and notebook editing.
- `task_*` tools for task tracking and background-command management.

## Quick Start

```csharp
using AIToolkit.Tools;

var taskStore = new InMemoryTaskToolStore();

var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = @"C:\repo"
    },
    taskStore);

var taskTools = TaskTools.CreateFunctions(taskStore: taskStore);

var tools = [.. workspaceTools, .. taskTools];
```

## Public Factories

Use `WorkspaceTools` for the `workspace_*` toolset:

- `CreateFunctions`
- `GetSystemPromptGuidance`
- `CreateRunBashFunction`
- `CreateRunPowerShellFunction`
- `CreateReadFileFunction`
- `CreateWriteFileFunction`
- `CreateEditFileFunction`
- `CreateGlobSearchFunction`
- `CreateGrepSearchFunction`
- `CreateEditNotebookFunction`

Use `TaskTools` for the `task_*` toolset:

- `CreateFunctions`
- `GetSystemPromptGuidance`
- `CreateCreateTaskFunction`
- `CreateGetTaskFunction`
- `CreateListTasksFunction`
- `CreateUpdateTaskFunction`
- `CreateStopTaskFunction`

## Notes

- `WorkspaceTools.CreateFunctions(...)` returns only `workspace_*` tools.
- `TaskTools.CreateFunctions(...)` returns only `task_*` tools.
- `WorkspaceTools.GetSystemPromptGuidance(...)` and `TaskTools.GetSystemPromptGuidance(...)` return prompt text you can append to your host system prompt.
- Both types also provide an overload that accepts the current system prompt and returns the combined prompt text.
- Reuse the same `ITaskToolStore` when background shell commands should be visible to `task_*` tools.
- `InMemoryTaskToolStore` is the built-in implementation. You can provide your own `ITaskToolStore` to use durable storage.
- File, search, notebook, and shell tools accept an optional `workingDirectory` override. Relative paths are resolved against `WorkspaceToolsOptions.WorkingDirectory`.
- `workspace_run_bash` runs bash-compatible shell commands. `workspace_run_powershell` explicitly targets `pwsh` or Windows PowerShell.