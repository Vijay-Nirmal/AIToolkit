namespace AIToolkit.Tools.Web;

/// <summary>
/// Fetches and normalizes content from a single URL.
/// </summary>
public interface IWebContentFetcher
{
    /// <summary>
    /// Fetches and normalizes content from the specified URL.
    /// </summary>
    /// <param name="request">The fetch request describing the URL and optional prompt hint.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The normalized fetch response.</returns>
    ValueTask<WebContentFetchResponse> FetchAsync(
        WebContentFetchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Searches the web using a provider-specific backend.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Gets the provider name surfaced to the model and caller.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Executes a search request and returns normalized search results.
    /// </summary>
    /// <param name="request">The search request to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>The normalized search response.</returns>
    ValueTask<WebSearchResponse> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a request to fetch and normalize content from a URL.
/// </summary>
/// <param name="Url">The fully-qualified URL to fetch.</param>
/// <param name="Prompt">An optional relevance hint used to trim the returned content.</param>
/// <param name="MaxCharacters">An optional per-call output cap.</param>
public sealed record WebContentFetchRequest(
    string Url,
    string? Prompt = null,
    int? MaxCharacters = null);

/// <summary>
/// Represents a request to search the web.
/// </summary>
/// <param name="Query">The search query.</param>
/// <param name="AllowedDomains">Optional domains that results must come from.</param>
/// <param name="BlockedDomains">Optional domains that results must not come from.</param>
/// <param name="MaxResults">An optional per-call result cap.</param>
public sealed record WebSearchRequest(
    string Query,
    string[]? AllowedDomains = null,
    string[]? BlockedDomains = null,
    int? MaxResults = null);