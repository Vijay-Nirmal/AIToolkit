# AIToolkit.Tools.Web.Bing

`AIToolkit.Tools.Web.Bing` provides a Bing-backed `IWebSearchProvider` and convenience factory helpers for the generic `web_*` tool surface.

## Example

```csharp
using AIToolkit.Tools.Web.Bing;

var tools = BingWebTools.CreateFunctions(
    new BingWebSearchOptions
    {
        ApiKey = "<api-key>"
    });
```

## Notes

- This package targets the Bing Web Search v7 endpoint.
- Bing requires the `Ocp-Apim-Subscription-Key` header.
- `web_fetch` still comes from `AIToolkit.Tools.Web`; this package supplies the `web_search` backend.