# AIToolkit.Tools.Web.DuckDuckGo

`AIToolkit.Tools.Web.DuckDuckGo` provides a DuckDuckGo-backed `IWebSearchProvider` and convenience factory helpers for the generic `web_*` tool surface.

## Example

```csharp
using AIToolkit.Tools.Web.DuckDuckGo;

var tools = DuckDuckGoWebTools.CreateFunctions(
    new DuckDuckGoWebSearchOptions());
```

## Notes

- This package uses DuckDuckGo's HTML search results page and does not require an API key.
- Because the provider parses HTML search results, it is best-effort and may need updates if DuckDuckGo changes its markup.
- `web_fetch` still comes from `AIToolkit.Tools.Web`; this package supplies the `web_search` backend.