# AIToolkit.Tools.Document.Word

`AIToolkit.Tools.Document.Word` provides Microsoft Office Word support for `AIToolkit.Tools.Document`.

It handles `.docx`, `.docm`, `.dotx`, and `.dotm` files and treats canonical AsciiDoc as the authoritative representation for read, write, and edit operations.

That representation is not limited to strict core-spec constructs. The Word provider can preserve and render non-standard role-style hints such as `[.text-center]`, `[.text-left]`, `[.text-right]`, `[.text-blue]`, `[.text-green]`, `[.text-yellow]`, `[.text-purple]`, `[.text-orange]`, `[.text-red]`, and `[.text-highlight]` when they are part of the embedded AsciiDoc payload.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Generic `document_*` tools with Word support | `WordDocumentTools.CreateFunctions(...)` |
| Word-aware system prompt guidance | `WordDocumentTools.GetSystemPromptGuidance(...)` |
| Just the reusable Word handler | `WordDocumentTools.CreateHandler(...)` |
| Hosted OneDrive or SharePoint Word docs | Configure `WordDocumentHandlerOptions.M365` |
| Disable local `.docx` path handling | Set `WordDocumentHandlerOptions.EnableLocalFileSupport = false` |
| Control import behavior for external Word files | `WordDocumentHandlerOptions` |
| Apply final branding or other Word-specific changes | `WordDocumentHandlerOptions.PostProcessor` |

## Quick Start

```csharp
using AIToolkit.Tools.Document.Word;

var tools = WordDocumentTools.CreateFunctions();
```

That creates the base `document_*` tool surface with a Word handler already registered.

## Hosted M365 Documents

The Word package can also resolve hosted OneDrive and SharePoint documents through Microsoft Graph when you provide a `TokenCredential`.

```csharp
using AIToolkit.Tools.Document;
using AIToolkit.Tools.Document.Word;
using Azure.Identity;

var tools = WordDocumentTools.CreateFunctions(
	new WordDocumentHandlerOptions
	{
		M365 = new WordDocumentM365Options
		{
			Credential = new DefaultAzureCredential(),
			Scopes = ["https://graph.microsoft.com/.default"],
		},
	},
	new DocumentToolsOptions
	{
		WorkingDirectory = @"C:\repo",
		MaxReadLines = 8_000,
	});
```

With that configuration, the same tool set can handle both local file paths and hosted M365 references. If you want a hosted-only tool set, set `EnableLocalFileSupport = false`.

Supported hosted reference forms:

- SharePoint or OneDrive HTTPS document URLs, for example `https://contoso.sharepoint.com/sites/Docs/Shared%20Documents/guide.docx`
- Current-user OneDrive drive-path references, for example `m365://drives/me/root/docs/release-notes.docx`
- Stable item references, for example `m365://drives/{driveId}/items/{itemId}`
- Drive-path references, for example `m365://drives/{driveId}/root/docs/release-notes.docx`

Use the drive-path form when you want `document_write_file` to create a new hosted file. Prefer `m365://drives/me/root/...` when the current user's OneDrive is the target and you do not have a drive ID yet. HTTPS URLs and item-id references target existing hosted documents.

Example usage:

```csharp
using AIToolkit.Tools.Document.Word;
using Azure.Identity;
using Microsoft.Extensions.AI;

var tools = WordDocumentTools.CreateFunctions(
	new WordDocumentHandlerOptions
	{
		M365 = new WordDocumentM365Options
		{
			// Replace this with the TokenCredential your environment requires.
			Credential = new DefaultAzureCredential(),
		},
	});

var writeFunction = tools.Single(tool => tool.Name == "document_write_file");
await writeFunction.InvokeAsync(new AIFunctionArguments
{
	["document_reference"] = "m365://drives/{driveId}/root/docs/release-notes.docx",
	["content"] = "= Release Notes\n\nGenerated through Microsoft Graph.",
});

var readFunction = tools.Single(tool => tool.Name == "document_read_file");
var readResult = await readFunction.InvokeAsync(new AIFunctionArguments
{
	["document_reference"] = "https://contoso.sharepoint.com/sites/Docs/Shared%20Documents/release-notes.docx",
});
```

## Public API Reference

### WordDocumentTools

| API | Purpose |
| --- | --- |
| `WordDocumentTools.CreateFunctions(...)` | Creates the complete `document_*` toolset with a registered Word handler |
| `WordDocumentTools.GetSystemPromptGuidance(...)` | Returns Word-aware prompt guidance that combines the generic document instructions with the enabled Word capabilities |
| `WordDocumentTools.CreateReadFileFunction(...)` | Creates `document_read_file` with a registered Word handler |
| `WordDocumentTools.CreateWriteFileFunction(...)` | Creates `document_write_file` with a registered Word handler |
| `WordDocumentTools.CreateEditFileFunction(...)` | Creates `document_edit_file` with a registered Word handler |
| `WordDocumentTools.CreateGrepSearchFunction(...)` | Creates `document_grep_search` with a registered Word handler |
| `WordDocumentTools.CreateM365ReferenceResolver(...)` | Creates an `IDocumentReferenceResolver` for hosted OneDrive and SharePoint Word documents |
| `WordDocumentTools.CreateHandler(...)` | Creates an `IDocumentHandler` for Word formats |

### WordDocumentHandlerOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `EnableLocalFileSupport` | `true` | Enables direct local `.docx`, `.docm`, `.dotx`, and `.dotm` path handling for read, write, edit, and grep |
| `PreferEmbeddedAsciiDoc` | `true` | Prefer the embedded canonical AsciiDoc payload when the document contains one |
| `EnableBestEffortImport` | `true` | Import external Word files to AsciiDoc when no embedded canonical payload is present |
| `PostProcessor` | `null` | Applies final Word-level changes such as branding, stamps, or custom package mutations before the write completes |
| `M365` | `null` | Enables hosted OneDrive and SharePoint Word document references through Microsoft Graph |

### WordDocumentM365Options

| Property | Default | Purpose |
| --- | --- | --- |
| `Credential` | `null` | The caller-supplied `TokenCredential` used for Microsoft Graph authentication |
| `Scopes` | `https://graph.microsoft.com/.default` | Optional Microsoft Graph scopes to request |

## Format Behavior

| Scenario | Behavior |
| --- | --- |
| Document written through `document_write_file` | The package embeds canonical AsciiDoc in a custom XML part and renders a readable Word body from it |
| Document edited through `document_edit_file` | The embedded canonical AsciiDoc is updated and the visible Word body is regenerated |
| `:toc:` is present | The rendered Word body contains a generated table of contents with internal anchor links to headings |
| External Word document with no embedded payload | The package performs a best-effort Word-to-AsciiDoc import |
| Hosted M365 document reference | The package resolves the document through Microsoft Graph and then reads or writes the `.docx` stream through the same Word handler |
| Round-trip fidelity | Lossless for documents written or rewritten by this package; best-effort for external imports |
| Final customization | An optional post-processor can modify the generated `WordprocessingDocument` before the write completes |

## Non-Standard Syntax Notes

| Syntax | Meaning in this provider | Portability note |
| --- | --- | --- |
| `[.text-center]`, `[.text-left]`, `[.text-right]` | Paragraph or heading alignment hint used by the Word renderer | Not guaranteed to be interpreted by other AsciiDoc processors |
| `[.text-blue]`, `[.text-green]`, `[.text-yellow]`, `[.text-purple]`, `[.text-orange]`, `[.text-red]` | Color hint used by the Word renderer | Provider-specific styling hint, not a core-spec color feature |
| `[.text-highlight]` | Highlight hint used by the Word renderer | Provider-specific styling hint |
| `[.underline]#text#` and `+text+` | Underline hint understood by the Word renderer | Provider-specific underline behavior |
| `+++text+++` | Combined bold+underline shorthand understood by the Word renderer | Provider-specific shorthand |
| `[role="text-center.bold"]` | Additional role attribute applied to the target block | Provider-specific role parsing extension |
| `[.text-red]#Newer C\# improvements#` | Role span with an escaped literal `#` inside styled text | Required when `#` would otherwise terminate the role span |

## Notes

- The embedded canonical AsciiDoc is authoritative. This is what `document_read_file` returns for tool-authored documents.
- Provider-authored AsciiDoc can include non-standard role lines such as `[.text-center]` or `[.text-purple]`, inline underline shorthand such as `+text+`, and named role attributes such as `[role="text-center.bold"]`. Those are preserved because they influence Word rendering, even though they are not standard AsciiDoc guarantees.
- The visible Word body is intentionally readable in Word, but it is not intended to be a complete semantic renderer for every AsciiDoc construct.
- Best-effort imports preserve heading, paragraph, table, hyperlink, alignment, color, underline, bold+underline shorthand, and several inline formatting signals where practical.
- Hosted M365 support lives in the resolver layer, so local file paths and hosted references can be used in the same tool set.
- `document_grep_search` can search hosted M365 documents when you pass explicit `document_references`. Directory-based grep still scans the local workspace and does not enumerate remote document libraries automatically.
- When styled text contains a literal `#`, escape it inside role spans, for example `[.text-red]#Newer C\# improvements#`, or isolate the portion with passthrough markup such as `[.text-red]#Newer +C#+ improvements#`.

## Final Post-Processing

Use `IWordDocumentPostProcessor` when you need to add branding, watermarks, footers, or other final Word-level changes after the canonical AsciiDoc has been rendered.

```csharp
using AIToolkit.Tools.Document.Word;
using DocumentFormat.OpenXml.Wordprocessing;

public sealed class BrandingPostProcessor : IWordDocumentPostProcessor
{
	public ValueTask ProcessAsync(
		WordDocumentPostProcessorContext context,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var body = context.MainDocumentPart.Document.Body ?? context.MainDocumentPart.Document.AppendChild(new Body());
		body.AppendChild(new Paragraph(new Run(new Text("Contoso Confidential"))));
		return ValueTask.CompletedTask;
	}
}

var tools = WordDocumentTools.CreateFunctions(
	new WordDocumentHandlerOptions
	{
		PostProcessor = new BrandingPostProcessor(),
	});
```

The post-processor receives the open `WordprocessingDocument`, the `MainDocumentPart`, the `DocumentHandlerContext`, and the canonical AsciiDoc that produced the document. Use `DocumentContext.ResolvedReference` for a provider-neutral identity; `DocumentContext.FilePath` is only populated for local-file-backed resolutions.

## Sample

See `samples/AIToolkit.Tools.Document.Word.Sample` for a runnable host that demonstrates:

- creating Word documents from canonical AsciiDoc,
- reading those documents back through `document_read_file`,
- searching their content through `document_grep_search`,
- editing them with `document_edit_file`, and
- importing an external `.docx` that does not contain an embedded canonical payload.