using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Brave;

/// <summary>
/// Creates generic <c>web_*</c> tools backed by the Brave Search API.
/// </summary>
public static class BraveWebTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        BraveWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    public static AIFunction CreateSearchFunction(
        BraveWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    public static BraveWebSearchProvider CreateSearchProvider(
        BraveWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}