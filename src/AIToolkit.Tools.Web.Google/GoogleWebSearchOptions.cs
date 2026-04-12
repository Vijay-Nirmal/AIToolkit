namespace AIToolkit.Tools.Web.Google;

/// <summary>
/// Configures the Google Custom Search provider.
/// </summary>
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
    /// Gets or sets the Google endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "https://customsearch.googleapis.com/customsearch/v1";

    /// <summary>
    /// Gets or sets the default provider-level result cap.
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// Gets or sets the optional country-code boost.
    /// </summary>
    public string? CountryCode { get; init; }

    /// <summary>
    /// Gets or sets the optional Google language restriction such as <c>lang_en</c>.
    /// </summary>
    public string? LanguageRestriction { get; init; }

    /// <summary>
    /// Gets or sets the optional interface language code.
    /// </summary>
    public string? InterfaceLanguage { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether SafeSearch should be enabled.
    /// </summary>
    public bool SafeSearch { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Google's duplicate-content filtering should remain enabled.
    /// </summary>
    public bool FilterDuplicates { get; init; } = true;
}