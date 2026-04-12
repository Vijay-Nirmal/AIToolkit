namespace AIToolkit.Tools.Web.Brave;

/// <summary>
/// Configures the Brave Web Search provider.
/// </summary>
public sealed class BraveWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Brave API key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Brave endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.search.brave.com/res/v1/web/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional country code.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Gets or sets the optional search language code.
    /// </summary>
    public string? SearchLanguage { get; init; }

    /// <summary>
    /// Gets or sets the Brave safe-search mode.
    /// </summary>
    public string SafeSearch { get; init; } = "moderate";

    /// <summary>
    /// Gets or sets a value indicating whether extra snippets should be requested.
    /// </summary>
    public bool ExtraSnippets { get; init; }
}