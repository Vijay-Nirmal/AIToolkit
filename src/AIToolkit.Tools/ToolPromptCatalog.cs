namespace AIToolkit.Tools;

internal static class ToolPromptCatalog
{
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

    public static string WorkspaceRunBashDescription =>
        """
        Executes a given bash command and returns its output.

        Use this tool for terminal operations that genuinely require bash execution.

        Important guidance:
        - Avoid using this tool for reading, writing, editing, or searching files when a dedicated workspace_* tool can accomplish the task.
        - Prefer workspace_glob_search instead of find or recursive ls for file discovery.
        - Prefer workspace_grep_search instead of grep or rg for content search.
        - Prefer workspace_read_file instead of cat, head, or tail for reading files.
        - Prefer workspace_edit_file and workspace_write_file instead of shell-based text editing or redirection.
        - If commands are independent, make separate tool calls in parallel.
        - If commands depend on each other, run them sequentially.
        """;

    public static string WorkspaceRunPowerShellDescription =>
        "Executes a PowerShell command and returns its output. Use this for terminal operations via PowerShell, including cmdlets and Windows-oriented shell workflows. Do not use it for reading, writing, editing, or searching files when a dedicated workspace_* tool can do the job. If commands are independent, make separate tool calls in parallel. If commands depend on each other, run them sequentially.";

    public static string WorkspaceReadFileDescription =>
        "Reads a file from the local filesystem. Use this to inspect text files, optionally by line range. It can also read Jupyter notebook files as structured notebook content. Use shell commands only when you need directory or process operations rather than file reading.";

    public static string WorkspaceWriteFileDescription =>
        "Writes a file to the local filesystem. It can create a new file or overwrite an existing one. Prefer workspace_edit_file for targeted modifications to an existing file, and use this tool for new files or full rewrites.";

    public static string WorkspaceEditFileDescription =>
        "Performs deterministic text edits in a file. Prefer editing existing files instead of rewriting them. Use the smallest clearly unique target text when replacing or inserting content, and preserve the file's exact indentation and surrounding formatting.";

    public static string WorkspaceGlobSearchDescription =>
        "Fast file pattern matching tool for workspace searches. Use it to find files by name patterns such as **/*.cs or src/**/*.ts. Prefer this tool over shell-based find or recursive directory listing when you are looking for files by path pattern.";

    public static string WorkspaceGrepSearchDescription =>
        "Powerful workspace content search tool. Always prefer it for search tasks instead of invoking grep, rg, Select-String, or similar shell commands. It supports plain text or regex matching and can filter files with glob patterns.";

    public static string WorkspaceEditNotebookDescription =>
        "Edits a Jupyter notebook as structured notebook data. Use it to insert, replace, or delete notebook cells instead of treating .ipynb files as raw text when you need cell-aware changes.";

    public static string TaskCreateDescription =>
        """
        Use this tool to create a structured task list for your current coding session. This helps you track progress, organize complex tasks, and demonstrate thoroughness to the user. It also helps the user understand the progress of the task and the overall progress of their requests.

        Use this tool proactively in these scenarios:
        - Complex multi-step tasks where the work requires 3 or more distinct steps or actions.
        - Non-trivial tasks that require careful planning or multiple operations.
        - When the user explicitly asks for a todo list or gives you multiple requested changes.
        - After receiving new instructions, so you capture the user's requirements as tasks.

        Skip this tool when there is only a single straightforward or trivial task and tracking it provides no organizational benefit.

        Task fields:
        - subject: A brief, actionable title in imperative form.
        - description: What needs to be done.
        - activeForm: Optional present-continuous form shown while the task is in progress.

        All tasks are created with status pending.

        Tips:
        - Create clear, specific subjects that describe the outcome.
        - Check task_list first to avoid creating duplicate tasks.
        - After creating tasks, use task_update to set status, ownership, or follow-up details as work progresses.
        """;

    public static string TaskGetDescription =>
        """
        Use this tool to retrieve a task by its ID from the task list.

        Use it when:
        - You need the full description and context before starting work on a task.
        - You want the latest task state before updating it.
        - You want to understand whether a task is still blocked or already complete.

        It returns the full task details, including subject, description, status, active progress wording, ownership, metadata, and timestamps.

        Tips:
        - Read a task with task_get before making a meaningful task_update.
        - Use task_list for the summary view and task_get when you need the full task details.
        """;

    public static string TaskListDescription =>
        """
        Use this tool to list all tasks in the task list.

        Use it when:
        - You want to see what tasks are available to work on.
        - You want to check overall progress on the current request.
        - You want to find blocked tasks or newly unblocked work.
        - You just completed a task and need to decide what to do next.

        Returns a summary of each task, including:
        - id: Task identifier, used with task_get, task_update, and task_stop.
        - subject: Brief description of the task.
        - status: Current task status.
        - owner: Task owner when one is recorded.

        Tips:
        - Prefer working on tasks in ID order when several tasks are available, because earlier tasks often establish context for later ones.
        - Use task_get with a specific task ID when you need the full description and latest details.
        - After completing your current task, call task_list to find the next available work.
        """;

    public static string TaskUpdateDescription =>
        """
        Use this tool to update a task in the task list.

        Use it when:
        - You start work on a task and need to mark it in progress.
        - You complete work and need to mark the task completed.
        - Requirements change and the subject, description, or metadata need to be updated.
        - You discover a blocker and need to record it without losing the current task state.

        Status workflow:
        - pending -> in_progress -> completed

        Guidance:
        - Mark a task as in_progress before beginning work.
        - Mark a task completed only when the work is fully finished.
        - If the implementation is partial, blocked, or unresolved, keep the task active instead of marking it completed.
        - Read the latest task state with task_get before a significant update.
        - After completing a task, call task_list to find the next task or newly unblocked work.
        """;

    public static string TaskStopDescription =>
        """
        Stops a running background task or cancels a manual task by task ID.

        Use this tool when:
        - A background command needs to be terminated.
        - A tracked task should be canceled instead of completed.

        Return values indicate whether the stop succeeded and include the updated task state when available.
        """;

    public static string GetWorkspaceSystemPromptGuidance(bool taskToolsEnabled)
    {
        var lines = new List<string>
        {
            "# Using workspace tools",
            "- Do not use workspace_run_bash or workspace_run_powershell when a dedicated workspace_* tool can do the job. Dedicated tools make file and search operations clearer and easier to review.",
            "- To read files, use workspace_read_file instead of shell commands such as cat, head, tail, or Get-Content.",
            "- To edit existing files, use workspace_edit_file instead of shell-based text replacement.",
            "- To create new files or fully rewrite existing files, use workspace_write_file instead of shell redirection or Set-Content style commands.",
            "- To search for files by name or path pattern, use workspace_glob_search instead of find, recursive ls, or Get-ChildItem -Recurse.",
            "- To search file contents, use workspace_grep_search instead of grep, rg, or Select-String.",
            "- To edit notebook cells, use workspace_edit_notebook instead of treating a .ipynb file as raw text when you need cell-aware edits.",
            "- Reserve workspace_run_bash and workspace_run_powershell for terminal operations that genuinely require shell execution.",
            "- If independent tool calls can run in parallel, make them in parallel. If later calls depend on earlier results, run them sequentially.",
        };

        if (taskToolsEnabled)
        {
            lines.Add("- Break down and manage your work with the task_* tools. These tools help with planning and help the user track progress. Mark each task completed as soon as you are done with it instead of batching multiple task updates until the end.");
        }

        return string.Join("\n", lines);
    }

    public static string GetTaskSystemPromptGuidance(bool workspaceToolsEnabled)
    {
        var lines = new List<string>
        {
            "# Using task tools",
            "- Use task_create proactively for complex multi-step work, explicit todo-list requests, or multi-part user instructions.",
            "- Do not use task_* tools for a single straightforward or trivial task when tracking adds no value.",
            "- Use task_list to inspect overall progress, find available work, and check for newly unblocked tasks.",
            "- Use task_get when you need the full description and latest context for a task before starting work or updating it.",
            "- Use task_update to move tasks through pending to in_progress to completed, and mark a task in_progress before starting work.",
            "- Mark a task completed only when the work is fully finished. If you encounter blockers, unresolved errors, or partial implementation, keep the task active and update its details instead.",
            "- After completing a task, call task_list to find the next task or newly unblocked work.",
            "- Use task_stop to stop a running background task or cancel a task by task ID.",
        };

        if (workspaceToolsEnabled)
        {
            lines.Add("- Use workspace_* tools for the actual file, search, notebook, and shell work. The task_* tools are for planning, tracking progress, and managing background task state.");
            lines.Add("- When a workspace_run_bash or workspace_run_powershell call runs in the background, use task_list, task_get, and task_stop against the shared task store to inspect or stop it.");
        }

        return string.Join("\n", lines);
    }
}