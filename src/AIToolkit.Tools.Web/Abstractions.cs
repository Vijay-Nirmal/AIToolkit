namespace AIToolkit.Tools.Web;

/// <summary>
/// Fetches content from a single public URL and normalizes it into a <see cref="WebContentFetchResponse"/>.
/// </summary>
/// <remarks>
/// Implementations are responsible for the transport-specific work needed by <c>web_fetch</c>, such as validation,
/// redirect handling, response-size limits, and format normalization. <see cref="DefaultWebContentFetcher"/> is the
/// built-in implementation used by <see cref="WebToolService"/> when no custom fetcher is supplied.
/// </remarks>
/// <seealso cref="DefaultWebContentFetcher"/>
/// <seealso cref="WebContentFetchRequest"/>
/// <seealso cref="WebContentFetchResponse"/>
public interface IWebContentFetcher
{
    /// <summary>
    /// Fetches and normalizes content from the specified URL.
    /// </summary>
    /// <param name="request">
    /// The fetch request describing the URL to retrieve, the optional relevance prompt, and any per-call character cap.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A normalized fetch response that captures HTTP metadata and transformed content.</returns>
    /// <remarks>
    /// Implementations may cache normalized responses, but they should still honor the caller's
    /// <see cref="WebContentFetchRequest.MaxCharacters"/> and <see cref="WebContentFetchRequest.Prompt"/> values when
    /// shaping the final output.
    /// </remarks>
    ValueTask<WebContentFetchResponse> FetchAsync(
        WebContentFetchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Searches the web through a provider-specific backend and returns normalized search hits.
/// </summary>
/// <remarks>
/// Implementations translate the shared <see cref="WebSearchRequest"/> contract into provider-specific HTTP calls and
/// response parsing. Some providers can apply domain filters server-side, while others rely on
/// <see cref="WebToolService"/> to perform a final host-based pass after the provider response is normalized.
/// </remarks>
/// <seealso cref="WebSearchRequest"/>
/// <seealso cref="WebSearchResponse"/>
public interface IWebSearchProvider
{
    /// <summary>
    /// Gets the stable provider identifier surfaced to the model and copied into
    /// <see cref="WebSearchResponse.Provider"/>.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Executes a search request and returns normalized search results.
    /// </summary>
    /// <param name="request">The search request to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A normalized search response containing provider metadata and normalized hits.</returns>
    /// <remarks>
    /// Implementations should keep provider-specific fields internal and surface a consistent
    /// <see cref="WebSearchResult"/> shape. When possible, providers should push domain filters down into the remote
    /// query to reduce irrelevant results before <see cref="WebToolService"/> applies its final filtering step.
    /// </remarks>
    ValueTask<WebSearchResponse> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a request to fetch and normalize content from a URL.
/// </summary>
/// <remarks>
/// This record is the shared fetch contract used by <see cref="WebToolService"/>, <see cref="IWebContentFetcher"/>,
/// and <see cref="DefaultWebContentFetcher"/>.
/// </remarks>
/// <param name="Url">The fully-qualified URL to fetch.</param>
/// <param name="Prompt">
/// An optional relevance hint used to select the most useful sections of the normalized content before returning it.
/// </param>
/// <param name="MaxCharacters">
/// An optional per-call output cap. When omitted, the active <see cref="WebToolsOptions.MaxFetchCharacters"/> limit is
/// used.
/// </param>
public sealed record WebContentFetchRequest(
    string Url,
    string? Prompt = null,
    int? MaxCharacters = null);

/// <summary>
/// Represents a provider-agnostic request to search the web.
/// </summary>
/// <remarks>
/// Providers may translate domain filters into provider-specific query syntax, and
/// <see cref="WebToolService"/> performs a final normalized host filter after the provider response is returned.
/// </remarks>
/// <param name="Query">The search query.</param>
/// <param name="AllowedDomains">
/// Optional domains that results must come from. Values may be raw hosts or full URLs; shared tooling normalizes both.
/// </param>
/// <param name="BlockedDomains">Optional domains that results must not come from.</param>
/// <param name="MaxResults">
/// An optional per-call result cap. The final value is clamped by <see cref="WebToolsOptions.MaxSearchResults"/> and
/// any provider-specific hard limits.
/// </param>
public sealed record WebSearchRequest(
    string Query,
    string[]? AllowedDomains = null,
    string[]? BlockedDomains = null,
    int? MaxResults = null);
