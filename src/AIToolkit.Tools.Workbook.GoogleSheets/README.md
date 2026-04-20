# AIToolkit.Tools.Workbook.GoogleSheets

`AIToolkit.Tools.Workbook.GoogleSheets` adds hosted Google Sheets support to the generic `workbook_*` tool surface from `AIToolkit.Tools.Workbook`.

## What it provides

- `https://docs.google.com/spreadsheets/d/{spreadsheetId}` URL support
- `gsheets://spreadsheets/{spreadsheetId}` direct hosted references
- `gsheets://folders/{folderId}/spreadsheets/{title}` create-or-locate references
- canonical WorkbookDoc round-tripping through a managed Google Drive appData payload
- `.xlsx` export/import bridging through the Excel WorkbookDoc engine for formulas, formatting, merges, charts, sparklines, and other WorkbookDoc features

## Basic usage

```csharp
using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.GoogleSheets;
using Google.Apis.Auth.OAuth2;

var functions = GoogleSheetsWorkbookTools.CreateFunctions(
    new GoogleSheetsWorkbookToolSetOptions
    {
        Workspace = new GoogleSheetsWorkspaceOptions
        {
            Credential = GoogleCredential.GetApplicationDefault(),
        },
    });
```

## Reference formats

| Format | Meaning |
| --- | --- |
| `https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit` | Existing hosted Google Sheet |
| `gsheets://spreadsheets/{spreadsheetId}` | Existing hosted Google Sheet |
| `gsheets://folders/root/spreadsheets/{title}` | Create or locate a spreadsheet in Drive root |
| `gsheets://folders/{folderId}/spreadsheets/{title}` | Create or locate a spreadsheet in a specific Drive folder |

## Notes

- Reads prefer the managed WorkbookDoc payload, then embedded WorkbookDoc inside the exported XLSX, then best-effort XLSX import.
- Writes generate an XLSX with embedded WorkbookDoc, upload it through Drive conversion, and refresh the managed payload sidecar.
- `workbook_grep_search` can search hosted Google Sheets only when you pass explicit `workbook_references`; directory scans remain local-workspace search.
