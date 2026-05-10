namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Supplies PowerPoint-specific prompt guidance for the generic deck tools.
/// </summary>
internal sealed class PowerPointDeckPromptProvider(PowerPointDeckHandlerOptions options) : IDeckToolPromptProvider
{
    private readonly PowerPointDeckHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public DeckToolPromptContribution GetPromptContribution()
    {
        var locationLines = CreateLocationLines();
        var renderingLines = CreateRenderingLines();

        var writeLines = new List<string>(locationLines);
        writeLines.AddRange(renderingLines);

        var editLines = new List<string>(locationLines)
        {
            "Slide-aware edits can replace, delete, or insert full slide blocks without touching unrelated slides.",
            "Prefer the visually rendered subset for PowerPoint output: layout targets, text, lists, shapes, images, tables, charts, notes, hidden slides, slide transitions, supported group directives, and supported animate directives.",
            "Basic animate directives on rendered targets are written into native PowerPoint timing nodes, and resolved group directives become native PowerPoint group shapes. Provider-specific x directives are still preserved in embedded DeckDoc for future reads rather than fully rendered as visual slide behavior.",
        };

        var grepLines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            grepLines.Add("Local PowerPoint files can be searched by their .pptx, .pptm, .potx, and .potm extensions.");
        }

        if (_options.M365 is not null)
        {
            grepLines.Add("Hosted OneDrive and SharePoint PowerPoint files can also be searched by passing explicit deck_references to deck_grep_search. Directory-based grep still scans only the local workspace.");
        }

        var specLines = new List<string>
        {
            "Use deck_spec_lookup before drafting any non-trivial DeckDoc and resolve exact section_ids for the syntax you plan to use rather than guessing from memory.",
            "For PowerPoint-backed decks, look up layout-block-background, layout-fixed-lines, layout-slot-lines, object-lines-target, object-attrlists, payload-forms, pattern-image, pattern-icon, pattern-shape, pattern-line, pattern-numbered-list, table-block, chart-block, shared-motion, explicit-object-overrides, grouping, animate, slide-notes, slide-transition, and canonical-serialization before writing.",
            "For grouped or animated visual compositions, prefer integer-grid named geometry such as '@box1 B6 13x5 [shape rect fill=#DBEAFE stroke=#3B82F6]' and '@txt1 B7 11x2 [text size=16 bold fg=#1E40AF] | Core Optimization', then group and animate those names. Do not use fractional anchors like 'B6.5', do not add a blank '|' when a shape or image line has no payload, and define presets with '[motion ...]' rather than '[shared motion ...]'.",
            "When a slide uses split, grid, stack, or layout slots, fill those targets with '@target [attrlist] | payload' rather than '@name = target [...]'. If a target-bound slide object needs a stable id for grouping or animation, use '@name target [attrlist] | payload'. Treat each named target as one rectangle: when a card, sidebar, or panel needs multiple independent text/list/icon elements, subdivide it first with nested split/grid/stack or explicit geometry instead of dropping all of them onto the same target. The same rule applies inside layout blocks: do not bind both subtitle and body to the same slot source like '@subtitle = left' and '@body = left' unless 'left' is first subdivided into separate child targets, and do not emit '[obj ...]' inside a layout block because explicit object overrides are slide-level only. Tables and charts remain standalone blocks, but their 'at=...' placement may use either concrete geometry or named targets such as 'at=right'; choose the final table/chart placement on that block itself instead of trying to reposition it afterward with a second '[obj ... at=...]' line.",
        };

        var systemLines = new List<string>(locationLines);
        systemLines.AddRange(renderingLines);
        systemLines.Add("Use deck_export_slide_images when you need one PNG per slide for visual validation or template-authoring comparison loops. It requires Windows with Microsoft PowerPoint installed.");
        if (_options.M365 is not null)
        {
            systemLines.Add("Hosted OneDrive and SharePoint presentation references are addressed directly by reference. To search hosted content, pass explicit deck_references to deck_grep_search; directory-based grep remains local-workspace search.");
        }

        return new DeckToolPromptContribution(
            ReadFileDescriptionLines: locationLines,
            WriteFileDescriptionLines: writeLines,
            EditFileDescriptionLines: editLines,
            GrepSearchDescriptionLines: grepLines,
            SpecificationLookupDescriptionLines: specLines,
            SystemPromptLines: systemLines);
    }

    private List<string> CreateLocationLines()
    {
        var lines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            lines.Add($"Local PowerPoint file paths are supported for {string.Join(", ", PowerPointDeckHandler.SupportedFileExtensions)}.");
        }
        else
        {
            lines.Add("Local PowerPoint file paths are disabled in this tool set. Use hosted references or another resolver-backed reference instead.");
        }

        if (_options.M365 is not null)
        {
            lines.Add("Hosted PowerPoint files are supported through SharePoint or OneDrive HTTPS URLs, m365://drives/me/root/path/to/file.pptx for the current user's OneDrive when a drive ID is not given, and m365://drives/{driveId}/root/path/to/file.pptx or m365://drives/{driveId}/items/{itemId} when the drive ID is known.");
            lines.Add("To create a hosted PowerPoint file, use the drive-path form. Prefer m365://drives/me/root/path/to/file.pptx when the current user's OneDrive is the target and no drive ID is available.");
        }

        return lines;
    }

    private static List<string> CreateRenderingLines() =>
    [
        "Use shared [asset ...] directives for reusable images and let deck_asset_create/deck_asset_search manage the backing files instead of inventing ad hoc paths.",
        "The PowerPoint writer directly renders layout-targeted text, lists, shapes, image assets, tables, charts, speaker notes, hidden-slide state, object-override text content, common fade/push/wipe transitions, supported group directives, and supported animate directives into slides, then stores the full DeckDoc payload inside the package for lossless future reads.",
        "For visually dependable output, keep the authored deck centered on that rendered subset. Animate works for the supported effects and timing options, group works when members resolve to rendered objects, and provider-specific x directives should still be treated as preserved metadata unless a later implementation explicitly renders them.",
        "When opening an external .pptx that does not contain embedded DeckDoc, the PowerPoint reader performs a best-effort import of slide titles, visible text, speaker notes, hidden-slide state, common transitions, and tables into canonical DeckDoc.",
    ];
}
