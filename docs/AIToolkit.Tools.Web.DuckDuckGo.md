# AIToolkit.Tools.Web.DuckDuckGo

`AIToolkit.Tools.Web.DuckDuckGo` plugs DuckDuckGo HTML search into the generic `web_*` tool surface from `AIToolkit.Tools.Web`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| DuckDuckGo-backed `web_search` without API keys | `DuckDuckGoWebTools.CreateFunctions(...)` |
| Only the provider object | `new DuckDuckGoWebSearchProvider(...)` |

## Quick Start

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.DuckDuckGo;

var tools = DuckDuckGoWebTools.CreateFunctions(
    new DuckDuckGoWebSearchOptions
    {
        MaxResults = 8
    },
    new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

## Configuration Reference

### DuckDuckGoWebSearchOptions

| Property | Required | Default | Purpose |
| --- | --- | --- | --- |
| `Endpoint` | No | `https://html.duckduckgo.com/html/` | DuckDuckGo HTML search endpoint |
| `MaxResults` | No | `10` | Provider-level result cap |
| `UserAgent` | No | `AIToolkit.Tools.Web.DuckDuckGo/0.1` | User agent used for requests |

## Provider Notes

| Behavior | Details |
| --- | --- |
| Backend | DuckDuckGo HTML search results page |
| Auth model | No API key required |
| Result extraction | Parses result title links, snippets, and display URLs from HTML |
| Domain filters | Composed into the query with `site:` and `-site:` operators and enforced again after normalization |
| Operational caveat | Because this provider parses public HTML, it is best-effort and may require updates if DuckDuckGo changes its markup |

## Recommended Use

Use this package when you want a zero-key search backend for general research and are comfortable with the tradeoff that the provider depends on stable public search-result markup instead of a dedicated JSON API.