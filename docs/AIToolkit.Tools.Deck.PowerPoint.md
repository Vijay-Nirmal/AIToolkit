# AIToolkit.Tools.Deck.PowerPoint

`AIToolkit.Tools.Deck.PowerPoint` adds Microsoft PowerPoint `.pptx`/`.pptm`/`.potx`/`.potm` support to the provider-neutral `deck_*` tools from `AIToolkit.Tools.Deck`.

## Quick start

Use the package in one of these common ways.

### Create the full PowerPoint-backed `deck_*` tool set

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
        EnableLocalFileSupport = true,
        PreferEmbeddedDeckDoc = true,
        EnableBestEffortImport = true,
    });

// Pass functions to your AI host or agent runtime.
```

### Add provider-aware system prompt guidance

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var systemPrompt = PowerPointDeckTools.GetSystemPromptGuidance(
    "You are generating PowerPoint presentations.",
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });
```

### Expose only one deck tool

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var writeFunction = PowerPointDeckTools.CreateWriteFileFunction(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });

// Useful when a host wants a narrower tool surface.
```

### Convert PowerPoint to DeckDoc or render DeckDoc directly

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var readResult = await PowerPointDeckTemplateUtilities.ConvertPresentationToDeckDocAsync(
    "presentations/source-deck.pptx",
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
        EnableBestEffortImport = true,
    });

var writeResult = await PowerPointDeckTemplateUtilities.ConvertDeckDocToPresentationAsync(
    "presentations/preview.pptx",
    readResult.DeckDoc!,
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });
```

### Generate a reusable template from an existing deck

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var result = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
    chatClient,
    "presentations/source-deck.pptx",
    new PowerPointDeckTemplateGenerationOptions
    {
        ToolOptions = new PowerPointDeckToolSetOptions
        {
            WorkingDirectory = Environment.CurrentDirectory,
            EnableBestEffortImport = true,
        },
        GeneratedPresentationReference = "presentations/source-template-preview.pptx",
    });

// result.TemplateDeckDoc contains the reusable template text.
// result.GeneratedPresentationReference points to the rendered preview deck.
```

### Use the built-in template catalog

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var templates = PowerPointDeckTemplates.CreateDefaultTemplates();
var launchStory = templates.Single(template => template.Name == "launch-story");

// launchStory.DeckDoc is plain DeckDoc you can pass to deck_write_file.
```

## Common features

| Feature | What it does |
| --- | --- |
| PowerPoint-backed `deck_*` tool surface | Exposes `deck_read_file`, `deck_write_file`, `deck_edit_file`, `deck_grep_search`, `deck_spec_lookup`, `deck_asset_create`, `deck_asset_search`, `deck_template_list`, `deck_template_get`, and `deck_export_slide_images` |
| Canonical DeckDoc workflow | Uses the provider-neutral `AIToolkit.Tools.Deck` model, validation flow, and syntax guidance |
| Slide-aware editing | Supports exact DeckDoc replacements plus slide insert, replace, and delete workflows through `deck_edit_file` |
| DeckDoc syntax lookup | Uses the embedded specification index through `deck_spec_lookup` before drafting or editing DeckDoc |
| Direct host APIs | Adds non-AI `ConvertPresentationToDeckDocAsync`, `ConvertDeckDocToPresentationAsync`, and `ExportSlidesToImagesAsync` helpers for host code |
| Asset and template abstractions | Reuses the base library asset and template storage model, with PowerPoint built-ins layered on top |
| Open XML PowerPoint support | Reads and writes `.pptx`, `.pptm`, `.potx`, and `.potm` |
| Lossless round-trip | `deck_write_file` embeds canonical DeckDoc into the PowerPoint package |
| Best-effort import | Reads external presentations without embedded DeckDoc into canonical DeckDoc when enabled |
| Slide image export | Exports one PNG per slide for visual validation and template-authoring comparison loops on Windows with Microsoft PowerPoint installed |
| Native rendering | Writes layout-targeted text, lists, shapes, images, tables, charts, notes, hidden-slide state, common transitions, grouping, and supported animate directives into the visual slide output |
| Asset-backed images | Resolves `[asset ...]` and direct image references during write |
| Built-in templates | Ships with `signal-brief`, `board-update`, and `launch-story` templates |
| AI-assisted template generation | Uses `PowerPointDeckTemplateUtilities.CreateTemplateAsync` to read a source deck, draft a template, render a preview, export slide images, and iterate toward the original design |
| Custom templates from PPTX | Lets hosts turn an existing PowerPoint deck into a reusable template by generating DeckDoc and optionally storing it through `IDeckTemplateStore.StoreAsync` |
| Hosted Microsoft 365 | Resolves SharePoint and OneDrive presentation references through Microsoft Graph when configured |

## Supported formats

| Format | Read | Write | Notes |
| --- | --- | --- | --- |
| `.pptx` | Yes | Yes | Default PowerPoint presentation format |
| `.pptm` | Yes | Yes | Macro-enabled presentation package |
| `.potx` | Yes | Yes | PowerPoint template package |
| `.potm` | Yes | Yes | Macro-enabled template package |

## Main usage patterns

### Use the inherited base deck tool surface

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });

// The returned set includes the provider-neutral deck_* tools,
// backed by the PowerPoint handler for .pptx/.pptm/.potx/.potm files.
```

### Use spec lookup before authoring non-trivial DeckDoc

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var specLookup = PowerPointDeckTools.CreateSpecificationLookupFunction(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });

// Call this before drafting complex layout, chart, table, grouping, or animation syntax.
```

### Configure local file handling and import behavior

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var options = new PowerPointDeckToolSetOptions
{
    WorkingDirectory = @"D:\Presentations",
    EnableLocalFileSupport = true,
    PreferEmbeddedDeckDoc = true,
    EnableBestEffortImport = true,
};

var functions = PowerPointDeckTools.CreateFunctions(options);
```

### Export slide images for visual validation

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var exportResult = await PowerPointDeckTemplateUtilities.ExportSlidesToImagesAsync(
    "presentations/preview.pptx",
    new PowerPointDeckSlideImageExportOptions
    {
        ToolOptions = new PowerPointDeckToolSetOptions
        {
            WorkingDirectory = Environment.CurrentDirectory,
        },
        OutputDirectory = "artifacts/preview-slides",
        Width = 1600,
        Height = 900,
        Force = true,
    });

// exportResult.Slides contains one PNG path per slide.
```

### Create only the handler

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var handler = PowerPointDeckTools.CreateHandler(
    new PowerPointDeckHandlerOptions
    {
        EnableLocalFileSupport = true,
        PreferEmbeddedDeckDoc = true,
        EnableBestEffortImport = true,
    });

// Use this when you are composing Deck handlers yourself.
```

### Enable Microsoft 365 hosted references

```csharp
using Azure.Identity;
using AIToolkit.Tools.Deck.PowerPoint;

var options = new PowerPointDeckToolSetOptions
{
    WorkingDirectory = Environment.CurrentDirectory,
    M365 = new PowerPointDeckM365Options
    {
        Credential = new DefaultAzureCredential(),
        Scopes = ["https://graph.microsoft.com/.default"],
    },
};

var functions = PowerPointDeckTools.CreateFunctions(options);
```

### Create just the Microsoft 365 resolver

```csharp
using Azure.Identity;
using AIToolkit.Tools.Deck.PowerPoint;

var resolver = PowerPointDeckTools.CreateM365ReferenceResolver(
    new PowerPointDeckM365Options
    {
        Credential = new DefaultAzureCredential(),
    });

// Combine with DeckToolsOptions.ReferenceResolver when you need custom host wiring.
```

### Create a reusable in-memory template store

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var templateStore = PowerPointDeckTemplates.CreateDefaultStore();

// Supply this through PowerPointDeckToolSetOptions.TemplateStore if you want an explicit store instance.
```

### Create your own template from an existing PowerPoint deck

```csharp
using AIToolkit.Tools.Deck;
using AIToolkit.Tools.Deck.PowerPoint;

var templateStore = PowerPointDeckTemplates.CreateDefaultStore();

var generatedTemplate = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
    chatClient,
    "presentations/team-offsite-master.pptx",
    new PowerPointDeckTemplateGenerationOptions
    {
        ToolOptions = new PowerPointDeckToolSetOptions
        {
            WorkingDirectory = Environment.CurrentDirectory,
            EnableBestEffortImport = true,
        },
        GeneratedPresentationReference = "presentations/team-offsite-template-preview.pptx",
    });

if (generatedTemplate.Success && generatedTemplate.TemplateDeckDoc is not null)
{
    await templateStore.StoreAsync(
        new DeckTemplateRecord(
            Name: "team-offsite-template",
            Description: "Reusable template derived from the team offsite master deck.",
            DeckDoc: generatedTemplate.TemplateDeckDoc,
            Source: "user"));
}
```

## Microsoft 365 reference forms

When `PowerPointDeckM365Options` is configured, the package supports:

- SharePoint or OneDrive HTTPS URLs
- `m365://drives/{driveId}/items/{itemId}`
- `m365://drives/me/root/path/to/file.pptx`
- `m365://drives/{driveId}/root/path/to/file.pptx`

Use the drive-path form when creating a new hosted presentation.

## Base deck capabilities included here

`PowerPointDeckTools` is built on top of `AIToolkit.Tools.Deck`, so the PowerPoint package includes the same core deck-tool workflow in addition to the PowerPoint-specific provider behavior.

### Included tool surface

| Tool | What you get through the PowerPoint package |
| --- | --- |
| `deck_read_file` | Reads supported PowerPoint files into canonical DeckDoc, including slide-range paging |
| `deck_write_file` | Creates or fully rewrites PowerPoint decks from canonical DeckDoc |
| `deck_edit_file` | Applies exact-string or slide-aware edits to canonical DeckDoc stored in PowerPoint decks |
| `deck_grep_search` | Searches canonical DeckDoc across local or explicit resolver-backed deck references |
| `deck_spec_lookup` | Returns compact DeckDoc syntax guidance from the embedded spec index |
| `deck_asset_create` | Registers reusable deck assets for later `[asset ...]` use |
| `deck_asset_search` | Searches global and session-scoped deck assets |
| `deck_template_list` | Lists template names from the configured template store |
| `deck_template_get` | Returns full DeckDoc content for a named template |
| `deck_export_slide_images` | Exports one PNG per slide for visual comparison on Windows with Microsoft PowerPoint installed |

### Included base behaviors

| Base behavior | What it means in the PowerPoint package |
| --- | --- |
| Canonical validation | Writes and edits validate DeckDoc before persisting changes |
| Slide-aware operations | `deck_edit_file` supports slide replace, insert, and delete flows |
| Parse diagnostics | Syntax failures return line and column information plus DeckDoc guidance snippets |
| Prompt guidance | System prompt guidance combines the base deck guidance with PowerPoint-specific guidance |
| Extensibility | The PowerPoint package still participates in the generic `IDeckHandler`, `IDeckReferenceResolver`, `IDeckAssetInterceptor`, `IDeckTemplateStore`, and `IDeckToolPromptProvider` model |
| Custom template persistence | Hosts can store PPTX-derived DeckDoc as `DeckTemplateRecord` entries in any configured `IDeckTemplateStore` |

### Base workflow example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var functions = PowerPointDeckTools.CreateFunctions(
    new PowerPointDeckToolSetOptions
    {
        WorkingDirectory = Environment.CurrentDirectory,
    });

// Typical flow:
// 1. deck_spec_lookup for uncertain syntax
// 2. deck_write_file for a new presentation
// 3. deck_read_file or deck_edit_file for later refinement
```

## Public API reference

### PowerPointDeckTools

Static entry point for creating PowerPoint-backed `deck_*` tools, single-function variants, handlers, and system prompt guidance.

#### Main methods

| Method | Purpose |
| --- | --- |
| `GetSystemPromptGuidance(PowerPointDeckToolSetOptions)` | Returns provider-aware Deck system prompt guidance |
| `GetSystemPromptGuidance(string?, PowerPointDeckToolSetOptions)` | Appends provider-aware guidance to an existing prompt |
| `GetSystemPromptGuidance(PowerPointDeckHandlerOptions?, DeckToolsOptions?)` | Builds guidance from separate handler and generic deck options |
| `GetSystemPromptGuidance(string?, PowerPointDeckHandlerOptions?, DeckToolsOptions?)` | Appends guidance using separate handler and generic deck options |
| `CreateFunctions(PowerPointDeckToolSetOptions)` | Creates the full PowerPoint-backed `deck_*` tool set |
| `CreateFunctions(PowerPointDeckHandlerOptions?, DeckToolsOptions?)` | Creates the full tool set from split option objects |
| `CreateReadFileFunction(...)` | Creates only `deck_read_file` |
| `CreateWriteFileFunction(...)` | Creates only `deck_write_file` |
| `CreateEditFileFunction(...)` | Creates only `deck_edit_file` |
| `CreateGrepSearchFunction(...)` | Creates only `deck_grep_search` |
| `CreateSpecificationLookupFunction(...)` | Creates only `deck_spec_lookup` |
| `CreateAssetCreateFunction(...)` | Creates only `deck_asset_create` |
| `CreateAssetSearchFunction(...)` | Creates only `deck_asset_search` |
| `CreateTemplateListFunction(...)` | Creates only `deck_template_list` |
| `CreateTemplateGetFunction(...)` | Creates only `deck_template_get` |
| `CreateExportSlideImagesFunction(...)` | Creates only `deck_export_slide_images` |
| `CreateHandler(PowerPointDeckHandlerOptions?)` | Creates an `IDeckHandler` for PowerPoint formats |
| `CreateM365ReferenceResolver(PowerPointDeckM365Options)` | Creates an `IDeckReferenceResolver` for hosted Microsoft 365 presentations |

#### Single-function example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var readFunction = PowerPointDeckTools.CreateReadFileFunction(
    handlerOptions: new PowerPointDeckHandlerOptions
    {
        EnableLocalFileSupport = true,
        PreferEmbeddedDeckDoc = true,
    });
```

### PowerPointDeckTemplateUtilities

Direct host-facing utility surface for non-AI conversion helpers plus the AI-assisted template-generation workflow.

| Method | Purpose |
| --- | --- |
| `ConvertPresentationToDeckDocAsync(...)` | Reads a PowerPoint deck and returns canonical DeckDoc without requiring an agent loop |
| `ConvertDeckDocToPresentationAsync(...)` | Renders canonical DeckDoc into a PowerPoint presentation without requiring an agent loop |
| `ExportSlidesToImagesAsync(...)` | Exports one PNG per slide for visual validation or template review |
| `CreateTemplateAsync(...)` | Generates a reusable DeckDoc template, renders a preview deck, exports slide images, and iterates toward the source design |

#### Template utility example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var result = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
    chatClient,
    "presentations/source-deck.pptx",
    new PowerPointDeckTemplateGenerationOptions
    {
        ToolOptions = new PowerPointDeckToolSetOptions
        {
            WorkingDirectory = Environment.CurrentDirectory,
            EnableBestEffortImport = true,
        },
        GeneratedPresentationReference = "presentations/source-template-preview.pptx",
    });
```

### PowerPointDeckOperationResult

Base record for PowerPoint-specific helper results.

| Property | Purpose |
| --- | --- |
| `Success` | Indicates whether the operation completed successfully |
| `Message` | Optional guidance or error information |

### PowerPointDeckReadResult

| Property | Purpose |
| --- | --- |
| `PresentationReference` | Resolved presentation reference that was read |
| `DeckDoc` | Canonical DeckDoc recovered from the source presentation |
| `TotalSlideCount` | Total slides recovered from the presentation |
| `PreservesDeckDocRoundTrip` | Whether future writes and reads are expected to preserve the same DeckDoc exactly |

### PowerPointDeckWriteResult

| Property | Purpose |
| --- | --- |
| `PresentationReference` | Resolved presentation reference that was written |
| `DeckDoc` | Canonical DeckDoc recovered after the write completed |
| `PreservesDeckDocRoundTrip` | Whether future reads are expected to recover the same DeckDoc exactly |

### PowerPointDeckSlideImage

| Property | Purpose |
| --- | --- |
| `SlideNumber` | 1-based slide number |
| `ImagePath` | Absolute PNG path for the exported slide |

### PowerPointDeckSlideImageExportOptions

| Property | Purpose |
| --- | --- |
| `ToolOptions` | PowerPoint tool options used to resolve the source presentation reference |
| `OutputDirectory` | Destination directory for exported PNG files |
| `Width` | Optional export width in pixels |
| `Height` | Optional export height in pixels |
| `Force` | Allows overwriting an existing export directory |

### PowerPointDeckSlideImageExportResult

| Property | Purpose |
| --- | --- |
| `PresentationReference` | Resolved presentation reference that was exported |
| `OutputDirectory` | Directory containing the exported PNG files |
| `Slides` | One exported image record per slide |

### PowerPointDeckTemplateGenerationOptions

| Property | Purpose |
| --- | --- |
| `ToolOptions` | PowerPoint tool options used for read, write, spec lookup, and export |
| `GeneratedPresentationReference` | Output presentation reference for the rendered preview |
| `SourceSlideImageOutputDirectory` | Optional export directory for source slide PNG files |
| `GeneratedSlideImageOutputDirectory` | Optional export directory for preview slide PNG files |
| `AdditionalInstructions` | Extra authoring guidance appended to the template prompt |
| `SpecificationLookupQuery` | DeckDoc spec lookup query used before drafting the template |
| `MaxCorrectionRounds` | Maximum rendered correction rounds after the initial draft |
| `AttachSlideImagesToPrompts` | Attaches exported PNG files to the review prompts when the chat client supports images |
| `ExportWidth` | Slide export width used during visual comparison |
| `ExportHeight` | Slide export height used during visual comparison |

### PowerPointDeckTemplateGenerationResult

| Property | Purpose |
| --- | --- |
| `SourcePresentationReference` | Resolved source presentation reference |
| `GeneratedPresentationReference` | Resolved preview presentation reference generated from the template |
| `TemplateDeckDoc` | Final template DeckDoc produced by the utility |
| `IterationCount` | Number of rendered comparison rounds that completed |
| `SimilarEnough` | Whether the final preview was judged visually close enough to the source deck |
| `Summary` | Short summary of the generation outcome |
| `Issues` | Remaining issues reported for the final iteration |
| `SourceSlideImages` | Exported source slide image metadata |
| `GeneratedSlideImages` | Exported preview slide image metadata |

### PowerPointDeckToolSetOptions

Flattened host-facing configuration for the PowerPoint package plus the generic deck tool pipeline.

| Property | Purpose |
| --- | --- |
| `WorkingDirectory` | Base path for resolving local presentations and assets |
| `ReferenceResolver` | Optional extra resolver for non-path references |
| `MaxReadLines` | Default maximum DeckDoc lines returned by reads |
| `MaxReadSlides` | Default maximum slides returned by reads |
| `MaxEditFileBytes` | Maximum file size allowed for deck edits |
| `MaxSearchResults` | Maximum grep and spec lookup results |
| `EnableLocalFileSupport` | Enables direct local PowerPoint paths |
| `PreferEmbeddedDeckDoc` | Prefers embedded canonical DeckDoc during reads |
| `EnableBestEffortImport` | Enables import from external PowerPoint files without embedded DeckDoc |
| `M365` | Optional Microsoft 365 hosted presentation settings |
| `AssetInterceptor` | Optional asset storage and resolution abstraction |
| `AssetSessionId` | Optional session scope for assets |
| `TemplateStore` | Optional template store; built-ins are used when omitted |
| `LoggerFactory` | Optional logger factory for tool-call logging |
| `LogContentParameters` | Includes DeckDoc payload parameters in logs when enabled |
| `AdditionalHandlers` | Optional extra deck handlers appended after the PowerPoint handler |
| `AdditionalPromptProviders` | Optional extra prompt providers appended after the PowerPoint prompt provider |

#### Configuration example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var options = new PowerPointDeckToolSetOptions
{
    WorkingDirectory = Environment.CurrentDirectory,
    MaxReadLines = 4000,
    MaxReadSlides = 40,
    MaxSearchResults = 100,
    EnableLocalFileSupport = true,
    PreferEmbeddedDeckDoc = true,
    EnableBestEffortImport = true,
};
```

### PowerPointDeckHandlerOptions

Narrower handler-only configuration when you want to wire the provider into generic Deck plumbing yourself.

| Property | Purpose |
| --- | --- |
| `EnableLocalFileSupport` | Enables local `.pptx`/`.pptm`/`.potx`/`.potm` paths |
| `PreferEmbeddedDeckDoc` | Uses embedded DeckDoc when present |
| `EnableBestEffortImport` | Imports external presentations into best-effort DeckDoc when embedded DeckDoc is absent |
| `M365` | Optional hosted Microsoft 365 reference settings |

#### Handler-only example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

var handlerOptions = new PowerPointDeckHandlerOptions
{
    EnableLocalFileSupport = true,
    PreferEmbeddedDeckDoc = true,
    EnableBestEffortImport = false,
};
```

### PowerPointDeckM365Options

Configuration for hosted PowerPoint references resolved through Microsoft Graph.

| Property | Purpose |
| --- | --- |
| `Credential` | Azure credential used for Microsoft Graph authentication |
| `Scopes` | Optional Microsoft Graph scopes; defaults to `https://graph.microsoft.com/.default` |

#### Microsoft 365 example

```csharp
using Azure.Identity;
using AIToolkit.Tools.Deck.PowerPoint;

var m365 = new PowerPointDeckM365Options
{
    Credential = new DefaultAzureCredential(),
    Scopes = ["https://graph.microsoft.com/.default"],
};
```

### PowerPointDeckTemplates

Built-in template helper for hosts that want a ready-made template catalog.

| Method | Purpose |
| --- | --- |
| `CreateDefaultStore()` | Creates an in-memory `IDeckTemplateStore` preloaded with the built-in templates |
| `CreateDefaultTemplates()` | Returns the built-in `DeckTemplateRecord` list |

#### Built-in template example

```csharp
using AIToolkit.Tools.Deck.PowerPoint;

foreach (var template in PowerPointDeckTemplates.CreateDefaultTemplates())
{
    Console.WriteLine($"{template.Name}: {template.Description}");
}
```

## Built-in templates

| Template | Purpose |
| --- | --- |
| `signal-brief` | Warm executive briefing with a hero cover, agenda, and narrative update slides |
| `board-update` | Clean board-facing deck with headline slides, metric callouts, and decision framing |
| `launch-story` | Bold launch narrative with problem, proof, and rollout slides |

## Custom template workflow

The package supports custom templates in addition to the built-in catalog.

The supported flow is:

1. Call `PowerPointDeckTemplateUtilities.CreateTemplateAsync(...)` on an existing PowerPoint deck.
2. Let the utility read the deck, draft a reusable template, render a preview, export slide PNG files, and iterate toward the source design.
3. Store the resulting `TemplateDeckDoc` in an `IDeckTemplateStore` when you want to surface it later as a named template.
4. Use `deck_template_list` and `deck_template_get` to expose stored templates back to an agent.

The public `deck_*` tool surface still exposes template listing and retrieval. Template creation is now a first-class host utility layered on top of the same read, write, spec lookup, and slide-image-export behavior.

## Notes

- The concrete PowerPoint handler and Microsoft 365 resolver implementations are intentionally internal; the public package surface exposes them through factory methods that return `IDeckHandler` and `IDeckReferenceResolver`.
- For full DeckDoc language coverage, see `docs/DeckDoc-language-spec.md` and use `deck_spec_lookup` at runtime.
