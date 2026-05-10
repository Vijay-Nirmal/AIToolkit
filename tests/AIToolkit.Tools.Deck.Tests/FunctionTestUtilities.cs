using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Deck.Tests;

/// <summary>
/// Shared helpers for generic deck-tool tests.
/// </summary>
internal static class FunctionTestUtilities
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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

    public static IReadOnlyList<string> GetNames(IReadOnlyList<AIFunction> functions) =>
        functions.Select(static function => function.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();

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
        var directory = Path.Combine(Path.GetTempPath(), "AIToolkit.Tools.Deck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string StripNumbering(string numberedText) =>
        string.Join(
            "\n",
            numberedText
                .Split('\n', StringSplitOptions.TrimEntries)
                .Where(static line => line.Contains('\t', StringComparison.Ordinal))
                .Select(static line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]));
}
