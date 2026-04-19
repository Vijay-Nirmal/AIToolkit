using AIToolkit.Tools.Document;
using AIToolkit.Tools.Document.GoogleDocs;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Util.Store;
using Google.GenAI;
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

// To try hosted Google Docs in this sample:
// - set GoogleDocs:Authentication:Type to Default, CredentialsFile, ServiceAccount, AccessToken, or ApiKey
// - populate the matching fields under GoogleDocs:Authentication
// - AccessToken mode launches an interactive browser OAuth flow from the configured client-secrets file and caches the refresh token locally
// - API key mode is intended for public Google Docs reads; create, update, and managed payload access usually require OAuth credentials
var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
var hostedAuthType = GetOptionalSetting(configuration, "GoogleDocs:Authentication:Type") ?? "Default";
var normalizedHostedAuthType = NormalizeGoogleDocsAuthType(hostedAuthType);

GoogleDocsDocumentHandlerOptions handlerOptions;
try
{
    handlerOptions = await CreateGoogleDocsHandlerOptionsAsync(configuration, normalizedHostedAuthType).ConfigureAwait(false);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

var documentToolOptions = new DocumentToolsOptions
{
    WorkingDirectory = workspaceDirectory,
    MaxReadLines = 8_000,
};

var tools = GoogleDocsDocumentTools.CreateFunctions(handlerOptions, documentToolOptions);
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

var systemPrompt = GoogleDocsDocumentTools.GetSystemPromptGuidance(
    $$"""
    You are a document automation assistant for the AIToolkit.Tools.Document.GoogleDocs sample workspace.
    The workspace root is {{workspaceDirectory}}.
    Use the document tools agentically: inspect the user's request, choose the right document_* tools, and rely on tool results instead of assuming document contents.
    Existing Google Docs should be referenced by a docs.google.com URL or a gdocs://documents/{documentId} reference.
    To create a new Google Doc in Drive, use gdocs://folders/root/documents/{title} or gdocs://folders/{folderId}/documents/{title}.
    There are no pre-seeded sample documents in this app. Work only from references the user provides or creates during the session.
    Hosted Google Docs auth mode for this sample is {{hostedAuthType}}.
    document_grep_search can search hosted Google Docs when explicit document_references are provided. Without explicit references it remains local-workspace search only and does not crawl hosted Google Drive content.
    If the auth mode is AccessToken, the sample acquires a Google user access token through an interactive browser OAuth flow before tool calls begin.
    If the auth mode is ApiKey, treat Google Docs access as public-read-focused. Creating, editing, or reading managed payload sidecars usually requires OAuth credentials instead of an API key.
    """,
    handlerOptions,
    documentToolOptions);

string[] examplePrompts =
[
    "Summarize https://docs.google.com/document/d/{documentId}/edit.",
    "Create gdocs://folders/root/documents/Project%20Plan with a short outline for next quarter.",
    "Edit gdocs://documents/{documentId} to replace one sentence with a clearer version.",
];

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Document.GoogleDocs sample agent");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine($"Hosted Google Docs auth type: {hostedAuthType}");
Console.WriteLine("Provide a docs.google.com URL, gdocs://documents/{documentId}, or gdocs://folders/{folderId}/documents/{title} in your prompt.");
Console.WriteLine("document_grep_search can search hosted Google Docs when you pass explicit document_references. Directory-based scans still search only local workspace files.");
if (string.Equals(normalizedHostedAuthType, "accesstoken", StringComparison.Ordinal))
{
    Console.WriteLine("AccessToken mode uses an interactive browser OAuth flow and a locally cached refresh token.");
}

if (string.Equals(normalizedHostedAuthType, "apikey", StringComparison.Ordinal))
{
    Console.WriteLine("API key mode is intended for public Google Docs reads. Create, edit, and managed-payload scenarios usually require OAuth credentials.");
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

static async Task<GoogleDocsDocumentHandlerOptions> CreateGoogleDocsHandlerOptionsAsync(IConfiguration configuration, string authType)
{
    var scopes = ParseScopes(GetOptionalSetting(configuration, "GoogleDocs:Scopes")) ?? GetDefaultGoogleDocsScopes();

    return new GoogleDocsDocumentHandlerOptions
    {
        Workspace = new GoogleDocsWorkspaceOptions
        {
            Credential = CreateGoogleDocsCredential(configuration, authType),
            HttpClientInitializer = await CreateGoogleDocsHttpClientInitializerAsync(configuration, authType, scopes).ConfigureAwait(false),
            ApiKey = GetGoogleDocsApiKey(configuration, authType),
            Scopes = scopes,
            ApplicationName = GetOptionalSetting(configuration, "GoogleDocs:ApplicationName"),
        },
    };
}

static GoogleCredential? CreateGoogleDocsCredential(IConfiguration configuration, string authType)
{
    return authType switch
    {
        "default" => GoogleCredential.GetApplicationDefault(),
        "credentialsfile" => CreateCredentialFromFile(configuration, requireServiceAccount: false),
        "serviceaccount" => CreateCredentialFromFile(configuration, requireServiceAccount: true),
        "accesstoken" => null,
        "apikey" => null,
        _ => throw new InvalidOperationException("GoogleDocs:Authentication:Type must be one of Default, CredentialsFile, ServiceAccount, AccessToken, or ApiKey."),
    };
}

static async Task<IConfigurableHttpClientInitializer?> CreateGoogleDocsHttpClientInitializerAsync(
    IConfiguration configuration,
    string authType,
    IReadOnlyList<string> scopes)
{
    if (!string.Equals(authType, "accesstoken", StringComparison.Ordinal))
    {
        return null;
    }

    var clientSecretsFile = GetSettingPath(configuration, "GoogleDocs:Authentication:OAuthClientSecretsFile");
    var tokenStoreDirectory = GetGoogleDocsOAuthTokenStoreDirectory(configuration);
    var oauthUser = GetOptionalSetting(configuration, "GoogleDocs:Authentication:OAuthUser") ?? "default";

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

static string? GetGoogleDocsApiKey(IConfiguration configuration, string authType)
{
    return authType == "apikey"
        ? GetSetting(configuration, "GoogleDocs:Authentication:ApiKey")
        : null;
}

static GoogleCredential CreateCredentialFromFile(IConfiguration configuration, bool requireServiceAccount)
{
    var credentialsFile = GetSettingPath(configuration, "GoogleDocs:Authentication:CredentialsFile");

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
        throw new InvalidOperationException("GoogleDocs:Authentication:CredentialsFile must point to a service-account JSON file when Type is ServiceAccount.");
    }

    return CredentialFactory.FromFile(credentialsFile, credentialType);
}

static IReadOnlyList<string> GetDefaultGoogleDocsScopes() =>
[
    "https://www.googleapis.com/auth/drive",
    "https://www.googleapis.com/auth/drive.appdata",
];

static string GetGoogleDocsOAuthTokenStoreDirectory(IConfiguration configuration)
{
    var configuredPath = GetOptionalPath(configuration, "GoogleDocs:Authentication:OAuthTokenStoreDirectory");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return configuredPath;
    }

    var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localApplicationData))
    {
        return Path.Combine(AppContext.BaseDirectory, ".google-oauth-cache");
    }

    return Path.Combine(localApplicationData, "AIToolkit", "Tools.Document.GoogleDocs.Sample", "oauth");
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

static string GetSettingPath(IConfiguration configuration, string key)
{
    var path = Path.GetFullPath(GetSetting(configuration, key), AppContext.BaseDirectory);
    if (File.Exists(path))
    {
        return path;
    }

    throw new InvalidOperationException($"Configuration value '{key}' points to '{path}', but that file does not exist.");
}

static string? GetOptionalPath(IConfiguration configuration, string key)
{
    var value = GetOptionalSetting(configuration, key);
    return value is null ? null : Path.GetFullPath(value, AppContext.BaseDirectory);
}

static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
{
    var value = configuration[key];
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

static string NormalizeGoogleDocsAuthType(string authType)
{
    var normalized = authType.Trim().ToLowerInvariant();
    return normalized switch
    {
        "default" => "default",
        "credentialsfile" => "credentialsfile",
        "credentials-file" => "credentialsfile",
        "file" => "credentialsfile",
        "serviceaccount" => "serviceaccount",
        "service-account" => "serviceaccount",
        "service" => "serviceaccount",
        "accesstoken" => "accesstoken",
        "access-token" => "accesstoken",
        "token" => "accesstoken",
        "apikey" => "apikey",
        "api-key" => "apikey",
        "key" => "apikey",
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
