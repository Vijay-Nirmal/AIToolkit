# AIToolkit.Tools.Web

`AIToolkit.Tools.Web` provides generic `web_*` tools for `Microsoft.Extensions.AI` hosts.

The package exposes:

- `web_fetch`
- `web_search`
- `IWebContentFetcher`
- `IWebSearchProvider`

## Example

```csharp
using AIToolkit.Tools.Web;
using AIToolkit.Tools.Web.Brave;

var provider = new BraveWebSearchProvider(
    new BraveWebSearchOptions
    {
        ApiKey = "<api-key>"
    });

var tools = WebTools.CreateFunctions(
    searchProvider: provider,
    options: new WebToolsOptions
    {
        MaxSearchResults = 8
    });
```

## Notes

- `web_fetch` normalizes HTML to markdown and uses the optional `prompt` as a relevance hint for trimming content.
- `web_search` depends on an `IWebSearchProvider` implementation. Use one of the provider packages or register your own provider.
- `WebTools.GetSystemPromptGuidance(...)` appends Claude-style web-tool guidance, including source-link requirements for web-search-grounded answers.