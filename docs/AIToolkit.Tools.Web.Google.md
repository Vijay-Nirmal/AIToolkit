# AIToolkit.Tools.Web.Google

`AIToolkit.Tools.Web.Google` plugs Google Custom Search into the generic `web_*` tool surface from `AIToolkit.Tools.Web`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Google-backed `web_search` | `GoogleWebTools.CreateFunctions(...)` |
| Only the provider object | `new GoogleWebSearchProvider(...)` |

Google is a good fit when you already use the Google Programmable Search Engine stack and want a first-party backend.

## Quick Start

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Google;

var tools = GoogleWebTools.CreateFunctions(
    new GoogleWebSearchOptions
    {
        ApiKey = "<api-key>",
        SearchEngineId = "<search-engine-id>",
        CountryCode = "us",
        LanguageRestriction = "lang_en"
    },
    new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

That creates both `web_fetch` and `web_search`, with `web_search` routed through Google Custom Search.

## Configuration Reference

### GoogleWebSearchOptions

| Property | Required | Default | Purpose |
| --- | --- | --- | --- |
| `ApiKey` | Yes | None | Google API key |
| `SearchEngineId` | Yes | None | Programmable Search Engine ID (`cx`) |
| `Endpoint` | No | `https://customsearch.googleapis.com/customsearch/v1` | Google Custom Search endpoint |
| `MaxResults` | No | `10` | Provider-level result cap |
| `CountryCode` | No | `null` | `gl` country boost |
| `LanguageRestriction` | No | `null` | `lr` language restriction such as `lang_en` |
| `InterfaceLanguage` | No | `null` | `hl` interface language |
| `SafeSearch` | No | `true` | Maps to `safe=active` |
| `FilterDuplicates` | No | `true` | Maps to the `filter` query parameter |

## Provider Notes

| Behavior | Details |
| --- | --- |
| Backend | Google Custom Search JSON API |
| Auth model | API key in the query string |
| Engine scope | Results depend on the configured Programmable Search Engine |
| Result cap | Google returns at most 10 results per request |
| Domain filters | Composed into the Google query using `site:` operators and still enforced after normalization |

## Recommended Use

Use this package when your host already has access to Google Custom Search and you want a provider that is easy to reason about, predictable, and compatible with the generic `web_search` contract.