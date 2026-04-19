# AIToolkit.Tools.Document.GoogleDocs

`AIToolkit.Tools.Document.GoogleDocs` provides Google Docs support for `AIToolkit.Tools.Document`.

It works with hosted Google Docs references and the same canonical AsciiDoc syntax surface used by `AIToolkit.Tools.Document.Word`.

It uses Google Drive conversion to refresh the visible Google Doc body from a generated `.docx`, and it stores canonical AsciiDoc in a managed sidecar payload so read, write, and edit operations remain lossless for tool-authored documents.

Use `GoogleDocsDocumentTools.GetSystemPromptGuidance(...)` when you want the system prompt to reflect the hosted Google Docs capabilities and provider-specific AsciiDoc guidance.

## Example

```csharp
using AIToolkit.Tools.Document.GoogleDocs;
using Google.Apis.Auth.OAuth2;

var credential = GoogleCredential.GetApplicationDefault();
var tools = GoogleDocsDocumentTools.CreateFunctions(
    new GoogleDocsDocumentHandlerOptions
    {
        Workspace = new GoogleDocsWorkspaceOptions
        {
            Credential = credential,
        },
    });
```

For public, read-focused scenarios, `GoogleDocsWorkspaceOptions.ApiKey` is also supported. API-key mode typically cannot create, update, or access appData-backed managed payload files. If you already have a refreshable installed-app OAuth credential such as `UserCredential`, you can pass it through `GoogleDocsWorkspaceOptions.HttpClientInitializer`.

## Notes

- Tool-authored Google Docs keep canonical AsciiDoc in a managed sidecar payload so `document_read_file`, `document_write_file`, and `document_edit_file` remain lossless.
- The visible Google Doc body is refreshed through Google Drive Office-document conversion using the same AsciiDoc rendering engine as the Word provider.
- Existing Google Docs URLs and `gdocs://documents/{documentId}` references target existing documents.
- Use `gdocs://folders/root/documents/{title}` or `gdocs://folders/{folderId}/documents/{title}` when you want `document_write_file` or `document_edit_file` to create a new Google Doc by folder and title.
- `document_grep_search` can search hosted Google Docs when you pass explicit `document_references`; directory-based grep remains local-workspace search and does not crawl Drive automatically.
- The Google Docs bridge preserves the same non-standard role-style hints used by the Word provider, including alignment, color, underline shorthand, and the malformed-syntax recovery cases already tuned for agent output.