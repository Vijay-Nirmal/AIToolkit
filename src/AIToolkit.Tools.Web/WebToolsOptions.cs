namespace AIToolkit.Tools.Web;

/// <summary>
/// Configures the generic web tools created by <see cref="WebTools"/>.
/// </summary>
public sealed class WebToolsOptions
{
    /// <summary>
    /// Gets or sets the maximum number of characters returned from <c>web_fetch</c>.
    /// </summary>
    public int MaxFetchCharacters { get; init; } = 100_000;

    /// <summary>
    /// Gets or sets the maximum number of results returned from <c>web_search</c>.
    /// </summary>
    public int MaxSearchResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of response bytes read from one HTTP response.
    /// </summary>
    public int MaxResponseBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the HTTP timeout applied to fetch and search requests.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets or sets the maximum number of same-host redirects followed automatically.
    /// </summary>
    public int MaxRedirects { get; init; } = 10;

    /// <summary>
    /// Gets or sets the number of minutes a successful fetch is kept in the in-memory cache.
    /// </summary>
    public int CacheDurationMinutes { get; init; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether plain HTTP URLs should be upgraded to HTTPS before fetching.
    /// </summary>
    public bool UpgradeHttpToHttps { get; init; } = true;

    /// <summary>
    /// Gets or sets the user agent sent by the default web fetcher.
    /// </summary>
    public string UserAgent { get; init; } = "AIToolkit.Tools.Web/0.1 (+https://github.com/your-org/AIToolkit)";
}