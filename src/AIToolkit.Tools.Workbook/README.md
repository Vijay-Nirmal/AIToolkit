# AIToolkit.Tools.Workbook

`AIToolkit.Tools.Workbook` exposes five provider-neutral `Microsoft.Extensions.AI` tools for workbook files that can be converted to and from canonical WorkbookDoc.

WorkbookDoc is the compact plain-text workbook language defined in `docs/WorkbookDoc-language-spec.md`. It is row-first and agent-friendly: workbook title with `=`, sheet heading with `==`, anchored rows such as `@B5 | a | b | c`, and a small built-in directive surface for things like `[used ...]`, `[merge ...]`, `[fmt ...]`, `[type ...]`, charts, and sparklines.

The `workbook_reference` input used by `workbook_read_file`, `workbook_write_file`, and `workbook_edit_file` can also be a URL or opaque workbook ID when you register an `IWorkbookReferenceResolver` that returns a resolved workbook resource.

It is the provider-neutral base package. Register one or more `IWorkbookHandler` implementations from provider packages such as `AIToolkit.Tools.Workbook.Excel`. Multiple handlers can be active in the same tool set so a host can support Excel today and add providers such as Google Sheets later without changing the generic `workbook_*` surface.

## At a Glance

The package gives you five generic tools:

| Tool | Purpose |
| --- | --- |
| `workbook_read_file` | Reads a supported workbook and returns canonical WorkbookDoc |
| `workbook_write_file` | Writes a supported workbook from canonical WorkbookDoc |
| `workbook_edit_file` | Applies exact string edits against canonical WorkbookDoc |
| `workbook_grep_search` | Searches text inside supported workbooks |
| `workbook_spec_lookup` | Returns compact WorkbookDoc feature guidance for advanced syntax |

## Quick Start

Start with this package plus one provider package such as Excel:

```csharp
using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.Excel;

var tools = WorkbookTools.CreateFunctions(
    new WorkbookToolsOptions
    {
        WorkingDirectory = @"C:\repo",
        ReferenceResolver = new UrlOrIdWorkbookResolver(),
        Handlers = [ExcelWorkbookTools.CreateHandler()],
    });
```

## Extensibility Contract

Providers plug into the base tool surface by implementing `IWorkbookHandler`.

| Type | Purpose |
| --- | --- |
| `IWorkbookHandler` | Converts a provider-specific workbook format to and from canonical WorkbookDoc |
| `IWorkbookReferenceResolver` | Maps a tool input such as a path, URL, or ID to a resolved workbook resource |
| `WorkbookReferenceResolverContext` | Exposes the raw workbook reference, operation, working directory, options, and services to a resolver |
| `WorkbookReferenceResolution` | Exposes the resolved reference, extension, version/state metadata, and stream-based read/write access |
| `WorkbookHandlerContext` | Exposes the resolved reference, optional local file path, extension, length, options, services, and stream-based access to a handler |
| `WorkbookReadResponse` | Returns canonical WorkbookDoc and round-trip metadata from a handler |
| `WorkbookWriteResponse` | Returns provider-side write metadata |

## Notes

- `workbook_read_file` returns line-numbered WorkbookDoc using `lineNumber<TAB>content` format.
- `workbook_grep_search` returns a path or resolved reference and line offset that can be passed directly into `workbook_read_file`.
- `workbook_grep_search` scans the local workspace by default, or searches only the explicit `workbook_references` you provide when you want resolver-backed URLs or workbook IDs included.
- `workbook_spec_lookup` searches a small embedded WorkbookDoc feature index for advanced topics such as charts, conditional formatting, tables, pivots, and sparkline syntax.
- `workbook_write_file` and `workbook_edit_file` require a prior full read before changing an existing workbook.
- Configure `WorkbookToolsOptions.ReferenceResolver` when you want `workbook_reference` to accept URLs or workbook IDs instead of only direct paths, and return a `WorkbookReferenceResolution` rather than a raw file path string.
- The base package does not implement any workbook formats by itself. Register at least one `IWorkbookHandler` from a provider package.
