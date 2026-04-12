using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.DuckDuckGo;

/// <summary>
/// Creates generic <c>web_*</c> tools backed by DuckDuckGo HTML search.
/// </summary>
public static class DuckDuckGoWebTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    public static AIFunction CreateSearchFunction(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    public static DuckDuckGoWebSearchProvider CreateSearchProvider(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}