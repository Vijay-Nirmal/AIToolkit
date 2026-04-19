using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Tavily;

/// <summary>
/// Creates shared <c>web_*</c> tools backed by the Tavily Search API.
/// </summary>
/// <remarks>
/// This package plugs <see cref="TavilyWebSearchProvider"/> into the shared <see cref="WebTools"/> surface while the
/// common package continues to supply the default fetch implementation.
/// </remarks>
/// <seealso cref="TavilyWebSearchProvider"/>
/// <seealso cref="WebTools"/>
public static class TavilyWebTools
{
    /// <summary>
    /// Creates both <c>web_fetch</c> and <c>web_search</c> using Tavily for search operations.
    /// </summary>
    /// <param name="searchOptions">The Tavily-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="contentFetcher">An optional shared fetcher override.</param>
    /// <param name="httpClient">An optional HTTP client override for Tavily API calls.</param>
    /// <returns>The shared web tool set wired to <see cref="TavilyWebSearchProvider"/>.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        TavilyWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    /// <summary>
    /// Creates only the shared <c>web_search</c> function using Tavily as the search backend.
    /// </summary>
    /// <param name="searchOptions">The Tavily-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="httpClient">An optional HTTP client override for Tavily API calls.</param>
    /// <returns>An AI function backed by <see cref="TavilyWebSearchProvider"/>.</returns>
    public static AIFunction CreateSearchFunction(
        TavilyWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    /// <summary>
    /// Creates the reusable Tavily search provider instance.
    /// </summary>
    /// <param name="searchOptions">The Tavily-specific search configuration.</param>
    /// <param name="httpClient">An optional HTTP client override for Tavily API calls.</param>
    /// <returns>A configured <see cref="TavilyWebSearchProvider"/> instance.</returns>
    public static TavilyWebSearchProvider CreateSearchProvider(
        TavilyWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}
