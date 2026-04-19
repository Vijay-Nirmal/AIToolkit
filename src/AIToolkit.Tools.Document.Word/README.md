# AIToolkit.Tools.Document.Word

`AIToolkit.Tools.Document.Word` provides Microsoft Office Word support for `AIToolkit.Tools.Document`.

It supports Open XML Word formats such as `.docx`, `.docm`, `.dotx`, and `.dotm`.

It can also resolve hosted OneDrive and SharePoint Word documents through Microsoft Graph when you configure `WordDocumentHandlerOptions.M365` with a caller-supplied `TokenCredential`.

Use `WordDocumentTools.GetSystemPromptGuidance(...)` when you want the system prompt to reflect the enabled Word capabilities such as local-file support and hosted M365 references.

## Example

```csharp
using AIToolkit.Tools.Document.Word;

var tools = WordDocumentTools.CreateFunctions();
```

Hosted M365 example:

```csharp
using AIToolkit.Tools.Document.Word;
using Azure.Identity;

var tools = WordDocumentTools.CreateFunctions(
	new WordDocumentHandlerOptions
	{
		M365 = new WordDocumentM365Options
		{
			Credential = new DefaultAzureCredential(),
		},
	});
```

## Notes

- Documents written through this package embed canonical AsciiDoc in a custom XML part so read, write, and edit round-trips are lossless.
- External Word files that do not contain embedded canonical AsciiDoc are imported through a best-effort Word-to-AsciiDoc projection.
- Hosted M365 document references can use SharePoint or OneDrive HTTPS URLs, `m365://drives/me/root/path/to/file.docx` for the current user's OneDrive when no drive ID is known, or `m365://drives/{driveId}/items/{itemId}` and `m365://drives/{driveId}/root/path/to/file.docx` when the drive ID is known.
- Local file paths and hosted M365 references can be used together in the same `WordDocumentTools.CreateFunctions(...)` tool set, or you can disable local `.docx` path handling with `WordDocumentHandlerOptions.EnableLocalFileSupport = false`.
- The visible Word body is rendered from the canonical AsciiDoc so the document remains readable in Word while the embedded payload stays authoritative for tool-driven edits.
- `WordDocumentHandlerOptions.PostProcessor` can apply final Word-level changes such as branding or footer content before the write completes.
- `document_grep_search` can search hosted M365 docs when you pass explicit `document_references`; directory-based grep remains local-workspace search.
- The renderer can preserve and interpret non-standard role-style hints such as `[.text-center]`, `[.text-left]`, `[.text-right]`, `[.text-blue]`, and `[.text-purple]` when they are present in the embedded AsciiDoc payload.