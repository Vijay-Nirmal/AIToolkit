using AIToolkit.Tools;
using AIToolkit.Tools.PDF;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using GoogleGenAIClient = Google.GenAI.Client;
using GoogleGenAIHttpOptions = Google.GenAI.Types.HttpOptions;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";

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
        FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
    },
    taskStore);
var taskTools = TaskTools.CreateFunctions(taskStore: taskStore);
IReadOnlyList<AIFunction> tools = [.. workspaceTools, .. taskTools];

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole());

IChatClient chatClient;
try
{
    chatClient = CreateChatClient(configuration, chatProviderName);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

IChatClient agent = new ChatClientBuilder(chatClient)
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
    The configured chat provider is {chatProviderName}.

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
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- List the markdown, JSON, and notebook files in the workspace.");
Console.WriteLine("- Read docs/overview.md and summarize what this sample workspace contains.");
Console.WriteLine("- Read media/image.png and tell me what text appears in the image.");
Console.WriteLine("- Read media/document.pdf and summarize the PDF text plus any extracted images.");
Console.WriteLine("- Read docs/supported-files.md and tell me which default file types are seeded in the workspace.");
Console.WriteLine("- Read config/preferences.yaml, config/preferences-alt.yml, data/catalog.xml, and docs/table.csv.");
Console.WriteLine("- Read media/vector.svg, media/image.png, media/document.pdf, media/audio.mp3 and media/video.webm and explain the contents.");
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

static IChatClient CreateChatClient(IConfiguration configuration, string providerName)
{
    return NormalizeChatProvider(providerName) switch
    {
        "openai" => CreateOpenAIChatClient(configuration),
        "google" => CreateGoogleGenAIChatClient(configuration),
        _ => throw new InvalidOperationException("Chat:Provider must be one of OpenAI, Google, GoogleGenAI, or Gemini."),
    };
}

static IChatClient CreateOpenAIChatClient(IConfiguration configuration)
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

static IChatClient CreateGoogleGenAIChatClient(IConfiguration configuration)
{
    var model = GetSetting(configuration, "GoogleGenAI:Model");
    var useVertexAi = GetBool(configuration, "GoogleGenAI:UseVertexAI", defaultValue: false);
    var vertexBaseUrl = GetOptionalSetting(configuration, "GoogleGenAI:VertexBaseUrl");
    var geminiBaseUrl = GetOptionalSetting(configuration, "GoogleGenAI:GeminiBaseUrl");
    var apiVersion = GetOptionalSetting(configuration, "GoogleGenAI:ApiVersion");

    if (geminiBaseUrl is not null || vertexBaseUrl is not null)
    {
        GoogleGenAIClient.setDefaultBaseUrl(vertexBaseUrl: vertexBaseUrl, geminiBaseUrl: geminiBaseUrl);
    }

    GoogleGenAIHttpOptions? httpOptions = null;
    if (!string.IsNullOrWhiteSpace(apiVersion))
    {
        httpOptions = new GoogleGenAIHttpOptions
        {
            ApiVersion = apiVersion,
        };
    }

    GoogleGenAIClient client;
    if (useVertexAi)
    {
        client = new GoogleGenAIClient(
            vertexAI: true,
            project: GetSetting(configuration, "GoogleGenAI:Project"),
            location: GetSetting(configuration, "GoogleGenAI:Location"),
            httpOptions: httpOptions);
    }
    else
    {
        client = new GoogleGenAIClient(
            vertexAI: false,
            apiKey: GetSetting(configuration, "GoogleGenAI:ApiKey"),
            httpOptions: httpOptions);
    }

    return client.AsIChatClient(model);
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

static string? GetOptionalSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
{
    var value = configuration[key];
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

static string NormalizeChatProvider(string providerName)
{
    var normalized = providerName.Trim().ToLowerInvariant();
    return normalized switch
    {
        "openai" => "openai",
        "google" => "google",
        "googlegenai" => "google",
        "gemini" => "google",
        _ => normalized,
    };
}

static async Task SeedSampleWorkspaceAsync(string workspaceDirectory)
{
    Directory.CreateDirectory(workspaceDirectory);

    var textFiles = new Dictionary<string, string>
    {
        [Path.Combine(workspaceDirectory, "docs", "overview.md")] = """
            # AIToolkit.Tools Sample Workspace

            This workspace is seeded so an agent can exercise every workspace and task tool.
            It includes representative files for the built-in default file handlers.

            ## Contents

            - docs/overview.md explains the workspace.
            - docs/supported-files.md lists the default-supported file examples.
            - docs/page.html and docs/legacy.htm provide HTML text fixtures.
            - docs/table.csv provides a CSV text fixture.
            - notes/todo.txt holds an editable checklist.
            - src/reporting/telemetry.cs contains telemetry-related code for grep samples.
            - config/settings.json, config/preferences.yaml, and config/preferences-alt.yml provide structured text files.
            - data/catalog.xml provides an XML text fixture.
            - notebooks/analysis.ipynb gives the notebook tool a valid file to edit.
            - media/ contains checked-in sample media fixtures copied from the sample project.
            - PDFs are handled by the configured AIToolkit.Tools.PDF handler, which extracts page text and embedded images.

            Most media fixtures exercise the built-in media handler, which returns `DataContent` rather than parsing those formats.
            The PDF fixture exercises the configured PDF handler instead.
            """,
        [Path.Combine(workspaceDirectory, "docs", "supported-files.md")] = """
            # Default-Supported Sample Files

            This workspace includes examples for the built-in `workspace_read_file` handlers plus the configured PDF handler from `AIToolkit.Tools.PDF`.

            ## Text files

            - notes/todo.txt
            - docs/overview.md
            - docs/page.html
            - docs/legacy.htm
            - docs/table.csv
            - config/settings.json
            - config/preferences.yaml
            - config/preferences-alt.yml
            - data/catalog.xml
            - src/reporting/telemetry.cs

            ## Notebook files

            - notebooks/analysis.ipynb

            ## Media files

            - media/vector.svg
            - media/image.png
            - media/photo.jpg
            - media/photo.jpeg
            - media/banner.gif
            - media/bitmap.bmp
            - media/clip.webp
            - media/document.pdf
            - media/audio.wav
            - media/audio.mp3
            - media/video.mp4
            - media/video.webm

            ## PDF handler

            - media/document.pdf

            The sample registers `PdfWorkspaceTools.CreateFileHandler()`, so PDF files return extracted page text and embedded images instead of raw `application/pdf` bytes.
            """,
        [Path.Combine(workspaceDirectory, "docs", "page.html")] = """
            <!doctype html>
            <html>
            <head>
              <title>AIToolkit Sample Page</title>
            </head>
            <body>
              <main>
                <h1>AIToolkit.Tools Sample</h1>
                <p>This HTML file exists so the text handler can read a common web document format.</p>
              </main>
            </body>
            </html>
            """,
        [Path.Combine(workspaceDirectory, "docs", "legacy.htm")] = """
            <html>
            <body>
              <p>Legacy .htm fixture for the default text handler.</p>
            </body>
            </html>
            """,
        [Path.Combine(workspaceDirectory, "docs", "table.csv")] = """
            file,type,purpose
            overview.md,markdown,workspace summary
            settings.json,json,configuration sample
            analysis.ipynb,notebook,notebook edit target
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
        [Path.Combine(workspaceDirectory, "config", "preferences.yaml")] = """
            workspace: AIToolkit.Tools.Sample
            telemetry: true
            reviewers:
              - docs
              - tools
            """,
        [Path.Combine(workspaceDirectory, "config", "preferences-alt.yml")] = """
            workspace: AIToolkit.Tools.Sample
            notebook: analysis.ipynb
            preferredShell: pwsh
            """,
        [Path.Combine(workspaceDirectory, "data", "catalog.xml")] = """
            <catalog>
              <file path="docs/overview.md" kind="markdown" />
              <file path="config/settings.json" kind="json" />
              <file path="notebooks/analysis.ipynb" kind="notebook" />
            </catalog>
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

    var sourceMediaDirectory = Path.Combine(AppContext.BaseDirectory, "media");
    var destinationMediaDirectory = Path.Combine(workspaceDirectory, "media");

    foreach (var file in textFiles)
    {
        var directory = Path.GetDirectoryName(file.Key);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(file.Key, file.Value).ConfigureAwait(false);
    }

    await CopyDirectoryAsync(sourceMediaDirectory, destinationMediaDirectory).ConfigureAwait(false);
}

static async Task CopyDirectoryAsync(string sourceDirectory, string destinationDirectory)
{
    if (!Directory.Exists(sourceDirectory))
    {
        throw new DirectoryNotFoundException($"Sample media directory '{sourceDirectory}' was not found.");
    }

    Directory.CreateDirectory(destinationDirectory);

    foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
        var destinationPath = Path.Combine(destinationDirectory, relativePath);
        var destinationSubdirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationSubdirectory))
        {
            Directory.CreateDirectory(destinationSubdirectory);
        }

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
    }
}
