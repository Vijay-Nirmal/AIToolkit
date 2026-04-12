using System.Net;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Google;

/// <summary>
/// Implements <see cref="IWebSearchProvider"/> on top of Google Custom Search.
/// </summary>
public sealed class GoogleWebSearchProvider : IWebSearchProvider
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly GoogleWebSearchOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleWebSearchProvider"/> class.
    /// </summary>
    /// <param name="options">The Google provider options.</param>
    /// <param name="httpClient">An optional HTTP client override.</param>
    public GoogleWebSearchProvider(GoogleWebSearchOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public string ProviderName => "google";

    /// <inheritdoc />
    public async ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOptions();

        var maxResults = Math.Clamp(request.MaxResults.GetValueOrDefault(_options.MaxResults), 1, 10);
        var parameters = new Dictionary<string, string?>
        {
            ["key"] = _options.ApiKey,
            ["cx"] = _options.SearchEngineId,
            ["q"] = ComposeQuery(request),
            ["num"] = maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["safe"] = _options.SafeSearch ? "active" : "off",
            ["filter"] = _options.FilterDuplicates ? "1" : "0",
            ["gl"] = _options.CountryCode,
            ["lr"] = _options.LanguageRestriction,
            ["hl"] = _options.InterfaceLanguage,
        };

        using var response = await _httpClient.GetAsync(BuildUri(_options.Endpoint, parameters), cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(GetErrorMessage("Google Custom Search", response.StatusCode, document.RootElement));
        }

        var root = document.RootElement;
        var durationMs = 0d;
        var totalEstimatedMatches = default(int?);

        if (root.TryGetProperty("searchInformation", out var searchInformation))
        {
            if (searchInformation.TryGetProperty("searchTime", out var searchTimeElement)
                && searchTimeElement.TryGetDouble(out var searchTimeSeconds))
            {
                durationMs = searchTimeSeconds * 1000d;
            }

            if (searchInformation.TryGetProperty("totalResults", out var totalResultsElement)
                && totalResultsElement.ValueKind == JsonValueKind.String
                && long.TryParse(totalResultsElement.GetString(), out var totalResultsLong))
            {
                totalEstimatedMatches = totalResultsLong > int.MaxValue ? int.MaxValue : (int)totalResultsLong;
            }
        }

        var results = new List<WebSearchResult>();
        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var url = GetString(item, "link");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                results.Add(new WebSearchResult(
                    Title: GetString(item, "title") ?? "Untitled",
                    Url: url,
                    Snippet: GetString(item, "snippet"),
                    DisplayUrl: GetString(item, "displayLink")));
            }
        }

        return new WebSearchResponse(
            Provider: ProviderName,
            Query: request.Query,
            Results: [.. results],
            DurationMilliseconds: durationMs,
            TotalEstimatedMatches: totalEstimatedMatches);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("GoogleWebSearchOptions.ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.SearchEngineId))
        {
            throw new InvalidOperationException("GoogleWebSearchOptions.SearchEngineId is required.");
        }
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

    private static string GetErrorMessage(string provider, HttpStatusCode statusCode, JsonElement root)
    {
        if (root.TryGetProperty("error", out var error)
            && error.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            return $"{provider} request failed with {(int)statusCode}: {messageElement.GetString()}";
        }

        return $"{provider} request failed with {(int)statusCode}.";
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}