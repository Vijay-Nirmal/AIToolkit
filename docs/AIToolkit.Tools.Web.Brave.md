# AIToolkit.Tools.Web.Brave

`AIToolkit.Tools.Web.Brave` plugs Brave Search into the generic `web_*` tool surface from `AIToolkit.Tools.Web`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Brave-backed `web_search` | `BraveWebTools.CreateFunctions(...)` |
| Only the provider object | `new BraveWebSearchProvider(...)` |

## Quick Start

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Brave;

var tools = BraveWebTools.CreateFunctions(
    new BraveWebSearchOptions
    {
        ApiKey = "<api-key>",
        Country = "US",
        SearchLanguage = "en",
        ExtraSnippets = true
    },
    new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

## Configuration Reference

### BraveWebSearchOptions

| Property | Required | Default | Purpose |
| --- | --- | --- | --- |
| `ApiKey` | Yes | None | Brave API key |
| `Endpoint` | No | `https://api.search.brave.com/res/v1/web/search` | Brave web-search endpoint |
| `MaxResults` | No | `10` | Provider-level result cap |
| `Country` | No | `null` | Country targeting |
| `SearchLanguage` | No | `null` | Search language filter |
| `SafeSearch` | No | `moderate` | Brave safe-search mode |
| `ExtraSnippets` | No | `false` | Requests extra snippets per result |

## Provider Notes

| Behavior | Details |
| --- | --- |
| Backend | Brave Web Search API |
| Auth model | `X-Subscription-Token` request header |
| Result cap | Brave accepts up to 20 results per request |
| Extra context | When `ExtraSnippets` is enabled, extra snippets are merged into the normalized result snippet |
| Pagination hint | The provider maps Brave's `more_results_available` signal into the normalized `Truncated` flag |

## Recommended Use

Use this package when you want a dedicated search backend with simple authentication, strong domain-operator support, and optional richer snippets per hit.