namespace AIToolkit.Tools.Web.DuckDuckGo;

/// <summary>
/// Configures the DuckDuckGo HTML search provider.
/// </summary>
public sealed class DuckDuckGoWebSearchOptions
{
    /// <summary>
    /// Gets or sets the DuckDuckGo HTML search endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://html.duckduckgo.com/html/";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the user agent used for DuckDuckGo search requests.
    /// </summary>
    public string UserAgent { get; init; } = "AIToolkit.Tools.Web.DuckDuckGo/0.1";
}