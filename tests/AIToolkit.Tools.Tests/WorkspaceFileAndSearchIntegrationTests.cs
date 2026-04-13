using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;

namespace AIToolkit.Tools.Tests;

[TestClass]
public class WorkspaceFileAndSearchIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task FileToolsAndSearchToolsWorkTogether()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateWorkspaceFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "docs", "notes.txt");

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkspaceWriteFileToolResult>(
            functions,
            "workspace_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
                content = "alpha\nbeta\ngamma",
            }));

        Assert.IsTrue(writeResult.Success);
        Assert.IsTrue(File.Exists(writeResult.Path));
        Assert.AreEqual("create", writeResult.ChangeType);

        var readResult = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
                offset = 2,
                limit = 2,
            }));

        Assert.IsTrue(readResult.Count >= 1);
        Assert.IsTrue(readResult.OfType<TextContent>().Any(content => content.Text.Contains("2\tbeta", StringComparison.Ordinal) && content.Text.Contains("3\tgamma", StringComparison.Ordinal)));

        var fullRead = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
            }));

        Assert.IsTrue(fullRead.OfType<TextContent>().Any(content => content.Text.Contains("3\tgamma", StringComparison.Ordinal)));

        var editResult = await FunctionTestUtilities.InvokeAsync<WorkspaceEditFileToolResult>(
            functions,
            "workspace_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
                old_string = "gamma",
                new_string = "gamma\ndelta",
            }));

        Assert.IsTrue(editResult.Success);
        Assert.AreEqual(1, editResult.ChangesApplied);
        Assert.IsTrue(editResult.UpdatedContent?.Contains("delta", StringComparison.Ordinal) == true);

        var globResult = await FunctionTestUtilities.InvokeAsync<WorkspaceGlobSearchToolResult>(
            functions,
            "workspace_glob_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "**/*.txt",
            }));

        Assert.IsTrue(globResult.Success);
        CollectionAssert.Contains(globResult.Paths, "docs/notes.txt");

        var grepResult = await FunctionTestUtilities.InvokeAsync<WorkspaceGrepSearchToolResult>(
            functions,
            "workspace_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "delta",
                includePattern = "**/*.txt",
                useRegex = false,
                caseSensitive = false,
            }));

        Assert.IsTrue(grepResult.Success);
        Assert.IsTrue(grepResult.Matches.Any(static match => match.Path == "docs/notes.txt" && match.LineText.Contains("delta", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task WriteAndEditRequireFreshReadState()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "fresh.txt");
        await File.WriteAllTextAsync(filePath, "before");
        var functions = FunctionTestUtilities.CreateWorkspaceFunctions(workingDirectory);

        var writeWithoutRead = await FunctionTestUtilities.InvokeAsync<WorkspaceWriteFileToolResult>(
            functions,
            "workspace_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
                content = "after",
            }));

        Assert.IsFalse(writeWithoutRead.Success);
        StringAssert.Contains(writeWithoutRead.Message!, "File has not been read yet");

        _ = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
            }));

        await File.WriteAllTextAsync(filePath, "changed-outside");

        var editWithStaleRead = await FunctionTestUtilities.InvokeAsync<WorkspaceEditFileToolResult>(
            functions,
            "workspace_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = filePath,
                old_string = "changed-outside",
                new_string = "patched",
            }));

        Assert.IsFalse(editWithStaleRead.Success);
        StringAssert.Contains(editWithStaleRead.Message!, "File has been modified since read");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ReadToolSupportsCustomHandlersAndMediaContent()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var customPath = Path.Combine(workingDirectory, "data.custom");
        var imagePath = Path.Combine(workingDirectory, "image.png");
        await File.WriteAllTextAsync(customPath, "ignored");
        await File.WriteAllBytesAsync(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);

        var functions = WorkspaceTools.CreateFunctions(
            new WorkspaceToolsOptions
            {
                WorkingDirectory = workingDirectory,
                FileHandlers = [new CustomReadHandler()],
            });

        var customResult = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = customPath,
            }));

        Assert.AreEqual(1, customResult.Count);
        Assert.IsTrue(customResult[0] is TextContent customText && customText.Text == "custom-handler");

        var imageResult = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = imagePath,
            }));

        Assert.IsTrue(imageResult.OfType<DataContent>().Any(content => string.Equals(content.MediaType, "image/png", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task NotebookToolCanInsertAndReplaceCells()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var notebookPath = Path.Combine(workingDirectory, "sample.ipynb");
        await File.WriteAllTextAsync(
            notebookPath,
            """
            {
              "cells": [
                {
                  "cell_type": "markdown",
                  "id": "intro",
                  "metadata": {},
                  "source": ["# Title\n"]
                }
              ],
              "metadata": {},
              "nbformat": 4,
              "nbformat_minor": 5
            }
            """);

        var functions = FunctionTestUtilities.CreateWorkspaceFunctions(workingDirectory);

        var insertResult = await FunctionTestUtilities.InvokeAsync<WorkspaceNotebookEditToolResult>(
            functions,
            "workspace_edit_notebook",
            FunctionTestUtilities.CreateArguments(new
            {
                path = "sample.ipynb",
                operation = WorkspaceNotebookEditOperation.InsertBottom,
                cellType = WorkspaceNotebookCellType.Code,
                content = "print('hello')",
            }));

        Assert.IsTrue(insertResult.Success);
        Assert.AreEqual(2, insertResult.CellCount);
        Assert.IsNotNull(insertResult.AffectedCell);

        var replaceResult = await FunctionTestUtilities.InvokeAsync<WorkspaceNotebookEditToolResult>(
            functions,
            "workspace_edit_notebook",
            FunctionTestUtilities.CreateArguments(new
            {
                path = "sample.ipynb",
                operation = WorkspaceNotebookEditOperation.Replace,
                cellId = "intro",
                content = "# Updated\n",
                cellType = WorkspaceNotebookCellType.Markdown,
            }));

        Assert.IsTrue(replaceResult.Success);
        var savedNotebook = JsonNode.Parse(await File.ReadAllTextAsync(notebookPath))!.AsObject();
        var cells = savedNotebook["cells"]!.AsArray();
        Assert.AreEqual(2, cells.Count);
        Assert.AreEqual("# Updated\n", cells[0]!["source"]!.AsArray()[0]!.GetValue<string>());
        Assert.AreEqual("print('hello')", cells[1]!["source"]!.AsArray()[0]!.GetValue<string>());
    }
}

internal sealed class CustomReadHandler : IWorkspaceFileHandler
{
    public bool CanHandle(WorkspaceFileReadContext context) =>
        string.Equals(context.Extension, ".custom", StringComparison.OrdinalIgnoreCase);

    public ValueTask<IEnumerable<AIContent>> ReadAsync(WorkspaceFileReadContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IEnumerable<AIContent>>([new TextContent("custom-handler")]);
}