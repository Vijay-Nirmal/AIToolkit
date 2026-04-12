using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Bing;
using AIToolkit.Tools.Web.Brave;
using AIToolkit.Tools.Web.DuckDuckGo;
using AIToolkit.Tools.Web.Google;
using AIToolkit.Tools.Web.Tavily;
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
    Console.Error.WriteLine("dotnet user-secrets set \"OpenAI:ApiKey\" \"<your-api-key>\" --project samples/AIToolkit.Tools.Web.Sample");
    return;
}

var providerName = GetRequiredSetting(configuration, "Web:Provider");
var searchProvider = CreateSearchProvider(configuration, providerName);

var tools = WebTools.CreateFunctions(
    new WebToolsOptions
    {
        MaxFetchCharacters = 20_000,
        MaxSearchResults = 8,
        MaxResponseBytes = 2 * 1024 * 1024,
    },
    searchProvider: searchProvider);

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole());

IChatClient agent = new ChatClientBuilder(CreateChatClient(configuration))
    .UseFunctionInvocation()
    .Build(services.BuildServiceProvider());

var chatOptions = new ChatOptions
{
    Tools = [.. tools],
};

var systemPrompt = WebTools.GetSystemPromptGuidance(
    $"""
    Do a deep reseach on .NET 10.
    The configured search provider is {searchProvider.ProviderName}.
    Use web_search for current information and web_fetch for specific pages you want to inspect more closely.
    When search results are ambiguous, fetch the most relevant pages before summarizing them.
    Keep answers concise, but include the required source links whenever web_search materially informed the answer.
    """);

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Web sample agent");
Console.WriteLine($"Configured provider: {searchProvider.ProviderName}");
Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Search the web for the latest .NET 10 release notes and include sources.");
Console.WriteLine("- Search only learn.microsoft.com for ASP.NET Core 10 minimal API docs.");
Console.WriteLine("- Fetch https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/runtime and summarize the runtime changes.");
Console.WriteLine("- Search for recent C# language news from official Microsoft domains only.");
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
    var model = GetRequiredSetting(configuration, "OpenAI:Model");
    var endpoint = GetRequiredSetting(configuration, "OpenAI:Endpoint");
    var apiKey = GetRequiredSetting(configuration, "OpenAI:ApiKey");

    return new OpenAI.Chat.ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint, UriKind.Absolute),
            })
        .AsIChatClient();
}

static IWebSearchProvider CreateSearchProvider(IConfiguration configuration, string providerName)
{
    return providerName.Trim().ToLowerInvariant() switch
    {
        "google" => new GoogleWebSearchProvider(
            new GoogleWebSearchOptions
            {
                ApiKey = GetRequiredSetting(configuration, "Web:Google:ApiKey"),
                SearchEngineId = GetRequiredSetting(configuration, "Web:Google:SearchEngineId"),
                CountryCode = GetOptionalSetting(configuration, "Web:Google:CountryCode"),
                LanguageRestriction = GetOptionalSetting(configuration, "Web:Google:LanguageRestriction"),
                InterfaceLanguage = GetOptionalSetting(configuration, "Web:Google:InterfaceLanguage"),
            }),
        "bing" => new BingWebSearchProvider(
            new BingWebSearchOptions
            {
                ApiKey = GetRequiredSetting(configuration, "Web:Bing:ApiKey"),
                Market = GetOptionalSetting(configuration, "Web:Bing:Market"),
                SafeSearch = GetOptionalSetting(configuration, "Web:Bing:SafeSearch") ?? "Moderate",
            }),
        "duckduckgo" => new DuckDuckGoWebSearchProvider(
            new DuckDuckGoWebSearchOptions
            {
                Endpoint = GetOptionalSetting(configuration, "Web:DuckDuckGo:Endpoint") ?? "https://html.duckduckgo.com/html/",
                MaxResults = GetInt(configuration, "Web:DuckDuckGo:MaxResults", defaultValue: 8),
                UserAgent = GetOptionalSetting(configuration, "Web:DuckDuckGo:UserAgent") ?? "AIToolkit.Tools.Web.Sample/1.0",
            }),
        "brave" => new BraveWebSearchProvider(
            new BraveWebSearchOptions
            {
                ApiKey = GetRequiredSetting(configuration, "Web:Brave:ApiKey"),
                Country = GetOptionalSetting(configuration, "Web:Brave:Country"),
                SearchLanguage = GetOptionalSetting(configuration, "Web:Brave:SearchLanguage"),
                SafeSearch = GetOptionalSetting(configuration, "Web:Brave:SafeSearch") ?? "moderate",
                ExtraSnippets = GetBool(configuration, "Web:Brave:ExtraSnippets", defaultValue: true),
            }),
        "tavily" => new TavilyWebSearchProvider(
            new TavilyWebSearchOptions
            {
                ApiKey = GetRequiredSetting(configuration, "Web:Tavily:ApiKey"),
                SearchDepth = GetOptionalSetting(configuration, "Web:Tavily:SearchDepth") ?? "basic",
                Topic = GetOptionalSetting(configuration, "Web:Tavily:Topic") ?? "general",
                IncludeAnswer = GetBool(configuration, "Web:Tavily:IncludeAnswer", defaultValue: true),
                IncludeRawContent = GetBool(configuration, "Web:Tavily:IncludeRawContent", defaultValue: false),
                Country = GetOptionalSetting(configuration, "Web:Tavily:Country"),
            }),
        _ => throw new InvalidOperationException("Web:Provider must be one of DuckDuckGo, Google, Bing, Brave, or Tavily."),
    };
}

static string GetRequiredSetting(IConfiguration configuration, string key)
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

static int GetInt(IConfiguration configuration, string key, int defaultValue)
{
    var value = configuration[key];
    return int.TryParse(value, out var parsed) ? parsed : defaultValue;
}
