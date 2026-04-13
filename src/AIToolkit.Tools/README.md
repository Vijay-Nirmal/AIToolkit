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
- `workspace_read_file` now returns multimodal `IEnumerable<AIContent>` results, including numbered text, notebook output summaries, and binary `DataContent` for supported media files.
- Built-in default support covers any non-binary text file, `.ipynb` notebooks, `.bmp`, `.gif`, `.jpeg`, `.jpg`, `.mp3`, `.mp4`, `.pdf`, `.png`, `.svg`, `.wav`, `.webm`, `.webp`, and unknown binary files returned as `application/octet-stream`.
- Built-in PDF support is binary-only. Register a custom `IWorkspaceFileHandler` if you need page extraction or text extraction from PDFs.
- `AIToolkit.Tools.PDF` is the first-party PDF handler package for `workspace_read_file` when you want extracted page text and embedded images.
- Extend `workspace_read_file` by registering `IWorkspaceFileHandler` instances through `WorkspaceToolsOptions.FileHandlers` or dependency injection.
- `workspace_write_file` and `workspace_edit_file` follow a read-before-write flow similar to the Claude Code reference behavior and reject stale file state.
- Search, notebook, and shell tools accept an optional `workingDirectory` override. Relative paths are resolved against `WorkspaceToolsOptions.WorkingDirectory`.
- `workspace_run_bash` runs bash-compatible shell commands. `workspace_run_powershell` explicitly targets `pwsh` or Windows PowerShell.
- `samples/AIToolkit.Tools.Sample` can be configured to use either OpenAI or Google.GenAI as the backing `IChatClient`.

## File Handler Extensions

Use the public file handler abstraction when you want to add richer format support without changing `AIToolkit.Tools` itself.

```csharp
using AIToolkit.Tools;
using Microsoft.Extensions.AI;

public sealed class PdfReadHandler : IWorkspaceFileHandler
{
    public bool CanHandle(WorkspaceFileReadContext context) =>
        string.Equals(context.Extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public ValueTask<IEnumerable<AIContent>> ReadAsync(
        WorkspaceFileReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IEnumerable<AIContent>>(
        [
            new TextContent($"Custom PDF handling for {context.Request.FilePath}"),
        ]);
    }
}

var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = @"C:\repo",
        FileHandlers = [new PdfReadHandler()],
    },
    taskStore);
```