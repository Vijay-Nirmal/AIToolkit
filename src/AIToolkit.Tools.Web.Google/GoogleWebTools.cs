using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Google;

/// <summary>
/// Creates generic <c>web_*</c> tools backed by Google Custom Search.
/// </summary>
public static class GoogleWebTools
{
    public static IReadOnlyList<AIFunction> CreateFunctions(
        GoogleWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    public static AIFunction CreateSearchFunction(
        GoogleWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    public static GoogleWebSearchProvider CreateSearchProvider(
        GoogleWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}