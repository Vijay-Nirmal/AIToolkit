# AIToolkit.Tools.Web.Tavily

`AIToolkit.Tools.Web.Tavily` plugs Tavily Search into the generic `web_*` tool surface from `AIToolkit.Tools.Web`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Tavily-backed `web_search` | `TavilyWebTools.CreateFunctions(...)` |
| Only the provider object | `new TavilyWebSearchProvider(...)` |

## Quick Start

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Tavily;

var tools = TavilyWebTools.CreateFunctions(
    new TavilyWebSearchOptions
    {
        ApiKey = "<api-key>",
        SearchDepth = "basic",
        Topic = "general",
        IncludeAnswer = true
    },
    new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

## Configuration Reference

### TavilyWebSearchOptions

| Property | Required | Default | Purpose |
| --- | --- | --- | --- |
| `ApiKey` | Yes | None | Tavily API key |
| `Endpoint` | No | `https://api.tavily.com/search` | Tavily search endpoint |
| `MaxResults` | No | `5` | Provider-level result cap |
| `SearchDepth` | No | `basic` | Tavily search-depth mode |
| `Topic` | No | `general` | Tavily topic selection |
| `IncludeAnswer` | No | `true` | Requests a provider-generated answer summary |
| `IncludeRawContent` | No | `false` | Requests raw markdown content in Tavily's response |
| `Country` | No | `null` | Optional country boost |

## Provider Notes

| Behavior | Details |
| --- | --- |
| Backend | Tavily Search API |
| Auth model | Bearer token in the `Authorization` header |
| Result cap | Tavily supports up to 20 results per request |
| Provider answer | When `IncludeAnswer` is enabled, the normalized `Summary` field contains Tavily's generated answer |
| Domain filters | Passed as `include_domains` and `exclude_domains` and still enforced after normalization |

## Recommended Use

Use this package when you want a provider that can return both ranked result items and an optional provider-generated answer that the agent can use as additional grounding.