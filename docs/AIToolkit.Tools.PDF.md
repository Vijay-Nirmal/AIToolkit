# AIToolkit.Tools.PDF

`AIToolkit.Tools.PDF` adds first-party PDF extraction to the generic `workspace_read_file` tool from `AIToolkit.Tools`.

Instead of returning raw `application/pdf` bytes, this package plugs in an `IWorkspaceFileHandler` that extracts page text and embedded images and returns them as multimodal `AIContent` parts.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| PDF text and image extraction for `workspace_read_file` | `PdfWorkspaceTools.CreateFileHandler()` |
| Keep the rest of the `workspace_*` tool surface unchanged | Add the PDF handler through `WorkspaceToolsOptions.FileHandlers` |

## Quick Start

```csharp
using AIToolkit.Tools;
using AIToolkit.Tools.PDF;

var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = @"C:\src\MyRepo",
        FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
    });
```

With that registration in place, calls to `workspace_read_file` for `.pdf` files are handled by the PDF extractor before the built-in binary/media handler.

## Public API Reference

| API | Purpose |
| --- | --- |
| `PdfWorkspaceTools.CreateFileHandler(...)` | Creates the PDF `IWorkspaceFileHandler` instance |
| `PdfWorkspaceFileHandlerOptions` | Configures extraction limits and behavior |

## Configuration Reference

### PdfWorkspaceFileHandlerOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `IncludeText` | `true` | Return extracted page text |
| `IncludeImages` | `true` | Return embedded PDF images as `DataContent` |
| `MaxPages` | `50` | Upper bound for extracted pages in one read |
| `MaxImages` | `32` | Upper bound for returned embedded images |
| `MaxTextCharactersPerPage` | `20000` | Maximum extracted text returned per page |
| `MaxImageBytes` | `10485760` | Maximum bytes returned for one extracted image |
| `UseLenientParsing` | `true` | Enables PdfPig lenient parsing for more tolerant PDF reads |

## Behavior Notes

| Behavior | Details |
| --- | --- |
| File type | Handles `.pdf` files |
| Text extraction | Uses PdfPig content-order text extraction per selected page |
| Image extraction | Returns embedded page images as `DataContent` when bytes can be converted or identified |
| Page selection | Supports the `pages` parameter with values such as `1`, `1,3,5`, or `2-4` |
| Offset and limit | Ignores `offset` and `limit` because they are line-oriented text-file parameters |
| Fallback | If a PDF image format cannot be recognized, the handler falls back to `application/octet-stream` |

## When to Use It

Use this package when your host already relies on `AIToolkit.Tools` and you want PDF-specific extraction without changing the generic `workspace_*` tool names or contracts.