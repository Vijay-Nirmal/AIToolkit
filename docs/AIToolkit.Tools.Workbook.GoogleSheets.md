# AIToolkit.Tools.Workbook.GoogleSheets

`AIToolkit.Tools.Workbook.GoogleSheets` adds hosted Google Sheets support to the generic `workbook_*` tool surface from `AIToolkit.Tools.Workbook`.

## When to use it

Use this package when workbook references come from Google Sheets URLs or Drive folders and you still want the agent-facing format to stay canonical WorkbookDoc.

## Supported reference forms

| Reference | Use |
| --- | --- |
| `https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit` | Read or edit an existing Google Sheet |
| `gsheets://spreadsheets/{spreadsheetId}` | Read or edit an existing Google Sheet by ID |
| `gsheets://folders/root/spreadsheets/{title}` | Create or locate a Google Sheet in Drive root |
| `gsheets://folders/{folderId}/spreadsheets/{title}` | Create or locate a Google Sheet in a specific Drive folder |

## Behavior

- Reads prefer a managed WorkbookDoc payload stored in Drive appData.
- If the managed payload is missing, reads fall back to embedded WorkbookDoc inside the exported XLSX.
- If neither payload is present, the provider can import the Google-exported XLSX into best-effort WorkbookDoc.
- Writes render an XLSX through the Excel WorkbookDoc engine, embed canonical WorkbookDoc, upload through Drive conversion, and refresh the managed payload sidecar.

## Basic setup

```csharp
using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.GoogleSheets;
using Google.Apis.Auth.OAuth2;

var tools = GoogleSheetsWorkbookTools.CreateFunctions(
    new GoogleSheetsWorkbookToolSetOptions
    {
        Workspace = new GoogleSheetsWorkspaceOptions
        {
            Credential = GoogleCredential.GetApplicationDefault(),
        },
    });
```

## Notes

- `workbook_grep_search` can search hosted Google Sheets only when you pass explicit `workbook_references`.
- Directory-based grep remains local-workspace search and does not crawl Google Drive.
- Workbook rendering fidelity comes from the shared Excel WorkbookDoc engine, so WorkbookDoc features like formatting, formulas, charts, merges, and sparklines follow the same implementation surface as `AIToolkit.Tools.Workbook.Excel`.
