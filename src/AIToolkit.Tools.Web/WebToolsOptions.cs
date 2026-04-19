namespace AIToolkit.Tools.Web;

/// <summary>
/// Configures the shared limits and transport behavior for the generic web tools created by <see cref="WebTools"/>.
/// </summary>
/// <remarks>
/// Some options apply only to the shared <c>web_fetch</c> implementation, while others are enforced by
/// <see cref="WebToolService"/> after a provider returns results. Provider-specific search packages can still impose
/// tighter server-side limits.
/// </remarks>
public sealed class WebToolsOptions
{
    /// <summary>
    /// Gets or sets the maximum number of characters returned from <c>web_fetch</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="DefaultWebContentFetcher"/> uses this value as the upper bound for both full-document responses and
    /// prompt-based section selection.
    /// </remarks>
    public int MaxFetchCharacters { get; init; } = 100_000;

    /// <summary>
    /// Gets or sets the maximum number of results returned from <c>web_search</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="WebToolService"/> clamps requested result counts to this value before applying final host filtering.
    /// Individual providers may enforce smaller hard limits.
    /// </remarks>
    public int MaxSearchResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of response bytes read from one HTTP response.
    /// </summary>
    /// <remarks>
    /// This cap is enforced by <see cref="DefaultWebContentFetcher"/> before content normalization to protect the host
    /// from unexpectedly large payloads.
    /// </remarks>
    public int MaxResponseBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the per-request timeout, in seconds, applied by the default fetcher.
    /// </summary>
    /// <remarks>
    /// Provider-specific search packages manage their own <see cref="HttpClient"/> usage and do not currently consume
    /// this value directly.
    /// </remarks>
    public int RequestTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets or sets the maximum number of same-host redirects followed automatically.
    /// </summary>
    /// <remarks>
    /// Cross-host redirects are never followed automatically; they are surfaced back to the caller for explicit
    /// confirmation.
    /// </remarks>
    public int MaxRedirects { get; init; } = 10;

    /// <summary>
    /// Gets or sets the number of minutes a successful fetch is kept in the in-memory cache.
    /// </summary>
    public int CacheDurationMinutes { get; init; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether plain HTTP URLs should be upgraded to HTTPS before fetching.
    /// </summary>
    /// <remarks>
    /// The upgrade happens before the private-network safety checks and before any HTTP request is sent.
    /// </remarks>
    public bool UpgradeHttpToHttps { get; init; } = true;

    /// <summary>
    /// Gets or sets the user agent sent by the default web fetcher.
    /// </summary>
    public string UserAgent { get; init; } = "AIToolkit.Tools.Web/0.1 (+https://github.com/your-org/AIToolkit)";
}
