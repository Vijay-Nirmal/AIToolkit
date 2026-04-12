using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Implements the behavior behind the public <c>web_*</c> AI functions.
/// </summary>
internal sealed class WebToolService(WebToolsOptions options, IWebSearchProvider? searchProvider, IWebContentFetcher? contentFetcher)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "WebToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

    private readonly WebToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IWebSearchProvider? _searchProvider = searchProvider;
    private readonly IWebContentFetcher? _contentFetcher = contentFetcher;
    private readonly DefaultWebContentFetcher _defaultContentFetcher = new(options);

    public async Task<WebFetchToolResult> FetchAsync(
        string url,
        string? prompt = null,
        int? maxCharacters = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "web_fetch",
            new Dictionary<string, object?>
            {
                ["url"] = url,
                ["prompt"] = prompt,
                ["maxCharacters"] = maxCharacters,
            });

        var fetcher = ResolveContentFetcher(serviceProvider);

        try
        {
            var response = await fetcher.FetchAsync(
                new WebContentFetchRequest(url, prompt, maxCharacters),
                cancellationToken).ConfigureAwait(false);

            if (response.RedirectRequiresConfirmation && !string.IsNullOrWhiteSpace(response.RedirectUrl))
            {
                return new WebFetchToolResult(
                    true,
                    response,
                    $"Redirect detected to a different host. Call web_fetch again with '{response.RedirectUrl}' if you want to follow that redirect explicitly.");
            }

            return new WebFetchToolResult(true, response);
        }
        catch (Exception exception)
        {
            return new WebFetchToolResult(false, Result: null, Message: exception.Message);
        }
    }

    public async Task<WebSearchToolResult> SearchAsync(
        string query,
        string[]? allowedDomains = null,
        string[]? blockedDomains = null,
        int? maxResults = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "web_search",
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["allowedDomains"] = allowedDomains,
                ["blockedDomains"] = blockedDomains,
                ["maxResults"] = maxResults,
            });

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return new WebSearchToolResult(false, Result: null, Message: "Query must contain at least 2 characters.");
        }

        if (allowedDomains is { Length: > 0 } && blockedDomains is { Length: > 0 })
        {
            return new WebSearchToolResult(false, Result: null, Message: "Cannot specify both allowedDomains and blockedDomains in the same request.");
        }

        var provider = ResolveSearchProvider(serviceProvider);
        if (provider is null)
        {
            return new WebSearchToolResult(false, Result: null, Message: "No IWebSearchProvider is configured for web_search.");
        }

        try
        {
            var effectiveMaxResults = ResolveMaxResults(maxResults);
            var response = await provider.SearchAsync(
                new WebSearchRequest(query.Trim(), NormalizeDomains(allowedDomains), NormalizeDomains(blockedDomains), effectiveMaxResults),
                cancellationToken).ConfigureAwait(false);

            var originalResults = response.Results ?? [];
            var filteredResults = originalResults
                .Where(result => IsDomainAllowed(result.Url, allowedDomains, blockedDomains))
                .Take(effectiveMaxResults)
                .ToArray();

            var normalizedResponse = response with
            {
                Results = filteredResults,
                Truncated = response.Truncated || filteredResults.Length < originalResults.Length,
            };

            return new WebSearchToolResult(true, normalizedResponse);
        }
        catch (Exception exception)
        {
            return new WebSearchToolResult(false, Result: null, Message: exception.Message);
        }
    }

    private int ResolveMaxResults(int? maxResults)
    {
        var requested = maxResults.GetValueOrDefault(_options.MaxSearchResults);
        return Math.Clamp(requested, 1, _options.MaxSearchResults);
    }

    private IWebContentFetcher ResolveContentFetcher(IServiceProvider? serviceProvider) =>
        _contentFetcher
        ?? serviceProvider?.GetService(typeof(IWebContentFetcher)) as IWebContentFetcher
        ?? _defaultContentFetcher;

    private IWebSearchProvider? ResolveSearchProvider(IServiceProvider? serviceProvider) =>
        _searchProvider
        ?? serviceProvider?.GetService(typeof(IWebSearchProvider)) as IWebSearchProvider;

    private static void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, object parameters)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        if (loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger<WebToolService>();
        var serialized = JsonSerializer.Serialize(parameters, LogJsonOptions);
        ToolInvocationLog(logger, toolName, serialized, null);
    }

    private static string[]? NormalizeDomains(string[]? domains)
    {
        if (domains is null || domains.Length == 0)
        {
            return null;
        }

        var normalized = domains
            .Select(NormalizeDomain)
            .Where(static domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsDomainAllowed(string url, string[]? allowedDomains, string[]? blockedDomains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = NormalizeDomain(uri.Host);

        var normalizedAllowed = NormalizeDomains(allowedDomains);
        if (normalizedAllowed is { Length: > 0 } && !normalizedAllowed.Any(filter => DomainMatches(host, filter)))
        {
            return false;
        }

        var normalizedBlocked = NormalizeDomains(blockedDomains);
        if (normalizedBlocked is { Length: > 0 } && normalizedBlocked.Any(filter => DomainMatches(host, filter)))
        {
            return false;
        }

        return true;
    }

    private static bool DomainMatches(string host, string filter) =>
        string.Equals(host, filter, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{filter}", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.Host;
        }

        trimmed = trimmed.Trim().Trim('/').ToLowerInvariant();
        return trimmed.StartsWith("www.", StringComparison.Ordinal) ? trimmed[4..] : trimmed;
    }
}