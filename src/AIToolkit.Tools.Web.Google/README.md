# AIToolkit.Tools.Web.Google

`AIToolkit.Tools.Web.Google` provides a Google-backed `IWebSearchProvider` and convenience factory helpers for the generic `web_*` tool surface.

## Example

```csharp
using AIToolkit.Tools.Web.Google;

var tools = GoogleWebTools.CreateFunctions(
    new GoogleWebSearchOptions
    {
        ApiKey = "<api-key>",
        SearchEngineId = "<search-engine-id>"
    });
```

## Notes

- This package uses the Google Custom Search JSON API.
- Google requires both an API key and a Programmable Search Engine ID.
- `web_fetch` still comes from `AIToolkit.Tools.Web`; this package supplies the `web_search` backend.