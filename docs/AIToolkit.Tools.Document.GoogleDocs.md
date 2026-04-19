# AIToolkit.Tools.Document.GoogleDocs

`AIToolkit.Tools.Document.GoogleDocs` provides hosted Google Docs support for `AIToolkit.Tools.Document`.

It works with canonical AsciiDoc, Google Docs URLs, and `gdocs://` references.

The package uses the same AsciiDoc rendering and import surface as `AIToolkit.Tools.Document.Word`. It renders canonical AsciiDoc into a generated `.docx`, refreshes the visible Google Doc through Google Drive conversion, and stores canonical AsciiDoc in a managed sidecar payload so tool-authored Google Docs remain lossless for read, write, and edit operations.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Generic `document_*` tools with Google Docs support | `GoogleDocsDocumentTools.CreateFunctions(...)` |
| Google Docs-aware system prompt guidance | `GoogleDocsDocumentTools.GetSystemPromptGuidance(...)` |
| Just the reusable Google Docs handler | `GoogleDocsDocumentTools.CreateHandler(...)` |
| Resolve Google Docs URLs or `gdocs://` references | Configure `GoogleDocsDocumentHandlerOptions.Workspace` |
| Search hosted Google Docs by reference | Use `document_grep_search` with explicit `document_references` containing Google Docs URLs or `gdocs://` references |
| Force best-effort import from the exported document body | Set `PreferManagedAsciiDocPayload = false` and `PreferEmbeddedAsciiDoc = false` |
| Apply final branding before upload | Set `GoogleDocsDocumentHandlerOptions.PostProcessor` |

## Quick Start

```csharp
using AIToolkit.Tools.Document.GoogleDocs;
using Google.Apis.Auth.OAuth2;

var tools = GoogleDocsDocumentTools.CreateFunctions(
    new GoogleDocsDocumentHandlerOptions
    {
        Workspace = new GoogleDocsWorkspaceOptions
        {
            Credential = GoogleCredential.GetApplicationDefault(),
        },
    });
```

That creates the base `document_*` tool surface with a Google Docs handler already registered.

For public, read-focused scenarios, you can also configure `GoogleDocsWorkspaceOptions.ApiKey` instead of a `GoogleCredential`. API-key mode generally does not support create, update, or appData-backed payload operations. If you already have a refreshable Google user credential from an installed-app OAuth flow, you can pass it through `GoogleDocsWorkspaceOptions.HttpClientInitializer`.

## Supported Reference Forms

- Google Docs URLs such as `https://docs.google.com/document/d/{documentId}/edit`
- Stable document references such as `gdocs://documents/{documentId}`
- Folder-and-title references such as `gdocs://folders/root/documents/Release%20Notes` or `gdocs://folders/{folderId}/documents/Release%20Notes`

Use the folder-and-title form when you want `document_write_file` or `document_edit_file` to create a new Google Doc. Existing document URLs and `gdocs://documents/{documentId}` references target existing hosted documents.

`document_grep_search` can search hosted Google Docs when you pass explicit `document_references` with Google Docs URLs or `gdocs://` references. Directory-based grep still scans only the local workspace and does not enumerate Drive folders automatically.

## Public API Reference

### GoogleDocsDocumentTools

| API | Purpose |
| --- | --- |
| `GoogleDocsDocumentTools.CreateFunctions(...)` | Creates the complete `document_*` toolset with a registered Google Docs handler |
| `GoogleDocsDocumentTools.GetSystemPromptGuidance(...)` | Returns Google Docs-aware prompt guidance that combines the generic document instructions with the enabled Google Docs capabilities |
| `GoogleDocsDocumentTools.CreateReadFileFunction(...)` | Creates `document_read_file` with a registered Google Docs handler |
| `GoogleDocsDocumentTools.CreateWriteFileFunction(...)` | Creates `document_write_file` with a registered Google Docs handler |
| `GoogleDocsDocumentTools.CreateEditFileFunction(...)` | Creates `document_edit_file` with a registered Google Docs handler |
| `GoogleDocsDocumentTools.CreateGrepSearchFunction(...)` | Creates `document_grep_search` with a registered Google Docs handler |
| `GoogleDocsDocumentTools.CreateReferenceResolver(...)` | Creates an `IDocumentReferenceResolver` for hosted Google Docs references |
| `GoogleDocsDocumentTools.CreateHandler(...)` | Creates an `IDocumentHandler` for hosted Google Docs |

### GoogleDocsDocumentHandlerOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `PreferManagedAsciiDocPayload` | `true` | Prefer the managed canonical AsciiDoc sidecar stored for tool-authored Google Docs |
| `PreferEmbeddedAsciiDoc` | `true` | Prefer an embedded canonical AsciiDoc payload if an exported DOCX still contains one |
| `EnableBestEffortImport` | `true` | Import exported Google Docs content to AsciiDoc when no managed canonical payload is available |
| `PostProcessor` | `null` | Applies final Word-level changes before the generated DOCX is uploaded and converted into Google Docs |
| `Workspace` | `null` | Enables hosted Google Docs references through Google Drive |

### GoogleDocsWorkspaceOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `Credential` | `null` | The caller-supplied `GoogleCredential` used for Google Drive authentication |
| `HttpClientInitializer` | `null` | Optional Google HTTP client initializer such as `UserCredential` for installed-app OAuth flows |
| `ApiKey` | `null` | Optional Google API key for public Google Docs and Drive requests; typically read-only in practice |
| `Scopes` | Drive + Drive AppData | Optional Google API scopes to request |
| `ApplicationName` | `AIToolkit.Tools.Document.GoogleDocs` | Optional Google API application name |

## Format Behavior

| Scenario | Behavior |
| --- | --- |
| Document written through `document_write_file` | The package generates a `.docx` with canonical AsciiDoc, uploads it through Google Drive conversion, and stores canonical AsciiDoc in a managed sidecar payload |
| Document edited through `document_edit_file` | The canonical AsciiDoc sidecar is updated and the visible Google Doc body is refreshed through the same generated `.docx` conversion path |
| Tool-authored Google Doc | Lossless for read, write, and edit because the canonical AsciiDoc sidecar remains authoritative |
| External Google Doc with no managed payload | The package exports the Google Doc to `.docx` and performs a best-effort Word-to-AsciiDoc import |
| `document_grep_search` | Searches explicit hosted references when you pass `document_references`; directory-based scans still operate only on the local workspace |
| Final customization | An optional post-processor can modify the generated `WordprocessingDocument` before upload completes |

## Non-Standard Syntax Notes

The Google Docs bridge uses the same canonical AsciiDoc surface as the Word provider, including the same provider-specific styling hints and malformed-syntax recovery paths.

| Syntax | Meaning in this provider | Portability note |
| --- | --- | --- |
| `[.text-center]`, `[.text-left]`, `[.text-right]` | Paragraph or heading alignment hint used by the Word-backed bridge before conversion into Google Docs | Not guaranteed to be interpreted by other AsciiDoc processors |
| `[.text-blue]`, `[.text-green]`, `[.text-yellow]`, `[.text-purple]`, `[.text-orange]`, `[.text-red]` | Color hint used by the bridge renderer | Provider-specific styling hint, not a core-spec color feature |
| `[.text-highlight]` | Highlight hint used by the bridge renderer | Provider-specific styling hint |
| `[.underline]#text#` and `+text+` | Underline hint understood by the bridge renderer | Provider-specific underline behavior |
| `+++text+++` | Combined bold+underline shorthand understood by the bridge renderer | Provider-specific shorthand |
| `[role="text-center.bold"]` | Additional role attribute applied to the target block | Provider-specific role parsing extension |
| `[.text-red]#Newer C\# improvements#` | Role span with an escaped literal `#` inside styled text | Required when `#` would otherwise terminate the role span |

## Notes

- The managed canonical AsciiDoc sidecar is authoritative for tool-authored Google Docs. This is what `document_read_file` returns when it is available.
- Google Docs URLs and `gdocs://documents/{documentId}` references target existing documents. Folder-and-title references are the create path.
- The visible Google Doc body is refreshed through Google Drive Office-document conversion. This keeps the Google Doc readable while the canonical AsciiDoc sidecar stays authoritative for tool-driven edits.
- Best-effort imports preserve heading, paragraph, table, hyperlink, alignment, color, underline, bold+underline shorthand, and several malformed agent-output recoveries because the bridge uses the same Word importer as the Word provider.
- `document_grep_search` can search hosted Google Docs when you pass explicit `document_references`. Directory-based scans still remain local-workspace only.

## Final Post-Processing

Use `IWordDocumentPostProcessor` when you need to add branding, watermarks, footers, or other final document-level changes before the generated `.docx` is uploaded and converted into Google Docs.

```csharp
using AIToolkit.Tools.Document.GoogleDocs;
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
```

## Sample

See `samples/AIToolkit.Tools.Document.GoogleDocs.Sample` for a runnable host that demonstrates:

- connecting an interactive chat client to the full `document_*` tool surface,
- supplying the Google Docs-specific system prompt and tool wiring for an agentic chat loop,
- switching between application-default credentials, file-based credentials, explicit service-account auth, browser-based installed-app OAuth, and API-key mode through `appsettings.json`,
- reading and editing Google Docs through user-provided hosted references, and
- creating new Google Docs in Drive by folder and title reference.