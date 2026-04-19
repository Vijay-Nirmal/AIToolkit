namespace AIToolkit.Tools.Web.Google;

/// <summary>
/// Configures how <see cref="GoogleWebSearchProvider"/> calls Google Programmable Search Engine.
/// </summary>
/// <remarks>
/// Google requires both an API key and a search engine identifier. Localization options map directly to the provider's
/// <c>gl</c>, <c>lr</c>, and <c>hl</c> parameters instead of being encoded into the query string.
/// </remarks>
/// <seealso cref="GoogleWebSearchProvider"/>
/// <seealso cref="GoogleWebTools"/>
public sealed class GoogleWebSearchOptions
{
    /// <summary>
    /// Gets or sets the Google API key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Programmable Search Engine identifier.
    /// </summary>
    public string SearchEngineId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the Google search endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://customsearch.googleapis.com/customsearch/v1";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    /// <remarks>
    /// Google Custom Search accepts at most 10 results per request.
    /// </remarks>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional country-code boost.
    /// </summary>
    /// <remarks>
    /// This maps to Google's <c>gl</c> parameter.
    /// </remarks>
    public string? CountryCode { get; init; }

    /// <summary>
    /// Gets or sets the optional Google language restriction such as <c>lang_en</c>.
    /// </summary>
    /// <remarks>
    /// This maps to Google's <c>lr</c> parameter.
    /// </remarks>
    public string? LanguageRestriction { get; init; }

    /// <summary>
    /// Gets or sets the optional interface language code.
    /// </summary>
    /// <remarks>
    /// This maps to Google's <c>hl</c> parameter.
    /// </remarks>
    public string? InterfaceLanguage { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether SafeSearch should be enabled.
    /// </summary>
    /// <remarks>
    /// The provider maps <see langword="true"/> to <c>safe=active</c> and <see langword="false"/> to <c>safe=off</c>.
    /// </remarks>
    public bool SafeSearch { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Google's duplicate-content filtering should remain enabled.
    /// </summary>
    /// <remarks>
    /// The provider maps this value to Google's <c>filter</c> parameter.
    /// </remarks>
    public bool FilterDuplicates { get; init; } = true;
}
