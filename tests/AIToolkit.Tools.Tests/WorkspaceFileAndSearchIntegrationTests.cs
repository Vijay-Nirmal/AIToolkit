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

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkspaceWriteFileToolResult>(
            functions,
            "workspace_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                path = Path.Combine("docs", "notes.txt"),
                content = "alpha\nbeta\ngamma",
                overwrite = true,
            }));

        Assert.IsTrue(writeResult.Success);
        Assert.IsTrue(File.Exists(writeResult.Path));

        var readResult = await FunctionTestUtilities.InvokeAsync<WorkspaceReadFileToolResult>(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                path = Path.Combine("docs", "notes.txt"),
                startLine = 2,
                endLine = 3,
            }));

        Assert.IsTrue(readResult.Success);
        Assert.AreEqual("beta\ngamma", readResult.Content);

        var editResult = await FunctionTestUtilities.InvokeAsync<WorkspaceEditFileToolResult>(
            functions,
            "workspace_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                path = Path.Combine("docs", "notes.txt"),
                operation = WorkspaceFileEditOperation.Append,
                content = "\ndelta",
            }));

        Assert.IsTrue(editResult.Success);
        Assert.AreEqual(1, editResult.ChangesApplied);

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