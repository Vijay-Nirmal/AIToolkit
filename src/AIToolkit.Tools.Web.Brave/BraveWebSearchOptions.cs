namespace AIToolkit.Tools.Web.Brave;

/// <summary>
/// Configures how <see cref="BraveWebSearchProvider"/> calls the Brave Search API.
/// </summary>
/// <remarks>
/// Brave exposes localization and safe-search settings as first-class query parameters and can optionally return extra
/// snippets that this package folds into the normalized snippet text.
/// </remarks>
/// <seealso cref="BraveWebSearchProvider"/>
/// <seealso cref="BraveWebTools"/>
public sealed class BraveWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Brave API key sent as the <c>X-Subscription-Token</c> header.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Brave search endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.search.brave.com/res/v1/web/search";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    /// <remarks>
    /// The provider clamps each request to Brave's per-call maximum of 20 results.
    /// </remarks>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional country code used for ranking and localization.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Gets or sets the optional search language code.
    /// </summary>
    /// <remarks>
    /// This maps to Brave's <c>search_lang</c> parameter.
    /// </remarks>
    public string? SearchLanguage { get; init; }

    /// <summary>
    /// Gets or sets the Brave safe-search mode.
    /// </summary>
    /// <remarks>
    /// Common values include <c>off</c>, <c>moderate</c>, and <c>strict</c>.
    /// </remarks>
    public string SafeSearch { get; init; } = "moderate";

    /// <summary>
    /// Gets or sets a value indicating whether extra snippets should be requested.
    /// </summary>
    /// <remarks>
    /// When enabled, <see cref="BraveWebSearchProvider"/> merges the extra snippets into the normalized
    /// <see cref="WebSearchResult.Snippet"/> text.
    /// </remarks>
    public bool ExtraSnippets { get; init; }
}
