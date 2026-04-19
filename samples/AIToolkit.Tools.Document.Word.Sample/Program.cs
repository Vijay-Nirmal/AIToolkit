using AIToolkit.Tools.Document;
using AIToolkit.Tools.Document.Word;
using Azure.Core;
using Azure.Identity;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.Reflection;
using GoogleGenAIClient = Google.GenAI.Client;
using GoogleGenAIHttpOptions = Google.GenAI.Types.HttpOptions;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

// To try hosted OneDrive or SharePoint Word documents in this sample:
// - set M365:Enabled=true and M365:DocumentReference in appsettings.json or user-secrets
// - set M365:Authentication:Type to Default, DeviceCode, InteractiveBrowser, or ClientSecret
// - local workspace files continue to work even when hosted M365 support is enabled
var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
var hostedDocumentReference = GetOptionalSetting(configuration, "M365:DocumentReference");
var hostedAuthType = GetOptionalSetting(configuration, "M365:Authentication:Type") ?? "Default";
var wordHandlerOptions = CreateWordHandlerOptions(configuration);
var documentToolOptions = new DocumentToolsOptions
{
    WorkingDirectory = workspaceDirectory,
    MaxReadLines = 8_000,
};

var tools = WordDocumentTools.CreateFunctions(
    wordHandlerOptions,
    options: documentToolOptions);
await SeedSampleWorkspaceAsync(workspaceDirectory, tools);

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

var systemPrompt = WordDocumentTools.GetSystemPromptGuidance(
    $"""
    You are a document automation assistant for the AIToolkit.Tools.Document.Word sample workspace.
    The workspace root is {workspaceDirectory}.
    This sample runs on Windows and exposes document read, write, edit, and document-content search tools for Word files.
    Hosted OneDrive and SharePoint Word document references are {(wordHandlerOptions.M365 is null ? "disabled" : "enabled")} for this sample.
    """,
    wordHandlerOptions,
    documentToolOptions);

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Document.Word sample agent");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine($"Hosted M365 Word support: {(wordHandlerOptions.M365 is null ? "disabled" : "enabled")}");
if (wordHandlerOptions.M365 is not null)
{
    Console.WriteLine($"Hosted M365 auth type: {hostedAuthType}");
    Console.WriteLine("Hosted M365 references are enabled for direct document reads, writes, and edits.");
    Console.WriteLine("document_grep_search still scans only the local sample workspace.");
    if (!string.IsNullOrWhiteSpace(hostedDocumentReference))
    {
        Console.WriteLine($"Configured hosted document reference: {hostedDocumentReference}");
    }
}

Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Find the Word documents in the workspace.");
Console.WriteLine("- Summarize docs/guide.docx document.");
Console.WriteLine("- Search the Word documents for the phrase 'release template'.");
Console.WriteLine("- Explain what docs/imported.docx says.");
Console.WriteLine("- Update docs/guide.docx so it says 'authoritative document model' instead of 'canonical AsciiDoc'.");
Console.WriteLine("- Create docs/release-notes.docx with a short release summary.");
if (wordHandlerOptions.M365 is not null)
{
    Console.WriteLine("- Read a hosted SharePoint or OneDrive document by URL.");
    Console.WriteLine("- Write to an M365 drive-path reference such as m365://drives/{driveId}/root/docs/release-notes.docx.");
    if (!string.IsNullOrWhiteSpace(hostedDocumentReference))
    {
        Console.WriteLine($"- Summarize {hostedDocumentReference}.");
        Console.WriteLine($"- Update {hostedDocumentReference} with a short release note section.");
    }
}

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

static async Task SeedSampleWorkspaceAsync(string workspaceDirectory, IReadOnlyList<AIFunction> tools)
{
    Directory.CreateDirectory(Path.Combine(workspaceDirectory, "docs"));

    await InvokeToolAsync(
        tools,
        "document_write_file",
        new
        {
            document_reference = Path.Combine(workspaceDirectory, "docs", "guide.docx"),
            content =
            """
            = Word Provider Guide
            :doctype: article

            This sample document is stored as canonical AsciiDoc inside the Word package.

            == Features

            * read as canonical AsciiDoc
            * write from canonical AsciiDoc
            * exact string edits against canonical AsciiDoc
            * grep search across supported Word files
            """,
        });

    await InvokeToolAsync(
        tools,
        "document_write_file",
        new
        {
            document_reference = Path.Combine(workspaceDirectory, "docs", "release-template.dotx"),
            content =
            """
            = Release Template

            == Summary

            Fill in this section.

            == Changes

            * Added
            * Fixed
            * Removed
            """,
        });

    CreateExternalWordDocument(Path.Combine(workspaceDirectory, "docs", "imported.docx"));
}

static async Task InvokeToolAsync(IReadOnlyList<AIFunction> tools, string name, object values)
{
    var function = tools.Single(tool => tool.Name == name);
    var arguments = new AIFunctionArguments();
    foreach (var property in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
    {
        arguments[property.Name] = property.GetValue(values);
    }

    _ = await function.InvokeAsync(arguments);
}

static void CreateExternalWordDocument(string path)
{
    using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
    var mainPart = document.AddMainDocumentPart();
    mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
        new Body(
            new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("Imported External Document"))),
            new Paragraph(new Run(new Text("This file does not contain embedded canonical AsciiDoc."))),
            new Table(
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Key")))),
                    new TableCell(new Paragraph(new Run(new Text("Value"))))),
                new TableRow(
                    new TableCell(new Paragraph(new Run(new Text("Origin")))),
                    new TableCell(new Paragraph(new Run(new Text("External Word file"))))))));
    mainPart.Document.Save();
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

    throw new InvalidOperationException($"Configuration value '{key}' is required. Set it in appsettings.json or dotnet user-secrets.");
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

static WordDocumentHandlerOptions CreateWordHandlerOptions(IConfiguration configuration)
{
    // To try hosted OneDrive or SharePoint Word files in this sample:
    // 1. Set M365:Enabled=true and M365:DocumentReference in appsettings.json or user-secrets.
    // 2. Set M365:Authentication:Type to Default, DeviceCode, InteractiveBrowser, or ClientSecret.
    // 3. Fill in the matching auth settings under M365:Authentication.
    if (!GetBool(configuration, "M365:Enabled", defaultValue: false))
    {
        return new WordDocumentHandlerOptions();
    }

    return new WordDocumentHandlerOptions
    {
        M365 = new WordDocumentM365Options
        {
            // This credential must be able to acquire Graph tokens for the configured scopes.
            Credential = CreateM365Credential(configuration),
            Scopes = ParseScopes(GetOptionalSetting(configuration, "M365:Scopes")),
        },
    };
}

static TokenCredential CreateM365Credential(IConfiguration configuration)
{
    var authType = NormalizeM365AuthType(GetOptionalSetting(configuration, "M365:Authentication:Type") ?? "Default");

    return authType switch
    {
        "default" => new DefaultAzureCredential(),
        "devicecode" => CreateDeviceCodeCredential(configuration),
        "interactivebrowser" => CreateInteractiveBrowserCredential(configuration),
        "clientsecret" => CreateClientSecretCredential(configuration),
        _ => throw new InvalidOperationException("M365:Authentication:Type must be one of Default, DeviceCode, InteractiveBrowser, or ClientSecret."),
    };
}

static TokenCredential CreateDeviceCodeCredential(IConfiguration configuration)
{
    var clientId = GetSetting(configuration, "M365:Authentication:ClientId");
    var tenantId = GetOptionalSetting(configuration, "M365:Authentication:TenantId") ?? "common";

    return new DeviceCodeCredential(new DeviceCodeCredentialOptions
    {
        ClientId = clientId,
        TenantId = tenantId,
        DeviceCodeCallback = (code, cancellationToken) =>
        {
            Console.WriteLine();
            Console.WriteLine(code.Message);
            Console.WriteLine();
            return Task.CompletedTask;
        },
    });
}

static TokenCredential CreateInteractiveBrowserCredential(IConfiguration configuration)
{
    var clientId = GetSetting(configuration, "M365:Authentication:ClientId");
    var tenantId = GetOptionalSetting(configuration, "M365:Authentication:TenantId") ?? "common";
    var redirectUri = GetOptionalSetting(configuration, "M365:Authentication:RedirectUri");

    return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
    {
        ClientId = clientId,
        TenantId = tenantId,
        RedirectUri = string.IsNullOrWhiteSpace(redirectUri) ? null : new Uri(redirectUri, UriKind.Absolute),
    });
}

static TokenCredential CreateClientSecretCredential(IConfiguration configuration)
{
    return new ClientSecretCredential(
        tenantId: GetSetting(configuration, "M365:Authentication:TenantId"),
        clientId: GetSetting(configuration, "M365:Authentication:ClientId"),
        clientSecret: GetSetting(configuration, "M365:Authentication:ClientSecret"));
}

static IReadOnlyList<string>? ParseScopes(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static string NormalizeM365AuthType(string authType)
{
    var normalized = authType.Trim().ToLowerInvariant();
    return normalized switch
    {
        "default" => "default",
        "devicecode" => "devicecode",
        "device-code" => "devicecode",
        "interactivebrowser" => "interactivebrowser",
        "interactive-browser" => "interactivebrowser",
        "browser" => "interactivebrowser",
        "clientsecret" => "clientsecret",
        "client-secret" => "clientsecret",
        _ => normalized,
    };
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