using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Tavily;

/// <summary>
/// Creates generic <c>web_*</c> tools backed by the Tavily Search API.
/// </summary>
public static class TavilyWebTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        TavilyWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    public static AIFunction CreateSearchFunction(
        TavilyWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    public static TavilyWebSearchProvider CreateSearchProvider(
        TavilyWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}