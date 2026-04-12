using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Tests;

internal static class FunctionTestUtilities
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<AIFunction> CreateFunctions(
        IWebSearchProvider? searchProvider = null,
        IWebContentFetcher? contentFetcher = null) =>
        WebTools.CreateFunctions(
            new WebToolsOptions
            {
                MaxFetchCharacters = 4_000,
                MaxSearchResults = 10,
                MaxResponseBytes = 128 * 1024,
            },
            searchProvider,
            contentFetcher);

    public static async Task<T> InvokeAsync<T>(IReadOnlyList<AIFunction> functions, string name, AIFunctionArguments? arguments = null)
    {
        var function = functions.Single(function => function.Name == name);
        var invocationResult = await function.InvokeAsync(arguments);
        return invocationResult switch
        {
            JsonElement json => json.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException($"Unable to deserialize {name} result."),
            T typed => typed,
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {name}.'"),
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
}