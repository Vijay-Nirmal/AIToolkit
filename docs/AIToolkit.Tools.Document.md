# AIToolkit.Tools.Document

`AIToolkit.Tools.Document` exposes four generic `Microsoft.Extensions.AI` document tools built around canonical AsciiDoc.

That canonical representation can also include provider-specific or non-standard AsciiDoc extensions that a handler preserves for round-trip or rendering behavior. For example, a provider can return role-style hints such as `[.text-center]` or `[.text-purple]` even though those are not part of the core AsciiDoc spec.

The `document_reference` input used by the read, write, and edit tools can also represent a URL or opaque document ID when you register an `IDocumentReferenceResolver` that maps that input to a resolved document resource.

It is the provider-neutral base package. Register one or more `IDocumentHandler` implementations from provider packages such as `AIToolkit.Tools.Document.Word`.

## At a Glance

| Need | Recommended choice |
| --- | --- |
| Generic `document_*` tool surface | `DocumentTools.CreateFunctions(...)` |
| A Word provider for `.docx` and related formats | Add `AIToolkit.Tools.Document.Word` and register `WordDocumentTools.CreateHandler()` |
| Custom document format support | Implement `IDocumentHandler` |

## Quick Start

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

That gives the model a document tool surface where document contents are reasoned about as AsciiDoc regardless of the backing format.

## Public API Reference

### DocumentTools

Use these APIs to create the `document_*` toolset.

| API | Purpose |
| --- | --- |
| `DocumentTools.GetSystemPromptGuidance()` | Returns prompt text that explains how to use the `document_*` tools |
| `DocumentTools.GetSystemPromptGuidance(string? currentSystemPrompt)` | Appends that guidance to an existing system prompt |
| `DocumentTools.CreateFunctions(...)` | Creates the complete `document_*` toolset |
| `DocumentTools.CreateReadFileFunction(...)` | Creates `document_read_file` |
| `DocumentTools.CreateWriteFileFunction(...)` | Creates `document_write_file` |
| `DocumentTools.CreateEditFileFunction(...)` | Creates `document_edit_file` |
| `DocumentTools.CreateGrepSearchFunction(...)` | Creates `document_grep_search` |

### Extensibility Contracts

| Type | Purpose |
| --- | --- |
| `IDocumentHandler` | Converts a provider-specific document format to and from canonical AsciiDoc |
| `IDocumentReferenceResolver` | Maps a tool input such as a path, URL, or ID to a resolved document resource |
| `DocumentReferenceResolverContext` | Exposes the raw document reference, operation, working directory, options, and services to a resolver |
| `DocumentReferenceResolution` | Exposes the resolved reference, extension, version/state metadata, and stream-based read/write access |
| `DocumentHandlerContext` | Exposes the resolved reference, optional local file path, extension, length, tool options, services, and stream-based access |
| `DocumentReadResponse` | Returns canonical AsciiDoc plus round-trip metadata |
| `DocumentWriteResponse` | Returns provider-side write metadata |

## Configuration Reference

### DocumentToolsOptions

| Property | Default | Purpose |
| --- | --- | --- |
| `WorkingDirectory` | Current process directory | Default root used to resolve relative document paths |
| `ReferenceResolver` | `null` | Optional resolver that can map the `document_reference` input from a path, URL, or ID to a resolved document resource |
| `MaxReadLines` | `2000` | Maximum AsciiDoc lines returned when no explicit range is provided |
| `MaxEditFileBytes` | `1073741824` | Maximum file size allowed for exact canonical AsciiDoc edits |
| `MaxSearchResults` | `200` | Maximum document-content matches returned |
| `Handlers` | `null` | Registered `IDocumentHandler` implementations |

## Tool Reference

| Tool | Purpose | Required parameters | Optional parameters | Returns |
| --- | --- | --- | --- | --- |
| `document_read_file` | Reads a supported document and returns canonical AsciiDoc as numbered text, including provider-specific non-standard syntax when the handler preserves it | `document_reference` | `offset`, `limit` | `IEnumerable<AIContent>` |
| `document_write_file` | Creates or fully rewrites a supported document from canonical AsciiDoc, including provider-specific style-hint lines when supported | `document_reference`, `content` | None | `DocumentWriteFileToolResult` |
| `document_edit_file` | Applies exact string replacement edits against canonical AsciiDoc, including provider-specific style-hint lines such as `[.text-center]` | `document_reference`, `old_string`, `new_string` | `replace_all` | `DocumentEditFileToolResult` |
| `document_grep_search` | Searches text inside supported documents, including provider-specific role lines and style-hint syntax | `pattern` | `useRegex`, `includePattern`, `caseSensitive`, `contextLines`, `workingDirectory`, `maxResults` | `DocumentGrepSearchToolResult` |

Document behavior notes:

| Behavior | Details |
| --- | --- |
| Canonical abstraction | The LLM sees AsciiDoc, not raw provider-specific XML or binary content |
| Provider extensions | The AsciiDoc returned by a handler can include provider-specific or non-standard syntax such as `[.text-center]` or `[.text-purple]` when the provider uses those lines as styling hints |
| Reference resolution | `document_reference` can be a direct path or a higher-level document reference when `ReferenceResolver` maps it to a stable resolved document resource |
| Safe writes | `document_write_file` requires an existing document to be fully read before overwrite |
| Safe edits | `document_edit_file` rejects partial reads, stale file versions, and ambiguous replacements unless `replace_all` is `true` |
| Provider requirement | The base package does nothing without at least one registered `IDocumentHandler` |
| Search scope | `document_grep_search` searches canonical document text, not raw zipped Office XML |
| Grep/read handoff | Each grep match returns a `Path` and line `Offset` that can be passed directly to `document_read_file` |

## Reference Resolver

Use `IDocumentReferenceResolver` when the model should refer to a document by something other than a local path or when the resolved document lives behind an API such as M365 or Google Docs.

```csharp
using AIToolkit.Tools.Document;

public sealed class UrlOrIdDocumentResolver : IDocumentReferenceResolver
{
    public ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (documentReference.StartsWith("doc:", StringComparison.OrdinalIgnoreCase))
        {
            var slug = documentReference["doc:".Length..];
            return ValueTask.FromResult<DocumentReferenceResolution?>(
                DocumentReferenceResolution.CreateFile(Path.Combine(context.WorkingDirectory, "docs", slug + ".docx")));
        }

        if (Uri.TryCreate(documentReference, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, "contoso.example", StringComparison.OrdinalIgnoreCase))
        {
            var remoteState = new RemoteWordDocumentState(uri);
            return ValueTask.FromResult<DocumentReferenceResolution?>(
                DocumentReferenceResolution.CreateStreamBacked(
                    resolvedReference: uri.AbsoluteUri,
                    extension: ".docx",
                    existsAsync: innerCancellationToken => remoteState.ExistsAsync(innerCancellationToken),
                    openReadAsync: innerCancellationToken => remoteState.OpenReadAsync(innerCancellationToken),
                    openWriteAsync: innerCancellationToken => remoteState.OpenWriteAsync(innerCancellationToken),
                    version: remoteState.Version,
                    length: remoteState.Length,
                    state: remoteState));
        }

        return ValueTask.FromResult<DocumentReferenceResolution?>(null);
    }
}
```

Return `null` when the resolver does not want to handle the input and the built-in path resolution should continue. For read-before-write and stale-file safety to work correctly, return a `DocumentReferenceResolution` with a stable `ResolvedReference` and `ReadStateKey`, and expose streams that the handler can reopen later.

`RemoteWordDocumentState` in the example is a placeholder for whatever SDK/client object your resolver uses to talk to services such as M365 or Google Docs.

## Prompt Guidance

Append the built-in guidance to your host system prompt when you want the model nudged toward the dedicated `document_*` tools.

```csharp
var systemPrompt = DocumentTools.GetSystemPromptGuidance(
    "You are a document automation assistant.");
```

## Sample

See `samples/AIToolkit.Tools.Document.Word.Sample` for a runnable console host that uses the Word provider and seeds a sample workspace with both canonicalized and externally-authored Word documents.