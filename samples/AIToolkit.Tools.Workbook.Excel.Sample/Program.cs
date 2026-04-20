using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.Excel;
using Azure.Core;
using Azure.Identity;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

// To try hosted OneDrive or SharePoint Excel files in this sample:
// - set M365:Enabled=true and M365:WorkbookReference in appsettings.json or user-secrets
// - set M365:Authentication:Type to Default, DeviceCode, InteractiveBrowser, or ClientSecret
// - local workspace files continue to work even when hosted M365 support is enabled
var chatProviderName = GetOptionalSetting(configuration, "Chat:Provider") ?? "OpenAI";
var workspaceDirectory = Path.Combine(AppContext.BaseDirectory, "sample-workspace");
var hostedWorkbookReference = GetOptionalSetting(configuration, "M365:WorkbookReference");
var hostedAuthType = GetOptionalSetting(configuration, "M365:Authentication:Type") ?? "Default";
var excelHandlerOptions = CreateExcelHandlerOptions(configuration);
var workbookToolOptions = new WorkbookToolsOptions
{
    WorkingDirectory = workspaceDirectory,
    MaxReadLines = 8_000,
};

var tools = ExcelWorkbookTools.CreateFunctions(
    excelHandlerOptions,
    options: workbookToolOptions);
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

var systemPrompt = ExcelWorkbookTools.GetSystemPromptGuidance(
    $"""
    You are a workbook automation assistant for the AIToolkit.Tools.Workbook.Excel sample workspace.
    The workspace root is {workspaceDirectory}.
    This sample runs on Windows and exposes workbook read, write, edit, grep, and WorkbookDoc spec lookup tools for Excel files.
    Hosted OneDrive and SharePoint Excel workbook references are {(excelHandlerOptions.M365 is null ? "disabled" : "enabled")} for this sample.
    """,
    excelHandlerOptions,
    options: workbookToolOptions);

List<ChatMessage> chatHistory =
[
    new(ChatRole.System, systemPrompt)
];

Console.WriteLine("AIToolkit.Tools.Workbook.Excel sample agent");
Console.WriteLine($"Workspace ready: {workspaceDirectory}");
Console.WriteLine($"Configured chat provider: {chatProviderName}");
Console.WriteLine($"Hosted M365 Excel support: {(excelHandlerOptions.M365 is null ? "disabled" : "enabled")}");
if (excelHandlerOptions.M365 is not null)
{
    Console.WriteLine($"Hosted M365 auth type: {hostedAuthType}");
    Console.WriteLine("Hosted M365 references are enabled for direct workbook reads, writes, and edits.");
    Console.WriteLine("workbook_grep_search still scans only the local sample workspace unless you pass explicit workbook_references.");
    if (!string.IsNullOrWhiteSpace(hostedWorkbookReference))
    {
        Console.WriteLine($"Configured hosted workbook reference: {hostedWorkbookReference}");
    }
}

Console.WriteLine("Available tools:");
foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {tool.Name}");
}

Console.WriteLine();
Console.WriteLine("Try prompts like:");
Console.WriteLine("- Summarize workbooks/sales-summary.xlsx.");
Console.WriteLine("- Search the sample workbooks for Revenue.");
Console.WriteLine("- Explain the chart in workbooks/imported.xlsx.");
Console.WriteLine("- Update workbooks/sales-summary.xlsx so March revenue becomes 1100.");
Console.WriteLine("- Create workbooks/forecast.xlsx with a small quarterly forecast workbook.");
if (excelHandlerOptions.M365 is not null)
{
    Console.WriteLine("- Read a hosted SharePoint or OneDrive workbook by URL.");
    Console.WriteLine("- Write to an M365 drive-path reference such as m365://drives/{driveId}/root/workbooks/forecast.xlsx.");
    if (!string.IsNullOrWhiteSpace(hostedWorkbookReference))
    {
        Console.WriteLine($"- Summarize {hostedWorkbookReference}.");
        Console.WriteLine($"- Update {hostedWorkbookReference} with a short quarterly forecast sheet.");
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
    Directory.CreateDirectory(Path.Combine(workspaceDirectory, "workbooks"));

    await InvokeToolAsync(
        tools,
        "workbook_write_file",
        new
        {
            workbook_reference = Path.Combine(workspaceDirectory, "workbooks", "sales-summary.xlsx"),
            content =
            """
            = Sales Summary
            :wbdoc: 4
            :date-system: 1900
            :active: Summary

            [style hdr bold bg=#D9E2F3]

            == Summary
            [view freeze=1,1 zoom=90 grid]
            [used A1:D5]
            @A1 [.hdr] | Month | Revenue | Margin | Trend
            @A2 | January | 1200 | [fmt="0.0%"] 0.35 | blank
            @A3 | February | 1350 | [fmt="0.0%"] 0.42 | blank
            @A4 | March | 980 | [fmt="0.0%"] 0.28 | blank
            [spark D2 source=B2:C2 type=line color=#4472C4]
            [spark D3 source=B3:C3 type=line color=#4472C4]
            [spark D4 source=B4:C4 type=line color=#4472C4]
            [chart "Revenue Trend" type=column at=F2 size=480x280px]
            - series column "Revenue" cat=A2:A4 val=B2:B4
            [end]
            """,
        });

    CreateExternalWorkbook(Path.Combine(workspaceDirectory, "workbooks", "imported.xlsx"));
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

static void CreateExternalWorkbook(string path)
{
    using var spreadsheet = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
    var workbookPart = spreadsheet.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    var sheetData = new SheetData(
        new Row(
            new Cell { CellReference = "A1", DataType = CellValues.String, InlineString = new InlineString(new Text("Quarter")) },
            new Cell { CellReference = "B1", DataType = CellValues.String, InlineString = new InlineString(new Text("Revenue")) }),
        new Row(
            new Cell { CellReference = "A2", DataType = CellValues.String, InlineString = new InlineString(new Text("Q1")) },
            new Cell { CellReference = "B2", CellValue = new CellValue("1200") }),
        new Row(
            new Cell { CellReference = "A3", DataType = CellValues.String, InlineString = new InlineString(new Text("Q2")) },
            new Cell { CellReference = "B3", CellValue = new CellValue("1400") }));
    worksheetPart.Worksheet = new Worksheet(sheetData);
    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    sheets.Append(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1U,
        Name = "Imported",
    });
    workbookPart.Workbook.Save();
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

static ExcelWorkbookHandlerOptions CreateExcelHandlerOptions(IConfiguration configuration)
{
    if (!GetBool(configuration, "M365:Enabled", defaultValue: false))
    {
        return new ExcelWorkbookHandlerOptions();
    }

    return new ExcelWorkbookHandlerOptions
    {
        M365 = new ExcelWorkbookM365Options
        {
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
