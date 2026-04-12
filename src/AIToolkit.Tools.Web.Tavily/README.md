# AIToolkit.Tools.Web.Tavily

`AIToolkit.Tools.Web.Tavily` provides a Tavily-backed `IWebSearchProvider` and convenience factory helpers for the generic `web_*` tool surface.

## Example

```csharp
using AIToolkit.Tools.Web.Tavily;

var tools = TavilyWebTools.CreateFunctions(
    new TavilyWebSearchOptions
    {
        ApiKey = "<api-key>"
    });
```

## Notes

- This package uses the Tavily Search endpoint.
- Tavily can optionally return a provider-generated answer in addition to ranked result items.
- `web_fetch` still comes from `AIToolkit.Tools.Web`; this package supplies the `web_search` backend.