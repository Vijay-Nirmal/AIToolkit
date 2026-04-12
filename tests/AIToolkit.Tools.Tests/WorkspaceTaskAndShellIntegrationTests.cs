namespace AIToolkit.Tools.Tests;

[TestClass]
public class WorkspaceTaskAndShellIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task ManualTaskLifecycleWorks()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctionSet(workingDirectory).AllFunctions;

        var createResult = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskCreateToolResult>(
            functions,
            "task_create",
            FunctionTestUtilities.CreateArguments(new
            {
                subject = "Draft README",
                description = "Write initial project README",
                owner = "agent",
            }));

        Assert.IsTrue(createResult.Success);
        Assert.IsNotNull(createResult.Task);

        var updateResult = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskUpdateToolResult>(
            functions,
            "task_update",
            FunctionTestUtilities.CreateArguments(new
            {
                taskId = createResult.Task!.Id,
                status = WorkspaceTaskStatus.Completed,
                activeForm = "Writing README",
            }));

        Assert.IsTrue(updateResult.Success);
        Assert.IsNotNull(updateResult.Task);
        Assert.AreEqual(WorkspaceTaskStatus.Completed, updateResult.Task.Status);

        var getResult = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskGetToolResult>(
            functions,
            "task_get",
            FunctionTestUtilities.CreateArguments(new
            {
                taskId = createResult.Task.Id,
            }));

        Assert.IsTrue(getResult.Success);
        Assert.IsNotNull(getResult.Task);
        Assert.AreEqual("Draft README", getResult.Task.Subject);

        var listResult = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskListToolResult>(
            functions,
            "task_list",
            FunctionTestUtilities.CreateArguments(new
            {
                status = WorkspaceTaskStatus.Completed,
                maxResults = 10,
            }));

        Assert.IsTrue(listResult.Success);
        Assert.IsTrue(listResult.Tasks.Any(task => task.Id == createResult.Task.Id));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task CommandToolSupportsForegroundAndBackgroundTasks()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctionSet(workingDirectory).AllFunctions;

        var foregroundResult = await FunctionTestUtilities.InvokeAsync<WorkspaceCommandToolResult>(
            functions,
            "workspace_run_bash",
            FunctionTestUtilities.CreateArguments(new
            {
                command = FunctionTestUtilities.GetEchoCommand("hello-tools"),
            }));

        Assert.IsTrue(foregroundResult.Success, foregroundResult.Message);
        StringAssert.Contains(foregroundResult.StandardOutput, "hello-tools");

        var backgroundResult = await FunctionTestUtilities.InvokeAsync<WorkspaceCommandToolResult>(
            functions,
            "workspace_run_bash",
            FunctionTestUtilities.CreateArguments(new
            {
                command = FunctionTestUtilities.GetLongRunningCommand(),
                runInBackground = true,
                taskSubject = "Long running command",
            }));

        Assert.IsTrue(backgroundResult.Success, backgroundResult.Message);
        Assert.IsTrue(backgroundResult.RunningInBackground);
        Assert.IsFalse(string.IsNullOrWhiteSpace(backgroundResult.TaskId));

        var stopResult = await FunctionTestUtilities.InvokeAsync<WorkspaceTaskStopToolResult>(
            functions,
            "task_stop",
            FunctionTestUtilities.CreateArguments(new
            {
                taskId = backgroundResult.TaskId,
            }));

        Assert.IsTrue(stopResult.Success, stopResult.Message);
        Assert.IsNotNull(stopResult.Task);
        Assert.AreEqual(WorkspaceTaskStatus.Canceled, stopResult.Task.Status);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task PowerShellToolRunsWhenAvailable()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateWorkspaceFunctions(workingDirectory);

        var result = await FunctionTestUtilities.InvokeAsync<WorkspaceCommandToolResult>(
            functions,
            "workspace_run_powershell",
            FunctionTestUtilities.CreateArguments(new
            {
                command = "Write-Output 'hello-powershell'",
            }));

        if (!result.Success && result.Message?.Contains("Unable to start PowerShell", StringComparison.OrdinalIgnoreCase) == true)
        {
            Assert.Inconclusive("PowerShell is not available in this environment.");
        }

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.StandardOutput, "hello-powershell");
    }
}