using System.Text;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Builds the shared and provider-specific prompt text for the generic deck tools.
/// </remarks>
internal static class ToolPromptCatalog
{
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

    public static string GetDeckReadFileDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Reads a supported deck file and returns canonical DeckDoc for a slide range.",
            [
                "Use this when you need the deck as text for inspection, summarization, or planning edits.",
                "Returns the total slide count together with a numbered DeckDoc slice for the requested slide range.",
                "Use slide_offset and slide_limit to page through large decks.",
                "The deck_reference input can be a direct path or a resolver-backed deck reference such as a URL or ID.",
            ],
            GetProviderLines(options, static contribution => contribution.ReadFileDescriptionLines));

    public static string GetDeckWriteFileDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Creates or fully rewrites a supported deck from canonical DeckDoc.",
            [
                "Use this for new presentations or full rewrites of an existing deck.",
                "Read an existing deck first before overwriting it so stale changes are detected.",
                "deck_write_file expects one complete DeckDoc document, not a partial draft that only defines the header or layout blocks.",
                "DeckDoc basics: '= Presentation Name', optional shared directives, optional [layout ...] blocks, then slides starting with '== Slide Title'.",
                "Asset references such as [asset hero \"...\"] are resolved through the configured deck asset interceptor.",
            ],
            GetProviderLines(options, static contribution => contribution.WriteFileDescriptionLines));

    public static string GetDeckEditFileDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Edits a supported deck either by exact DeckDoc replacement or by slide-aware operations.",
            [
                "Use exact string replacement when you already know the precise DeckDoc fragment to patch.",
                "Use slide_operations for replacing, deleting, or inserting one or more slides by slide number.",
                "Read the deck first so stale changes are detected.",
                "Inserted or replacement slide text must start with '== Slide Title'.",
            ],
            GetProviderLines(options, static contribution => contribution.EditFileDescriptionLines));

    public static string GetDeckGrepSearchDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Searches canonical DeckDoc across supported decks.",
            [
                "Use this before deck_read_file when you only need specific keywords.",
                "Each match returns the surrounding text together with the slide number and slide title when available.",
                "Use deck_references to search explicit resolver-backed decks instead of scanning the local workspace.",
            ],
            GetProviderLines(options, static contribution => contribution.GrepSearchDescriptionLines));

    public static string GetDeckSpecificationLookupDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Looks up DeckDoc syntax guidance by section ID or keyword from the embedded DeckDoc specification index.",
            [
                "Prefer section_ids with stable values such as 'slide-transition', 'layout-split', 'table-block', or 'canonical-serialization' whenever you already know the feature you need.",
                "Use focused keywords such as 'layout split', 'table column widths', 'transition fade', or 'asset directive' only when you do not know the section ID yet.",
                "You can combine section_ids with keywords when you want exact sections plus nearby related guidance.",
                "Results return small syntax-oriented guidance snippets instead of the full markdown specification.",
                "Use this when a syntax error message asks for the right form of a DeckDoc feature.",
            ],
            GetProviderLines(options, static contribution => contribution.SpecificationLookupDescriptionLines));

    public static string GetDeckAssetCreateDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Registers a new asset for later use from DeckDoc [asset ...] directives.",
            [
                "Use this after generating or preparing an image, video, or audio file that a deck should reference.",
                "The configured deck asset interceptor decides where the file is stored.",
                "Use session_id for assets that should exist only for the current chat or tool session.",
                "asset_path must be unique within its scope.",
            ],
            []);

    public static string GetDeckAssetSearchDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Searches available global and session-scoped deck assets.",
            [
                "Use this to find reusable stock assets or session assets created earlier in the conversation.",
                "Searches asset_path, description, kind, and display name.",
                "When query is omitted, it lists the visible assets up to maxResults.",
            ],
            []);

    public static string GetDeckTemplateListDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Lists available named DeckDoc templates when a template store is configured.",
            [
                "Use this to discover inspiration templates or branded starting points before writing a new deck.",
                "Results return template name, description, and source.",
            ],
            []);

    public static string GetDeckTemplateGetDescription(DeckToolsOptions? options = null) =>
        BuildToolDescription(
            "Returns one named DeckDoc template when a template store is configured.",
            [
                "Use the exact template name returned by deck_template_list.",
                "The template is guidance and starting structure, not a mandatory output shape unless the user asks for it.",
            ],
            []);

    public static string GetDeckSystemPromptGuidance(DeckToolsOptions? options)
    {
        var sectionIds = string.Join(", ", DeckSpecificationCatalog.GetSectionIds());
        var lines = MergeUniqueLines(
            [
                "Use deck_read_file to inspect a deck as DeckDoc instead of raw binary bytes.",
                "DeckDoc basics: '= Presentation Name', shared directives like [theme ...] or [style ...], optional [layout ...] blocks, then slides starting with '== Slide Title'.",
                "Small example: '= Demo\\n:deckdoc: 1\\n:grid: 32x18\\n\\n[theme primary=#0B5FFF ink=#0F172A]\\n[style .title size=28 bold fg=$ink]\\n[style .body size=16 fg=$ink]\\n[asset hero \"./hero.png\"]\\n[layout cover]\\n[grid 40x22]\\n[transition fade dur=0.3s]\\n@title = B3:M4 [.title]\\n@hero = N3:Y12 [image fit=cover]\\n[end]\\n\\n== Cover\\n[use cover]\\n@title [text .title] | Hello world\\n@hero [image asset=hero alt=\"Team photo\"]\\n@body [list .body bullet=disc] | First point | Second point\\n[notes \"Lead with the headline.\"]'.",
                "Grid/layout basics: document grid uses ':grid: 32x18' only in the header; inside a layout block the override form is '[grid 40x22]'. Use uppercase spreadsheet anchors/spans like 'B3 24x2' or range shorthand like 'B3:M4' for geometry, and use '[split ...]', '[grid ...]', and '[stack ...]' to build common layouts without repeating coordinates.",
                "Use layout fixed lines for recurring always-visible brand chrome such as logos, badges, separators, and decorative hero panels; use slot lines for slide-specific content placeholders that each slide will fill.",
                "Before calling deck_write_file, finish the whole DeckDoc document in memory first. Do not probe with a header-only or layout-only fragment; the write call must include at least one '== Slide Title' block.",
                "Before deck_write_file or deck_edit_file, call deck_spec_lookup for every non-trivial syntax surface you plan to use and do not draft that part of the DeckDoc until the exact syntax has been looked up. When the deck uses animation presets, split/grid/stack targets, or standalone tables/charts, resolve 'shared-motion', 'object-lines-target', and the relevant block sections before drafting. You can do it in agentic loop as well which mean based on the first lookups, you can fo the follow-up lookups to clarify the syntax details of other commonly used features as well.",
                "When writing a new deck, prefer exact section_ids over free-text keywords and resolve the concrete syntax you need before drafting.",
                "When the user asks for a showcase, sample, or comprehensive deck, treat that as a request to cover the supported rendered feature set broadly but naturally: reusable layouts, backgrounds, text treatments, images, icons, shapes, lines, tables, charts, notes, hidden slides, transitions, supported animations, and grouping where it meaningfully improves the story. A canonical grouped example is '@box1 B6 13x5 [shape rect fill=#DBEAFE stroke=#3B82F6]', '@txt1 B7 11x2 [text size=16 bold fg=#1E40AF] | Core Optimization', then '[group pillar1 box1 txt1]' and '[animate pillar1 enter=fade order=1]'.",
                "Common syntax traps: '[theme ...]' uses named tokens like 'primary=#...' rather than raw color lists; use '[background ...]' rather than '[bg ...]'; define reusable animation presets with '[motion <name> ...]', not '[shared motion ...]'; use 'fill=' instead of 'bg=' and use 'fg=', 'fill=', or 'stroke=' instead of 'color='; use 'bullet=number' rather than 'bullet=numbered', and when a numbered list already uses 'bullet=number' write plain item text rather than literal '1.' or '2.' prefixes inside the payload; color entries are hex values or theme tokens rather than ad hoc values like 'stroke=none'; use '[state hidden]' for hidden slides; write areas as '[area main B4:M20]' or '[area main = B4:M20]'; anchors are integer spreadsheet-style cells such as 'B6' or 'AA14', not fractional coordinates like 'B6.5'; when a geometry-addressed object needs a stable id for grouping or animation, use a non-anchor name first such as '@box1 B6 13x5 [shape ...]' or '@txt1 B7 11x2 [text ...] | Label'; if an object has no payload, omit the pipe entirely instead of writing a blank '|'; explicit '[obj ...]' overrides are slide directives, not layout-block directives, so use fixed lines like '!heroPanel ...' inside layouts and only restyle or relocate them from the slide body; layouts do not inherit other layouts with '[use ...]' inside a layout block; '[split ...]', '[grid ...]', and '[stack ...]' are standalone directives, not object or slot attrlists such as '@slot = target [stack ...]' or '@zone B6:Z17 [split ...]'; each split chooses one axis, so do not combine 'rows=(...)' and 'cols=(...)' on the same split line; split directives may omit the source and then default to the current layout grid, but any split/grid/stack directive that needs reusable child targets must name them explicitly with 'as=...'; if a slide needs a reusable split/grid/stack region, define it with its own directive line first and then fill the resulting targets on later lines; if two slots bind to the same target rectangle they overlap, so subdivide the target first before assigning title and subtitle separately; for example, do not write '@subtitle = left' and '@body = left' against the same visual region unless 'left' is subdivided first into separate child targets; named targets are single rectangles, so if a slide needs several independent elements inside one card, sidebar, or panel, subdivide that target first with split/grid/stack or use explicit geometry instead of dropping multiple text/list/icon objects onto the same target; inside slides fill split/grid/stack/layout targets directly with '@target [attrlist] | payload' or '@target | payload', and when a target-bound object needs a stable id you may use '@name target [attrlist] | payload' rather than '@name = target [...]'; when a card or panel uses a background shape plus label, value, and caption, create an inner pad target or spacer rows first so text does not ride directly on the border; standalone '[table ...]' and '[chart ...]' blocks still use 'at=...' but that placement may now be either concrete geometry like 'at=B8 size=18x6' or a named target such as 'at=left' or 'at=cards[2]'; place tables and charts where you want them on the block itself rather than writing a second '[obj \"Chart Name\" at=...]' or '[obj Matrix at=...]' relocation line afterward; use '[obj existing-name at=...]' or '[obj target-name at=...]' when you need to relocate or restyle an object that was already defined earlier on the slide; icons use 'asset=' or 'ref=' rather than invented forms like 'icon name=TrendingUp'; line is an object kind in a compact object line rather than a standalone block, so write '@rule [line stroke=#CBD5E1]' or a slot/fixed-line equivalent instead of '[line ...] at=...'; geometry may use the canonical 'B4 29x3' form, and the tolerant shorthand 'B4:AD3' is also accepted to mean anchor B4 spanning through column AD for 3 rows; layout fixed lines may use geometry like '!B2 28x1 [line ...]', range shorthand like '!B2:Y22 [shape ...]', or a named form such as '!bg B2:Y22 [shape ...]' or '!right [shape ...]'; line objects use the documented line syntax instead of ad hoc properties like 'width='; list continuation lines are text items only, so do not append inline object attrlists such as '- item [shape ...]'; plain '| ...' payload text is literal, so if an explicit '[obj ...]' line needs inline emphasis use the looked-up rich-text surface rather than putting '[b]...[/b]' tags into plain payload text; names like '@row1' and '@col2' are target references, not grid anchors; reserve '@name = ...' for layout slot lines, not slide content; named geometry objects can use shorthand like '@pillar1 B4 8x10 [shape rect ...]' when they need stable group members; multiline list payloads may continue on following '| item' lines or '- item' lines; grouping is not a nested '[group ...] ... [end]' block; use '[group sidebar cap box line]' after the objects already exist; use ':size: wide' or a physical size like ':size: 13.333x7.5in', not ':size: 16:9'.",
                "Shared style definitions can be written as [style title ...] or [style .title ...]; object references like [text .title] resolve to the same shared style.",
                "Slide notes use single-line quoted syntax such as [notes \"Lead with the headline.\"], not a multi-line [notes] block.",
                "Use deck_spec_lookup multiple times before deck_write_file or deck_edit_file whenever the deck needs several less-common constructs; resolve each uncertain syntax surface before writing and then follow the looked-up syntax literally.",
                "Use deck_grep_search first when you only need keywords, then use deck_read_file with slide_offset and slide_limit to inspect the relevant slides.",
                "Use deck_write_file for new decks or full rewrites and deck_edit_file for exact-string patches or slide-aware insert, replace, or delete operations.",
                "Use deck_spec_lookup whenever you need compact syntax guidance for less common DeckDoc features, and prefer section_ids over free-text keywords when possible.",
                $"Available deck_spec_lookup section_ids: {sectionIds}.",
                "Use deck_asset_create and deck_asset_search for [asset ...] workflows instead of inventing file paths in DeckDoc.",
            ],
            GetProviderLines(options, static contribution => contribution.SystemPromptLines));

        var builder = new StringBuilder("# Using deck tools");
        foreach (var line in lines)
        {
            builder.Append('\n');
            builder.Append("- ");
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string BuildToolDescription(
        string summary,
        IEnumerable<string> baseLines,
        IEnumerable<string> providerLines)
    {
        var lines = MergeUniqueLines(baseLines, providerLines);
        var builder = new StringBuilder();
        builder.AppendLine(summary);
        builder.AppendLine();
        builder.AppendLine("Usage:");
        foreach (var line in lines)
        {
            builder.Append("- ");
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static List<string> GetProviderLines(
        DeckToolsOptions? options,
        Func<DeckToolPromptContribution, IEnumerable<string>?> selector)
    {
        if (options?.PromptProviders is null)
        {
            return [];
        }

        return MergeUniqueLines(
            options.PromptProviders
                .Where(static provider => provider is not null)
                .SelectMany(provider => selector(provider.GetPromptContribution()) ?? []));
    }

    private static List<string> MergeUniqueLines(
        IEnumerable<string> first,
        IEnumerable<string>? second = null)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in first)
        {
            AddLine(line, merged, seen);
        }

        if (second is not null)
        {
            foreach (var line in second)
            {
                AddLine(line, merged, seen);
            }
        }

        return merged;
    }

    private static void AddLine(string? line, List<string> merged, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var normalized = line.Trim();
        if (seen.Add(normalized))
        {
            merged.Add(normalized);
        }
    }
}

