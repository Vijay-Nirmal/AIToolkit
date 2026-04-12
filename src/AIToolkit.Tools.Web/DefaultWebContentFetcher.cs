using HtmlAgilityPack;
using ReverseMarkdown;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Provides the default HTTP fetch and HTML normalization implementation for <c>web_fetch</c>.
/// </summary>
public sealed class DefaultWebContentFetcher : IWebContentFetcher
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "i", "if", "in", "into", "is", "it",
        "latest", "me", "my", "of", "on", "or", "please", "show", "tell", "that", "the", "their", "there", "these",
        "this", "to", "up", "use", "what", "when", "where", "which", "with", "you", "your",
    ];

    private readonly WebToolsOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultWebContentFetcher"/> class using the shared default HTTP client.
    /// </summary>
    /// <param name="options">The options controlling fetch and normalization behavior.</param>
    public DefaultWebContentFetcher(WebToolsOptions? options = null)
        : this(SharedHttpClient, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultWebContentFetcher"/> class with a caller-supplied HTTP client.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="options">The options controlling fetch and normalization behavior.</param>
    public DefaultWebContentFetcher(HttpClient httpClient, WebToolsOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new WebToolsOptions();
    }

    /// <inheritdoc />
    public async ValueTask<WebContentFetchResponse> FetchAsync(WebContentFetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedUrl = NormalizeAndValidateUrl(request.Url);
        var maxCharacters = Math.Clamp(request.MaxCharacters ?? _options.MaxFetchCharacters, 1, _options.MaxFetchCharacters);

        if (TryGetCached(normalizedUrl, out var cachedEntry))
        {
            return ApplyPromptSelection(cachedEntry, request, maxCharacters, fromCache: true);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        var startedAt = DateTimeOffset.UtcNow;
        var currentUrl = normalizedUrl;
        var redirectCount = 0;

        while (true)
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            requestMessage.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);

            if (IsRedirectStatusCode(response.StatusCode) && response.Headers.Location is not null)
            {
                var redirectUri = ResolveRedirectUri(currentUrl, response.Headers.Location);
                if (!IsPermittedRedirect(currentUrl, redirectUri))
                {
                    return new WebContentFetchResponse(
                        Url: normalizedUrl,
                        EffectiveUrl: currentUrl,
                        StatusCode: (int)response.StatusCode,
                        StatusText: response.ReasonPhrase ?? response.StatusCode.ToString(),
                        ContentType: response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                        Format: WebContentFormat.Text,
                        Content: string.Empty,
                        Bytes: 0,
                        DurationMilliseconds: (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                        RedirectUrl: redirectUri,
                        RedirectRequiresConfirmation: true);
                }

                redirectCount++;
                if (redirectCount > _options.MaxRedirects)
                {
                    throw new InvalidOperationException($"Too many redirects (exceeded {_options.MaxRedirects}).");
                }

                currentUrl = redirectUri;
                continue;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var rawBytes = await ReadResponseBytesAsync(response, timeoutCts.Token).ConfigureAwait(false);
            var rawText = DecodeBytes(rawBytes, response.Content.Headers.ContentType?.CharSet);
            var normalized = NormalizeContent(mediaType, rawText);

            var cached = new CacheEntry(
                Url: normalizedUrl,
                EffectiveUrl: currentUrl,
                StatusCode: (int)response.StatusCode,
                StatusText: response.ReasonPhrase ?? response.StatusCode.ToString(),
                ContentType: mediaType,
                Format: normalized.Format,
                Content: normalized.Content,
                Bytes: rawBytes.Length,
                Title: normalized.Title,
                StoredAt: DateTimeOffset.UtcNow,
                Truncated: normalized.Truncated);

            Cache[normalizedUrl] = cached;
            return ApplyPromptSelection(cached, request, maxCharacters, fromCache: false, startedAt);
        }
    }

    private static WebContentFetchResponse ApplyPromptSelection(
        CacheEntry cachedEntry,
        WebContentFetchRequest request,
        int maxCharacters,
        bool fromCache,
        DateTimeOffset? startedAt = null)
    {
        var promptApplied = false;
        var content = cachedEntry.Content;

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            content = SelectRelevantContent(content, request.Prompt!, maxCharacters, out promptApplied);
        }

        var truncated = cachedEntry.Truncated;
        if (content.Length > maxCharacters)
        {
            content = content[..maxCharacters].TrimEnd() + "\n\n[Truncated]";
            truncated = true;
        }

        return new WebContentFetchResponse(
            Url: cachedEntry.Url,
            EffectiveUrl: cachedEntry.EffectiveUrl,
            StatusCode: cachedEntry.StatusCode,
            StatusText: cachedEntry.StatusText,
            ContentType: cachedEntry.ContentType,
            Format: cachedEntry.Format,
            Content: content,
            Bytes: cachedEntry.Bytes,
            DurationMilliseconds: startedAt is null ? 0 : (DateTimeOffset.UtcNow - startedAt.Value).TotalMilliseconds,
            Title: cachedEntry.Title,
            Truncated: truncated,
            PromptApplied: promptApplied,
            FromCache: fromCache);
    }

    private bool TryGetCached(string url, out CacheEntry entry)
    {
        if (Cache.TryGetValue(url, out entry!))
        {
            var expiresAt = entry.StoredAt.AddMinutes(_options.CacheDurationMinutes);
            if (DateTimeOffset.UtcNow <= expiresAt)
            {
                return true;
            }

            Cache.TryRemove(url, out _);
        }

        entry = default!;
        return false;
    }

    private string NormalizeAndValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid URL '{url}'.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Only HTTP and HTTPS URLs are supported.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("URLs with embedded credentials are not supported.");
        }

        if (IsBlockedHost(uri.Host))
        {
            throw new InvalidOperationException($"Fetching private or internal hosts is not supported: {uri.Host}.");
        }

        if (_options.UpgradeHttpToHttps && string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = uri.IsDefaultPort ? -1 : uri.Port,
            };

            uri = builder.Uri;
        }

        return uri.ToString();
    }

    private static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ipAddress))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        return ipAddress.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedIpv4(ipAddress),
            AddressFamily.InterNetworkV6 => ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal || ipAddress == IPAddress.IPv6Loopback,
            _ => true,
        };
    }

    private static bool IsBlockedIpv4(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        return bytes[0] switch
        {
            0 => true,
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false,
        };
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string ResolveRedirectUri(string currentUrl, Uri location) =>
        location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(currentUrl), location).ToString();

    private static bool IsPermittedRedirect(string currentUrl, string redirectUrl)
    {
        var current = new Uri(currentUrl);
        var redirect = new Uri(redirectUrl);

        if (!string.Equals(current.Scheme, redirect.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentHost = StripWww(current.Host);
        var redirectHost = StripWww(redirect.Host);
        return string.Equals(currentHost, redirectHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private async Task<byte[]> ReadResponseBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var remaining = _options.MaxResponseBytes;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            remaining -= read;
        }

        if (remaining == 0 && stream.ReadByte() != -1)
        {
            throw new InvalidOperationException($"The response exceeded the configured {_options.MaxResponseBytes} byte limit.");
        }

        return memory.ToArray();
    }

    private static string DecodeBytes(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static (WebContentFormat Format, string Content, string? Title, bool Truncated) NormalizeContent(string mediaType, string rawText)
    {
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeHtml(rawText);
        }

        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return (WebContentFormat.Json, TryFormatJson(rawText), null, false);
        }

        if (mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return (WebContentFormat.Xml, TryFormatXml(rawText), null, false);
        }

        if (mediaType.Contains("markdown", StringComparison.OrdinalIgnoreCase))
        {
            return (WebContentFormat.Markdown, NormalizeWhitespace(rawText), null, false);
        }

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return (WebContentFormat.Text, NormalizeWhitespace(rawText), null, false);
        }

        throw new InvalidOperationException($"Unsupported content type '{mediaType}'.");
    }

    private static (WebContentFormat Format, string Content, string? Title, bool Truncated) NormalizeHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var removableNodes = document.DocumentNode.SelectNodes("//script|//style|//noscript|//svg|//canvas|//iframe|//form");
        foreach (var node in removableNodes ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var title = HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? string.Empty).Trim();
        var description = document.DocumentNode
            .SelectSingleNode("//meta[@name='description' or @property='og:description' or @name='twitter:description']")
            ?.GetAttributeValue("content", string.Empty);

        description = string.IsNullOrWhiteSpace(description) ? null : description;

        var bodyHtml = document.DocumentNode.SelectSingleNode("//body")?.InnerHtml ?? document.DocumentNode.InnerHtml;
        var converter = new Converter(new Config
        {
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
            UnknownTags = Config.UnknownTagsOption.Drop,
        });

        var markdown = converter.Convert(bodyHtml);
        markdown = NormalizeWhitespace(markdown);

        if (!string.IsNullOrWhiteSpace(title))
        {
            markdown = string.Join("\n\n", $"# {title}", markdown);
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            markdown = string.Join("\n\n", markdown, $"> {NormalizeWhitespace(description)}");
        }

        return (WebContentFormat.Markdown, markdown.Trim(), string.IsNullOrWhiteSpace(title) ? null : title, false);
    }

    private static string TryFormatJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
        }
        catch (JsonException)
        {
            return NormalizeWhitespace(content);
        }
    }

    private static string TryFormatXml(string content)
    {
        try
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            return document.ToString();
        }
        catch (Exception) when (content.Length > 0)
        {
            return NormalizeWhitespace(content);
        }
    }

    private static string NormalizeWhitespace(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = Regex.Replace(normalized, "\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string SelectRelevantContent(string content, string prompt, int maxCharacters, out bool promptApplied)
    {
        promptApplied = false;
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(prompt))
        {
            return content;
        }

        var queryTerms = ExtractTerms(prompt);
        if (queryTerms.Count == 0)
        {
            return content;
        }

        var sections = content
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((section, index) => new SectionScore(section, index, ScoreSection(section, queryTerms)))
            .Where(static section => section.Score > 0)
            .OrderByDescending(static section => section.Score)
            .ThenBy(static section => section.Index)
            .Take(8)
            .OrderBy(static section => section.Index)
            .ToArray();

        if (sections.Length == 0)
        {
            return content;
        }

        var builder = new StringBuilder(Math.Min(content.Length, maxCharacters));
        var leadingSection = content
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(leadingSection)
            && leadingSection.StartsWith('#')
            && builder.Length + leadingSection.Length <= maxCharacters)
        {
            builder.Append(leadingSection);
        }

        foreach (var section in sections)
        {
            if (string.Equals(section.Section, leadingSection, StringComparison.Ordinal))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            if (builder.Length + section.Section.Length > maxCharacters)
            {
                break;
            }

            builder.Append(section.Section);
        }

        if (builder.Length == 0)
        {
            return content;
        }

        promptApplied = builder.Length < content.Length;
        return builder.ToString();
    }

    private static HashSet<string> ExtractTerms(string prompt)
    {
        return Regex.Matches(prompt.ToLowerInvariant(), "[a-z0-9][a-z0-9._-]{2,}")
            .Select(static match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int ScoreSection(string section, HashSet<string> queryTerms)
    {
        var lowered = section.ToLowerInvariant();
        var score = 0;

        foreach (var term in queryTerms)
        {
            if (lowered.Contains(term, StringComparison.Ordinal))
            {
                score += 2;
            }
        }

        if (section.StartsWith('#'))
        {
            score++;
        }

        return score;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private sealed record CacheEntry(
        string Url,
        string EffectiveUrl,
        int StatusCode,
        string StatusText,
        string ContentType,
        WebContentFormat Format,
        string Content,
        int Bytes,
        string? Title,
        DateTimeOffset StoredAt,
        bool Truncated);

    private sealed record SectionScore(string Section, int Index, int Score);
}