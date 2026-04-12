using System.Text.Json.Serialization;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Represents the common success and message fields returned by web tool operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the tool call completed successfully; otherwise, <see langword="false"/>.</param>
/// <param name="Message">An optional message, typically used for errors or additional context.</param>
public abstract record WebToolResult(bool Success, string? Message = null);

/// <summary>
/// Identifies the normalized content format returned by <c>web_fetch</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebContentFormat
{
    Markdown,
    Text,
    Json,
    Xml,
}

/// <summary>
/// Represents normalized fetched content and its HTTP metadata.
/// </summary>
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
public sealed record WebFetchToolResult(
    bool Success,
    WebContentFetchResponse? Result,
    string? Message = null)
    : WebToolResult(Success, Message);

/// <summary>
/// Represents a single normalized search hit.
/// </summary>
public sealed record WebSearchResult(
    string Title,
    string Url,
    string? Snippet = null,
    string? DisplayUrl = null,
    DateTimeOffset? PublishedAt = null);

/// <summary>
/// Represents a normalized search response.
/// </summary>
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
public sealed record WebSearchToolResult(
    bool Success,
    WebSearchResponse? Result,
    string? Message = null)
    : WebToolResult(Success, Message);