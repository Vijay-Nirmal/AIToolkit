using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Implements the shared orchestration behind the public <c>web_fetch</c> and <c>web_search</c> AI functions.
/// </summary>
/// <remarks>
/// This service centralizes logging, option clamping, dependency resolution, and post-processing so provider packages
/// only need to supply <see cref="IWebSearchProvider"/> implementations. It also keeps the public tool surface stable
/// for <see cref="WebAIFunctionFactory"/> by exposing reflection-friendly instance methods.
/// </remarks>
/// <seealso cref="WebAIFunctionFactory"/>
/// <seealso cref="DefaultWebContentFetcher"/>
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

    /// <summary>
    /// Executes the shared <c>web_fetch</c> flow.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="prompt">An optional relevance hint used to trim the normalized content.</param>
    /// <param name="maxCharacters">An optional per-call character cap.</param>
    /// <param name="serviceProvider">
    /// An optional service provider used to resolve <see cref="ILoggerFactory"/> and override fetch dependencies.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A structured result that either contains a normalized fetch response or an error message suitable for tool output.
    /// </returns>
    /// <remarks>
    /// Provider and transport exceptions are converted into a failed <see cref="WebFetchToolResult"/> so AI hosts can
    /// surface a stable tool payload instead of an unhandled exception.
    /// </remarks>
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
                // Cross-host redirects are intentionally surfaced as an explicit follow-up action so a model does not
                // silently pivot from one site to another without acknowledging the change in origin.
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

    /// <summary>
    /// Executes the shared <c>web_search</c> flow.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="allowedDomains">Optional domains that results must come from.</param>
    /// <param name="blockedDomains">Optional domains that results must not come from.</param>
    /// <param name="maxResults">An optional per-call result cap.</param>
    /// <param name="serviceProvider">
    /// An optional service provider used to resolve <see cref="ILoggerFactory"/> and provider dependencies.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the asynchronous operation.</param>
    /// <returns>A structured search result containing normalized hits or a validation/provider error message.</returns>
    /// <remarks>
    /// The shared service performs final query validation and host filtering after the provider call returns. That
    /// extra pass keeps result behavior consistent even when a provider only approximates domain filtering.
    /// </remarks>
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

            // Providers use different query syntaxes and may not enforce include/exclude filters exactly, so the shared
            // service re-applies normalized host filtering before it returns the result to the model.
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
