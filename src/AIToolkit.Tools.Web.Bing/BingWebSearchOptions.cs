namespace AIToolkit.Tools.Web.Bing;

/// <summary>
/// Configures the Bing Web Search provider.
/// </summary>
public sealed class BingWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Bing subscription key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Bing endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.bing.microsoft.com/v7.0/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional market code such as <c>en-US</c>.
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Gets or sets the Bing safe-search mode.
    /// </summary>
    public string SafeSearch { get; init; } = "Moderate";
}