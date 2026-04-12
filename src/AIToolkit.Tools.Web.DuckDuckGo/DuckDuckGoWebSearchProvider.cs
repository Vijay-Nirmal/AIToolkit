using HtmlAgilityPack;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Web;

namespace AIToolkit.Tools.Web.DuckDuckGo;

/// <summary>
/// Implements <see cref="IWebSearchProvider"/> on top of DuckDuckGo's HTML results page.
/// </summary>
public sealed class DuckDuckGoWebSearchProvider : IWebSearchProvider
{
    private const string ResultCardXPath = "//div[@id='links']//div[contains(concat(' ', normalize-space(@class), ' '), ' web-result ')]";
    private const string TitleLinkXPath = ".//h2[contains(concat(' ', normalize-space(@class), ' '), ' result__title ')]/a[contains(concat(' ', normalize-space(@class), ' '), ' result__a ')]";
    private const string DisplayUrlXPath = ".//div[contains(concat(' ', normalize-space(@class), ' '), ' result__extras__url ')]/a[contains(concat(' ', normalize-space(@class), ' '), ' result__url ')]";
    private const string SnippetXPath = ".//a[contains(concat(' ', normalize-space(@class), ' '), ' result__snippet ')] | .//div[contains(concat(' ', normalize-space(@class), ' '), ' result__snippet ')]";
    private const string PublishedAtXPath = ".//div[contains(concat(' ', normalize-space(@class), ' '), ' result__extras__url ')]/span[normalize-space()] [last()]";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly DuckDuckGoWebSearchOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDuckGoWebSearchProvider"/> class.
    /// </summary>
    /// <param name="options">The DuckDuckGo provider options.</param>
    /// <param name="httpClient">An optional HTTP client override.</param>
    public DuckDuckGoWebSearchProvider(DuckDuckGoWebSearchOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new DuckDuckGoWebSearchOptions();
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public string ProviderName => "duckduckgo";

    /// <inheritdoc />
    public async ValueTask<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var maxResults = Math.Clamp(request.MaxResults.GetValueOrDefault(_options.MaxResults), 1, _options.MaxResults);

        var endpoint = BuildUri(_options.Endpoint, ComposeQuery(request));
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, endpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        requestMessage.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DuckDuckGo request failed with {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var results = ParseHtmlResults(html)
            .Where(result => IsAllowedByFilters(result.Url, request.AllowedDomains, request.BlockedDomains))
            .Take(maxResults)
            .ToArray();

        stopwatch.Stop();

        return new WebSearchResponse(
            Provider: ProviderName,
            Query: request.Query,
            Results: results,
            DurationMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
            Truncated: results.Length == maxResults);
    }

    private static string BuildUri(string endpoint, string query) =>
        endpoint + "?q=" + Uri.EscapeDataString(query);

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

    private static WebSearchResult[] ParseHtmlResults(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var results = new List<WebSearchResult>();
        var nodes = document.DocumentNode.SelectNodes(ResultCardXPath);
        if (nodes is null)
        {
            return [];
        }

        foreach (var node in nodes)
        {
            var titleNode = node.SelectSingleNode(TitleLinkXPath);
            var title = NormalizeText(titleNode?.InnerText);
            var href = ExtractResultUrl(titleNode?.GetAttributeValue("href", string.Empty));
            if (!IsUsableResult(title, href))
            {
                continue;
            }

            var snippetNode = node.SelectSingleNode(SnippetXPath);
            var snippet = NormalizeText(snippetNode?.InnerText);

            var displayNode = node.SelectSingleNode(DisplayUrlXPath);
            var displayUrl = NormalizeText(displayNode?.InnerText);
            displayUrl = string.IsNullOrWhiteSpace(displayUrl) ? TryGetHost(href) : displayUrl;

            var publishedAtNode = node.SelectSingleNode(PublishedAtXPath);
            var publishedAt = ParsePublishedAt(NormalizeText(publishedAtNode?.InnerText));

            results.Add(new WebSearchResult(title!, href!, snippet, displayUrl, publishedAt));
        }

        return [.. results];
    }

    private static string? ExtractResultUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            href = "https:" + href;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
        {
            if (IsDuckDuckGoRedirect(absoluteUri))
            {
                var query = HttpUtility.ParseQueryString(absoluteUri.Query);
                var uddg = query["uddg"];
                return Uri.UnescapeDataString(uddg ?? string.Empty);
            }

            return absoluteUri.ToString();
        }

        if (Uri.TryCreate(new Uri("https://duckduckgo.com"), href, out var duckUri) && IsDuckDuckGoRedirect(duckUri))
        {
            var query = HttpUtility.ParseQueryString(duckUri.Query);
            var uddg = query["uddg"];
            return string.IsNullOrWhiteSpace(uddg) ? null : Uri.UnescapeDataString(uddg);
        }

        return null;
    }

    private static bool IsDuckDuckGoRedirect(Uri uri) =>
        uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/l/", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableResult(string? title, string? href)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (title.Contains("Sponsored", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Next Page", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Search only for", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out _);
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return HtmlEntity.DeEntitize(value).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    }

    private static string? TryGetHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : null;

    private static DateTimeOffset? ParsePublishedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool IsAllowedByFilters(string url, string[]? allowedDomains, string[]? blockedDomains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = NormalizeDomain(uri.Host);

        if (allowedDomains is { Length: > 0 } && !allowedDomains.Any(domain => DomainMatches(host, NormalizeDomain(domain))))
        {
            return false;
        }

        if (blockedDomains is { Length: > 0 } && blockedDomains.Any(domain => DomainMatches(host, NormalizeDomain(domain))))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim('/').ToLowerInvariant();
        return trimmed.StartsWith("www.", StringComparison.Ordinal) ? trimmed[4..] : trimmed;
    }

    private static bool DomainMatches(string host, string filter) =>
        string.Equals(host, filter, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{filter}", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}