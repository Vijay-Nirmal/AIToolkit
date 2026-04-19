using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Tests;

/// <summary>
/// Provides reusable helpers for invoking AI functions inside the web tool test suite.
/// </summary>
/// <remarks>
/// These helpers keep the tests focused on behavior by centralizing function registration, argument construction, and
/// JSON result deserialization.
/// </remarks>
internal static class FunctionTestUtilities
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Creates the shared <c>web_*</c> functions with deterministic limits for tests.
    /// </summary>
    /// <param name="searchProvider">The optional provider used by <c>web_search</c>.</param>
    /// <param name="contentFetcher">The optional fetcher used by <c>web_fetch</c>.</param>
    /// <returns>The configured AI function list used by the tests.</returns>
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

    /// <summary>
    /// Invokes a named function and converts the result to the expected test type.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="functions">The registered functions to search.</param>
    /// <param name="name">The function name to invoke.</param>
    /// <param name="arguments">The optional invocation arguments.</param>
    /// <returns>The typed invocation result.</returns>
    /// <exception cref="InvalidOperationException">The function result cannot be converted to <typeparamref name="T"/>.</exception>
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

    /// <summary>
    /// Converts an anonymous object's public properties into <see cref="AIFunctionArguments"/>.
    /// </summary>
    /// <param name="values">The object whose public properties should become function arguments.</param>
    /// <returns>A populated argument collection.</returns>
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
