using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Bing;

/// <summary>
/// Creates generic <c>web_*</c> tools backed by the Bing Web Search API.
/// </summary>
public static class BingWebTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        BingWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    public static AIFunction CreateSearchFunction(
        BingWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    public static BingWebSearchProvider CreateSearchProvider(
        BingWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}