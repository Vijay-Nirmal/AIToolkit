using System.Text.Json.Serialization;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Represents the common success and message fields returned by web tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
/// <remarks>
/// Specialized tool results inherit from this record so callers can check success consistently before inspecting the
/// operation-specific payload.
/// </remarks>
public abstract record WebToolResult(bool Success, string? Message = null);

/// <summary>
/// Identifies the normalized content format returned by <c>web_fetch</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebContentFormat
{
    /// <summary>
    /// Markdown content, typically produced from HTML or already-markdown responses.
    /// </summary>
    Markdown,

    /// <summary>
    /// Plain text content with normalized whitespace.
    /// </summary>
    Text,

    /// <summary>
    /// JSON content, formatted when the payload parses successfully.
    /// </summary>
    Json,

    /// <summary>
    /// XML content, formatted when the payload parses successfully.
    /// </summary>
    Xml,
}

/// <summary>
/// Represents normalized fetched content together with the HTTP metadata needed to reason about it.
/// </summary>
/// <remarks>
/// <see cref="DefaultWebContentFetcher"/> produces this response shape for the shared <c>web_fetch</c> tool. The
/// combination of URL metadata, truncation flags, and normalized content lets agents decide whether they need to issue
/// a narrower follow-up request.
/// </remarks>
/// <param name="Url">The original normalized request URL.</param>
/// <param name="EffectiveUrl">The final same-host URL that was fetched after any automatic redirects.</param>
/// <param name="StatusCode">The final HTTP status code returned by the server.</param>
/// <param name="StatusText">The HTTP reason phrase or a fallback status description.</param>
/// <param name="ContentType">The response media type used to choose the normalization path.</param>
/// <param name="Format">The normalized output format surfaced to the caller.</param>
/// <param name="Content">The normalized response body returned to the agent.</param>
/// <param name="Bytes">The number of raw response bytes read before normalization.</param>
/// <param name="DurationMilliseconds">The elapsed fetch time in milliseconds.</param>
/// <param name="Title">The extracted document title when one is available.</param>
/// <param name="Truncated"><see langword="true"/> when the returned content was shortened.</param>
/// <param name="PromptApplied"><see langword="true"/> when prompt-based section selection trimmed the content.</param>
/// <param name="FromCache"><see langword="true"/> when the response came from the in-memory fetch cache.</param>
/// <param name="RedirectUrl">The explicit follow-up URL for a cross-host redirect, when confirmation is required.</param>
/// <param name="RedirectRequiresConfirmation">
/// <see langword="true"/> when the caller must opt in to following a redirect onto a different host.
/// </param>
public sealed record WebContentFetchResponse(
    string Url,
    string? EffectiveUrl,
    int StatusCode,
    string StatusText,
    string ContentType,
    WebContentFormat Format,
    string Content,
    int Bytes,
    double DurationMilliseconds,
    string? Title = null,
    bool Truncated = false,
    bool PromptApplied = false,
    bool FromCache = false,
    string? RedirectUrl = null,
    bool RedirectRequiresConfirmation = false);

/// <summary>
/// Represents the outcome of a <c>web_fetch</c> operation.
/// </summary>
/// <param name="Success"><see langword="true"/> when the fetch completed successfully.</param>
/// <param name="Result">The normalized fetch response when the operation succeeds.</param>
/// <param name="Message">Optional additional context such as redirect instructions or an error message.</param>
/// <seealso cref="WebContentFetchResponse"/>
public sealed record WebFetchToolResult(
    bool Success,
    WebContentFetchResponse? Result,
    string? Message = null)
    : WebToolResult(Success, Message);

/// <summary>
/// Represents a single normalized search hit returned by an <see cref="IWebSearchProvider"/>.
/// </summary>
/// <param name="Title">The provider-supplied title for the result.</param>
/// <param name="Url">The canonical destination URL for the result.</param>
/// <param name="Snippet">An optional provider-supplied summary snippet.</param>
/// <param name="DisplayUrl">An optional display-friendly host or breadcrumb string.</param>
/// <param name="PublishedAt">An optional publication timestamp when the provider surfaces one.</param>
public sealed record WebSearchResult(
    string Title,
    string Url,
    string? Snippet = null,
    string? DisplayUrl = null,
    DateTimeOffset? PublishedAt = null);

/// <summary>
/// Represents a normalized search response returned by a provider.
/// </summary>
/// <remarks>
/// Provider-specific packages populate this record and <see cref="WebToolService"/> may further trim the
/// <see cref="Results"/> array after applying final domain filters and option-based limits.
/// </remarks>
/// <param name="Provider">The provider identifier, usually taken from <see cref="IWebSearchProvider.ProviderName"/>.</param>
/// <param name="Query">The final user query associated with the response.</param>
/// <param name="Results">The normalized search hits returned to the caller.</param>
/// <param name="DurationMilliseconds">The provider-reported or locally measured search duration in milliseconds.</param>
/// <param name="Summary">An optional provider-generated summary or answer.</param>
/// <param name="Truncated"><see langword="true"/> when additional results were available but omitted.</param>
/// <param name="TotalEstimatedMatches">The provider's approximate total match count, when available.</param>
public sealed record WebSearchResponse(
    string Provider,
    string Query,
    WebSearchResult[] Results,
    double DurationMilliseconds,
    string? Summary = null,
    bool Truncated = false,
    int? TotalEstimatedMatches = null);

/// <summary>
/// Represents the outcome of a <c>web_search</c> operation.
/// </summary>
/// <param name="Success"><see langword="true"/> when the search completed successfully.</param>
/// <param name="Result">The normalized search response when the operation succeeds.</param>
/// <param name="Message">Optional additional context such as validation or provider error details.</param>
/// <seealso cref="WebSearchResponse"/>
public sealed record WebSearchToolResult(
    bool Success,
    WebSearchResponse? Result,
    string? Message = null)
    : WebToolResult(Success, Message);
