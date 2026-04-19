using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Brave;

/// <summary>
/// Creates shared <c>web_*</c> tools backed by the Brave Search API.
/// </summary>
/// <remarks>
/// This package plugs <see cref="BraveWebSearchProvider"/> into the shared <see cref="WebTools"/> surface while
/// leaving fetch behavior to the common package unless a custom <see cref="IWebContentFetcher"/> is supplied.
/// </remarks>
/// <seealso cref="BraveWebSearchProvider"/>
/// <seealso cref="WebTools"/>
public static class BraveWebTools
{
    /// <summary>
    /// Creates both <c>web_fetch</c> and <c>web_search</c> using Brave for search operations.
    /// </summary>
    /// <param name="searchOptions">The Brave-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="contentFetcher">An optional shared fetcher override.</param>
    /// <param name="httpClient">An optional HTTP client override for Brave API calls.</param>
    /// <returns>The shared web tool set wired to <see cref="BraveWebSearchProvider"/>.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        BraveWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    /// <summary>
    /// Creates only the shared <c>web_search</c> function using Brave as the search backend.
    /// </summary>
    /// <param name="searchOptions">The Brave-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="httpClient">An optional HTTP client override for Brave API calls.</param>
    /// <returns>An AI function backed by <see cref="BraveWebSearchProvider"/>.</returns>
    public static AIFunction CreateSearchFunction(
        BraveWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    /// <summary>
    /// Creates the reusable Brave search provider instance.
    /// </summary>
    /// <param name="searchOptions">The Brave-specific search configuration.</param>
    /// <param name="httpClient">An optional HTTP client override for Brave API calls.</param>
    /// <returns>A configured <see cref="BraveWebSearchProvider"/> instance.</returns>
    public static BraveWebSearchProvider CreateSearchProvider(
        BraveWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}
