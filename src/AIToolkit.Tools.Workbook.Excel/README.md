# AIToolkit.Tools.Workbook.Excel

`AIToolkit.Tools.Workbook.Excel` provides Microsoft Excel support for `AIToolkit.Tools.Workbook`.

It supports Open XML workbook formats such as `.xlsx`, `.xlsm`, `.xltx`, and `.xltm`.

Writes embed canonical WorkbookDoc in a custom XML part so later reads, edits, and rewrites remain lossless. External Excel workbooks that do not contain embedded canonical WorkbookDoc are imported through a best-effort Excel-to-WorkbookDoc projection.

It can also resolve hosted OneDrive and SharePoint Excel workbooks through Microsoft Graph when you configure `ExcelWorkbookToolSetOptions.M365` or `ExcelWorkbookHandlerOptions.M365` with a caller-supplied `TokenCredential`.

## Example

```csharp
using AIToolkit.Tools.Workbook.Excel;

var tools = ExcelWorkbookTools.CreateFunctions();
```

## Notes

- Generated workbooks keep WorkbookDoc authoritative through an embedded payload.
- Best-effort import focuses on the common workbook surface used by WorkbookDoc: sheets, rows, formulas, styles, merges, conditional formatting, tables, charts, and sparklines.
- The visible `.xlsx` rendering focuses on common workbook features; unsupported advanced directives remain preserved in the embedded WorkbookDoc payload for lossless tool-driven round-trips.
- Hosted M365 workbook references can use SharePoint or OneDrive HTTPS URLs, `m365://drives/me/root/path/to/file.xlsx` for the current user's OneDrive when no drive ID is known, or `m365://drives/{driveId}/items/{itemId}` and `m365://drives/{driveId}/root/path/to/file.xlsx` when the drive ID is known.
- Local file paths and hosted M365 references can be used together in the same `ExcelWorkbookTools.CreateFunctions(...)` tool set, or you can disable local `.xlsx` handling with `ExcelWorkbookToolSetOptions.EnableLocalFileSupport = false`.
- If you want WorkbookDoc payloads in invocation logs, set `ExcelWorkbookToolSetOptions.LogContentParameters = true`; it stays off by default.
- `workbook_grep_search` can search hosted M365 workbooks when you pass explicit `workbook_references`; directory-based grep remains local-workspace search.
