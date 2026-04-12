# AIToolkit.Tools.Web.Bing

`AIToolkit.Tools.Web.Bing` plugs the Bing Web Search API into the generic `web_*` tool surface from `AIToolkit.Tools.Web`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Bing-backed `web_search` | `BingWebTools.CreateFunctions(...)` |
| Only the provider object | `new BingWebSearchProvider(...)` |

## Quick Start

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Bing;

var tools = BingWebTools.CreateFunctions(
    new BingWebSearchOptions
    {
        ApiKey = "<api-key>",
        Market = "en-US",
        SafeSearch = "Moderate"
    },
    new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

## Configuration Reference

### BingWebSearchOptions

| Property | Required | Default | Purpose |
| --- | --- | --- | --- |
| `ApiKey` | Yes | None | Bing subscription key |
| `Endpoint` | No | `https://api.bing.microsoft.com/v7.0/search` | Bing search endpoint |
| `MaxResults` | No | `10` | Provider-level result cap |
| `Market` | No | `null` | Market code such as `en-US` |
| `SafeSearch` | No | `Moderate` | Bing safe-search mode |

## Provider Notes

| Behavior | Details |
| --- | --- |
| Backend | Bing Web Search v7 endpoint |
| Auth model | `Ocp-Apim-Subscription-Key` request header |
| Result cap | The package clamps to the provider's request limit |
| Domain filters | Composed into the query with `site:` and `-site:` operators and also enforced after normalization |
| Availability | Verify that your subscription and deployment environment still expose the Bing Search API endpoint you intend to use |

## Recommended Use

Use this package when you already rely on Bing Search APIs and want the simplest path to a normalized `web_search` surface in `Microsoft.Extensions.AI` hosts.