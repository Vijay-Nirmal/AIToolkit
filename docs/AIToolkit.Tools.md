# AIToolkit.Tools

`AIToolkit.Tools` exposes two generic `Microsoft.Extensions.AI` tool families:

- `workspace_*` tools for shell execution, files, search, and notebook editing.
- `task_*` tools for task tracking and background-command management.

## At a Glance

Most applications need one shared task store and then compose both tool families.

| Need | Recommended choice |
| --- | --- |
| File, search, notebook, and shell tools | `WorkspaceTools.CreateFunctions(...)` |
| Task tracking and background command control | `TaskTools.CreateFunctions(...)` |
| Shared state between background commands and `task_*` tools | Reuse one `ITaskToolStore` instance |
| Built-in task persistence | `InMemoryTaskToolStore` |
| Custom or durable task persistence | Implement `ITaskToolStore` |

## Quick Start

```csharp
using AIToolkit.Tools;

var taskStore = new InMemoryTaskToolStore();

var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = @"C:\src\MyRepo"
    },
    taskStore);

var taskTools = TaskTools.CreateFunctions(taskStore: taskStore);

var tools = [.. workspaceTools, .. taskTools];
```

That gives the model the full generic workspace and task tool surface while keeping background shell commands and `task_*` operations pointed at the same store.

## Tool Families

| Family | Factory | Tool names | Notes |
| --- | --- | --- | --- |
| Workspace tools | `WorkspaceTools` | `workspace_*` | File, search, notebook, and shell operations |
| Task tools | `TaskTools` | `task_*` | Manual task management plus background-command control |

`WorkspaceTools.CreateFunctions(...)` no longer includes `task_*` tools. Add `TaskTools.CreateFunctions(...)` explicitly when you want task management.

## Shared Task Store Model

The shared task store is the bridge between the two tool families.

| Behavior | What it means |
| --- | --- |
| Background shell commands | `workspace_run_bash` and `workspace_run_powershell` can create process-backed tasks when `runInBackground` is `true` |
| Task visibility | Those background tasks are visible through `task_list`, `task_get`, and `task_stop` only if both tool families use the same `ITaskToolStore` |
| Built-in store | `InMemoryTaskToolStore` is included for simple host setups |
| Custom store | Implement `ITaskToolStore` if you need durable task state or custom orchestration |

## Public API Reference

### WorkspaceTools

Use these APIs to create the `workspace_*` toolset.

| API | Purpose |
| --- | --- |
| `WorkspaceTools.GetSystemPromptGuidance(...)` | Returns prompt text that explains how to use the `workspace_*` tools |
| `WorkspaceTools.CreateFunctions(...)` | Creates the complete `workspace_*` toolset |
| `WorkspaceTools.CreateRunBashFunction(...)` | Creates `workspace_run_bash` |
| `WorkspaceTools.CreateRunPowerShellFunction(...)` | Creates `workspace_run_powershell` |
| `WorkspaceTools.CreateReadFileFunction(...)` | Creates `workspace_read_file` |
| `WorkspaceTools.CreateWriteFileFunction(...)` | Creates `workspace_write_file` |
| `WorkspaceTools.CreateEditFileFunction(...)` | Creates `workspace_edit_file` |
| `WorkspaceTools.CreateGlobSearchFunction(...)` | Creates `workspace_glob_search` |
| `WorkspaceTools.CreateGrepSearchFunction(...)` | Creates `workspace_grep_search` |
| `WorkspaceTools.CreateEditNotebookFunction(...)` | Creates `workspace_edit_notebook` |

Every workspace tool factory accepts:

- `WorkspaceToolsOptions? options`
- `ITaskToolStore? taskStore`

Pass the same `taskStore` when those workspace tools need to participate in the same background-task lifecycle as your `task_*` tools.

Append `WorkspaceTools.GetSystemPromptGuidance(taskToolsEnabled: true)` to your host system prompt when you want reference-style guidance that steers the model toward dedicated `workspace_*` tools and optionally mentions the separate `task_*` toolset. Use the overload that accepts the current prompt when you want the method to concatenate the strings for you.

### TaskTools

Use these APIs to create the `task_*` toolset.

| API | Purpose |
| --- | --- |
| `TaskTools.GetSystemPromptGuidance(...)` | Returns prompt text that explains how to use the `task_*` tools |
| `TaskTools.CreateFunctions(...)` | Creates the complete `task_*` toolset |
| `TaskTools.CreateCreateTaskFunction(...)` | Creates `task_create` |
| `TaskTools.CreateGetTaskFunction(...)` | Creates `task_get` |
| `TaskTools.CreateListTasksFunction(...)` | Creates `task_list` |
| `TaskTools.CreateUpdateTaskFunction(...)` | Creates `task_update` |
| `TaskTools.CreateStopTaskFunction(...)` | Creates `task_stop` |

Every task tool factory accepts:

- `TaskToolsOptions? options`
- `ITaskToolStore? taskStore`

Append `TaskTools.GetSystemPromptGuidance(workspaceToolsEnabled: true)` to your host system prompt when you want task-planning guidance based on the TodoV2 flow and, optionally, cross-guidance that tells the model to use `workspace_*` tools for the actual file and shell work. Use the overload that accepts the current prompt when you want the method to concatenate the strings for you.

### InMemoryTaskToolStore

Use the built-in task store when in-memory task state is sufficient.

```csharp
var taskStore = new InMemoryTaskToolStore(maxOutputCharacters: 32_000);
```

`maxOutputCharacters` controls how much stdout and stderr is retained for process-backed tasks.

## Configuration Reference

### WorkspaceToolsOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `WorkingDirectory` | Current process directory | Default root used to resolve relative paths and shell execution |
| `DefaultCommandTimeoutSeconds` | `30` | Foreground shell timeout when a call does not specify one |
| `MaxCommandTimeoutSeconds` | `600` | Upper bound for shell timeouts |
| `MaxReadLines` | `400` | Maximum file lines returned when no explicit end range is provided |
| `MaxSearchResults` | `200` | Maximum glob or grep matches returned |
| `MaxSearchContextLines` | `3` | Maximum context lines returned around grep matches |
| `MaxTaskOutputCharacters` | `64000` | Default retained output cap for the built-in task store created by `WorkspaceTools` |

### TaskToolsOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `MaxListResults` | `200` | Upper bound applied to `task_list` results |

## Tool Reference

### Workspace Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `workspace_run_bash` | Runs a bash command | `command` | `workingDirectory`, `timeoutSeconds`, `runInBackground`, `taskSubject` | `WorkspaceCommandToolResult` |
| `workspace_run_powershell` | Runs a PowerShell command | `command` | `workingDirectory`, `timeoutSeconds`, `runInBackground`, `taskSubject` | `WorkspaceCommandToolResult` |
| `workspace_read_file` | Reads a text file | `path` | `startLine`, `endLine`, `workingDirectory` | `WorkspaceReadFileToolResult` |
| `workspace_write_file` | Creates or overwrites a text file | `path`, `content` | `overwrite`, `workingDirectory` | `WorkspaceWriteFileToolResult` |
| `workspace_edit_file` | Applies a deterministic file edit | `path`, `operation` | `oldText`, `newText`, `anchorText`, `content`, `workingDirectory` | `WorkspaceEditFileToolResult` |
| `workspace_glob_search` | Finds files by glob pattern | `pattern` | `workingDirectory`, `maxResults` | `WorkspaceGlobSearchToolResult` |
| `workspace_grep_search` | Searches file contents | `pattern` | `useRegex`, `includePattern`, `caseSensitive`, `contextLines`, `maxResults`, `workingDirectory` | `WorkspaceGrepSearchToolResult` |
| `workspace_edit_notebook` | Inserts, replaces, or deletes notebook cells | `path`, `operation` | `cellId`, `cellIndex`, `afterCellId`, `afterCellIndex`, `cellType`, `content`, `workingDirectory` | `WorkspaceNotebookEditToolResult` |

Workspace shell notes:

| Behavior | Details |
| --- | --- |
| Bash shell | `workspace_run_bash` tries `bash` first, then `sh` |
| PowerShell | `workspace_run_powershell` tries `pwsh` first, then Windows PowerShell on Windows |
| Background mode | When `runInBackground` is `true`, the tool returns a task ID and records the process in the shared task store |
| Task visibility | Use `task_list`, `task_get`, and `task_stop` against the same shared task store to inspect or stop those background commands |

### Task Tools

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `task_create` | Creates a manual task | `subject`, `description` | `activeForm`, `owner`, `metadata` | `WorkspaceTaskCreateToolResult` |
| `task_get` | Gets one task | `taskId` | None | `WorkspaceTaskGetToolResult` |
| `task_list` | Lists tasks | None | `status`, `maxResults` | `WorkspaceTaskListToolResult` |
| `task_update` | Updates a task | `taskId` | `subject`, `description`, `activeForm`, `owner`, `status`, `metadata` | `WorkspaceTaskUpdateToolResult` |
| `task_stop` | Stops or cancels a task | `taskId` | None | `WorkspaceTaskStopToolResult` |

Task notes:

| Behavior | Details |
| --- | --- |
| Manual tasks | Created through `task_create` and updated through `task_update` |
| Background tasks | Created automatically by workspace shell tools when `runInBackground` is `true` |
| Stop behavior | Running process tasks are killed; manual tasks are marked canceled |
| Persistence | Persistence depends on the `ITaskToolStore` implementation; `InMemoryTaskToolStore` does not survive process restarts |

## Individual Tool Composition

If you only want a small subset of tools, compose them directly.

```csharp
using AIToolkit.Tools;

var taskStore = new InMemoryTaskToolStore();

var tools = new[]
{
    WorkspaceTools.CreateReadFileFunction(
        new WorkspaceToolsOptions { WorkingDirectory = @"C:\repo" },
        taskStore),
    WorkspaceTools.CreateGrepSearchFunction(
        new WorkspaceToolsOptions { WorkingDirectory = @"C:\repo" },
        taskStore),
    TaskTools.CreateListTasksFunction(taskStore: taskStore),
};
```

Use a shared `ITaskToolStore` whenever individually-created workspace shell tools and task tools need to cooperate.

## Prompt Addenda

The package includes public helpers for appending tool-usage guidance to an existing system prompt.

```csharp
var systemPrompt = WorkspaceTools.GetSystemPromptGuidance(
    baseSystemPrompt,
    taskToolsEnabled: true);

systemPrompt = TaskTools.GetSystemPromptGuidance(
    systemPrompt,
    workspaceToolsEnabled: true);
```

These prompt sections are modeled after the Claude Code reference guidance for dedicated file/search tools and TodoV2 task tracking, using the non-swarm task flow.

## Sample

See `samples/AIToolkit.Tools.Sample` for an interactive agent sample built on a real `IChatClient`. The sample:

- seeds a workspace with text files, JSON, source code, and a notebook,
- composes `WorkspaceTools` and `TaskTools` with one `InMemoryTaskToolStore`, and
- prints prompt ideas that cover every `workspace_*` and `task_*` tool.