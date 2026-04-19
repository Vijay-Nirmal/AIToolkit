using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Renders the parsed AsciiDoc block model into WordprocessingML.
/// </summary>
internal static partial class WordAsciiDocRenderer
{
    public static void Write(MainDocumentPart mainPart, string asciiDoc)
    {
        var model = WordAsciiDocParser.Parse(asciiDoc);
        var document = new DocumentFormat.OpenXml.Wordprocessing.Document();
        var body = new Body();
        document.Append(body);
        mainPart.Document = document;

        var state = new RenderState(model);
        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            body.Append(CreateHeadingParagraph(model.Title, 1, WordAsciiDocBlockMetadata.Empty, isTitle: true));
        }

        if (ShouldRenderToc(model))
        {
            foreach (var paragraph in CreateTableOfContentsParagraphs(model, state))
            {
                body.Append(paragraph);
            }
        }

        foreach (var block in model.Blocks)
        {
            RenderBlock(mainPart, body, block, model, state);
        }

        mainPart.Document.Save();
    }

    public static string? TryExtractTitle(string asciiDoc) =>
        WordAsciiDocParser.Parse(asciiDoc).Title;

    private static void RenderBlock(
        MainDocumentPart mainPart,
        Body body,
        WordAsciiDocBlockModel block,
        WordAsciiDocDocumentModel model,
        RenderState state)
    {
        RenderBlockTitle(mainPart, body, block.Metadata);

        switch (block)
        {
            case WordAsciiDocHeadingBlockModel heading:
                var headingInfo = state.TakeNextHeadingInfo();
                body.Append(CreateHeadingParagraph(headingInfo.DisplayText, heading.Level, heading.Metadata, bookmarkName: headingInfo.BookmarkName, bookmarkId: headingInfo.BookmarkId));
                break;

            case WordAsciiDocParagraphBlockModel paragraph:
                body.Append(CreateParagraph(mainPart, paragraph.Text, paragraph.Metadata));
                break;

            case WordAsciiDocListBlockModel list:
                foreach (var paragraph in CreateListParagraphs(mainPart, list))
                {
                    body.Append(paragraph);
                }

                break;

            case WordAsciiDocTableBlockModel table:
                body.Append(CreateTable(mainPart, table));
                break;

            case WordAsciiDocDelimitedBlockModel delimited:
                body.Append(CreateDelimitedBlock(mainPart, delimited));
                break;

            case WordAsciiDocMacroBlockModel macro:
                body.Append(CreateMacroPlaceholder(mainPart, macro));
                break;

            case WordAsciiDocPageBreakBlockModel:
                body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                break;

            case WordAsciiDocThematicBreakBlockModel:
                body.Append(CreateThematicBreakParagraph());
                break;
        }
    }

    private static string RenderHeadingText(WordAsciiDocHeadingBlockModel heading, WordAsciiDocDocumentModel model, int[] sectionNumbers)
    {
        if (!model.HasAttribute("sectnums"))
        {
            return heading.Text;
        }

        var logicalLevel = Math.Max(1, heading.Level - 1);
        var maxLevel = int.TryParse(model.GetAttribute("sectnumlevels"), out var configuredMax) ? configuredMax : 3;
        if (logicalLevel > maxLevel || heading.Level == 1)
        {
            return heading.Text;
        }

        for (var index = logicalLevel; index < sectionNumbers.Length; index++)
        {
            sectionNumbers[index] = 0;
        }

        sectionNumbers[logicalLevel - 1]++;
        var prefix = string.Join('.', sectionNumbers.Take(logicalLevel).Where(static value => value > 0));
        return string.IsNullOrWhiteSpace(prefix) ? heading.Text : prefix + " " + heading.Text;
    }

    private static bool ShouldRenderToc(WordAsciiDocDocumentModel model) =>
        model.HasAttribute("toc");

    private static IEnumerable<Paragraph> CreateTableOfContentsParagraphs(WordAsciiDocDocumentModel model, RenderState state)
    {
        var levels = int.TryParse(model.GetAttribute("toclevels"), out var configuredLevels) ? Math.Clamp(configuredLevels, 1, 6) : 3;
        var title = string.IsNullOrWhiteSpace(model.GetAttribute("toc-title")) ? "Table of Contents" : model.GetAttribute("toc-title");

        yield return new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = "TOCHeading" },
                new SpacingBetweenLines { Before = "160", After = "80" }),
            new Run(new RunProperties(new Bold()), new Text(title!)));

        foreach (var entry in state.HeadingInfos.Where(static heading => heading.LogicalLevel >= 1).Where(heading => heading.LogicalLevel <= levels))
        {
            yield return CreateTableOfContentsEntryParagraph(entry);
        }
    }

    private static Paragraph CreateTableOfContentsEntryParagraph(RenderedHeadingInfo entry)
    {
        var paragraphProperties = new ParagraphProperties(
            new SpacingBetweenLines { After = "40" },
            new Indentation { Left = ((entry.LogicalLevel - 1) * 360).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var paragraph = new Paragraph(paragraphProperties);
        var hyperlink = new Hyperlink(
            new Run(
                CreateRunProperties(WordAsciiDocBlockMetadata.Empty, bold: false, italic: false, code: false, highlight: false, hyperlink: true),
                new Text(entry.DisplayText) { Space = SpaceProcessingModeValues.Preserve }))
        {
            Anchor = entry.BookmarkName,
            History = OnOffValue.FromBoolean(true),
        };
        paragraph.Append(hyperlink);
        return paragraph;
    }

    private static void RenderBlockTitle(MainDocumentPart mainPart, Body body, WordAsciiDocBlockMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            body.Append(CreateParagraph(mainPart, metadata.Title, metadata, italicOverride: true, spacingAfter: "40"));
        }
    }

    private static Paragraph CreateHeadingParagraph(string text, int level, WordAsciiDocBlockMetadata metadata, bool isTitle = false, string? bookmarkName = null, uint? bookmarkId = null)
    {
        var logicalLevel = Math.Max(1, level - 1);
        var fontSize = (isTitle ? 0 : logicalLevel) switch
        {
            0 => "40",
            1 => "34",
            2 => "30",
            3 => "26",
            4 => "24",
            5 => "22",
            _ => "20",
        };

        var paragraphProperties = new ParagraphProperties(
            new ParagraphStyleId { Val = isTitle ? "Title" : $"Heading{Math.Clamp(logicalLevel, 1, 9)}" },
            new SpacingBetweenLines { Before = isTitle ? "320" : "240", After = isTitle ? "200" : "120" });
        if (!isTitle)
        {
            paragraphProperties.Append(new OutlineLevel { Val = logicalLevel - 1 });
        }

        ApplyParagraphStyle(paragraphProperties, metadata, defaultAlignment: isTitle ? JustificationValues.Center : null);

        var paragraph = new Paragraph(paragraphProperties);
        if (!string.IsNullOrWhiteSpace(bookmarkName) && bookmarkId is not null)
        {
            paragraph.Append(new BookmarkStart { Name = bookmarkName, Id = bookmarkId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        paragraph.Append(
            new Run(
                CreateRunProperties(metadata, bold: true, italic: false, code: false, highlight: false, fontSize: fontSize),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

        if (!string.IsNullOrWhiteSpace(bookmarkName) && bookmarkId is not null)
        {
            paragraph.Append(new BookmarkEnd { Id = bookmarkId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        return paragraph;
    }

    private static IEnumerable<Paragraph> CreateListParagraphs(MainDocumentPart mainPart, WordAsciiDocListBlockModel list)
    {
        var orderedCounters = new Dictionary<int, int>();
        foreach (var item in list.Items)
        {
            var prefix = item.Kind switch
            {
                WordAsciiDocListKind.Unordered => "•",
                WordAsciiDocListKind.ChecklistChecked => "☒",
                WordAsciiDocListKind.ChecklistUnchecked => "☐",
                WordAsciiDocListKind.Callout => "↳",
                _ => ResolveOrderedPrefix(orderedCounters, item.Level),
            };

            yield return CreateParagraph(mainPart, prefix + " " + item.Text, list.Metadata, leftIndentTwips: 360 * (item.Level - 1));

            if (!string.IsNullOrWhiteSpace(item.ContinuationText))
            {
                yield return CreateParagraph(mainPart, item.ContinuationText, list.Metadata, leftIndentTwips: 360 * item.Level);
            }
        }
    }

    private static string ResolveOrderedPrefix(Dictionary<int, int> counters, int level)
    {
        var deeperLevels = counters.Keys.Where(existing => existing > level).ToArray();
        foreach (var deeperLevel in deeperLevels)
        {
            counters.Remove(deeperLevel);
        }

        counters[level] = counters.TryGetValue(level, out var count) ? count + 1 : 1;
        return counters[level].ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
    }

    private static Table CreateTable(MainDocumentPart mainPart, WordAsciiDocTableBlockModel table)
    {
        var columnWeights = ResolveColumnWeights(table.Metadata, table.Rows);
        var totalWeight = Math.Max(1, columnWeights.Sum());
        var tableProperties = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8 },
                new BottomBorder { Val = BorderValues.Single, Size = 8 },
                new LeftBorder { Val = BorderValues.Single, Size = 8 },
                new RightBorder { Val = BorderValues.Single, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableLayout { Type = TableLayoutValues.Fixed });

        var result = new Table(tableProperties);
        var grid = new TableGrid();
        foreach (var weight in columnWeights)
        {
            grid.Append(new GridColumn { Width = Math.Max(1, 9000 * weight / totalWeight).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        result.Append(grid);

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = new TableRow();
            var isHeaderRow = table.HasHeader && rowIndex == 0;
            var rowCells = NormalizeRowCells(table.Rows[rowIndex], columnWeights.Length);
            for (var cellIndex = 0; cellIndex < rowCells.Count; cellIndex++)
            {
                var cellText = rowCells[cellIndex];
                var cellParagraph = CreateParagraph(mainPart, cellText, table.Metadata, boldOverride: isHeaderRow, spacingAfter: "20");
                var cellWidth = Math.Max(1, 5000 * columnWeights[cellIndex] / totalWeight).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var cellProperties = new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = cellWidth });
                if (isHeaderRow)
                {
                    cellProperties.Append(new Shading { Fill = "EAEAEA", Val = ShadingPatternValues.Clear, Color = "auto" });
                }

                row.Append(new TableCell(cellParagraph, cellProperties));
            }

            result.Append(row);
        }

        return result;
    }

    private static Table CreateDelimitedBlock(MainDocumentPart mainPart, WordAsciiDocDelimitedBlockModel block)
    {
        var table = CreateSingleCellContainer(ResolveBlockFill(block), ResolveBlockBorder(block));
        var cell = table.Elements<TableRow>().Single().Elements<TableCell>().Single();

        if (!string.IsNullOrWhiteSpace(block.Label))
        {
            cell.Append(CreateParagraph(mainPart, block.Label, block.Metadata, boldOverride: true, spacingAfter: "40"));
        }

        if (block.Kind == WordAsciiDocDelimitedBlockKind.Source && !string.IsNullOrWhiteSpace(block.Language))
        {
            cell.Append(CreateParagraph(mainPart, block.Language, block.Metadata, italicOverride: true, spacingAfter: "40"));
        }

        foreach (var line in WordAsciiDocTextUtilities.NormalizeLineEndings(block.Content).Split('\n'))
        {
            var paragraph = block.Kind switch
            {
                WordAsciiDocDelimitedBlockKind.Source or WordAsciiDocDelimitedBlockKind.Literal => CreateParagraph(mainPart, line, block.Metadata, codeOverride: true, spacingAfter: "20"),
                WordAsciiDocDelimitedBlockKind.Quote or WordAsciiDocDelimitedBlockKind.Verse => CreateParagraph(mainPart, line, block.Metadata, italicOverride: true, leftIndentTwips: 240),
                _ => CreateParagraph(mainPart, line, block.Metadata),
            };
            cell.Append(paragraph);
        }

        return table;
    }

    private static Paragraph CreateMacroPlaceholder(MainDocumentPart mainPart, WordAsciiDocMacroBlockModel macro)
    {
        var label = macro.Kind switch
        {
            WordAsciiDocMacroKind.Image => $"Image: {macro.Label ?? macro.Target}",
            WordAsciiDocMacroKind.Audio => $"Audio: {macro.Label ?? macro.Target}",
            _ => $"Video: {macro.Label ?? macro.Target}",
        };

        return CreateParagraph(mainPart, label, macro.Metadata, italicOverride: true, centerOverride: true, colorOverride: "4F81BD");
    }

    private static Paragraph CreateThematicBreakParagraph() =>
        new(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 8, Space = 1, Color = "808080" }),
                new SpacingBetweenLines { Before = "80", After = "80" }));

    private static Table CreateSingleCellContainer(string fill, string borderColor)
    {
        var cell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));
        cell.TableCellProperties!.Append(new Shading { Fill = fill, Color = "auto", Val = ShadingPatternValues.Clear });

        return new Table(
            new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 8, Color = borderColor },
                    new BottomBorder { Val = BorderValues.Single, Size = 8, Color = borderColor },
                    new LeftBorder { Val = BorderValues.Single, Size = 8, Color = borderColor },
                    new RightBorder { Val = BorderValues.Single, Size = 8, Color = borderColor }),
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }),
            new TableRow(cell));
    }

    private static string ResolveBlockFill(WordAsciiDocDelimitedBlockModel block) =>
        block.Kind switch
        {
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "NOTE", StringComparison.OrdinalIgnoreCase) => "E8F1FB",
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "IMPORTANT", StringComparison.OrdinalIgnoreCase) => "FFF2CC",
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "WARNING", StringComparison.OrdinalIgnoreCase) => "FDE9E7",
            WordAsciiDocDelimitedBlockKind.Source or WordAsciiDocDelimitedBlockKind.Literal => "F5F5F5",
            _ => "FAFAFA",
        };

    private static string ResolveBlockBorder(WordAsciiDocDelimitedBlockModel block) =>
        block.Kind switch
        {
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "NOTE", StringComparison.OrdinalIgnoreCase) => "4F81BD",
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "IMPORTANT", StringComparison.OrdinalIgnoreCase) => "C9A227",
            WordAsciiDocDelimitedBlockKind.Admonition when string.Equals(block.Label, "WARNING", StringComparison.OrdinalIgnoreCase) => "C0504D",
            WordAsciiDocDelimitedBlockKind.Source or WordAsciiDocDelimitedBlockKind.Literal => "808080",
            _ => "A6A6A6",
        };

    private static Paragraph CreateParagraph(
        MainDocumentPart mainPart,
        string text,
        WordAsciiDocBlockMetadata metadata,
        bool boldOverride = false,
        bool italicOverride = false,
        bool codeOverride = false,
        bool centerOverride = false,
        string? colorOverride = null,
        int leftIndentTwips = 0,
        string spacingAfter = "80")
    {
        var paragraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = spacingAfter });
        ApplyParagraphStyle(paragraphProperties, metadata, centerOverride ? JustificationValues.Center : null, leftIndentTwips);

        var inlines = ParseInlineContent(text);
        ApplyAlignmentRolesFromInline(inlines, paragraphProperties);

        var paragraph = new Paragraph(paragraphProperties);
        foreach (var child in CreateInlineElements(mainPart, inlines, metadata, boldOverride, italicOverride, codeOverride, colorOverride))
        {
            paragraph.Append(child);
        }

        return paragraph;
    }

    private static List<WordAsciiDocInlineModel> ParseInlineContent(string text) =>
        ParseInlineContentCore(text, 0, text.Length);

    private static List<WordAsciiDocInlineModel> ParseInlineContentCore(string text, int start, int length)
    {
        var result = new List<WordAsciiDocInlineModel>();
        var builder = new StringBuilder();
        var end = start + length;
        for (var index = start; index < end; index++)
        {
            var current = text[index];
            if (current == '\\' && index + 1 < end)
            {
                builder.Append(text[index + 1]);
                index++;
                continue;
            }

            if (current == '\n')
            {
                FlushText(builder, result);
                result.Add(new WordAsciiDocLineBreakInlineModel());
                continue;
            }

            if (TryParseMalformedStyledLink(text, index, end, out var consumed, out var malformedStyledLinkInline))
            {
                FlushText(builder, result);
                result.Add(malformedStyledLinkInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseMalformedTrailingHashLink(text, index, end, out consumed, out var trailingHashLinkInline))
            {
                FlushText(builder, result);
                result.Add(trailingHashLinkInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseLink(text, index, end, out consumed, out var linkInline))
            {
                FlushText(builder, result);
                result.Add(linkInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseRoleAttributeSpan(text, index, end, out consumed, out var roleAttributeInline))
            {
                FlushText(builder, result);
                result.Add(roleAttributeInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseRoleSpan(text, index, end, out consumed, out var roleInline))
            {
                FlushText(builder, result);
                result.Add(roleInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseUnclosedRoleSpanToEnd(text, index, end, out consumed, out var unclosedRoleInline))
            {
                FlushText(builder, result);
                result.Add(unclosedRoleInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseRolePrefixedDelimitedInline(text, index, end, out consumed, out var rolePrefixedInline))
            {
                FlushText(builder, result);
                result.Add(rolePrefixedInline);
                index = consumed - 1;
                continue;
            }

            if (TryParseDelimitedInline(text, index, end, "+++", "+++", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true, Roles: ["underline"]), out var styledInline)
                || TryParseDelimitedInline(text, index, end, "**", "**", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true), out styledInline)
                || TryParseDelimitedInline(text, index, end, "*", "*", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true), out styledInline)
                || TryParseDelimitedInline(text, index, end, "__", "__", out consumed, children => new WordAsciiDocStyledInlineModel(children, Italic: true), out styledInline)
                || TryParseDelimitedInline(text, index, end, "_", "_", out consumed, children => new WordAsciiDocStyledInlineModel(children, Italic: true), out styledInline)
                || TryParseDelimitedInline(text, index, end, "`", "`", out consumed, children => new WordAsciiDocStyledInlineModel(children, Code: true), out styledInline)
                || TryParseDelimitedInline(text, index, end, "+", "+", out consumed, children => new WordAsciiDocStyledInlineModel(children, Roles: ["underline"]), out styledInline)
                || TryParseDelimitedInline(text, index, end, "#", "#", out consumed, children => new WordAsciiDocStyledInlineModel(children, Highlight: true), out styledInline))
            {
                FlushText(builder, result);
                result.Add(styledInline);
                index = consumed - 1;
                continue;
            }

            builder.Append(current);
        }

        FlushText(builder, result);
        return result;
    }

    private static bool TryParseMalformedStyledLink(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        var match = MalformedStyledLinkPattern().Match(text[index..end]);
        if (!match.Success || match.Index != 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = index + match.Length;
        var url = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[4].Value;
        var rawRoles = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[5].Value;
        var label = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[6].Value;
        inline = new WordAsciiDocStyledInlineModel(
            ParseInlineContent(label),
            HyperlinkUrl: url,
            Roles: ParseInlineRoleList(rawRoles));
        return true;
    }

    private static bool TryParseMalformedTrailingHashLink(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        var match = MalformedTrailingHashLinkPattern().Match(text[index..end]);
        if (!match.Success || match.Index != 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = index + match.Length;
        var label = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
        var url = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
        inline = new WordAsciiDocStyledInlineModel(ParseInlineContent(label), HyperlinkUrl: url);
        return true;
    }

    private static bool TryParseLink(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        var match = LinkPattern().Match(text[index..end]);
        if (!match.Success || match.Index != 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = index + match.Length;
        var label = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
        var url = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
        inline = new WordAsciiDocStyledInlineModel(ParseInlineContent(label), HyperlinkUrl: url);
        return true;
    }

    private static bool TryParseRoleSpan(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        if (index + 4 > end || text[index] != '[' || text[index + 1] != '.')
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var closingBracket = text.IndexOf(']', index + 2, end - (index + 2));
        if (closingBracket < 0 || closingBracket + 1 >= end || text[closingBracket + 1] != '#')
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var closingHash = FindClosingRoleSpanHash(text, closingBracket + 2, end);
        if (closingHash < 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = closingHash + 1;
        var roles = ParseInlineRoleList(text[(index + 2)..closingBracket]);
        var content = text[(closingBracket + 2)..closingHash];
        inline = new WordAsciiDocStyledInlineModel(ParseInlineContent(content), Roles: roles);
        return true;
    }

    private static bool TryParseRoleAttributeSpan(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        const string prefix = "[role=\"";
        if (index + prefix.Length + 3 > end || !text.AsSpan(index, prefix.Length).SequenceEqual(prefix.AsSpan()))
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var roleValueStart = index + prefix.Length;
        var roleValueEnd = text.IndexOf("\"]#", roleValueStart, StringComparison.Ordinal);
        if (roleValueEnd < 0 || roleValueEnd + 3 > end)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var closingHash = FindClosingRoleSpanHash(text, roleValueEnd + 3, end);
        if (closingHash < 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = closingHash + 1;
        inline = new WordAsciiDocStyledInlineModel(
            ParseInlineContent(text[(roleValueEnd + 3)..closingHash]),
            Roles: ParseInlineRoleAttributeValue(text[roleValueStart..roleValueEnd]));
        return true;
    }

    private static bool TryParseUnclosedRoleSpanToEnd(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        if (TryParseUnclosedShorthandRoleSpanToEnd(text, index, end, out consumed, out inline))
        {
            return true;
        }

        return TryParseUnclosedNamedRoleSpanToEnd(text, index, end, out consumed, out inline);
    }

    private static bool TryParseUnclosedShorthandRoleSpanToEnd(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        if (index + 4 > end || text[index] != '[' || text[index + 1] != '.')
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var closingBracket = text.IndexOf(']', index + 2, end - (index + 2));
        if (closingBracket < 0 || closingBracket + 1 >= end || text[closingBracket + 1] != '#')
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        if (FindClosingRoleSpanHash(text, closingBracket + 2, end) >= 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = end;
        var roles = ParseInlineRoleList(text[(index + 2)..closingBracket]);
        inline = new WordAsciiDocStyledInlineModel(ParseInlineContent(text[(closingBracket + 2)..end]), Roles: roles);
        return roles.Length > 0;
    }

    private static bool TryParseUnclosedNamedRoleSpanToEnd(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        const string prefix = "[role=\"";
        if (index + prefix.Length + 3 > end || !text.AsSpan(index, prefix.Length).SequenceEqual(prefix.AsSpan()))
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var roleValueStart = index + prefix.Length;
        var roleValueEnd = text.IndexOf("\"]#", roleValueStart, StringComparison.Ordinal);
        if (roleValueEnd < 0 || roleValueEnd + 3 > end)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        if (FindClosingRoleSpanHash(text, roleValueEnd + 3, end) >= 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = end;
        inline = new WordAsciiDocStyledInlineModel(
            ParseInlineContent(text[(roleValueEnd + 3)..end]),
            Roles: ParseInlineRoleAttributeValue(text[roleValueStart..roleValueEnd]));
        return true;
    }

    private static bool TryParseRolePrefixedDelimitedInline(string text, int index, int end, out int consumed, out WordAsciiDocInlineModel inline)
    {
        if (!TryParseInlineRolePrefix(text, index, end, out var contentStart, out var roles))
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        if (TryParseDelimitedInline(text, contentStart, end, "+++", "+++", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true, Roles: [.. roles, "underline"]), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "**", "**", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true, Roles: roles), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "*", "*", out consumed, children => new WordAsciiDocStyledInlineModel(children, Bold: true, Roles: roles), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "__", "__", out consumed, children => new WordAsciiDocStyledInlineModel(children, Italic: true, Roles: roles), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "_", "_", out consumed, children => new WordAsciiDocStyledInlineModel(children, Italic: true, Roles: roles), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "`", "`", out consumed, children => new WordAsciiDocStyledInlineModel(children, Code: true, Roles: roles), out inline)
            || TryParseDelimitedInline(text, contentStart, end, "+", "+", out consumed, children => new WordAsciiDocStyledInlineModel(children, Roles: [.. roles, "underline"]), out inline))
        {
            return true;
        }

        consumed = 0;
        inline = null!;
        return false;
    }

    private static bool TryParseInlineRolePrefix(string text, int index, int end, out int contentStart, out string[] roles)
    {
        if (index + 4 <= end && text[index] == '[' && text[index + 1] == '.')
        {
            var closingBracket = text.IndexOf(']', index + 2, end - (index + 2));
            if (closingBracket >= 0 && closingBracket + 1 < end)
            {
                contentStart = closingBracket + 1;
                roles = ParseInlineRoleList(text[(index + 2)..closingBracket]);
                return roles.Length > 0;
            }
        }

        const string prefix = "[role=\"";
        if (index + prefix.Length + 2 <= end && text.AsSpan(index, prefix.Length).SequenceEqual(prefix.AsSpan()))
        {
            var roleValueStart = index + prefix.Length;
            var roleValueEnd = text.IndexOf("\"]", roleValueStart, StringComparison.Ordinal);
            if (roleValueEnd >= 0 && roleValueEnd + 2 <= end)
            {
                contentStart = roleValueEnd + 2;
                roles = ParseInlineRoleAttributeValue(text[roleValueStart..roleValueEnd]);
                return roles.Length > 0;
            }
        }

        contentStart = 0;
        roles = [];
        return false;
    }

    private static bool TryParseDelimitedInline(
        string text,
        int index,
        int end,
        string startDelimiter,
        string endDelimiter,
        out int consumed,
        Func<IReadOnlyList<WordAsciiDocInlineModel>, WordAsciiDocInlineModel> factory,
        out WordAsciiDocInlineModel inline)
    {
        if (index + startDelimiter.Length > end || !text.AsSpan(index, startDelimiter.Length).SequenceEqual(startDelimiter.AsSpan()))
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var searchStart = index + startDelimiter.Length;
        var closeIndex = text.IndexOf(endDelimiter, searchStart, StringComparison.Ordinal);
        if (closeIndex < 0 || closeIndex >= end)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        var content = text[searchStart..closeIndex];
        if (content.Length == 0)
        {
            consumed = 0;
            inline = null!;
            return false;
        }

        consumed = closeIndex + endDelimiter.Length;
        inline = factory(ParseInlineContent(content));
        return true;
    }

    private static IEnumerable<OpenXmlElement> CreateInlineElements(
        MainDocumentPart mainPart,
        IReadOnlyList<WordAsciiDocInlineModel> inlines,
        WordAsciiDocBlockMetadata metadata,
        bool boldOverride,
        bool italicOverride,
        bool codeOverride,
        string? colorOverride,
        bool hyperlinkOverride = false)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case WordAsciiDocTextInlineModel textInline:
                    yield return new Run(
                        CreateRunProperties(metadata, boldOverride, italicOverride, codeOverride, highlight: false, colorOverride: colorOverride, hyperlink: hyperlinkOverride),
                        new Text(textInline.Text) { Space = SpaceProcessingModeValues.Preserve });
                    break;

                case WordAsciiDocLineBreakInlineModel:
                    yield return new Run(new Break());
                    break;

                case WordAsciiDocStyledInlineModel styledInline when !string.IsNullOrWhiteSpace(styledInline.HyperlinkUrl):
                    if (Uri.TryCreate(styledInline.HyperlinkUrl, UriKind.Absolute, out var hyperlinkUri))
                    {
                        var relationship = mainPart.AddHyperlinkRelationship(hyperlinkUri, true);
                        var hyperlink = new Hyperlink { Id = relationship.Id, History = OnOffValue.FromBoolean(true) };
                        foreach (var child in CreateInlineElements(
                                     mainPart,
                                     styledInline.Children,
                                     MergeMetadata(metadata, styledInline.Roles),
                                     boldOverride || styledInline.Bold,
                                     italicOverride || styledInline.Italic,
                                     codeOverride || styledInline.Code,
                                     ResolveInlineColor(metadata, styledInline.Roles, colorOverride, styledInline.Highlight),
                                     hyperlinkOverride: true))
                        {
                            hyperlink.Append(child.CloneNode(true));
                        }

                        yield return hyperlink;
                    }
                    else
                    {
                        foreach (var child in CreateInlineElements(
                                     mainPart,
                                     styledInline.Children,
                                     MergeMetadata(metadata, styledInline.Roles),
                                     boldOverride || styledInline.Bold,
                                     italicOverride || styledInline.Italic,
                                     codeOverride || styledInline.Code,
                                     ResolveInlineColor(metadata, styledInline.Roles, colorOverride, styledInline.Highlight)))
                        {
                            yield return child;
                        }
                    }

                    break;

                case WordAsciiDocStyledInlineModel styledInline:
                    foreach (var child in CreateInlineElements(
                                 mainPart,
                                 styledInline.Children,
                                 MergeMetadata(metadata, styledInline.Roles),
                                 boldOverride || styledInline.Bold,
                                 italicOverride || styledInline.Italic,
                                 codeOverride || styledInline.Code,
                                 ResolveInlineColor(metadata, styledInline.Roles, colorOverride, styledInline.Highlight),
                                 hyperlinkOverride))
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }

    private static string? ResolveInlineColor(
        WordAsciiDocBlockMetadata metadata,
        IReadOnlyList<string>? inlineRoles,
        string? colorOverride,
        bool highlight)
    {
        if (!string.IsNullOrWhiteSpace(colorOverride))
        {
            return colorOverride;
        }

        if (highlight)
        {
            return "000000";
        }

        foreach (var role in inlineRoles ?? [])
        {
            var resolved = ResolveColor(role);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        foreach (var role in metadata.Roles)
        {
            var resolved = ResolveColor(role);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static WordAsciiDocBlockMetadata MergeMetadata(WordAsciiDocBlockMetadata metadata, IReadOnlyList<string>? extraRoles)
    {
        if (extraRoles is null || extraRoles.Count == 0)
        {
            return metadata;
        }

        return new WordAsciiDocBlockMetadata(
            metadata.Title,
            [.. metadata.Roles, .. extraRoles],
            metadata.PositionalAttributes,
            metadata.NamedAttributes);
    }

    private static void ApplyAlignmentRolesFromInline(List<WordAsciiDocInlineModel> inlines, ParagraphProperties paragraphProperties)
    {
        if (inlines.Count != 1 || inlines[0] is not WordAsciiDocStyledInlineModel styledInline || styledInline.Roles is null)
        {
            return;
        }

        var alignment = ResolveAlignment(styledInline.Roles);
        if (alignment is not null)
        {
            paragraphProperties.Justification = new Justification { Val = alignment.Value };
        }
    }

    private static void ApplyParagraphStyle(
        ParagraphProperties paragraphProperties,
        WordAsciiDocBlockMetadata metadata,
        JustificationValues? defaultAlignment = null,
        int leftIndentTwips = 0)
    {
        var alignment = ResolveAlignment(metadata.Roles);
        if (alignment is null && defaultAlignment is not null)
        {
            alignment = defaultAlignment;
        }

        if (alignment is not null)
        {
            paragraphProperties.Justification = new Justification { Val = alignment.Value };
        }

        if (leftIndentTwips > 0)
        {
            paragraphProperties.Indentation = new Indentation { Left = leftIndentTwips.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }
    }

    private static RunProperties CreateRunProperties(
        WordAsciiDocBlockMetadata metadata,
        bool bold,
        bool italic,
        bool code,
        bool highlight,
        string? colorOverride = null,
        string? fontSize = null,
        bool hyperlink = false)
    {
        var effectiveBold = bold || HasAnyRole(metadata, "bold", "strong");
        var effectiveItalic = italic || HasAnyRole(metadata, "italic", "em", "emphasis");
        var effectiveCode = code || HasAnyRole(metadata, "code", "monospace");
        var effectiveHighlight = highlight || metadata.HasRole("text-highlight");
        var effectiveUnderline = hyperlink || HasAnyRole(metadata, "underline");
        var properties = new RunProperties();
        if (effectiveBold)
        {
            properties.Append(new Bold());
        }

        if (effectiveItalic)
        {
            properties.Append(new Italic());
        }

        if (effectiveUnderline)
        {
            properties.Append(new Underline { Val = UnderlineValues.Single });
        }

        if (hyperlink)
        {
            properties.Append(new RunStyle { Val = "Hyperlink" });
        }

        if (effectiveCode)
        {
            properties.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            properties.Append(new Shading { Fill = "F5F5F5", Val = ShadingPatternValues.Clear, Color = "auto" });
        }

        if (!string.IsNullOrWhiteSpace(fontSize) || effectiveCode)
        {
            properties.Append(new FontSize { Val = fontSize ?? "20" });
        }

        if (effectiveHighlight)
        {
            properties.Append(new Highlight { Val = HighlightColorValues.Yellow });
        }

        var color = colorOverride ?? metadata.Roles.Select(ResolveColor).FirstOrDefault(static value => value is not null);
        if (string.IsNullOrWhiteSpace(color) && hyperlink)
        {
            color = "0563C1";
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            properties.Append(new Color { Val = color });
        }

        return properties;
    }

    private static JustificationValues? ResolveAlignment(IEnumerable<string> roles)
    {
        foreach (var role in roles)
        {
            if (string.Equals(role, "text-center", StringComparison.OrdinalIgnoreCase))
            {
                return JustificationValues.Center;
            }

            if (string.Equals(role, "text-right", StringComparison.OrdinalIgnoreCase))
            {
                return JustificationValues.Right;
            }

            if (string.Equals(role, "text-left", StringComparison.OrdinalIgnoreCase))
            {
                return JustificationValues.Left;
            }
        }

        return null;
    }

    private static string? ResolveColor(string role) =>
        role.ToLowerInvariant() switch
        {
            "text-blue" => "1F4E79",
            "text-green" => "2E8B57",
            "text-yellow" => "BF9000",
            "text-purple" => "7030A0",
            "text-orange" => "C55A11",
            "text-red" => "C00000",
            _ => null,
        };

    private static void FlushText(StringBuilder builder, List<WordAsciiDocInlineModel> result)
    {
        if (builder.Length == 0)
        {
            return;
        }

        result.Add(new WordAsciiDocTextInlineModel(builder.ToString()));
        builder.Clear();
    }

    private static bool HasAnyRole(WordAsciiDocBlockMetadata metadata, params string[] roles) =>
        roles.Any(metadata.HasRole);

    private static int[] ResolveColumnWeights(WordAsciiDocBlockMetadata metadata, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (metadata.TryGetNamedAttribute("cols", out var columnSpecification) && !string.IsNullOrWhiteSpace(columnSpecification))
        {
            var parsedWeights = columnSpecification
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseColumnWeight)
                .ToArray();
            if (parsedWeights.Length > 0)
            {
                return parsedWeights;
            }
        }

        var columnCount = rows.Count == 0 ? 1 : rows.Max(static row => row.Count);
        return Enumerable.Repeat(1, Math.Max(1, columnCount)).ToArray();
    }

    private static int ParseColumnWeight(string rawValue)
    {
        var digits = new string(rawValue.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsedWeight) && parsedWeight > 0 ? parsedWeight : 1;
    }

    private static IReadOnlyList<string> NormalizeRowCells(IReadOnlyList<string> row, int columnCount)
    {
        if (row.Count == columnCount)
        {
            return row;
        }

        var normalized = new string[columnCount];
        for (var index = 0; index < columnCount; index++)
        {
            normalized[index] = index < row.Count ? row[index] : string.Empty;
        }

        return normalized;
    }

    private static RenderedHeadingInfo[] BuildHeadingInfos(WordAsciiDocDocumentModel model)
    {
        var headingBlocks = model.Blocks.OfType<WordAsciiDocHeadingBlockModel>().ToArray();
        var sectionNumbers = new int[Math.Max(6, headingBlocks.Select(static block => block.Level).DefaultIfEmpty(1).Max())];
        var usedBookmarkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new RenderedHeadingInfo[headingBlocks.Length];
        uint nextBookmarkId = 1;
        for (var index = 0; index < headingBlocks.Length; index++)
        {
            var heading = headingBlocks[index];
            var displayText = RenderHeadingText(heading, model, sectionNumbers);
            entries[index] = new RenderedHeadingInfo(
                Math.Max(1, heading.Level - 1),
                displayText,
                CreateBookmarkName(heading, displayText, usedBookmarkNames),
                nextBookmarkId++);
        }

        return entries;
    }

    private static string CreateBookmarkName(WordAsciiDocHeadingBlockModel heading, string displayText, HashSet<string> usedBookmarkNames)
    {
        var source = heading.Metadata.TryGetNamedAttribute("id", out var explicitId) && !string.IsNullOrWhiteSpace(explicitId)
            ? explicitId
            : displayText;
        var builder = new StringBuilder("_toc_");
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
            else if (!char.IsControl(character))
            {
                builder.Append('_');
            }
        }

        var bookmarkName = builder.ToString().TrimEnd('_');
        if (bookmarkName.Length <= 5)
        {
            bookmarkName = $"_toc_section_{usedBookmarkNames.Count + 1}";
        }

        var uniqueName = bookmarkName;
        var suffix = 2;
        while (!usedBookmarkNames.Add(uniqueName))
        {
            uniqueName = bookmarkName + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffix++;
        }

        return uniqueName;
    }

    private static string[] ParseInlineRoleList(string rawRoles)
    {
        var anchorSeparator = rawRoles.IndexOf('#');
        var rolePortion = anchorSeparator >= 0 ? rawRoles[..anchorSeparator] : rawRoles;
        if (rolePortion.Length > 0 && rolePortion[0] == '.')
        {
            rolePortion = rolePortion[1..];
        }

        return rolePortion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] ParseInlineRoleAttributeValue(string rawRoles) =>
        rawRoles.Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int FindClosingRoleSpanHash(string text, int contentStart, int end)
    {
        for (var index = contentStart; index < end; index++)
        {
            if (text[index] == '+' && !IsEscaped(text, index, contentStart))
            {
                var delimiterLength = index + 2 < end && text[index + 1] == '+' && text[index + 2] == '+' ? 3 : 1;
                var closingDelimiter = FindClosingDelimitedInline(text, index + delimiterLength, end, '+', delimiterLength, contentStart);
                if (closingDelimiter >= 0)
                {
                    index = closingDelimiter + delimiterLength - 1;
                    continue;
                }
            }

            if (text[index] != '#')
            {
                continue;
            }

            if (!IsEscaped(text, index, contentStart))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindClosingDelimitedInline(string text, int searchStart, int end, char delimiterCharacter, int delimiterLength, int lowerBound)
    {
        for (var index = searchStart; index <= end - delimiterLength; index++)
        {
            if (text[index] != delimiterCharacter || IsEscaped(text, index, lowerBound))
            {
                continue;
            }

            var matches = true;
            for (var offset = 1; offset < delimiterLength; offset++)
            {
                if (text[index + offset] != delimiterCharacter)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsEscaped(string text, int index, int lowerBound)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= lowerBound && text[current] == '\\'; current--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private sealed class RenderState(WordAsciiDocDocumentModel model)
    {
        private readonly RenderedHeadingInfo[] _headingInfos = BuildHeadingInfos(model);
        private int _nextHeadingIndex;

        public IReadOnlyList<RenderedHeadingInfo> HeadingInfos => _headingInfos;

        public RenderedHeadingInfo TakeNextHeadingInfo() => _headingInfos[_nextHeadingIndex++];
    }

    private sealed record RenderedHeadingInfo(int LogicalLevel, string DisplayText, string BookmarkName, uint BookmarkId);

    [GeneratedRegex(@"^(?:link:([^\[]+)\[([^\]]*)\]|(https?://[^\[]+)\[([^\]]*)\])", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"^(?:link:([^\[]+)\[([^\]]*)\]#|(https?://[^\[]+)\[([^\]]*)\]#)", RegexOptions.CultureInvariant)]
    private static partial Regex MalformedTrailingHashLinkPattern();

    [GeneratedRegex(@"^(?:link:([^\[]+)\[(\.[^\]]+)\]#(.*?)#|(https?://[^\[]+)\[(\.[^\]]+)\]#(.*?)#)", RegexOptions.CultureInvariant)]
    private static partial Regex MalformedStyledLinkPattern();

}