# AIToolkit.Tools.Deck.PowerPoint

`AIToolkit.Tools.Deck.PowerPoint` adds Microsoft PowerPoint `.pptx`/`.pptm`/`.potx`/`.potm` support behind the generic `deck_*` tool surface from `AIToolkit.Tools.Deck`.

## What the provider adds

- A local-file PowerPoint deck handler.
- Optional Microsoft 365 OneDrive and SharePoint reference resolution.
- Best-effort external `.pptx` import when embedded DeckDoc is not present.
- Embedded DeckDoc payload storage for lossless round-trip reads after tool-authored writes.
- Built-in DeckDoc templates surfaced through `deck_template_list` and `deck_template_get`.

## Current rendering behavior

PowerPoint writes render the dependable subset directly into slides:

- slide titles
- body text and list content
- resolved image assets and icons referenced through shared `[asset ...]` directives
- shape and line primitives
- real PowerPoint charts for common column, bar, line, pie, doughnut, and combo deck charts
- real PowerPoint tables
- visible text-driven `[obj ...]` overrides such as rich-text and runs-based replacements
- speaker notes
- hidden-slide state
- common fade/push/wipe transitions
- real PowerPoint group shapes for `[group ...]` when the referenced members resolve to rendered objects
- basic `[animate ...]` directives for rendered targets, including fade, wipe, zoom, fly, grow, and spin entrance, exit, emphasis, and click or after-previous timing

The exact DeckDoc source is also embedded into the package, so extension and other advanced directives remain lossless for future `deck_read_file` calls even when the visual `.pptx` rendering is centered on the directly rendered subset above.

External `.pptx` reads without embedded DeckDoc use a best-effort importer that recovers slide titles, visible text, speaker notes, hidden-slide state, common transitions, and PowerPoint tables into canonical DeckDoc.

## Microsoft 365 references

When `PowerPointDeckM365Options` is configured, the provider can resolve:

- SharePoint or OneDrive HTTPS URLs
- `m365://drives/{driveId}/items/{itemId}`
- `m365://drives/me/root/path/to/file.pptx`
- `m365://drives/{driveId}/root/path/to/file.pptx`

## Example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });
```
