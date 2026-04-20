# AIToolkit.Tools.Workbook.Excel

`AIToolkit.Tools.Workbook.Excel` provides Microsoft Excel support for `AIToolkit.Tools.Workbook`.

It handles Open XML workbook formats such as `.xlsx`, `.xlsm`, `.xltx`, and `.xltm`, and treats canonical WorkbookDoc as the authoritative representation for tool-driven read, write, and edit operations.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Generic `workbook_*` tools with Excel support | `ExcelWorkbookTools.CreateFunctions(...)` |
| Excel-aware system prompt guidance | `ExcelWorkbookTools.GetSystemPromptGuidance(...)` |
| Just the reusable Excel handler | `ExcelWorkbookTools.CreateHandler(...)` |
| Hosted OneDrive or SharePoint Excel workbooks | Configure `ExcelWorkbookToolSetOptions.M365` or `ExcelWorkbookHandlerOptions.M365` |
| Disable local `.xlsx` path handling | Set `ExcelWorkbookToolSetOptions.EnableLocalFileSupport = false` or `ExcelWorkbookHandlerOptions.EnableLocalFileSupport = false` |
| Control embedded-payload or import behavior | `ExcelWorkbookHandlerOptions` |
| Opt in to logging WorkbookDoc payloads | Set `WorkbookToolsOptions.LogContentParameters = true` or `ExcelWorkbookToolSetOptions.LogContentParameters = true` |

## Quick Start

```csharp
using AIToolkit.Tools.Workbook.Excel;

var tools = ExcelWorkbookTools.CreateFunctions();
```

That creates the base `workbook_*` tool surface with an Excel handler already registered.

Hosted M365 example:

```csharp
using AIToolkit.Tools.Workbook;
using AIToolkit.Tools.Workbook.Excel;
using Azure.Identity;

var tools = ExcelWorkbookTools.CreateFunctions(
    new ExcelWorkbookToolSetOptions
    {
        WorkingDirectory = @"C:\repo",
        EnableLocalFileSupport = false,
        M365 = new ExcelWorkbookM365Options
        {
            Credential = new DefaultAzureCredential(),
            Scopes = ["https://graph.microsoft.com/.default"],
        },
        MaxReadLines = 8_000,
        LogContentParameters = false,
    });
```

With that configuration, the same tool set can handle both local file paths and hosted M365 references. If you want a hosted-only tool set, set `EnableLocalFileSupport = false`.

Supported hosted reference forms:

- SharePoint or OneDrive HTTPS workbook URLs, for example `https://contoso.sharepoint.com/sites/Docs/Shared%20Documents/forecast.xlsx`
- Current-user OneDrive drive-path references, for example `m365://drives/me/root/workbooks/forecast.xlsx`
- Stable item references, for example `m365://drives/{driveId}/items/{itemId}`
- Drive-path references, for example `m365://drives/{driveId}/root/workbooks/forecast.xlsx`

## Public API Reference

### ExcelWorkbookTools

| API | Purpose |
| --- | --- |
| `ExcelWorkbookTools.CreateFunctions(...)` | Creates the complete `workbook_*` toolset with a registered Excel handler |
| `ExcelWorkbookTools.GetSystemPromptGuidance(...)` | Returns Excel-aware prompt guidance that combines the generic workbook instructions with enabled Excel capabilities |
| `ExcelWorkbookTools.CreateReadFileFunction(...)` | Creates `workbook_read_file` with a registered Excel handler |
| `ExcelWorkbookTools.CreateWriteFileFunction(...)` | Creates `workbook_write_file` with a registered Excel handler |
| `ExcelWorkbookTools.CreateEditFileFunction(...)` | Creates `workbook_edit_file` with a registered Excel handler |
| `ExcelWorkbookTools.CreateGrepSearchFunction(...)` | Creates `workbook_grep_search` with a registered Excel handler |
| `ExcelWorkbookTools.CreateSpecificationLookupFunction(...)` | Creates `workbook_spec_lookup` with a registered Excel handler |
| `ExcelWorkbookTools.CreateHandler(...)` | Creates an `IWorkbookHandler` for Excel formats |
| `ExcelWorkbookTools.CreateM365ReferenceResolver(...)` | Creates an `IWorkbookReferenceResolver` for hosted OneDrive and SharePoint Excel workbooks |

### ExcelWorkbookToolSetOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `WorkingDirectory` | Current process directory | Default root used to resolve relative local workbook paths |
| `ReferenceResolver` | `null` | Optional additional resolver for non-path workbook references |
| `MaxReadLines` | `2000` | Maximum WorkbookDoc lines returned when no explicit range is provided |
| `MaxEditFileBytes` | `1073741824` | Maximum file size allowed for exact WorkbookDoc edits |
| `MaxSearchResults` | `200` | Maximum workbook-content matches returned |
| `EnableLocalFileSupport` | `true` | Enables direct local Excel path handling |
| `PreferEmbeddedWorkbookDoc` | `true` | Prefer embedded canonical WorkbookDoc when present |
| `EnableBestEffortImport` | `true` | Import external Excel workbooks when no embedded payload exists |
| `M365` | `null` | Enables hosted OneDrive and SharePoint Excel references |
| `LoggerFactory` | `null` | Optional logger factory used for tool invocation logging |
| `LogContentParameters` | `false` | Include WorkbookDoc payload parameters in logs only when explicitly enabled |

### ExcelWorkbookHandlerOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `EnableLocalFileSupport` | `true` | Enables direct local `.xlsx`, `.xlsm`, `.xltx`, and `.xltm` path handling for read, write, edit, and grep |
| `PreferEmbeddedWorkbookDoc` | `true` | Prefer the embedded canonical WorkbookDoc payload when the workbook contains one |
| `EnableBestEffortImport` | `true` | Import external Excel workbooks to WorkbookDoc when no embedded canonical payload is present |
| `M365` | `null` | Enables hosted OneDrive and SharePoint Excel workbook references through Microsoft Graph |

### ExcelWorkbookM365Options

| Property | Default | Purpose |
| --- | --- | --- |
| `Credential` | `null` | The caller-supplied `TokenCredential` used for Microsoft Graph authentication |
| `Scopes` | `https://graph.microsoft.com/.default` | Optional Microsoft Graph scopes to request |

## Format Behavior

| Scenario | Behavior |
| --- | --- |
| Workbook written through `workbook_write_file` | The package embeds canonical WorkbookDoc in a custom XML part and renders a readable `.xlsx` workbook from it |
| Workbook edited through `workbook_edit_file` | The embedded canonical WorkbookDoc is updated and the visible workbook is regenerated |
| External workbook with no embedded payload | The package performs a best-effort Excel-to-WorkbookDoc import |
| Hosted M365 workbook reference | The package resolves the workbook through Microsoft Graph and then reads or writes the `.xlsx` stream through the same Excel handler |
| Round-trip fidelity | Lossless for workbooks written or rewritten by this package; best-effort for external imports |
| Formula cells | WorkbookDoc keeps the formula as the actual cell content and can optionally preserve the computed result as `result=...` |
| Advanced directives | Visible rendering focuses on common workbook features; unsupported advanced directives remain preserved in the embedded WorkbookDoc payload for tool-driven round-trips |

## Import and Render Notes

| Topic | Details |
| --- | --- |
| Common grid content | Reads and writes workbook title, sheet headings, anchored rows, formulas, hyperlinks, and shared strings |
| Styling | Preserves commonly used formatting such as number formats, font styles, foreground/background colors, and non-default alignment |
| Non-grid features | Imports merges, conditional formatting, tables, charts, and sparklines into WorkbookDoc where available |
| Default styling | Canonical WorkbookDoc omits default styling and default left/middle alignment when exporting |
| Best-effort scope | External imports are practical and compact, but not every Excel feature has a full visible renderer |
| Hosted search | `workbook_grep_search` can search hosted M365 workbooks when you pass explicit `workbook_references`; directory-based grep remains local-workspace only |

## WorkbookDoc Notes

WorkbookDoc is intentionally compact. The common case is:

```text
= Sales Summary
:wbdoc: 4

== Summary
[used A1:C4]
@A1 | Month | Revenue | Margin
@A2 | January | 1200 | [fmt="0.0%"] 0.35
@A3 | February | 1350 | [fmt="0.0%"] 0.42
[chart "Revenue Trend" type=column at=E2 size=420x260px]
- series column "Revenue" cat=A2:A3 val=B2:B3
[end]
```

Use `workbook_spec_lookup` when the model needs a compact reminder for more advanced WorkbookDoc constructs such as charts, conditional formatting, tables, pivots, or sparkline syntax.

## Sample

See `samples/AIToolkit.Tools.Workbook.Excel.Sample` for a runnable host that demonstrates:

- creating Excel workbooks from WorkbookDoc,
- reading those workbooks back through `workbook_read_file`,
- searching workbook content through `workbook_grep_search`,
- editing them with `workbook_edit_file`, and
- importing an external `.xlsx` that does not contain an embedded canonical payload, and
- optionally targeting hosted OneDrive or SharePoint Excel workbooks through Microsoft Graph.
