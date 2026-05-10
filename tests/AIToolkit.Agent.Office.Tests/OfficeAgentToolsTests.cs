using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AIToolkit.Agent.Office.Tests;

[TestClass]
public sealed class OfficeAgentToolsTests
{
    [TestMethod]
    public void GetSystemPromptGuidanceReturnsConciseOfficeToolPrompt()
    {
        var guidance = OfficeAgentTools.GetSystemPromptGuidance();

        Assert.IsFalse(string.IsNullOrWhiteSpace(guidance));
        StringAssert.Contains(guidance, "Use the office tool");
        StringAssert.Contains(guidance, "load the main office skill");
        StringAssert.Contains(guidance, "skill named `office`");
        StringAssert.Contains(guidance, "before loading any specialized office-* skill");
        StringAssert.Contains(guidance, "help command");
        Assert.IsFalse(guidance.Contains("# officecli", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CreateFunctionExposesUnifiedOfficeToolMetadata()
    {
        var function = OfficeAgentTools.CreateFunction();

        Assert.AreEqual("office", function.Name);
        StringAssert.Contains(function.Description, "Create, read, and modify Office documents");
        StringAssert.Contains(function.Description, "Commands: create");
    }

    [TestMethod]
    public void CreateToolReturnsOfficeAIFunctionAsAITool()
    {
        var tool = OfficeAgentTools.CreateTool();

        Assert.AreEqual("office", tool.Name);
        Assert.IsInstanceOfType<AIFunction>(tool);
    }

    [TestMethod]
    public async Task OfficeFunctionInvokesHelpCommand()
    {
        var function = OfficeAgentTools.CreateFunction();
        var arguments = new AIFunctionArguments
        {
            ["command"] = "help",
            ["format"] = "docx",
        };

        var invocationResult = await function.InvokeAsync(arguments);
        var result = invocationResult switch
        {
            string text => text,
            JsonElement json => json.GetString() ?? string.Empty,
            _ => invocationResult?.ToString() ?? string.Empty,
        };

        StringAssert.Contains(result, "## Strategy");
        StringAssert.Contains(result, "# DOCX Reference");
    }
}