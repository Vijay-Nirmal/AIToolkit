using AIToolkit.Agent.Office;
using GoogleGenAIClient = Google.GenAI.Client;
using GoogleGenAIHttpOptions = Google.GenAI.Types.HttpOptions;
using Microsoft.Agents.AI;
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

var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "office-workspace");
Directory.CreateDirectory(workspaceDirectory);
Directory.SetCurrentDirectory(workspaceDirectory);

var serviceProvider = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole())
    .BuildServiceProvider();

var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
var toolCallLoggingMiddleware = new OfficeToolCallLoggingMiddleware(serviceProvider.GetRequiredService<ILogger<OfficeToolCallLoggingMiddleware>>());
var officeTools = OfficeAgentTools.CreateFunctions();
var officeToolContextProvider = new OfficeToolAIContextProvider(officeTools);
var officeSkills = OfficeAgentSkills.CreateSkillsProvider();

const string OfficeQualityGuidance = """
    Generate polished, business-ready Office output by default.

    For Word documents:
    - Create a clear title and use heading hierarchy instead of plain paragraphs.
    - Prefer concise executive writing, bullet lists, numbered steps, and tables where they improve readability.
    - If the request is brief, infer a sensible structure with useful section titles.

    For Excel workbooks:
    - Use descriptive sheet names.
    - Create bold header rows, realistic sample data, formulas, totals, and summary sections when relevant.
    - Add charts when the request is about trends, comparisons, or dashboard-style output.

    For PowerPoint decks:
    - Build a coherent slide narrative with a title slide, logical middle slides, and a strong summary or next-steps slide.
    - Keep slide text concise and presentation-ready rather than document-like.
    - Prefer one main message per slide with strong headlines.

    Across all file types:
    - Avoid placeholder filler unless the user explicitly asks for a template.
    - Make strong reasonable assumptions when details are missing, and mention those assumptions briefly in your response.
    - If a property name, command path, or value format is unclear, call the office help command before making changes.
    """;

IChatClient toolEnabledChatClient = new ChatClientBuilder(CreateChatClient(configuration, chatProviderName))
    .UseFunctionInvocation()
    .Build(serviceProvider);

string[] examplePrompts =
[
    "Create launch-plan.docx titled 'Contoso Copilot Rollout Plan' with a title page, executive summary, milestone table with owner and date columns, a risk register table, and a numbered next-steps section. Make it board-ready and concise.",
    "Create board-update.docx for a Q2 steering committee with Heading 1 and Heading 2 sections, a KPI summary table, decisions made, open risks, and a numbered action list with owners and due dates.",
    "Create staffing-model.xlsx with a sheet named Team Plan, a bold header row, six realistic team members, monthly rate, allocation percentage, monthly cost and annual cost formulas, and a totals row.",
    "Create sales-dashboard.xlsx with quarterly revenue and margin data for four regions, growth-rate formulas, a summary section, and a chart comparing quarterly revenue by region.",
    "Create qbr-deck.pptx for Contoso Q2 FY25 with a title slide, agenda, revenue highlights, top risks, and next steps. Use concise executive wording and keep one core message per slide.",
    "Create product-launch-deck.pptx for a new AI add-in with slides for problem, solution, target users, rollout timeline, risks, and launch decisions. Use strong headlines and a clean professional tone.",
    "Inspect qbr-deck.pptx and summarize the slide structure, shapes, and any issues you find.",
    "Create operations-review.pptx with a title slide, KPI summary slide, timeline slide, and decision slide, using short presentation-ready bullets instead of dense paragraphs."
];

AIAgent baseAgent = new ChatClientAgent(
    toolEnabledChatClient,
    new ChatClientAgentOptions
    {
        Name = "OfficeAssistant",
        ChatOptions = new()
        {
            Instructions = string.Join(
                Environment.NewLine,
                [
                    $"You are an Office document assistant working in {workspaceDirectory}.",
                    "Prefer files in the current working directory unless the user asks for another location.",
                    "There are no pre-seeded sample Office documents in this app. Create files from the user's request or inspect files that already exist in the working directory.",
                    OfficeQualityGuidance,
                ]),
        },
        AIContextProviders = [officeToolContextProvider, officeSkills],
    },
    loggerFactory,
    serviceProvider);

AIAgent agent = baseAgent
    .AsBuilder()
    .Use(toolCallLoggingMiddleware.InvokeAsync)
    .Build();

List<ChatMessage> chatHistory = [];

Console.WriteLine("AIToolkit.Agent.Office sample agent");
Console.WriteLine($"Working directory: {workspaceDirectory}");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine("Tool call input/output logging: enabled");
Console.WriteLine("Available Office tools:");
foreach (var tool in officeTools)
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
foreach (var examplePrompt in examplePrompts)
{
    Console.WriteLine($"- {examplePrompt}");
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
    await foreach (var update in agent.RunStreamingAsync(chatHistory))
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

    if (vertexBaseUrl is not null || geminiBaseUrl is not null)
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
