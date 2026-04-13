using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Tests;

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

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateWorkspaceFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void CreateRunBashFunctionUsesExpectedName()
    {
        var function = WorkspaceTools.CreateRunBashFunction();

        Assert.AreEqual("workspace_run_bash", function.Name);
        StringAssert.Contains(function.Description, "Executes a given bash command");
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesTaskGuidanceWhenEnabled()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance(taskToolsEnabled: true);

        StringAssert.Contains(prompt, "# Using workspace tools");
        StringAssert.Contains(prompt, "workspace_read_file");
        StringAssert.Contains(prompt, "task_* tools");
    }

    [TestMethod]
    public void GetSystemPromptGuidanceOmitsTaskGuidanceWhenDisabled()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance();

        Assert.IsFalse(prompt.Contains("task_* tools", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetSystemPromptGuidanceAppendsToExistingPrompt()
    {
        var prompt = WorkspaceTools.GetSystemPromptGuidance("Base prompt", taskToolsEnabled: true);

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using workspace tools");
    }

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

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateTaskFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void CreateListTasksFunctionUsesExpectedName()
    {
        var function = TaskTools.CreateListTasksFunction();

        Assert.AreEqual("task_list", function.Name);
        StringAssert.Contains(function.Description, "overall progress");
    }

    [TestMethod]
    public void CreateCreateTaskFunctionUsesReferenceStylePrompt()
    {
        var function = TaskTools.CreateCreateTaskFunction();

        StringAssert.Contains(function.Description, "structured task list for your current coding session");
        StringAssert.Contains(function.Description, "Complex multi-step tasks");
        StringAssert.Contains(function.Description, "Check task_list first to avoid creating duplicate tasks");
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesWorkspaceGuidanceWhenEnabled()
    {
        var prompt = TaskTools.GetSystemPromptGuidance(workspaceToolsEnabled: true);

        StringAssert.Contains(prompt, "# Using task tools");
        StringAssert.Contains(prompt, "workspace_* tools");
        StringAssert.Contains(prompt, "task_create");
    }

    [TestMethod]
    public void GetSystemPromptGuidanceOmitsWorkspaceGuidanceWhenDisabled()
    {
        var prompt = TaskTools.GetSystemPromptGuidance();

        Assert.IsFalse(prompt.Contains("workspace_* tools", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetSystemPromptGuidanceAppendsToExistingPrompt()
    {
        var prompt = TaskTools.GetSystemPromptGuidance("Base prompt", workspaceToolsEnabled: true);

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using task tools");
    }

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

        public object? GetService(Type serviceType) =>
            serviceType == typeof(ILoggerFactory) ? _loggerFactory : null;
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        public List<string> Entries { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new TestLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(List<string> entries) : ILogger
    {
        private readonly List<string> _entries = entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

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