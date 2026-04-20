using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Workbook.Excel.Tests;

internal static class FunctionTestUtilities
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<AIFunction> CreateFunctions(string? workingDirectory = null, ExcelWorkbookHandlerOptions? handlerOptions = null) =>
        ExcelWorkbookTools.CreateFunctions(
            handlerOptions,
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });

    public static async Task<T> InvokeAsync<T>(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(function => function.Name == name);
        var invocationResult = await function.InvokeAsync(arguments);
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
        var invocationResult = await function.InvokeAsync(arguments);
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
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Workbook.Excel.Tests", Guid.NewGuid().ToString("N"));
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

    public static string GetRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "AIToolkit.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate the repository root from the test output directory.");
    }
}
