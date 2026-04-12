using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIToolkit.Tools.Web.Tavily;

/// <summary>
/// Implements <see cref="IWebSearchProvider"/> on top of the Tavily Search API.
/// </summary>
public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly TavilyWebSearchOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="TavilyWebSearchProvider"/> class.
    /// </summary>
    /// <param name="options">The Tavily provider options.</param>
    /// <param name="httpClient">An optional HTTP client override.</param>
    public TavilyWebSearchProvider(TavilyWebSearchOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public string ProviderName => "tavily";

    /// <inheritdoc />
    public async ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("TavilyWebSearchOptions.ApiKey is required.");
        }

        var body = new Dictionary<string, object?>
        {
            ["query"] = request.Query,
            ["search_depth"] = _options.SearchDepth,
            ["topic"] = _options.Topic,
            ["max_results"] = Math.Clamp(request.MaxResults.GetValueOrDefault(_options.MaxResults), 1, 20),
            ["include_answer"] = _options.IncludeAnswer,
            ["include_raw_content"] = _options.IncludeRawContent ? "markdown" : false,
            ["include_domains"] = request.AllowedDomains,
            ["exclude_domains"] = request.BlockedDomains,
            ["country"] = _options.Country,
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
        if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultsElement.EnumerateArray())
            {
                var url = GetString(item, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                results.Add(new WebSearchResult(
                    Title: GetString(item, "title") ?? "Untitled",
                    Url: url,
                    Snippet: GetString(item, "content"),
                    DisplayUrl: Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ? parsedUri.Host : null,
                    PublishedAt: TryGetDate(item, "published_date")));
            }
        }

        return new WebSearchResponse(
            Provider: ProviderName,
            Query: request.Query,
            Results: [.. results],
            DurationMilliseconds: GetResponseTimeMilliseconds(root),
            Summary: GetString(root, "answer"));
    }

    private static double GetResponseTimeMilliseconds(JsonElement root)
    {
        if (root.TryGetProperty("response_time", out var responseTimeElement))
        {
            if (responseTimeElement.ValueKind == JsonValueKind.Number
                && responseTimeElement.TryGetDouble(out var responseTimeNumber))
            {
                return responseTimeNumber * 1000d;
            }

            if (responseTimeElement.ValueKind == JsonValueKind.String
                && double.TryParse(responseTimeElement.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var responseTimeString))
            {
                return responseTimeString * 1000d;
            }
        }

        return 0;
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
        var detail = GetString(root, "detail") ?? GetString(root, "message");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return $"Tavily Search request failed with {(int)statusCode}: {detail}";
        }

        return $"Tavily Search request failed with {(int)statusCode}.";
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}