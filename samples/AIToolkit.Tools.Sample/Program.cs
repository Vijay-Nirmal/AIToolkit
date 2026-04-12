using AIToolkit.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var apiKey = configuration["OpenAI:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI:ApiKey is not configured.");
    Console.Error.WriteLine("Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Tools.Sample");
    return;
}

var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
await SeedSampleWorkspaceAsync(workspaceDirectory);

var taskStore = new InMemoryTaskToolStore();
var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = workspaceDirectory,
        DefaultCommandTimeoutSeconds = 20,
        MaxCommandTimeoutSeconds = 120,
        MaxTaskOutputCharacters = 32_000,
    },
    taskStore);
var taskTools = TaskTools.CreateFunctions(taskStore: taskStore);
IReadOnlyList<AIFunction> tools = [.. workspaceTools, .. taskTools];

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole());

IChatClient agent = new ChatClientBuilder(CreateChatClient(configuration))
    .UseFunctionInvocation()
    .Build(services.BuildServiceProvider());

var chatOptions = new ChatOptions
{
    Tools = [.. tools],
};

var systemPrompt = WorkspaceTools.GetSystemPromptGuidance(
    $"""
    You are a coding assistant and workspace automation assistant for the AIToolkit.Tools sample workspace.
    When a question is asked or a goal is provided, use your tools to interact with the workspace files, understand the needed details beforehand, and complete tasks.
    The workspace root is {workspaceDirectory}.
    This sample runs on Windows using PowerShell.

    # Communicating with the user
    When sending user-facing text, you're writing for a person, not logging to a console. Assume users can't see most tool calls or thinking - only your text output. Before your first tool call, briefly state what you're about to do. While working, give short updates at key moments: when you find something load-bearing (a bug, a root cause), when changing direction, when you've made progress without an update.
    When making updates, assume the person has stepped away and lost the thread. They don't know codenames, abbreviations, or shorthand you created along the way, and didn't track your process. Write so they can pick back up cold: use complete, grammatically correct sentences without unexplained jargon. Expand technical terms. Err on the side of more explanation. Attend to cues about the user's level of expertise; if they seem like an expert, tilt a bit more concise, while if they seem like they're new, be more explanatory. 
    Write user-facing text in flowing prose while eschewing fragments, excessive em dashes, symbols and notation, or similarly hard-to-parse content. Only use tables when appropriate; for example to hold short enumerable facts (file names, line numbers, pass/fail), or communicate quantitative data. Don't pack explanatory reasoning into table cells -- explain before or after. Avoid semantic backtracking: structure each sentence so a person can read it linearly, building up meaning without having to re-parse what came before. 
    What's most important is the reader understanding your output without mental overhead or follow-ups, not how terse you are. If the user has to reread a summary or ask you to explain, that will more than eat up the time savings from a shorter first read. Match responses to the task: a simple question gets a direct answer in prose, not headers and numbered sections. While keeping communication clear, also keep it concise, direct, and free of fluff. Avoid filler or stating the obvious. Get straight to the point. Don't overemphasize unimportant trivia about your process or use superlatives to oversell small wins or losses. Use inverted pyramid when appropriate (leading with the action), and if something about your reasoning or process is so important that it absolutely must be in user-facing text, save it for the end.
    These user-facing text instructions do not apply to code or tool calls.

    # Output efficiency
    IMPORTANT: Go straight to the point. Try the simplest approach first without going in circles. Do not overdo it. Be extra concise.
    Keep your text output brief and direct. Lead with the answer or action, not the reasoning. Skip filler words, preamble, and unnecessary transitions. Do not restate what the user said — just do it. When explaining, include only what is necessary for the user to understand.
    Focus text output on:
    - Decisions that need the user's input
    - High-level status updates at natural milestones
    - Errors or blockers that change the plan
    If you can say it in one sentence, don't use three. Prefer short, direct sentences over long explanations. This does not apply to code or tool calls.
    """,
    taskToolsEnabled: true);
systemPrompt = TaskTools.GetSystemPromptGuidance(systemPrompt, workspaceToolsEnabled: true);

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools sample agent");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- List the markdown, JSON, and notebook files in the workspace.");
Console.WriteLine("- Read docs/overview.md and summarize what this sample workspace contains.");
Console.WriteLine("- Create notes/release-summary.txt with a short summary of the workspace.");
Console.WriteLine("- Append a checklist item to notes/todo.txt that says review prompt coverage.");
Console.WriteLine("- Search the workspace for the word telemetry in markdown and C# files.");
Console.WriteLine("- Add a code cell to notebooks/analysis.ipynb that prints 'hello from AIToolkit.Tools'.");
Console.WriteLine("- Run a shell command that lists the files under the docs folder.");
Console.WriteLine("- Run a PowerShell command that lists the JSON files under the workspace.");
Console.WriteLine("- Create a task called Review docs, then list all current tasks.");
Console.WriteLine("- Get the Review docs task, mark it completed, and list completed tasks.");
Console.WriteLine("- Start a background command that waits a few seconds, then list tasks and stop the running one.");
Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        continue;
    }

    if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    chatHistory.Add(new ChatMessage(ChatRole.User, prompt));

    Console.WriteLine();
    Console.WriteLine("Assistant:");

    var responseText = string.Empty;
    await foreach (var update in agent.GetStreamingResponseAsync(chatHistory, chatOptions))
    {
        if (!string.IsNullOrWhiteSpace(update.Text))
        {
            Console.Write(update.Text);
            responseText += update.Text;
        }
    }

    chatHistory.Add(new ChatMessage(ChatRole.Assistant, responseText));
    Console.WriteLine();
    Console.WriteLine();
}

static IChatClient CreateChatClient(IConfiguration configuration)
{
    var model = GetSetting(configuration, "OpenAI:Model");
    var endpoint = GetSetting(configuration, "OpenAI:Endpoint");
    var apiKey = GetSetting(configuration, "OpenAI:ApiKey");

    return new OpenAI.Chat.ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint, UriKind.Absolute),
            })
        .AsIChatClient();
}

static string GetSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (!string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    throw new InvalidOperationException(
        $"Configuration value '{key}' is required. Set it in appsettings.json or dotnet user-secrets.");
}

static async Task SeedSampleWorkspaceAsync(string workspaceDirectory)
{
    Directory.CreateDirectory(workspaceDirectory);

    var files = new Dictionary<string, string>
    {
        [Path.Combine(workspaceDirectory, "docs", "overview.md")] = """
            # AIToolkit.Tools Sample Workspace

            This workspace is seeded so an agent can exercise every workspace and task tool.

            ## Contents

            - docs/overview.md explains the workspace.
            - notes/todo.txt holds an editable checklist.
            - src/reporting/telemetry.cs contains telemetry-related code for grep samples.
            - config/settings.json gives the sample a JSON file to inspect.
            - notebooks/analysis.ipynb gives the notebook tool a valid file to edit.
            """,
        [Path.Combine(workspaceDirectory, "notes", "todo.txt")] = """
            - inspect docs
            - summarize telemetry usage
            - update notebook cells
            """,
        [Path.Combine(workspaceDirectory, "src", "reporting", "telemetry.cs")] = """
            namespace SampleWorkspace.Reporting;

            public static class Telemetry
            {
                public const string WorkspaceName = "AIToolkit.Tools.Sample";
                public const bool TelemetryEnabled = true;
                public const string Sink = "console";
            }
            """,
        [Path.Combine(workspaceDirectory, "config", "settings.json")] = """
            {
              "workspace": "AIToolkit.Tools.Sample",
              "telemetry": true,
              "logLevel": "Information"
            }
            """,
        [Path.Combine(workspaceDirectory, "notebooks", "analysis.ipynb")] = """
            {
              "cells": [
                {
                  "cell_type": "markdown",
                  "id": "intro",
                  "metadata": {},
                  "source": [
                    "# Workspace Analysis\n",
                    "Use notebook edits to add or replace cells.\n"
                  ]
                },
                {
                  "cell_type": "code",
                  "id": "setup",
                  "metadata": {},
                  "execution_count": null,
                  "outputs": [],
                  "source": [
                    "files = ['docs/overview.md', 'notes/todo.txt']\n",
                    "print(files)\n"
                  ]
                }
              ],
              "metadata": {},
              "nbformat": 4,
              "nbformat_minor": 5
            }
            """,
    };

    foreach (var file in files)
    {
        var directory = Path.GetDirectoryName(file.Key);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(file.Key, file.Value).ConfigureAwait(false);
    }
}
