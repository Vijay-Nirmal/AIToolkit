using AIToolkit.Tools.Deck;
using AIToolkit.Tools.Deck.PowerPoint;
using Azure.Core;
using Azure.Identity;
using DocumentFormat.OpenXml.Packaging;
using GoogleGenAIClient = Google.GenAI.Client;
using GoogleGenAIHttpOptions = Google.GenAI.Types.HttpOptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.Reflection;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
var samplePromptPath = Path.Combine(AppContext.BaseDirectory, "SamplePrompt.md");
SampleMode sampleMode;
try
{
    sampleMode = SampleModeParser.Parse(args);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

var hostedPresentationReference = GetOptionalSetting(configuration, "M365:PresentationReference");
var hostedAuthType = GetOptionalSetting(configuration, "M365:Authentication:Type") ?? "Default";
var toolOptions = CreateToolOptions(configuration, workspaceDirectory);
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
};

if (Directory.Exists(workspaceDirectory))
{
    Directory.Delete(workspaceDirectory, recursive: true);
}

var tools = PowerPointDeckTools.CreateFunctions(toolOptions);
await SeedSampleWorkspaceAsync(workspaceDirectory, tools, jsonOptions);

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
    Console.Error.WriteLine($"The sample workspace was prepared at: {workspaceDirectory}");
    Console.Error.WriteLine($"The feature-rich authoring prompt is available at: {samplePromptPath}");
    return;
}

IChatClient agent = new ChatClientBuilder(chatClient)
    .UseFunctionInvocation()
    .Build(services.BuildServiceProvider());

if (sampleMode == SampleMode.TemplateCreate)
{
    await TemplateCreationSample.RunAsync(agent, workspaceDirectory, toolOptions);
    return;
}

var chatOptions = new ChatOptions
{
    Tools = [.. tools],
};

var systemPrompt = PowerPointDeckTools.GetSystemPromptGuidance(
    $"""
    You are a presentation automation assistant for the AIToolkit.Tools.Deck.PowerPoint sample workspace.
    The workspace root is {workspaceDirectory}.
    This sample runs on Windows and exposes deck read, write, edit, grep, spec lookup, asset, template, and slide-image export tools for PowerPoint presentations.
    Hosted OneDrive and SharePoint PowerPoint references are {(toolOptions.M365 is null ? "disabled" : "enabled")} for this sample.
    Prefer working with the seeded sample presentations and sample assets unless the user asks for something new.
    """,
    toolOptions);

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Deck.PowerPoint sample agent");
Console.WriteLine($"Sample mode: {sampleMode}");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine($"Feature-rich authoring prompt: {samplePromptPath}");
Console.WriteLine("The sample workspace is recreated on each run so deck assets and presentations stay consistent.");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine($"Hosted M365 PowerPoint support: {(toolOptions.M365 is null ? "disabled" : "enabled")}");
if (toolOptions.M365 is not null)
{
    Console.WriteLine($"Hosted M365 auth type: {hostedAuthType}");
    Console.WriteLine("Hosted M365 references are enabled for direct presentation reads, writes, and edits.");
    Console.WriteLine("deck_grep_search still scans only the local sample workspace unless you pass explicit deck_references.");
    if (!string.IsNullOrWhiteSpace(hostedPresentationReference))
    {
        Console.WriteLine($"Configured hosted presentation reference: {hostedPresentationReference}");
    }
}

Console.WriteLine("Seeded sample files:");
Console.WriteLine("- presentations/quarterly-ops-review.pptx (tool-authored deck with embedded canonical DeckDoc)");
Console.WriteLine("- presentations/imported-external.pptx (best-effort import demo without embedded DeckDoc)");
Console.WriteLine("- Registered sample assets: sample/hero.png, sample/logo.png, sample/badge.png");

Console.WriteLine("Built-in templates:");
foreach (var template in PowerPointDeckTemplates.CreateDefaultTemplates().OrderBy(static template => template.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {template.Name}: {template.Description}");
}

Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Summarize presentations/quarterly-ops-review.pptx and explain how its layout system works.");
Console.WriteLine("- Search the sample presentations for Risk, renewal, or Appendix.");
Console.WriteLine("- Read presentations/imported-external.pptx and tell me what the best-effort importer recovered.");
Console.WriteLine("- Use the signal-brief template to create presentations/board-readout.pptx with a concise executive update.");
Console.WriteLine("- Use the prompt in SamplePrompt.md to create presentations/strategy-offsite.pptx.");
Console.WriteLine("- Choose the template creation mode at startup, then enter the path to the PowerPoint file you want to turn into a reusable template.");
Console.WriteLine("- Type /prompt to load and send the checked-in SamplePrompt.md request.");
Console.WriteLine("- Type /paste to enter multiline prompt mode, then finish with a line containing only ::end.");
if (toolOptions.M365 is not null)
{
    Console.WriteLine("- Read a hosted SharePoint or OneDrive presentation by URL.");
    Console.WriteLine("- Write to an M365 drive-path reference such as m365://drives/{driveId}/root/presentations/board-readout.pptx.");
    if (!string.IsNullOrWhiteSpace(hostedPresentationReference))
    {
        Console.WriteLine($"- Summarize {hostedPresentationReference}.");
        Console.WriteLine($"- Refresh {hostedPresentationReference} with a short operating review deck.");
    }
}

Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var prompt = await ReadUserPromptAsync(samplePromptPath);

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

static async Task SeedSampleWorkspaceAsync(string workspaceDirectory, IReadOnlyList<AIFunction> tools, JsonSerializerOptions jsonOptions)
{
    Directory.CreateDirectory(Path.Combine(workspaceDirectory, "presentations"));
    Directory.CreateDirectory(Path.Combine(workspaceDirectory, "source-assets"));

    var heroSourcePath = Path.Combine(workspaceDirectory, "source-assets", "hero.png");
    var logoSourcePath = Path.Combine(workspaceDirectory, "source-assets", "logo.png");
    var badgeSourcePath = Path.Combine(workspaceDirectory, "source-assets", "badge.png");

    await File.WriteAllBytesAsync(heroSourcePath, HeroPngBytes());
    await File.WriteAllBytesAsync(logoSourcePath, LogoPngBytes());
    await File.WriteAllBytesAsync(badgeSourcePath, BadgePngBytes());

    await RegisterAssetAsync(tools, jsonOptions, heroSourcePath, "sample/hero.png", "Hero image for seeded PowerPoint decks.");
    await RegisterAssetAsync(tools, jsonOptions, logoSourcePath, "sample/logo.png", "Logo mark for seeded PowerPoint decks.");
    await RegisterAssetAsync(tools, jsonOptions, badgeSourcePath, "sample/badge.png", "Badge icon for seeded PowerPoint decks.");

    var canonicalPresentationPath = Path.Combine(workspaceDirectory, "presentations", "quarterly-ops-review.pptx");
    var canonicalDeckDoc = """
        = Quarterly Operations Review
        :deckdoc: 1
        :locale: en-US
        :size: wide
        :grid: 32x18

        [theme primary=#0B5FFF accent=#F97316 ink=#0F172A muted=#475569 surface=#F8FAFC display="Aptos Display" body="Aptos"]
        [style title font=$display size=28 bold fg=$ink]
        [style subtitle font=$body size=15 fg=$muted]
        [style body font=$body size=16 fg=$ink]
        [style caption font=$body size=12 fg=$muted]
        [style metric font=$display size=20 bold fg=$primary]
        [asset hero "sample/hero.png"]
        [asset logo "sample/logo.png"]
        [asset badge "sample/badge.png"]
        [motion fade-in enter=fade dur=0.35s]
        [motion rise-up motion=from-bottom dur=0.45s]

        [layout cover]
        [grid 40x22]
        [background fill=$surface]
        [transition fade dur=0.30s]
        [split B2:AN18 cols=(12,26) gap=1 as=copy,visual]
        [stack chips in=copy dir=down count=2 gap=0.4]
        !A1 40x22 [shape rect fill=$surface layer=back]
        !A1 40x2 [shape rect fill=$primary layer=back]
        !AJ19 5x2 [image asset=logo fit=contain alt="Company logo"]
        @title = B4 18x2 [.title]
        @subtitle = B7 18x1 [.subtitle]
        @chipPrimary = chips[1] [shape roundrect fill=#DBEAFE fg=$primary]
        @chipSecondary = chips[2] [shape roundrect fill=#DCFCE7 fg=#166534]
        @hero = visual [image fit=cover radius=0.16]
        [end]

        [layout evidence]
        [background fill=#FFFFFF]
        [transition push dir=left dur=0.30s]
        [split B2:AN18 rows=(2,12) gap=0.75 as=head,body]
        [split body cols=(17,14) gap=1 as=copy,visual]
        [grid cards in=copy cols=2 rows=2 gap=0.5]
        @title = head [.title]
        @body = copy [list .body bullet=disc]
        @visual = visual [image fit=contain]
        [end]

        == Cover
        [use cover]
        [section Overview]
        [notes "Lead with the operating story before you open the chart-heavy slides."]
        @title | Quarterly Operations Review
        @subtitle | Revenue quality improved while backlog and cycle time both moved down.
        @chipPrimary [shape roundrect fill=#DBEAFE fg=$primary] | Revenue quality improved
        @chipSecondary [shape roundrect fill=#DCFCE7 fg=#166534] | Backlog down 23%
        @hero [image asset=hero fit=cover alt="Leadership workshop"]
        [animate title preset=fade-in order=1]
        [animate hero motion=from-right dur=0.45s order=2]
        [x powerpoint section-collapsed]

        == Proof Points
        [use evidence]
        [section Evidence]
        [notes "Use the chart to connect margin improvement to better mix rather than raw volume."]
        @title | What moved this quarter
        @cards[1] [shape roundrect fill=#F8FAFC stroke=#CBD5E1 radius=0.16in] | Revenue mix favored higher-retention segments.
        @cards[2] [list .body bullet=dash] | Cycle time down 18% | Approval handoffs simplified
        @cards[3] [list .body bullet=check] | Security review complete | Support launch complete
        @cards[4] [list .body bullet=number start=4] | Regional rollout | Partner enablement
        @visual [icon asset=badge fit=contain fg=$accent alt="Highlight badge"]
        [table Risks at=B13 size=18x5 .body header banded]
        | Risk | Owner | Status |
        | --- | --- | --- |
        | Vendor delay | Ops | Open |
        | Scope creep | Product | Mitigated |
        [end]
        [chart "Revenue Trend" type=combo at=T8 size=12x7]
        - series column "Revenue" cat=(Q1,Q2,Q3,Q4) val=(12,14,17,19) color=$primary
        - series line "Margin %" cat=(Q1,Q2,Q3,Q4) val=(0.22,0.24,0.27,0.29) axis=secondary color=$accent labels
        [end]
        [obj title link="https://example.com/qor"]
        [group proof title cards[1] cards[2] cards[3] cards[4]]
        [animate visual emphasis=pulse dur=0.25s on=click]

        == Decisions
        [section Decisions]
        [background fill=#FFFFFF]
        [transition wipe dir=left dur=0.30s]
        !B3 18x2 [.title] | Decisions and next actions
        !B6 18x4 [list .body bullet=disc] | Approve regional expansion | Keep automation investment flat | Publish customer-ready proof points
        !B12 12x1 [line stroke=#CBD5E1 weight=1]
        !B14 16x2 [.caption] | Source: Q2 operating review and renewal cohort analysis.
        !T4 6x6 [image asset=logo fit=contain alt="Logo mark"]

        == Appendix
        [section Appendix]
        [state hidden]
        [notes "Keep this slide hidden unless someone asks for implementation details."]
        !B3 14x2 [.title] | Appendix
        !B6 12x2 [text .subtitle] | "  Supporting detail and follow-up links  "
        !B10 14x3 [list .body bullet=disc] | Security rollout checklist | Regional assumptions | Open vendor items
        [x document direction=rtl]
        """;
    await WriteDeckAsync(tools, jsonOptions, canonicalPresentationPath, canonicalDeckDoc);

    var externalPresentationPath = Path.Combine(workspaceDirectory, "presentations", "imported-external.pptx");
    var externalDeckDoc = """
        = External Import Demo

        == Overview
        [notes "Use this presentation to demo best-effort PowerPoint import."]
        [transition fade dur=0.35s]
        @title | External Import Demo
        @body [text .body] | This slide keeps notes, a transition, and a visible table.
        [table Risks at=B8 size=18x5 header banded]
        | Risk | Owner | Status |
        | --- | --- | --- |
        | Vendor delay | Ops | Open |
        [end]

        == Appendix
        [state hidden]
        [transition wipe dir=left dur=0.30s]
        @title | Appendix
        @body [text .body] | This hidden slide should be detected by the best-effort importer.
        """;
    await WriteDeckAsync(tools, jsonOptions, externalPresentationPath, externalDeckDoc);
    StripEmbeddedDeckDocPayload(externalPresentationPath);
}

static async Task<string?> ReadUserPromptAsync(string samplePromptPath)
{
    var input = Console.ReadLine();
    if (input is null)
    {
        return null;
    }

    var trimmed = input.Trim();
    if (trimmed.Length == 0)
    {
        return string.Empty;
    }

    if (string.Equals(trimmed, "/paste", StringComparison.OrdinalIgnoreCase)
        || string.Equals(trimmed, "/multiline", StringComparison.OrdinalIgnoreCase))
    {
        return await ReadMultilinePromptAsync();
    }

    if (ShouldUseSamplePrompt(trimmed))
    {
        return await LoadSamplePromptAsync(samplePromptPath);
    }

    return input;
}

static async Task<string> ReadMultilinePromptAsync()
{
    Console.WriteLine("Paste your prompt. End with a line containing only ::end.");

    List<string> lines = [];
    while (true)
    {
        var line = Console.ReadLine();
        if (line is null)
        {
            break;
        }

        if (string.Equals(line.Trim(), "::end", StringComparison.Ordinal))
        {
            break;
        }

        lines.Add(line);
    }

    return await Task.FromResult(string.Join(Environment.NewLine, lines));
}

static bool ShouldUseSamplePrompt(string input)
{
    return string.Equals(input, "/prompt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "/sampleprompt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "sample prompt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "use sample prompt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "load sample prompt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input, "run sample prompt", StringComparison.OrdinalIgnoreCase);
}

static async Task<string> LoadSamplePromptAsync(string samplePromptPath)
{
    if (!File.Exists(samplePromptPath))
    {
        throw new InvalidOperationException($"Sample prompt file was not found at '{samplePromptPath}'.");
    }

    var markdown = await File.ReadAllTextAsync(samplePromptPath);
    var extractedPrompt = ExtractFirstFencedBlock(markdown) ?? markdown;
    var prompt = extractedPrompt.Trim();

    if (prompt.Length == 0)
    {
        throw new InvalidOperationException("Sample prompt file is empty.");
    }

    Console.WriteLine($"Loaded sample prompt from {samplePromptPath}.");
    return prompt;
}

static string? ExtractFirstFencedBlock(string markdown)
{
    const string fence = "```";
    var start = markdown.IndexOf(fence, StringComparison.Ordinal);
    if (start < 0)
    {
        return null;
    }

    var firstLineEnd = markdown.IndexOf('\n', start);
    if (firstLineEnd < 0)
    {
        return null;
    }

    var contentStart = firstLineEnd + 1;
    var end = markdown.IndexOf(fence, contentStart, StringComparison.Ordinal);
    if (end < 0)
    {
        return null;
    }

    return markdown[contentStart..end];
}

static async Task RegisterAssetAsync(
    IReadOnlyList<AIFunction> tools,
    JsonSerializerOptions jsonOptions,
    string sourceReference,
    string assetPath,
    string description)
{
    var result = await InvokeAsync<DeckAssetCreateToolResult>(
        tools,
        jsonOptions,
        "deck_asset_create",
        CreateArguments(new
        {
            source_reference = sourceReference,
            asset_path = assetPath,
            description = description,
        }));

    if (!result.Success)
    {
        throw new InvalidOperationException($"Failed to register asset '{assetPath}': {result.Message}");
    }
}

static async Task WriteDeckAsync(
    IReadOnlyList<AIFunction> tools,
    JsonSerializerOptions jsonOptions,
    string deckReference,
    string content)
{
    var result = await InvokeAsync<DeckWriteFileToolResult>(
        tools,
        jsonOptions,
        "deck_write_file",
        CreateArguments(new
        {
            deck_reference = deckReference,
            content = content,
        }));

    if (!result.Success)
    {
        throw new InvalidOperationException($"Failed to write '{deckReference}': {result.Message}");
    }
}

static void StripEmbeddedDeckDocPayload(string presentationPath)
{
    using var presentation = PresentationDocument.Open(presentationPath, true);
    var presentationPart = presentation.PresentationPart
        ?? throw new InvalidOperationException("The generated presentation is missing its PresentationPart.");

    // The seeded external-import demo starts as a tool-authored sample, so removing its custom XML parts is enough
    // to simulate an ordinary third-party .pptx that no longer carries embedded canonical DeckDoc.
    foreach (var customXmlPart in presentationPart.CustomXmlParts.ToList())
    {
        presentationPart.DeletePart(customXmlPart);
    }
}

static async Task<T> InvokeAsync<T>(
    IReadOnlyList<AIFunction> functions,
    JsonSerializerOptions jsonOptions,
    string name,
    AIFunctionArguments arguments)
{
    var function = functions.Single(candidate => candidate.Name == name);
    var result = await function.InvokeAsync(arguments);
    return result switch
    {
        JsonElement json => json.Deserialize<T>(jsonOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize result for {name}."),
        T typed => typed,
        _ => throw new InvalidOperationException($"Unexpected result type '{result?.GetType().FullName ?? "null"}' for {name}."),
    };
}

static AIFunctionArguments CreateArguments(object values)
{
    var arguments = new AIFunctionArguments();
    foreach (var property in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
    {
        arguments[property.Name] = property.GetValue(values);
    }

    return arguments;
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

    GoogleGenAIClient client = useVertexAi
        ? new GoogleGenAIClient(
            vertexAI: true,
            project: GetSetting(configuration, "GoogleGenAI:Project"),
            location: GetSetting(configuration, "GoogleGenAI:Location"),
            httpOptions: httpOptions)
        : new GoogleGenAIClient(
            vertexAI: false,
            apiKey: GetSetting(configuration, "GoogleGenAI:ApiKey"),
            httpOptions: httpOptions);

    return client.AsIChatClient(model);
}

static PowerPointDeckToolSetOptions CreateToolOptions(IConfiguration configuration, string workspaceDirectory)
{
    return new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = workspaceDirectory,
        MaxReadLines = 8_000,
        MaxReadSlides = 20,
        LogContentParameters = true,
        TemplateStore = PowerPointDeckTemplates.CreateDefaultStore(),
        M365 = GetBool(configuration, "M365:Enabled", defaultValue: false)
            ? new PowerPointDeckM365Options
            {
                Credential = CreateM365Credential(configuration),
                Scopes = ParseScopes(GetOptionalSetting(configuration, "M365:Scopes")),
            }
            : null,
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

static byte[] HeroPngBytes() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAQAAAACQCAYAAADnY7WRAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAUHSURBVHhe7doxrhRHFIZRduDQe2ApBCyAdRAQsg0CIpaBSFgGImAZCFkaa56N0WNuddetGpCl/wQnsaqmBqvv1z0NT/748+kFyPTk5/8A5BAACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJQNerj5ev3/567PO7y7Of14109ldrN7x/9WvOKD93wcsPt5/9K87hBwHoqgZmNMCVzv5q7YZygO54xqe3L24//8zK+aP/X7QJQFd1wXYuyM7+au2GXx2Aq/kIvLi8+Xy7v2P+LEYEoKsamNEAVzr7q7UbfkcAvn77cnnzvDjnkf3h/8+H18XnM0sAuqqBGQ1wpbO/s3bV8hkHQ3wylIe/9cu9B2d5EtgiAF3LA7Owv7N21dYZo8H8eHl5s/bgvLM93z1/d/l0s+9q5qmDigB0VRfw9MA093fWrto9o9o/HMjXl/c3a68mhv+7UQTKJwfOCEBXdcHvDsxof2ftqt0zBgPZed9Qrj3w7O2Xm88YR4cjAtBVXcSdgens76xdtXtGIwDlb//OWSdnehfQJwBduwPT2d9Zu2r3jGp/eTeuH//Xhnbw7qHzvXkgAF3VBd+58Dr7q7WLqjvy8IzR97nRGMTBXXv4vU6UTxOddwk8EICuamB2VQNz57OGg1adMfo+j9R39Kvyrl6dUz4pzPEe4D4EoKu8kDeNBu6OZ7UCsKP1Z9kY2Ht/XigB6CovvE2toVnzWwIw+nMMz9kY2Ht/XigB6CovvE2jwanOGq1dVZ2xYBiYw3M2BvbenxdKALqqC68zlJ39nbWrqjMmlb/1R8pz1gfWO4D7EICu6kLuDGVnf2ftqqkzBm/7H0y+efe3AP9LAtA1NTAHOvs7a1c1zqjvulczd97B3xos/RPeQZAG35sxAehqDEyps7+zdlXzjPrOe3V+9633nu+7MXiaaP0k4YEAdDUH5kZnf2ftqvYZgzv51dndvDprYXDrkMw8hfAzAeiqLuLDgdnY31m7auWMas+/jn/Tj+LRGN7R2WffmZIAdFUXYOfi6+zvrF21eMb4fcDxI/1430QEqu86u5eSAHRVF+HEwCzt76xdtXzG4EXc1clPgfoR/ujs0ZPDP7o/IfhBALqWB2Zhf7V2R3VOdUa1rjJ4GXd1/FPgIB5dJ7HhmAB07QxMd3+1dkd1TnVGtW5g+Eg/8RmHTwIT3Pn3CUDX5sC09ldrd1TnVGdU64bGd/OpAT14ihg7fs/APAHo2h2Yzv5q7Y7qnOqMat2R4RD3Xs6dPRFMBYUWAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABPsbK3lWYnEUGUgAAAAASUVORK5CYII=");

static byte[] LogoPngBytes() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAQAAAACQCAYAAADnY7WRAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAASzSURBVHhe7dpBbtQ8HMZhbvAtOQOchQVrLoLEjovAhoOAxEGAiwzqRwtt8ndip55G8D6P5A3qxONR/HOm5dkFiPVs+Q9ADgGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgADPl6efv8xeW/5y/vjTeXD9+XPzff53fLeR+Ot1+Wr5jgy/vVPMsxe95T1hlMAIY8cQA6NuBqvP50+ba8zpBqjT3j/eXz8lK9TlknNwRgSLU5rhGAH5cPr5fzjI1XH38sL7rr28c3q+uMjrF5z1knfwjAkKcIwOM3xe/x7uvy4k17j94jo29TnrNOHhKAIdcPwOZGLG/07Y3Usxm3T/7Go/33T5dXq8/ibux/JmeskzUBGHLlADS/Czc24X3NDbnz/o6+7lYzHuUmvnXGOikJwJBrBqC6duemuNPaHM3N2DpVB+Zsnuata5yxTloEYEh1804KQONUHP2zV30iN97jpDkfbMi9TThpzqF10iQAQ64XgPIUPfKnrsbpWH1HnjbngGlzDqyTNgEYcq0AVNc9ejM3HutXm2zmnL1mztm7TrYIwJDqBp4QgMZpNvpYfKc8ZZffsSfP2WXynF3rZJMADLlSAMrvxcev2/X9ePKcXSbP2bVONgnAkL8jAF3X6/mZpcYJ3hqrk/3InFtmXy+QAAwRAAH4twjAEAEQgH+LAAz5OwLQ9d24sZlXm/a+xmtaY3WtM9bJJgEYcqUANDbWagN16vrt+NQ5q8+luNbUOTvXySYBGFLd6BMCUF6343/VlXr/Pt74uUNz1u9/vbHrnzs2Z+P9r9bJFgEYUt3AMwIw8TRrnLLVf7apH6EPzFl+LlUAzlknbQIwpLrR5wSg/n48fkPXG6zxHqdtoupzqQNwyjppEoAh1Y0+66arrj14/cbm2nosnrKRGvOWAThpndQEYEh18w7cuDvqR/LOOVqbYu+1jaeA3dfdqgPya9QBOGmdlARgyHUDcGNrQ9UnXPWe/oyeR+v2hmzNufM+b0crADc2X1/O+fh1siYAQ7ZvwpHRvmEbv90+MgZ+u74ZgYNjKwBnrZOHBGDIUwTgl80TsmPsXb/UfLweHOUJXjtlnfwmAEOeLgD/a34/3xoH/qT2wCNO5qMn8Snr5IYADHniANyzd1KOXq9Lx8acPe8p6wwmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYAIAwQQAggkABBMACCYAEEwAIJgAQDABgGACAMEEAIIJAAQTAAgmABBMACCYAEAwAYBgAgDBBACCCQAEEwAIJgAQTAAgmABAMAGAYD8BAvtr4yAyOmgAAAAASUVORK5CYII=");

static byte[] BadgePngBytes() => Convert.FromBase64String(
    "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");
