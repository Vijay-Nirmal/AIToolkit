using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Bing;

/// <summary>
/// Creates shared <c>web_*</c> tools backed by the Bing Web Search API.
/// </summary>
/// <remarks>
/// The Bing package contributes only the <see cref="IWebSearchProvider"/> implementation. The returned tool set still
/// uses the shared <see cref="WebTools"/> fetch implementation unless a custom <see cref="IWebContentFetcher"/> is
/// supplied.
/// </remarks>
/// <seealso cref="WebTools"/>
/// <seealso cref="BingWebSearchProvider"/>
public static class BingWebTools
{
    /// <summary>
    /// Creates both <c>web_fetch</c> and <c>web_search</c> using Bing for search operations.
    /// </summary>
    /// <param name="searchOptions">The Bing-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="contentFetcher">An optional shared fetcher override.</param>
    /// <param name="httpClient">An optional HTTP client override for Bing API calls.</param>
    /// <returns>The shared web tool set wired to <see cref="BingWebSearchProvider"/>.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        BingWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    /// <summary>
    /// Creates only the shared <c>web_search</c> function using Bing as the search backend.
    /// </summary>
    /// <param name="searchOptions">The Bing-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="httpClient">An optional HTTP client override for Bing API calls.</param>
    /// <returns>An AI function backed by <see cref="BingWebSearchProvider"/>.</returns>
    public static AIFunction CreateSearchFunction(
        BingWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    /// <summary>
    /// Creates the reusable Bing search provider instance.
    /// </summary>
    /// <param name="searchOptions">The Bing-specific search configuration.</param>
    /// <param name="httpClient">An optional HTTP client override for Bing API calls.</param>
    /// <returns>A configured <see cref="BingWebSearchProvider"/> instance.</returns>
    public static BingWebSearchProvider CreateSearchProvider(
        BingWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}
