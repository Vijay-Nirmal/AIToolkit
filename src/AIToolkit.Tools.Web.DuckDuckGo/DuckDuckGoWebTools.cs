using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web.DuckDuckGo;

/// <summary>
/// Creates shared <c>web_*</c> tools backed by DuckDuckGo HTML search.
/// </summary>
/// <remarks>
/// The DuckDuckGo package contributes only the search provider. The shared fetch implementation still comes from
/// <see cref="WebTools"/> unless you pass a custom <see cref="IWebContentFetcher"/>.
/// </remarks>
/// <seealso cref="DuckDuckGoWebSearchProvider"/>
/// <seealso cref="WebTools"/>
public static class DuckDuckGoWebTools
{
    /// <summary>
    /// Creates both <c>web_fetch</c> and <c>web_search</c> using DuckDuckGo HTML search for search operations.
    /// </summary>
    /// <param name="searchOptions">The DuckDuckGo-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="contentFetcher">An optional shared fetcher override.</param>
    /// <param name="httpClient">An optional HTTP client override for DuckDuckGo requests.</param>
    /// <returns>The shared web tool set wired to <see cref="DuckDuckGoWebSearchProvider"/>.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateFunctions(options, CreateSearchProvider(searchOptions, httpClient), contentFetcher);

    /// <summary>
    /// Creates only the shared <c>web_search</c> function using DuckDuckGo HTML search as the backend.
    /// </summary>
    /// <param name="searchOptions">The DuckDuckGo-specific search configuration.</param>
    /// <param name="options">The shared web tool options.</param>
    /// <param name="httpClient">An optional HTTP client override for DuckDuckGo requests.</param>
    /// <returns>An AI function backed by <see cref="DuckDuckGoWebSearchProvider"/>.</returns>
    public static AIFunction CreateSearchFunction(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        WebToolsOptions? options = null,
        HttpClient? httpClient = null) =>
        WebTools.CreateSearchFunction(options, CreateSearchProvider(searchOptions, httpClient));

    /// <summary>
    /// Creates the reusable DuckDuckGo search provider instance.
    /// </summary>
    /// <param name="searchOptions">The DuckDuckGo-specific search configuration.</param>
    /// <param name="httpClient">An optional HTTP client override for DuckDuckGo requests.</param>
    /// <returns>A configured <see cref="DuckDuckGoWebSearchProvider"/> instance.</returns>
    public static DuckDuckGoWebSearchProvider CreateSearchProvider(
        DuckDuckGoWebSearchOptions? searchOptions = null,
        HttpClient? httpClient = null) =>
        new(searchOptions, httpClient);
}
