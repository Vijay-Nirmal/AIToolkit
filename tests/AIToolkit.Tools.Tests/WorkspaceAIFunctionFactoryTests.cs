using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Tests;

/// <summary>
/// Verifies the public workspace tool factory surface and prompt guidance.
/// </summary>
[TestClass]
public class WorkspaceAIFunctionFactoryTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "workspace_edit_file",
        "workspace_edit_notebook",
        "workspace_glob_search",
        "workspace_grep_search",
        "workspace_read_file",
        "workspace_run_bash",
        "workspace_run_powershell",
        "workspace_write_file",
    ];

    /// <summary>
    /// Confirms the combined workspace tool set exposes the expected function names.
    /// </summary>
    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateWorkspaceFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    /// <summary>
    /// Confirms the single-function factory preserves the stable bash tool name.
    /// </summary>
    [TestMethod]
    public void CreateRunBashFunctionUsesExpectedName()
    {
        var function = WorkspaceTools.CreateRunBashFunction();

        Assert.AreEqual("workspace_run_bash", function.Name);
        StringAssert.Contains(function.Description, "Executes a given bash command");
    }

    /// <summary>
    /// Confirms workspace prompt guidance mentions task tools when requested.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceIncludesTaskGuidanceWhenEnabled()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance(taskToolsEnabled: true);

        StringAssert.Contains(prompt, "# Using workspace tools");
        StringAssert.Contains(prompt, "workspace_read_file");
        StringAssert.Contains(prompt, "task_* tools");
    }

    /// <summary>
    /// Confirms workspace prompt guidance omits task-specific instructions by default.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceOmitsTaskGuidanceWhenDisabled()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance();

        Assert.IsFalse(prompt.Contains("task_* tools", StringComparison.Ordinal));
    }

    /// <summary>
    /// Confirms workspace prompt guidance appends to an existing system prompt.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceAppendsToExistingPrompt()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance("Base prompt", taskToolsEnabled: true);

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using workspace tools");
    }

    /// <summary>
    /// Confirms the file-tool descriptions expose the expected reference guidance.
    /// </summary>
    [TestMethod]
    public void FileToolDescriptionsUseReferenceStyleGuidance()
    {
        var readFunction = WorkspaceTools.CreateReadFileFunction();
        var writeFunction = WorkspaceTools.CreateWriteFileFunction();
        var editFunction = WorkspaceTools.CreateEditFileFunction();

        StringAssert.Contains(readFunction.Description, "Reads a file from the local filesystem. You can access any file directly by using this tool.");
        StringAssert.Contains(readFunction.Description, "line number + tab");
        StringAssert.Contains(writeFunction.Description, "you MUST use the workspace_read_file tool first");
        StringAssert.Contains(editFunction.Description, "Performs exact string replacements in files.");
        StringAssert.Contains(editFunction.Description, "replace_all");
    }
}

/// <summary>
/// Verifies the public task tool factory surface and prompt guidance.
/// </summary>
[TestClass]
public class TaskAIFunctionFactoryTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "task_create",
        "task_get",
        "task_list",
        "task_stop",
        "task_update",
    ];

    /// <summary>
    /// Confirms the combined task tool set exposes the expected function names.
    /// </summary>
    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateTaskFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    /// <summary>
    /// Confirms the single-function factory preserves the stable task-list name.
    /// </summary>
    [TestMethod]
    public void CreateListTasksFunctionUsesExpectedName()
    {
        var function = TaskTools.CreateListTasksFunction();

        Assert.AreEqual("task_list", function.Name);
        StringAssert.Contains(function.Description, "overall progress");
    }

    /// <summary>
    /// Confirms the task-create prompt contains the expected workflow guidance.
    /// </summary>
    [TestMethod]
    public void CreateCreateTaskFunctionUsesReferenceStylePrompt()
    {
        var function = TaskTools.CreateCreateTaskFunction();

        StringAssert.Contains(function.Description, "structured task list for your current coding session");
        StringAssert.Contains(function.Description, "Complex multi-step tasks");
        StringAssert.Contains(function.Description, "Check task_list first to avoid creating duplicate tasks");
    }

    /// <summary>
    /// Confirms task prompt guidance mentions workspace tools when requested.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceIncludesWorkspaceGuidanceWhenEnabled()
    {
        var prompt = TaskTools.GetSystemPromptGuidance(workspaceToolsEnabled: true);

        StringAssert.Contains(prompt, "# Using task tools");
        StringAssert.Contains(prompt, "workspace_* tools");
        StringAssert.Contains(prompt, "task_create");
    }

    /// <summary>
    /// Confirms task prompt guidance omits workspace-specific instructions by default.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceOmitsWorkspaceGuidanceWhenDisabled()
    {
        var prompt = TaskTools.GetSystemPromptGuidance();

        Assert.IsFalse(prompt.Contains("workspace_* tools", StringComparison.Ordinal));
    }

    /// <summary>
    /// Confirms task prompt guidance appends to an existing system prompt.
    /// </summary>
    [TestMethod]
    public void GetSystemPromptGuidanceAppendsToExistingPrompt()
    {
        var prompt = TaskTools.GetSystemPromptGuidance("Base prompt", workspaceToolsEnabled: true);

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using task tools");
    }

    /// <summary>
    /// Confirms task-tool invocations log through an injected <see cref="ILoggerFactory"/>.
    /// </summary>
    [TestMethod]
    public async Task TaskListLogsWhenLoggerFactoryIsAvailable()
    {
        var functions = FunctionTestUtilities.CreateTaskFunctions();
        var loggerFactory = new TestLoggerFactory();
        var arguments = new AIFunctionArguments
        {
            Services = new TestServiceProvider(loggerFactory),
        };

        _ = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskListToolResult>(functions, "task_list", arguments);

        Assert.HasCount(1, loggerFactory.Entries);
        StringAssert.Contains(loggerFactory.Entries[0], "task_list");
    }

    private sealed class TestServiceProvider(ILoggerFactory loggerFactory) : IServiceProvider
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        /// <summary>
        /// Resolves the logger factory for the test service provider.
        /// </summary>
        /// <param name="serviceType">The requested service type.</param>
        /// <returns>The logger factory when requested; otherwise, <see langword="null"/>.</returns>
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ILoggerFactory) ? _loggerFactory : null;
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        /// <summary>
        /// Gets the captured log entries.
        /// </summary>
        public List<string> Entries { get; } = [];

        /// <summary>
        /// Ignores logger-provider registration for this test double.
        /// </summary>
        /// <param name="provider">The provider being added.</param>
        public void AddProvider(ILoggerProvider provider)
        {
        }

        /// <summary>
        /// Creates a logger that appends formatted messages to <see cref="Entries"/>.
        /// </summary>
        /// <param name="categoryName">The requested logger category.</param>
        /// <returns>A logger that records messages in memory.</returns>
        public ILogger CreateLogger(string categoryName) => new TestLogger(Entries);

        /// <summary>
        /// Releases resources held by the test logger factory.
        /// </summary>
        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(List<string> entries) : ILogger
    {
        private readonly List<string> _entries = entries;

        /// <summary>
        /// Begins a logging scope.
        /// </summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>Always <see langword="null"/> for this test double.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>
        /// Indicates whether logging is enabled for the supplied level.
        /// </summary>
        /// <param name="logLevel">The log level to evaluate.</param>
        /// <returns>Always <see langword="true"/>.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// Captures the formatted log message in memory.
        /// </summary>
        /// <typeparam name="TState">The log state type.</typeparam>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The logging event identifier.</param>
        /// <param name="state">The log state.</param>
        /// <param name="exception">The associated exception, if any.</param>
        /// <param name="formatter">The formatter used to render the log message.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(formatter(state, exception));
        }
    }
}
