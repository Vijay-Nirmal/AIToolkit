using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.GoogleSheets;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Util.Store;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.Text.Json;
using GoogleGenAIClient = Google.GenAI.Client;
using GoogleGenAIHttpOptions = Google.GenAI.Types.HttpOptions;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
var hostedAuthType = GetOptionalSetting(configuration, "GoogleSheets:Authentication:Type") ?? "Default";
var normalizedHostedAuthType = NormalizeGoogleAuthType(hostedAuthType);

GoogleSheetsWorkbookHandlerOptions handlerOptions;
try
{
    handlerOptions = await CreateGoogleSheetsHandlerOptionsAsync(configuration, normalizedHostedAuthType).ConfigureAwait(false);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

var workbookToolOptions = new WorkbookToolsOptions
{
    WorkingDirectory = workspaceDirectory,
    MaxReadLines = 8_000,
    LogContentParameters = true
};

var tools = GoogleSheetsWorkbookTools.CreateFunctions(handlerOptions, workbookToolOptions);
Directory.CreateDirectory(workspaceDirectory);

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

var systemPrompt = GoogleSheetsWorkbookTools.GetSystemPromptGuidance(
    $$"""
    You are a workbook automation assistant for the AIToolkit.Tools.Workbook.GoogleSheets sample workspace.
    The workspace root is {{workspaceDirectory}}.
    Use the workbook tools agentically: inspect the user's request, choose the right workbook_* tools, and rely on tool results instead of assuming workbook contents.
    Existing Google Sheets should be referenced by a docs.google.com spreadsheet URL or a gsheets://spreadsheets/{spreadsheetId} reference.
    To create a new Google Sheet in Drive, use gsheets://folders/root/spreadsheets/{title} or gsheets://folders/{folderId}/spreadsheets/{title}.
    There are no pre-seeded sample spreadsheets in this app. Work only from references the user provides or creates during the session.
    Hosted Google Sheets auth mode for this sample is {{hostedAuthType}}.
    workbook_grep_search can search hosted Google Sheets when explicit workbook_references are provided. Without explicit references it remains local-workspace search only and does not crawl hosted Google Drive content.
    If the auth mode is AccessToken, the sample acquires a Google user access token through an interactive browser OAuth flow before tool calls begin.
    If the auth mode is ApiKey, treat Google Sheets access as public-read-focused. Creating, editing, or reading managed payload sidecars usually requires OAuth credentials instead of an API key.
    """,
    handlerOptions,
    workbookToolOptions);

string[] examplePrompts =
[
    "Summarize https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit.",
    "Create gsheets://folders/root/spreadsheets/Quarterly%20Forecast with a short revenue workbook.",
    "Edit gsheets://spreadsheets/{spreadsheetId} to update one metric and keep the existing sheet layout.",
];

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Workbook.GoogleSheets sample agent");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine($"Hosted Google Sheets auth type: {hostedAuthType}");
Console.WriteLine("Provide a docs.google.com spreadsheet URL, gsheets://spreadsheets/{spreadsheetId}, or gsheets://folders/{folderId}/spreadsheets/{title} in your prompt.");
Console.WriteLine("workbook_grep_search can search hosted Google Sheets when you pass explicit workbook_references. Directory-based scans still search only local workspace files.");
if (string.Equals(normalizedHostedAuthType, "accesstoken", StringComparison.Ordinal))
{
    Console.WriteLine("AccessToken mode uses an interactive browser OAuth flow and a locally cached refresh token.");
}

if (string.Equals(normalizedHostedAuthType, "apikey", StringComparison.Ordinal))
{
    Console.WriteLine("API key mode is intended for public Google Sheets reads. Create, edit, and managed-payload scenarios usually require OAuth credentials.");
}

Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
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

static async Task<GoogleSheetsWorkbookHandlerOptions> CreateGoogleSheetsHandlerOptionsAsync(IConfiguration configuration, string authType)
{
    var scopes = ParseScopes(GetOptionalSetting(configuration, "GoogleSheets:Scopes")) ?? GetDefaultGoogleSheetsScopes();

    return new GoogleSheetsWorkbookHandlerOptions
    {
        Workspace = new GoogleSheetsWorkspaceOptions
        {
            Credential = CreateGoogleCredential(configuration, authType),
            HttpClientInitializer = await CreateGoogleHttpClientInitializerAsync(configuration, authType, scopes).ConfigureAwait(false),
            ApiKey = GetGoogleApiKey(configuration, authType),
            Scopes = scopes,
            ApplicationName = GetOptionalSetting(configuration, "GoogleSheets:ApplicationName"),
        },
    };
}

static GoogleCredential? CreateGoogleCredential(IConfiguration configuration, string authType)
{
    return authType switch
    {
        "default" => GoogleCredential.GetApplicationDefault(),
        "credentialsfile" => CreateCredentialFromFile(configuration, requireServiceAccount: false),
        "serviceaccount" => CreateCredentialFromFile(configuration, requireServiceAccount: true),
        "accesstoken" => null,
        "apikey" => null,
        _ => throw new InvalidOperationException("GoogleSheets:Authentication:Type must be one of Default, CredentialsFile, ServiceAccount, AccessToken, or ApiKey."),
    };
}

static async Task<IConfigurableHttpClientInitializer?> CreateGoogleHttpClientInitializerAsync(
    IConfiguration configuration,
    string authType,
    IReadOnlyList<string> scopes)
{
    if (!string.Equals(authType, "accesstoken", StringComparison.Ordinal))
    {
        return null;
    }

    var clientSecretsFile = GetSettingPath(configuration, "GoogleSheets:Authentication:OAuthClientSecretsFile");
    var tokenStoreDirectory = GetGoogleOAuthTokenStoreDirectory(configuration);
    var oauthUser = GetOptionalSetting(configuration, "GoogleSheets:Authentication:OAuthUser") ?? "default";

    Directory.CreateDirectory(tokenStoreDirectory);
    Console.WriteLine($"Starting interactive Google OAuth browser sign-in using client secrets from {clientSecretsFile}.");
    Console.WriteLine($"OAuth refresh tokens will be cached in {tokenStoreDirectory}.");

    using var clientSecretsStream = File.OpenRead(clientSecretsFile);
    var clientSecrets = GoogleClientSecrets.FromStream(clientSecretsStream).Secrets;
    return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            scopes,
            oauthUser,
            CancellationToken.None,
            new FileDataStore(tokenStoreDirectory, fullPath: true))
        .ConfigureAwait(false);
}

static string? GetGoogleApiKey(IConfiguration configuration, string authType)
{
    return authType == "apikey"
        ? GetSetting(configuration, "GoogleSheets:Authentication:ApiKey")
        : null;
}

static GoogleCredential CreateCredentialFromFile(IConfiguration configuration, bool requireServiceAccount)
{
    var credentialsFile = GetSettingPath(configuration, "GoogleSheets:Authentication:CredentialsFile");

    var credentialJson = File.ReadAllText(credentialsFile);
    using var credentialDocument = JsonDocument.Parse(credentialJson);
    var credentialType = credentialDocument.RootElement.TryGetProperty("type", out var typeProperty)
        ? typeProperty.GetString()
        : null;
    if (string.IsNullOrWhiteSpace(credentialType))
    {
        throw new InvalidOperationException("The configured Google credential file does not contain a credential 'type' field.");
    }

    if (requireServiceAccount && !string.Equals(credentialType, "service_account", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("GoogleSheets:Authentication:CredentialsFile must point to a service-account credential when Type=ServiceAccount.");
    }

    return CredentialFactory.FromFile(credentialsFile, credentialType);
}

static IReadOnlyList<string> GetDefaultGoogleSheetsScopes() =>
    [
        "https://www.googleapis.com/auth/drive",
        "https://www.googleapis.com/auth/drive.appdata",
    ];

static IReadOnlyList<string>? ParseScopes(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static string GetGoogleOAuthTokenStoreDirectory(IConfiguration configuration)
{
    var configured = GetOptionalSetting(configuration, "GoogleSheets:Authentication:OAuthTokenStoreDirectory");
    return !string.IsNullOrWhiteSpace(configured)
        ? Path.GetFullPath(configured)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIToolkit", "GoogleSheetsSampleTokens");
}

static string NormalizeGoogleAuthType(string value) =>
    value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

static string NormalizeChatProvider(string value) =>
    value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "googlegenai" => "google",
        "gemini" => "google",
        var normalized => normalized,
    };

static string GetSetting(IConfiguration configuration, string key) =>
    GetOptionalSetting(configuration, key)
    ?? throw new InvalidOperationException($"Missing required configuration value '{key}'.");

static string? GetOptionalSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static string GetSettingPath(IConfiguration configuration, string key)
{
    var value = GetSetting(configuration, key);
    return Path.GetFullPath(value);
}

static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
{
    var value = GetOptionalSetting(configuration, key);
    return value is null ? defaultValue : bool.Parse(value);
}
