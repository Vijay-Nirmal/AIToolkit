using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Brave;

/// <summary>
/// Implements <see cref="IWebSearchProvider"/> on top of the Brave Search API.
/// </summary>
public sealed class BraveWebSearchProvider : IWebSearchProvider
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly BraveWebSearchOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="BraveWebSearchProvider"/> class.
    /// </summary>
    /// <param name="options">The Brave provider options.</param>
    /// <param name="httpClient">An optional HTTP client override.</param>
    public BraveWebSearchProvider(BraveWebSearchOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public string ProviderName => "brave";

    /// <inheritdoc />
    public async ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("BraveWebSearchOptions.ApiKey is required.");
        }

        var maxResults = Math.Clamp(request.MaxResults.GetValueOrDefault(_options.MaxResults), 1, 20);
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = ComposeQuery(request),
            ["count"] = maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["country"] = _options.Country,
            ["search_lang"] = _options.SearchLanguage,
            ["safesearch"] = _options.SafeSearch,
            ["extra_snippets"] = _options.ExtraSnippets ? "true" : null,
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildUri(_options.Endpoint, parameters));
        requestMessage.Headers.Add("X-Subscription-Token", _options.ApiKey);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(GetErrorMessage(response.StatusCode, document.RootElement));
        }

        var root = document.RootElement;
        var results = new List<WebSearchResult>();
        var truncated = false;

        if (root.TryGetProperty("query", out var queryElement)
            && queryElement.TryGetProperty("more_results_available", out var moreResultsElement)
            && moreResultsElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            truncated = moreResultsElement.GetBoolean();
        }

        if (root.TryGetProperty("web", out var webElement)
            && webElement.TryGetProperty("results", out var resultsElement)
            && resultsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultsElement.EnumerateArray())
            {
                var url = GetString(item, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var snippet = GetString(item, "description");
                if (item.TryGetProperty("extra_snippets", out var extraSnippetsElement) && extraSnippetsElement.ValueKind == JsonValueKind.Array)
                {
                    var extraSnippets = extraSnippetsElement
                        .EnumerateArray()
                        .Where(static value => value.ValueKind == JsonValueKind.String)
                        .Select(static value => value.GetString())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();

                    if (extraSnippets.Length > 0)
                    {
                        var snippetParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(snippet))
                        {
                            snippetParts.Add(snippet);
                        }

                        snippetParts.AddRange(extraSnippets!);
                        snippet = string.Join("\n", snippetParts);
                    }
                }

                string? displayUrl = null;
                if (item.TryGetProperty("meta_url", out var metaUrlElement) && metaUrlElement.ValueKind == JsonValueKind.Object)
                {
                    displayUrl = GetString(metaUrlElement, "display") ?? GetString(metaUrlElement, "netloc") ?? GetString(metaUrlElement, "hostname");
                }

                results.Add(new WebSearchResult(
                    Title: GetString(item, "title") ?? "Untitled",
                    Url: url,
                    Snippet: snippet,
                    DisplayUrl: displayUrl));
            }
        }

        return new WebSearchResponse(
            Provider: ProviderName,
            Query: request.Query,
            Results: [.. results],
            DurationMilliseconds: 0,
            Truncated: truncated);
    }

    private static string ComposeQuery(WebSearchRequest request)
    {
        var query = request.Query.Trim();
        if (request.AllowedDomains is { Length: > 0 })
        {
            query += " (" + string.Join(" OR ", request.AllowedDomains.Select(static domain => $"site:{domain}")) + ")";
        }

        if (request.BlockedDomains is { Length: > 0 })
        {
            query += " " + string.Join(" ", request.BlockedDomains.Select(static domain => $"-site:{domain}"));
        }

        return query;
    }

    private static string BuildUri(string endpoint, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        return endpoint + "?" + string.Join(
            "&",
            parameters
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string GetErrorMessage(HttpStatusCode statusCode, JsonElement root)
    {
        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            var detail = GetString(errorElement, "detail") ?? GetString(errorElement, "message");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return $"Brave Search request failed with {(int)statusCode}: {detail}";
            }
        }

        return $"Brave Search request failed with {(int)statusCode}.";
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}