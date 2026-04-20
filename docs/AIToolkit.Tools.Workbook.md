# AIToolkit.Tools.Workbook

`AIToolkit.Tools.Workbook` exposes five provider-neutral `Microsoft.Extensions.AI` workbook tools built around canonical WorkbookDoc.

WorkbookDoc is the compact workbook language used by this repository for spreadsheet reasoning and round-tripping. In the common case it uses a workbook heading (`=`), sheet headings (`==`), anchored rows such as `@B5 | a | b | c`, and a small set of directives like `[used ...]`, `[merge ...]`, `[fmt ...]`, `[type ...]`, `[chart ...]`, and `[spark ...]`.

The package is provider-neutral. Register one or more `IWorkbookHandler` implementations from packages such as `AIToolkit.Tools.Workbook.Excel`. Multiple handlers can be active in the same tool set, so a host can support Excel now and add providers such as Google Sheets later while keeping the same generic `workbook_*` tools.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Generic `workbook_*` tool surface | `WorkbookTools.CreateFunctions(...)` |
| Workbook-aware system prompt guidance | `WorkbookTools.GetSystemPromptGuidance(...)` |
| Excel workbook support | Add `AIToolkit.Tools.Workbook.Excel` and register `ExcelWorkbookTools.CreateHandler()` |
| Custom workbook format support | Implement `IWorkbookHandler` |
| Resolver-backed workbook references | Implement `IWorkbookReferenceResolver` |

## Quick Start

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

That gives the model a workbook tool surface where workbook contents are reasoned about as WorkbookDoc regardless of the backing format.

## Public API Reference

### WorkbookTools

| API | Purpose |
| --- | --- |
| `WorkbookTools.GetSystemPromptGuidance()` | Returns prompt text that explains how to use the `workbook_*` tools |
| `WorkbookTools.GetSystemPromptGuidance(string? currentSystemPrompt)` | Appends that guidance to an existing system prompt |
| `WorkbookTools.CreateFunctions(...)` | Creates the complete `workbook_*` toolset |
| `WorkbookTools.CreateReadFileFunction(...)` | Creates `workbook_read_file` |
| `WorkbookTools.CreateWriteFileFunction(...)` | Creates `workbook_write_file` |
| `WorkbookTools.CreateEditFileFunction(...)` | Creates `workbook_edit_file` |
| `WorkbookTools.CreateGrepSearchFunction(...)` | Creates `workbook_grep_search` |
| `WorkbookTools.CreateSpecificationLookupFunction(...)` | Creates `workbook_spec_lookup` |

### Extensibility Contracts

| Type | Purpose |
| --- | --- |
| `IWorkbookHandler` | Converts a provider-specific workbook format to and from canonical WorkbookDoc |
| `IWorkbookReferenceResolver` | Maps a tool input such as a path, URL, or ID to a resolved workbook resource |
| `WorkbookReferenceResolverContext` | Exposes the raw workbook reference, operation, working directory, options, and services to a resolver |
| `WorkbookReferenceResolution` | Exposes the resolved reference, extension, version/state metadata, and stream-based read/write access |
| `WorkbookHandlerContext` | Exposes the resolved reference, optional local file path, extension, length, options, services, and stream-based access |
| `WorkbookReadResponse` | Returns canonical WorkbookDoc plus round-trip metadata |
| `WorkbookWriteResponse` | Returns provider-side write metadata |

## Configuration Reference

### WorkbookToolsOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `WorkingDirectory` | Current process directory | Default root used to resolve relative workbook paths |
| `ReferenceResolver` | `null` | Optional resolver that can map `workbook_reference` from a path, URL, or ID to a resolved workbook resource |
| `MaxReadLines` | `2000` | Maximum WorkbookDoc lines returned when no explicit range is provided |
| `MaxEditFileBytes` | `1073741824` | Maximum file size allowed for exact WorkbookDoc edits |
| `MaxSearchResults` | `200` | Maximum workbook-content matches returned |
| `Handlers` | `null` | Registered `IWorkbookHandler` implementations |
| `LoggerFactory` | `null` | Optional logger factory used when workbook tool invocations should be logged |
| `LogContentParameters` | `false` | Include large WorkbookDoc payload parameters such as `content`, `old_string`, and `new_string` in logs |

## Tool Reference

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `workbook_read_file` | Reads a supported workbook and returns canonical WorkbookDoc as numbered text | `workbook_reference` | `offset`, `limit` | `IEnumerable<AIContent>` |
| `workbook_write_file` | Creates or fully rewrites a supported workbook from canonical WorkbookDoc | `workbook_reference`, `content` | None | `WorkbookWriteFileToolResult` |
| `workbook_edit_file` | Applies exact string replacement edits against canonical WorkbookDoc | `workbook_reference`, `old_string`, `new_string` | `replace_all` | `WorkbookEditFileToolResult` |
| `workbook_grep_search` | Searches text inside supported workbooks | `pattern` | `useRegex`, `includePattern`, `caseSensitive`, `contextLines`, `workingDirectory`, `workbookReferences`, `maxResults` | `WorkbookGrepSearchToolResult` |
| `workbook_spec_lookup` | Returns concise WorkbookDoc guidance for advanced syntax topics | `keywords` | `maxSections` | `WorkbookSpecLookupToolResult` |

Workbook behavior notes:

| Behavior | Details |
| --- | --- |
| Canonical abstraction | The model sees WorkbookDoc, not raw zipped Office XML |
| Spec help | `workbook_spec_lookup` searches an embedded WorkbookDoc feature index for advanced syntax such as charts, sparklines, conditional formatting, tables, and pivots |
| Reference resolution | `workbook_reference` can be a path or a higher-level workbook reference when `ReferenceResolver` maps it to a stable resolved workbook resource |
| Safe writes | `workbook_write_file` requires an existing workbook to be fully read before overwrite |
| Safe edits | `workbook_edit_file` rejects partial reads, stale file versions, and ambiguous replacements unless `replace_all` is `true` |
| Provider requirement | The base package does nothing without at least one registered `IWorkbookHandler` |
| Search scope | `workbook_grep_search` searches canonical workbook text, not raw zipped Office XML |
| Grep/read handoff | Each grep match returns a path or resolved reference and line offset that can be passed directly to `workbook_read_file` |

## Prompt Guidance

Append the built-in guidance to your host system prompt when you want the model nudged toward the dedicated `workbook_*` tools.

```csharp
var systemPrompt = WorkbookTools.GetSystemPromptGuidance(
    "You are a workbook automation assistant.");
```

## Sample

See `samples/AIToolkit.Tools.Workbook.Excel.Sample` for a runnable console host that seeds a sample workspace with WorkbookDoc-authored and externally-authored Excel workbooks.
