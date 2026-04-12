# AIToolkit.Tools.Web

`AIToolkit.Tools.Web` exposes generic `Microsoft.Extensions.AI` functions for web fetch and provider-driven web search.

## At a Glance

Most applications need two things:

| Need | Recommended choice |
| --- | --- |
| The generic `web_fetch` and `web_search` tool surface | `WebTools.CreateFunctions(...)` |
| A search backend for `web_search` | Pass an `IWebSearchProvider` from one of the provider packages or your own implementation |

The package also includes the default `IWebContentFetcher` used by `web_fetch`.

## Quick Start

Start with the base package plus one search-provider package such as Brave:

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Brave;

var provider = new BraveWebSearchProvider(
    new BraveWebSearchOptions
    {
        ApiKey = "<api-key>"
    });

var tools = WebTools.CreateFunctions(
    new WebToolsOptions
    {
        MaxSearchResults = 8,
        MaxFetchCharacters = 20_000
    },
    searchProvider: provider);
```

That gives the model:

| Tool | Purpose |
| --- | --- |
| `web_fetch` | Fetches and normalizes a specific URL, including HTML-to-Markdown conversion |
| `web_search` | Executes a provider-backed search and returns normalized search hits |

## Public API Reference

### WebTools

Use these APIs to create the `web_*` toolset.

| API | Purpose |
| --- | --- |
| `WebTools.GetSystemPromptGuidance()` | Returns Claude-style guidance for `web_fetch` and `web_search` |
| `WebTools.GetSystemPromptGuidance(string? currentSystemPrompt)` | Appends that guidance to an existing system prompt |
| `WebTools.CreateFunctions(...)` | Creates `web_fetch` and `web_search` together |
| `WebTools.CreateFetchFunction(...)` | Creates `web_fetch` only |
| `WebTools.CreateSearchFunction(...)` | Creates `web_search` only |

### Extensibility Contracts

| Type | Purpose |
| --- | --- |
| `IWebContentFetcher` | Fetches and normalizes one URL |
| `IWebSearchProvider` | Executes a provider-backed web search |
| `DefaultWebContentFetcher` | Built-in HTTP + HTML normalization implementation |
| `WebContentFetchRequest` | Input contract for fetchers |
| `WebSearchRequest` | Input contract for search providers |

If `WebTools.CreateFunctions(...)` does not receive a custom `IWebContentFetcher`, it uses `DefaultWebContentFetcher`. If it does not receive an `IWebSearchProvider`, `web_search` is still created but returns a failure result until a provider is supplied.

### Custom Search Provider Example

If you want to plug in your own search backend, implement `IWebSearchProvider` and pass it to `WebTools.CreateFunctions(...)`.

```csharp
using AIToolkit.Tools.Web;

sealed class ContosoSearchProvider : IWebSearchProvider
{
    public string ProviderName => "contoso";

    public ValueTask<WebSearchResponse> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        WebSearchResult[] results =
        [
            new(
                Title: "Contoso documentation",
                Url: "https://docs.contoso.com/search?q=" + Uri.EscapeDataString(request.Query),
                Snippet: "Example result returned by a custom search provider.",
                DisplayUrl: "docs.contoso.com")
        ];

        return ValueTask.FromResult(
            new WebSearchResponse(
                Provider: ProviderName,
                Query: request.Query,
                Results: results,
                DurationMilliseconds: 0));
    }
}

var provider = new ContosoSearchProvider();

var tools = WebTools.CreateFunctions(
    new WebToolsOptions
    {
        MaxSearchResults = 8,
        MaxFetchCharacters = 20_000
    },
    searchProvider: provider);
```

## Tool Reference

### web_fetch

| Item | Details |
| --- | --- |
| Required parameters | `url` |
| Optional parameters | `prompt`, `maxCharacters` |
| Returns | `WebFetchToolResult` with a `WebContentFetchResponse` payload |

Default fetch behavior:

| Behavior | Details |
| --- | --- |
| Protocols | Supports `http` and `https`; `http` is upgraded to `https` by default |
| HTML normalization | Converts HTML to Markdown and preserves page title metadata when available |
| Prompt hint | Uses the optional `prompt` to trim the returned content toward relevant sections |
| Cache | Keeps successful fetches in a short in-memory cache |
| Redirect handling | Follows same-host redirects; returns cross-host redirects for explicit follow-up |
| Internal URL safety | Rejects `localhost`, private IPs, and common internal hostnames |

### web_search

| Item | Details |
| --- | --- |
| Required parameters | `query` |
| Optional parameters | `allowedDomains`, `blockedDomains`, `maxResults` |
| Returns | `WebSearchToolResult` with a `WebSearchResponse` payload |

Search behavior:

| Behavior | Details |
| --- | --- |
| Backend | Delegated to the configured `IWebSearchProvider` |
| Domain filters | Passed through to the provider and also enforced on normalized results |
| Result shape | Returns normalized `title`, `url`, `snippet`, `displayUrl`, and optional `publishedAt` |
| Sources guidance | `WebTools.GetSystemPromptGuidance(...)` requires a `Sources:` section after web-search-grounded answers |

## Configuration Reference

### WebToolsOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `MaxFetchCharacters` | `100000` | Maximum characters returned by `web_fetch` |
| `MaxSearchResults` | `10` | Upper bound for returned search hits |
| `MaxResponseBytes` | `10485760` | Maximum HTTP payload size read from one response |
| `RequestTimeoutSeconds` | `60` | Timeout applied to fetch and search requests |
| `MaxRedirects` | `10` | Maximum same-host redirects followed automatically |
| `CacheDurationMinutes` | `15` | In-memory fetch cache duration |
| `UpgradeHttpToHttps` | `true` | Upgrades `http` URLs before fetching |
| `UserAgent` | `AIToolkit.Tools.Web/0.1 (+https://github.com/your-org/AIToolkit)` | User agent used by the default fetcher |

## Built-in Provider Packages

| Package | Use when |
| --- | --- |
| `AIToolkit.Tools.Web.DuckDuckGo` | You want DuckDuckGo HTML search based `web_search` without an API key |
| `AIToolkit.Tools.Web.Google` | You want Google Custom Search based `web_search` |
| `AIToolkit.Tools.Web.Bing` | You want Bing Web Search based `web_search` |
| `AIToolkit.Tools.Web.Brave` | You want Brave Search based `web_search` |
| `AIToolkit.Tools.Web.Tavily` | You want Tavily Search based `web_search` |

## Prompt Guidance

Append the built-in guidance to your host prompt so the model follows the reference behavior for sources and search-year freshness.

```csharp
var systemPrompt = WebTools.GetSystemPromptGuidance(
    "You are a web research assistant.");
```

That guidance includes:

- a mandatory `Sources:` section when `web_search` materially informed the answer
- explicit instructions to use the current year for freshness-sensitive searches
- warnings about authenticated URLs and cross-host redirects for `web_fetch`

## Sample

See `samples/AIToolkit.Tools.Web.Sample` for a runnable console host that can switch between DuckDuckGo, Google, Bing, Brave, and Tavily through configuration.