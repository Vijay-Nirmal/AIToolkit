using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Workbook.GoogleSheets.Tests;

internal static class GoogleSheetsFunctionTestUtilities
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<AIFunction> CreateFunctions(
        FakeGoogleSheetsWorkspaceClient workspace,
        string? workingDirectory = null,
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var sourceOptions = handlerOptions ?? new GoogleSheetsWorkbookHandlerOptions();
        var effectiveHandlerOptions = new GoogleSheetsWorkbookHandlerOptions
        {
            PreferManagedWorkbookDocPayload = sourceOptions.PreferManagedWorkbookDocPayload,
            PreferEmbeddedWorkbookDoc = sourceOptions.PreferEmbeddedWorkbookDoc,
            EnableBestEffortImport = sourceOptions.EnableBestEffortImport,
            Workspace = new GoogleSheetsWorkspaceOptions
            {
                Client = workspace,
            },
        };

        return GoogleSheetsWorkbookTools.CreateFunctions(
            effectiveHandlerOptions,
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });
    }

    public static async Task<T> InvokeAsync<T>(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(function => function.Name == name);
        var invocationResult = await function.InvokeAsync(arguments).ConfigureAwait(false);
        return invocationResult switch
        {
            JsonElement json => json.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException($"Unable to deserialize {name} result."),
            T typed => typed,
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}."),
        };
    }

    public static async Task<IReadOnlyList<AIContent>> InvokeContentAsync(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(candidate => candidate.Name == name);
        var invocationResult = await function.InvokeAsync(arguments).ConfigureAwait(false);
        return invocationResult switch
        {
            IEnumerable<AIContent> parts => parts.ToArray(),
            AIContent part => [part],
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}."),
        };
    }

    public static AIFunctionArguments CreateArguments(object values)
    {
        var arguments = new AIFunctionArguments();
        foreach (var property in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            arguments[property.Name] = property.GetValue(values);
        }

        return arguments;
    }

    public static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Workbook.GoogleSheets.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string ReadWorkbookDocText(IReadOnlyList<AIContent> contents)
    {
        var numberedText = contents
            .OfType<TextContent>()
            .Select(static content => content.Text)
            .Last(static text => text.Contains('\t', StringComparison.Ordinal));

        return string.Join(
            "\n",
            numberedText
                .Split('\n')
                .Select(static line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]));
    }

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
