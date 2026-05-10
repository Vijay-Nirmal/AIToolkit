# AIToolkit.Tools.Deck

`AIToolkit.Tools.Deck` is the provider-neutral package for canonical DeckDoc presentation tooling.

## Quick start

Use this package when you want the stable `deck_*` tool surface and will pair it with one or more providers such as `AIToolkit.Tools.Deck.PowerPoint`.

```csharp
using AIToolkit.Tools.Deck;
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });
```

## Tool surface

| Tool | Purpose |
| --- | --- |
| `deck_read_file` | Reads a supported deck into canonical DeckDoc, including slide-range paging and total slide count |
| `deck_write_file` | Creates or fully rewrites a deck from canonical DeckDoc |
| `deck_edit_file` | Applies exact-string or slide-aware insert/replace/delete edits |
| `deck_grep_search` | Searches canonical DeckDoc across local files or explicit resolver-backed references |
| `deck_spec_lookup` | Returns compact DeckDoc syntax guidance from the embedded spec index |
| `deck_asset_create` | Registers a reusable asset for later DeckDoc use |
| `deck_asset_search` | Searches global and session-scoped assets |
| `deck_template_list` | Lists named templates when a template store is available |
| `deck_template_get` | Returns one named template when a template store is available |

## Key behaviors

- `deck_read_file` supports `slide_offset` and `slide_limit`.
- `deck_edit_file` accepts `slide_operations` with `replace`, `insert_before`, `insert_after`, and `delete` actions.
- Write and edit operations validate DeckDoc before persisting changes.
- Parse failures include line and column information plus DeckDoc guidance snippets from `DeckDocSpecificationIndex.json`.
- Asset storage and template storage are provider-neutral abstractions.

## Extensibility points

| Type | Purpose |
| --- | --- |
| `IDeckHandler` | Reads and writes one concrete deck provider |
| `IDeckReferenceResolver` | Resolves URLs, IDs, or other non-path references into stream-backed deck resources |
| `IDeckAssetInterceptor` | Stores, searches, resolves, exports, and imports reusable assets |
| `IDeckTemplateStore` | Lists, fetches, exports, and imports named DeckDoc templates |
| `IDeckToolPromptProvider` | Adds provider-specific prompt guidance to the generic tool descriptions |

## DeckDoc spec lookup

`deck_spec_lookup` is powered by `src/AIToolkit.Tools.Deck/DeckDocSpecificationIndex.json`. Keep that resource aligned with `docs/DeckDoc-language-spec.md` whenever the language changes.
