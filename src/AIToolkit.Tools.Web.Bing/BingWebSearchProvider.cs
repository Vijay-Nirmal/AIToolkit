using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Bing;

/// <summary>
/// Implements <see cref="IWebSearchProvider"/> on top of the Bing Web Search API.
/// </summary>
public sealed class BingWebSearchProvider : IWebSearchProvider
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly BingWebSearchOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="BingWebSearchProvider"/> class.
    /// </summary>
    /// <param name="options">The Bing provider options.</param>
    /// <param name="httpClient">An optional HTTP client override.</param>
    public BingWebSearchProvider(BingWebSearchOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public string ProviderName => "bing";

    /// <inheritdoc />
    public async ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("BingWebSearchOptions.ApiKey is required.");
        }

        var maxResults = Math.Clamp(request.MaxResults.GetValueOrDefault(_options.MaxResults), 1, 50);
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = ComposeQuery(request),
            ["count"] = maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["mkt"] = _options.Market,
            ["safeSearch"] = _options.SafeSearch,
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildUri(_options.Endpoint, parameters));
        requestMessage.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);
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
        var totalEstimatedMatches = default(int?);

        if (root.TryGetProperty("webPages", out var webPages))
        {
            if (webPages.TryGetProperty("totalEstimatedMatches", out var totalMatchesElement) && totalMatchesElement.TryGetInt32(out var totalMatches))
            {
                totalEstimatedMatches = totalMatches;
            }

            if (webPages.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueElement.EnumerateArray())
                {
                    var url = GetString(item, "url");
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    results.Add(new WebSearchResult(
                        Title: GetString(item, "name") ?? "Untitled",
                        Url: url,
                        Snippet: GetString(item, "snippet"),
                        DisplayUrl: GetString(item, "displayUrl"),
                        PublishedAt: TryGetDate(item, "dateLastCrawled")));
                }
            }
        }

        return new WebSearchResponse(
            Provider: ProviderName,
            Query: request.Query,
            Results: [.. results],
            DurationMilliseconds: 0,
            TotalEstimatedMatches: totalEstimatedMatches);
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

    private static DateTimeOffset? TryGetDate(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string GetErrorMessage(HttpStatusCode statusCode, JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            var firstError = errors.EnumerateArray().FirstOrDefault();
            if (firstError.ValueKind == JsonValueKind.Object
                && firstError.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                return $"Bing Web Search request failed with {(int)statusCode}: {messageElement.GetString()}";
            }
        }

        return $"Bing Web Search request failed with {(int)statusCode}.";
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}