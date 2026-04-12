# AIToolkit.Tools.Web.Brave

`AIToolkit.Tools.Web.Brave` provides a Brave-backed `IWebSearchProvider` and convenience factory helpers for the generic `web_*` tool surface.

## Example

```csharp
using AIToolkit.Tools.Web.Brave;

var tools = BraveWebTools.CreateFunctions(
    new BraveWebSearchOptions
    {
        ApiKey = "<api-key>"
    });
```

## Notes

- This package uses the Brave Web Search endpoint.
- Brave supports result counts up to 20 and optional extra snippets.
- `web_fetch` still comes from `AIToolkit.Tools.Web`; this package supplies the `web_search` backend.