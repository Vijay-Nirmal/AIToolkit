namespace AIToolkit.Tools.Web.DuckDuckGo;

/// <summary>
/// Configures how <see cref="DuckDuckGoWebSearchProvider"/> reads DuckDuckGo's public HTML results page.
/// </summary>
/// <remarks>
/// DuckDuckGo in this package is an HTML-scraping provider rather than an official JSON API integration, so endpoint
/// stability and the user agent both materially influence compatibility.
/// </remarks>
/// <seealso cref="DuckDuckGoWebSearchProvider"/>
/// <seealso cref="DuckDuckGoWebTools"/>
public sealed class DuckDuckGoWebSearchOptions
{
    /// <summary>
    /// Gets or sets the DuckDuckGo HTML search endpoint.
    /// </summary>
    /// <remarks>
    /// The default points at DuckDuckGo's HTML results page, which is parsed with XPath selectors.
    /// </remarks>
    public string Endpoint { get; init; } = "https://html.duckduckgo.com/html/";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    /// <remarks>
    /// The provider applies this cap after parsing the HTML result cards.
    /// </remarks>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the user agent used for DuckDuckGo search requests.
    /// </summary>
    /// <remarks>
    /// Because this provider consumes the HTML site rather than a public API, a stable user agent helps reduce the
    /// chance of the request being treated as suspicious automation.
    /// </remarks>
    public string UserAgent { get; init; } = "AIToolkit.Tools.Web.DuckDuckGo/0.1";
}
