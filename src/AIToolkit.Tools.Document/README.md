# AIToolkit.Tools.Document

`AIToolkit.Tools.Document` exposes provider-neutral `Microsoft.Extensions.AI` tools for document files that can be converted to and from canonical AsciiDoc.

That AsciiDoc can include provider-specific or non-standard syntax when a handler preserves rendering hints for round-trip fidelity. For example, a provider may return role-style lines such as `[.text-center]` or `[.text-purple]`.

The `document_reference` input used by `document_read_file`, `document_write_file`, and `document_edit_file` can also be a URL or opaque document ID when you register an `IDocumentReferenceResolver` that returns a resolved document resource.

It is the abstraction layer for document providers such as `AIToolkit.Tools.Document.Word`.

## At a Glance

The package gives you four generic tools:

| Tool | Purpose |
| --- | --- |
| `document_read_file` | Reads a supported document and returns canonical AsciiDoc |
| `document_write_file` | Writes a supported document from canonical AsciiDoc |
| `document_edit_file` | Applies exact string edits against canonical AsciiDoc |
| `document_grep_search` | Searches text inside supported documents |

## Quick Start

Start with this package plus one provider package such as Word:

```csharp
using AIToolkit.Tools.Document;
using AIToolkit.Tools.Document.Word;

var tools = DocumentTools.CreateFunctions(
    new DocumentToolsOptions
    {
        WorkingDirectory = @"C:\repo",
        ReferenceResolver = new UrlOrIdDocumentResolver(),
        Handlers = [WordDocumentTools.CreateHandler()],
    });
```

## Extensibility Contract

Providers plug into the base tool surface by implementing `IDocumentHandler`.

| Type | Purpose |
| --- | --- |
| `IDocumentHandler` | Converts a provider-specific document format to and from canonical AsciiDoc |
| `IDocumentReferenceResolver` | Maps a tool input such as a path, URL, or ID to a resolved document resource |
| `DocumentReferenceResolution` | Exposes the resolved reference, extension, version/state metadata, and stream-based read/write access |
| `DocumentHandlerContext` | Exposes the resolved reference, optional local file path, extension, length, options, services, and stream-based access to a handler |
| `DocumentReadResponse` | Returns canonical AsciiDoc and round-trip metadata from a handler |
| `DocumentWriteResponse` | Returns provider-side write metadata |

## Notes

- `document_read_file` returns line-numbered AsciiDoc using `lineNumber<TAB>content` format.
- `document_grep_search` returns a path or resolved reference and line offset that can be passed directly into `document_read_file`.
- `document_grep_search` scans the local workspace by default, or searches only the explicit `document_references` you provide when you want resolver-backed URLs or document IDs included.
- Returned AsciiDoc can contain provider-specific non-standard syntax such as `[.text-center]` or `[.text-purple]` when a handler uses those lines as style hints.
- `document_write_file` and `document_edit_file` require a prior full read before changing an existing document.
- Configure `DocumentToolsOptions.ReferenceResolver` when you want `document_reference` to accept URLs or document IDs instead of only direct paths, and return a `DocumentReferenceResolution` rather than a raw file path string.
- The base package does not implement any document formats by itself. Register at least one `IDocumentHandler` from a provider package.