namespace AIToolkit.Tools.Web.Tavily;

/// <summary>
/// Configures the Tavily Search provider.
/// </summary>
public sealed class TavilyWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Tavily API key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Tavily endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.tavily.com/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    public int MaxResults { get; init; } = 5;

    /// <summary>
    /// Gets or sets the Tavily search depth.
    /// </summary>
    public string SearchDepth { get; init; } = "basic";

    /// <summary>
    /// Gets or sets the Tavily topic.
    /// </summary>
    public string Topic { get; init; } = "general";

    /// <summary>
    /// Gets or sets a value indicating whether Tavily should include a provider-generated answer.
    /// </summary>
    public bool IncludeAnswer { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Tavily should include raw markdown content.
    /// </summary>
    public bool IncludeRawContent { get; init; }

    /// <summary>
    /// Gets or sets the optional country boost value.
    /// </summary>
    public string? Country { get; init; }
}