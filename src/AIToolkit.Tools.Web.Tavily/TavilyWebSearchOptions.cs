namespace AIToolkit.Tools.Web.Tavily;

/// <summary>
/// Configures how <see cref="TavilyWebSearchProvider"/> calls the Tavily Search API.
/// </summary>
/// <remarks>
/// Tavily accepts domain filters and summary-generation flags directly in the JSON request body, which makes it the
/// closest provider match to the shared <see cref="WebSearchRequest"/> model.
/// </remarks>
/// <seealso cref="TavilyWebSearchProvider"/>
/// <seealso cref="TavilyWebTools"/>
public sealed class TavilyWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Tavily API key sent as a bearer token.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Tavily search endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.tavily.com/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    /// <remarks>
    /// The provider clamps each request to Tavily's documented maximum of 20 results.
    /// </remarks>
    public int MaxResults { get; init; } = 5;

    /// <summary>
    /// Gets or sets the Tavily search depth.
    /// </summary>
    /// <remarks>
    /// Common values include <c>basic</c> and <c>advanced</c>.
    /// </remarks>
    public string SearchDepth { get; init; } = "basic";

    /// <summary>
    /// Gets or sets the Tavily topic.
    /// </summary>
    /// <remarks>
    /// Common values include <c>general</c> and <c>news</c>.
    /// </remarks>
    public string Topic { get; init; } = "general";

    /// <summary>
    /// Gets or sets a value indicating whether Tavily should include a provider-generated answer.
    /// </summary>
    public bool IncludeAnswer { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Tavily should include raw markdown content.
    /// </summary>
    /// <remarks>
    /// When enabled, the provider sends Tavily's <c>include_raw_content</c> flag as <c>markdown</c>.
    /// </remarks>
    public bool IncludeRawContent { get; init; }

    /// <summary>
    /// Gets or sets the optional country boost value.
    /// </summary>
    public string? Country { get; init; }
}
