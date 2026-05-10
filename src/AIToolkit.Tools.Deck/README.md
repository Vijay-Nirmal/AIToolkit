# AIToolkit.Tools.Deck

`AIToolkit.Tools.Deck` provides provider-neutral `deck_*` tools for reading, writing, editing, searching, and looking up syntax for canonical DeckDoc presentations.

## Included tools

- `deck_read_file`
- `deck_write_file`
- `deck_edit_file`
- `deck_grep_search`
- `deck_spec_lookup`
- `deck_asset_create`
- `deck_asset_search`
- `deck_template_list` when a template store is configured
- `deck_template_get` when a template store is configured

## What this package does

- Defines the stable public AI-function surface for DeckDoc.
- Enforces read-before-write stale state checks.
- Supports slide-range reads with total slide counts.
- Supports exact-string edits and slide-aware insert/replace/delete operations.
- Returns syntax errors with exact line and column information plus DeckDoc guidance snippets.
- Provides provider-neutral abstractions for reference resolvers, deck handlers, prompt contributors, assets, and templates.

## Provider model

Use this package with a provider such as `AIToolkit.Tools.Deck.PowerPoint`.

The generic package does not know how to read or write `.pptx` files by itself. A provider contributes:

- an `IDeckHandler`
- optional `IDeckReferenceResolver` support
- provider-specific prompt guidance
- provider-specific template defaults

## DeckDoc lookup catalog

`deck_spec_lookup` uses the embedded `DeckDocSpecificationIndex.json` resource. Prefer exact `section_ids` such as `slide-transition`, `layout-split`, or `table-block` when you already know the feature, and fall back to keyword search only when needed. Keep that JSON aligned with `docs/DeckDoc-language-spec.md`.

## Example

```csharp
using AIToolkit.Tools.Deck;
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });
```
