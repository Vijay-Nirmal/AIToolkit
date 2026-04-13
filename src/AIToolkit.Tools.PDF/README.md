# AIToolkit.Tools.PDF

`AIToolkit.Tools.PDF` provides a first-party PDF `IWorkspaceFileHandler` for `AIToolkit.Tools`.

It plugs into `workspace_read_file` so PDF documents can return extracted page text plus embedded images as multimodal `AIContent` parts instead of raw binary PDF bytes.

## Example

```csharp
using AIToolkit.Tools;
using AIToolkit.Tools.PDF;

var workspaceTools = WorkspaceTools.CreateFunctions(
    new WorkspaceToolsOptions
    {
        WorkingDirectory = @"C:\repo",
        FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
    });
```

## Notes

- PDF text extraction uses PdfPig content-order text extraction.
- PDF images are returned as `DataContent` parts when the image bytes can be converted or identified.
- The `pages` parameter is supported for PDF reads using formats such as `1`, `1,3,5`, or `2-4`.
- `offset` and `limit` are line-oriented parameters from the generic file tool and are ignored by the PDF handler.