using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.Google;

/// <summary>
/// Creates shared <c>web_*</c> tools backed by Google Custom Search.
/// </summary>
/// <remarks>
/// This package plugs <see cref="GoogleWebSearchProvider"/> into the shared <see cref="WebTools"/> surface while the
/// common package continues to supply the default fetch implementation.
/// </remarks>
/// <seealso cref="GoogleWebSearchProvider"/>
/// <seealso cref="WebTools"/>
public static class GoogleWebTools
{
    /// <summary>
    /// Creates both <c>web_fetch</c> and <c>web_search</c> using Google for search operations.
    /// </summary>
    /// <param name="searchOptions">The Google-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="contentFetcher">An optional shared fetcher override.</param>
    /// <param name="httpClient">An optional HTTP client override for Google API calls.</param>
    /// <returns>The shared web tool set wired to <see cref="GoogleWebSearchProvider"/>.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        GoogleWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    /// <summary>
    /// Creates only the shared <c>web_search</c> function using Google as the search backend.
    /// </summary>
    /// <param name="searchOptions">The Google-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="httpClient">An optional HTTP client override for Google API calls.</param>
    /// <returns>An AI function backed by <see cref="GoogleWebSearchProvider"/>.</returns>
    public static AIFunction CreateSearchFunction(
        GoogleWebSearchOptions searchOptions,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    /// <summary>
    /// Creates the reusable Google search provider instance.
    /// </summary>
    /// <param name="searchOptions">The Google-specific search configuration.</param>
    /// <param name="httpClient">An optional HTTP client override for Google API calls.</param>
    /// <returns>A configured <see cref="GoogleWebSearchProvider"/> instance.</returns>
    public static GoogleWebSearchProvider CreateSearchProvider(
        GoogleWebSearchOptions searchOptions,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}
