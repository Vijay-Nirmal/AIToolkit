namespace AIToolkit.Tools.Web.Bing;

/// <summary>
/// Configures how <see cref="BingWebSearchProvider"/> calls the Bing Web Search REST API.
/// </summary>
/// <remarks>
/// Bing accepts market and safe-search settings as request parameters rather than requiring them to be embedded in the
/// query text. Domain filters are still expressed through <c>site:</c> operators when the shared
/// <see cref="WebSearchRequest"/> contract includes them.
/// </remarks>
/// <seealso cref="BingWebSearchProvider"/>
/// <seealso cref="BingWebTools"/>
public sealed class BingWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Bing subscription key sent as the <c>Ocp-Apim-Subscription-Key</c> header.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Bing search endpoint.
    /// </summary>
    /// <remarks>
    /// Override this when routing through a proxy or a Bing-compatible endpoint. The default targets Bing Web Search
    /// v7.
    /// </remarks>
    public string Endpoint { get; init; } = "https://api.bing.microsoft.com/v7.0/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    /// <remarks>
    /// The provider clamps each request to Bing's per-call maximum of 50 results.
    /// </remarks>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional market code such as <c>en-US</c>.
    /// </summary>
    /// <remarks>
    /// Bing uses the market to localize ranking and snippets. Leave this unset to use Bing's default market behavior.
    /// </remarks>
    public string? Market { get; init; }

    /// <summary>
    /// Gets or sets the Bing safe-search mode.
    /// </summary>
    /// <remarks>
    /// Common values include <c>Off</c>, <c>Moderate</c>, and <c>Strict</c>.
    /// </remarks>
    public string SafeSearch { get; init; } = "Moderate";
}
