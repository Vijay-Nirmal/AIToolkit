using AIToolkit.Tools.Document;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Performs a best-effort projection from WordprocessingML content into canonical AsciiDoc.
/// </summary>
internal static partial class WordAsciiDocImporter
{
    public static string Import(WordprocessingDocument document)
    {
        var mainPart = document.MainDocumentPart;
        var body = mainPart?.Document?.Body;
        if (mainPart is null || body is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var child in body.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    AppendParagraph(lines, paragraph, mainPart);
                    break;
                case Table table:
                    AppendTable(lines, table, mainPart);
                    break;
            }
        }

        TrimBlankLines(lines);
        return string.Join("\n", lines);
    }

    private static void AppendParagraph(List<string> lines, Paragraph paragraph, MainDocumentPart mainPart)
    {
        if (IsPageBreakParagraph(paragraph))
        {
            AppendBlankLine(lines);
            lines.Add("<<<");
            AppendBlankLine(lines);
            return;
        }

        if (IsThematicBreakParagraph(paragraph))
        {
            AppendBlankLine(lines);
            lines.Add("'''");
            AppendBlankLine(lines);
            return;
        }

        var headingLevel = ResolveHeadingLevel(paragraph);
        var isTitleParagraph = IsTitleParagraph(paragraph);
        var blockRoleLine = ResolveParagraphBlockRoleLine(paragraph, includeAlignment: !isTitleParagraph);
        var text = ExtractInlineText(
                paragraph,
                mainPart,
                suppressBold: headingLevel > 0,
                restoreHardBreakMarkup: headingLevel == 0)
            .TrimEnd();
        if (text.Length == 0)
        {
            AppendBlankLine(lines);
            return;
        }

        if (headingLevel > 0)
        {
            AppendBlankLine(lines);
            if (blockRoleLine is not null)
            {
                lines.Add(blockRoleLine);
            }

            lines.Add(new string('=', headingLevel) + " " + text);
            AppendBlankLine(lines);
            return;
        }

        var marker = ResolveListMarker(paragraph, mainPart);
        if (marker is null)
        {
            marker = ResolveSyntheticListMarker(paragraph, ref text);
        }

        if (blockRoleLine is not null && marker is null)
        {
            AppendBlankLine(lines);
            lines.Add(blockRoleLine);
        }

        if (blockRoleLine is null && marker is null)
        {
            AppendBlankLine(lines);
        }

        lines.Add(marker is null ? text : marker + text);
        if (marker is null)
        {
            AppendBlankLine(lines);
        }
    }

    private static void AppendTable(List<string> lines, Table table, MainDocumentPart mainPart)
    {
        AppendBlankLine(lines);
        if (TryAppendDelimitedBlock(lines, table, mainPart))
        {
            return;
        }

        var rows = table.Elements<TableRow>().ToArray();
        var hasHeader = HasHeaderRow(rows);
        var columnSpecification = ResolveTableColumnSpecification(table);
        if (!string.IsNullOrWhiteSpace(columnSpecification) || hasHeader)
        {
            var attributes = new List<string>();
            if (!string.IsNullOrWhiteSpace(columnSpecification))
            {
                attributes.Add($"cols=\"{columnSpecification}\"");
            }

            if (hasHeader)
            {
                attributes.Add("options=\"header\"");
            }

            lines.Add("[" + string.Join(',', attributes) + "]");
        }

        lines.Add("|===");
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            var isHeaderRow = hasHeader && rowIndex == 0;
            var builder = new StringBuilder();
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = string.Join(
                    " ",
                    cell.Elements<Paragraph>()
                        .Select(paragraph => ApplyTableCellParagraphRoles(paragraph, ExtractInlineText(paragraph, mainPart, suppressBold: isHeaderRow).Trim()))
                        .Where(static value => value.Length > 0));

                builder.Append("| ");
                builder.Append(cellText);
                builder.Append(' ');
            }

            lines.Add(builder.ToString().TrimEnd());
        }

        lines.Add("|===");
        AppendBlankLine(lines);
    }

    private static string ExtractInlineText(
        OpenXmlElement element,
        MainDocumentPart mainPart,
        bool suppressBold = false,
        bool suppressItalic = false,
        bool suppressCode = false,
        bool restoreHardBreakMarkup = false)
    {
        var builder = new StringBuilder();
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    builder.Append(FormatRun(run, suppressBold, suppressItalic, suppressCode));
                    break;
                case Hyperlink hyperlink:
                    builder.Append(FormatHyperlink(hyperlink, mainPart));
                    break;
                case SimpleField field:
                    builder.Append(field.InnerText);
                    break;
                default:
                    if (child is OpenXmlCompositeElement composite)
                    {
                        builder.Append(ExtractInlineText(composite, mainPart, suppressBold, suppressItalic, suppressCode, restoreHardBreakMarkup: false));
                    }
                    else
                    {
                        builder.Append(child.InnerText);
                    }

                    break;
            }
        }

        var normalized = WordAsciiDocTextUtilities.NormalizeLineEndings(builder.ToString());
        return restoreHardBreakMarkup ? RestoreHardBreakMarkup(normalized) : normalized;
    }

    private static string FormatRun(Run run, bool suppressBold = false, bool suppressItalic = false, bool suppressCode = false)
    {
        var builder = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            builder.Append(child switch
            {
                Text text => text.Text,
                TabChar => "\t",
                Break lineBreak => lineBreak.Type?.Value == BreakValues.Page ? string.Empty : "\n",
                _ => child.InnerText,
            });
        }

        var textValue = builder.ToString();
        if (textValue.Length == 0)
        {
            return string.Empty;
        }

        var properties = run.RunProperties;
        var isCode = properties?.RunStyle?.Val?.Value?.Contains("code", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(properties?.RunFonts?.Ascii?.Value, "Consolas", StringComparison.OrdinalIgnoreCase)
            || string.Equals(properties?.RunFonts?.Ascii?.Value, "Courier New", StringComparison.OrdinalIgnoreCase);
        var isBold = !suppressBold && properties?.Bold is not null;
        var isItalic = !suppressItalic && properties?.Italic is not null;
        var isUnderline = properties?.Underline is not null;
        var colorRole = ResolveColorRole(properties?.Color?.Val?.Value);
        var isHighlight = properties?.Highlight is not null;
        return ApplyInlineMarkup(textValue, !suppressCode && isCode, isBold, isItalic, isUnderline, colorRole, isHighlight);
    }

    private static string FormatHyperlink(Hyperlink hyperlink, MainDocumentPart mainPart)
    {
        var text = ExtractPlainInlineText(hyperlink);
        var relationshipId = hyperlink.Id?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return text;
        }

        var relationship = mainPart.HyperlinkRelationships.FirstOrDefault(candidate => candidate.Id == relationshipId);
        if (relationship is null)
        {
            return text;
        }

        var link = $"link:{NormalizeHyperlinkTarget(relationship.Uri)}[{EscapeLinkLabel(text)}]";
        var colorRole = ResolveColorRole(hyperlink.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
        return colorRole is null ? link : WrapWithRoleSpan(link, [colorRole]);
    }

    private static string ApplyInlineMarkup(string text, bool isCode, bool isBold, bool isItalic, bool isUnderline, string? colorRole, bool isHighlight)
    {
        var leading = text.Length - text.TrimStart().Length;
        var trailing = text.Length - text.TrimEnd().Length;
        var core = text.Trim();
        if (core.Length == 0)
        {
            return text;
        }

        if (isCode)
        {
            core = WrapWithDelimitedMarkup(core, "`");
        }
        else
        {
            if (isBold && isItalic)
            {
                core = "*_" + EscapeDelimitedContent(core, '_') + "_*";
            }
            else if (isBold && isUnderline && colorRole is null && !isHighlight)
            {
                core = WrapWithDelimitedMarkup(core, "+++");
                isUnderline = false;
                isBold = false;
            }
            else
            {
                if (isBold)
                {
                    core = WrapWithDelimitedMarkup(core, "*");
                }

                if (isItalic)
                {
                    core = WrapWithDelimitedMarkup(core, "_");
                }

                if (isUnderline && colorRole is null && !isHighlight && !isBold)
                {
                    core = WrapWithDelimitedMarkup(core, "+");
                    isUnderline = false;
                }
            }
        }

        var roles = new List<string>();
        if (!string.IsNullOrWhiteSpace(colorRole))
        {
            roles.Add(colorRole);
        }

        if (isHighlight)
        {
            roles.Add("text-highlight");
        }

        if (isUnderline)
        {
            roles.Add("underline");
        }

        if (roles.Count > 0)
        {
            core = WrapWithRoleSpan(core, roles);
        }

        return new string(' ', leading) + core + new string(' ', trailing);
    }

    private static string? ResolveParagraphBlockRoleLine(Paragraph paragraph, bool includeAlignment = true)
    {
        var roles = new List<string>();
        var alignmentRole = includeAlignment ? ResolveAlignmentRole(paragraph.ParagraphProperties?.Justification?.Val?.Value) : null;
        if (alignmentRole is not null)
        {
            roles.Add(alignmentRole);
        }

        if (roles.Count == 0)
        {
            return null;
        }

        return "[." + string.Join('.', roles) + "]";
    }

    private static string ApplyTableCellParagraphRoles(Paragraph paragraph, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var roles = new List<string>();
        var alignmentRole = ResolveAlignmentRole(paragraph.ParagraphProperties?.Justification?.Val?.Value);
        if (alignmentRole is not null)
        {
            roles.Add(alignmentRole);
        }

        return roles.Count == 0 ? text : WrapWithRoleSpan(text, roles);
    }

    private static string? ResolveAlignmentRole(JustificationValues? justification)
    {
        if (justification == JustificationValues.Center)
        {
            return "text-center";
        }

        if (justification == JustificationValues.Right)
        {
            return "text-right";
        }

        if (justification == JustificationValues.Left)
        {
            return "text-left";
        }

        return null;
    }

    private static string ExtractPlainInlineText(OpenXmlElement element)
    {
        var builder = new StringBuilder();
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    foreach (var runChild in run.ChildElements)
                    {
                        builder.Append(runChild switch
                        {
                            Text text => text.Text,
                            TabChar => "\t",
                            Break => "\n",
                            _ => runChild.InnerText,
                        });
                    }

                    break;
                case Hyperlink hyperlink:
                    builder.Append(ExtractPlainInlineText(hyperlink));
                    break;
                default:
                    if (child is OpenXmlCompositeElement composite)
                    {
                        builder.Append(ExtractPlainInlineText(composite));
                    }
                    else
                    {
                        builder.Append(child.InnerText);
                    }

                    break;
            }
        }

        return WordAsciiDocTextUtilities.NormalizeLineEndings(builder.ToString());
    }

    private static string WrapWithDelimitedMarkup(string text, string delimiter) =>
        delimiter + EscapeDelimitedContent(text, delimiter[^1]) + delimiter;

    private static string EscapeDelimitedContent(string text, char delimiterCharacter) =>
        text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(delimiterCharacter.ToString(), "\\" + delimiterCharacter, StringComparison.Ordinal);

    private static string EscapeLinkLabel(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    private static string WrapWithRoleSpan(string text, List<string> roles) =>
        roles.Count == 0
            ? text
            : "[." + string.Join('.', roles) + "]#" + EscapeRoleSpanContent(text) + "#";

    private static string EscapeRoleSpanContent(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("#", "\\#", StringComparison.Ordinal);

    private static string? ResolveColorRole(string? colorValue)
    {
        if (string.IsNullOrWhiteSpace(colorValue))
        {
            return null;
        }

        var normalized = colorValue.Trim().TrimStart('#').ToUpperInvariant();
        return normalized switch
        {
            "1F4E79" => "text-blue",
            "2E8B57" => "text-green",
            "BF9000" => "text-yellow",
            "7030A0" => "text-purple",
            "C55A11" => "text-orange",
            "C00000" => "text-red",
            _ => null,
        };
    }

    private static string NormalizeHyperlinkTarget(Uri uri)
    {
        var absolute = uri.AbsoluteUri;
        return uri.IsAbsoluteUri
            && string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(uri.Query)
            && string.IsNullOrWhiteSpace(uri.Fragment)
            ? absolute.TrimEnd('/')
            : absolute;
    }

    private static int ResolveHeadingLevel(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return 0;
        }

        if (string.Equals(styleId, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var match = HeadingStylePattern().Match(styleId);
        return match.Success && int.TryParse(match.Groups[1].Value, out var level)
            ? Math.Clamp(level + 1, 2, 6)
            : 0;
    }

    private static string? ResolveListMarker(Paragraph paragraph, MainDocumentPart mainPart)
    {
        var numberingProperties = paragraph.ParagraphProperties?.NumberingProperties;
        if (numberingProperties is null)
        {
            return null;
        }

        var level = (int?)numberingProperties.NumberingLevelReference?.Val?.Value ?? 0;
        var numberFormat = ResolveNumberFormat(numberingProperties.NumberingId?.Val?.Value, level, mainPart);
        var marker = numberFormat == NumberFormatValues.Bullet ? '*' : '.';
        return new string(marker, Math.Max(1, level + 1)) + " ";
    }

    private static string? ResolveSyntheticListMarker(Paragraph paragraph, ref string text)
    {
        var level = ResolveSyntheticListLevel(paragraph);
        if (text.StartsWith("• ", StringComparison.Ordinal))
        {
            text = text[2..];
            return new string('*', level) + " ";
        }

        if (text.StartsWith("☒ ", StringComparison.Ordinal))
        {
            text = text[2..];
            return new string('*', level) + " [x] ";
        }

        if (text.StartsWith("☐ ", StringComparison.Ordinal))
        {
            text = text[2..];
            return new string('*', level) + " [ ] ";
        }

        if (text.StartsWith("↳ ", StringComparison.Ordinal))
        {
            text = text[2..];
            return "<1> ";
        }

        var orderedMatch = OrderedListPrefixPattern().Match(text);
        if (!orderedMatch.Success)
        {
            return null;
        }

        text = text[orderedMatch.Length..];
        return new string('.', level) + " ";
    }

    private static int ResolveSyntheticListLevel(Paragraph paragraph)
    {
        var leftIndent = paragraph.ParagraphProperties?.Indentation?.Left?.Value;
        return int.TryParse(leftIndent, out var twips) && twips > 0
            ? Math.Max(1, (twips / 360) + 1)
            : 1;
    }

    private static bool IsTitleParagraph(Paragraph paragraph) =>
        string.Equals(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value, "Title", StringComparison.OrdinalIgnoreCase);

    private static bool IsPageBreakParagraph(Paragraph paragraph) =>
        paragraph.Descendants<Break>().Any(static lineBreak => lineBreak.Type?.Value == BreakValues.Page);

    private static bool IsThematicBreakParagraph(Paragraph paragraph) =>
        string.IsNullOrWhiteSpace(paragraph.InnerText)
        && paragraph.ParagraphProperties?.ParagraphBorders?.BottomBorder is not null;

    private static string RestoreHardBreakMarkup(string text)
    {
        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        var segments = text.Split('\n');
        return string.Join(" +\n", segments);
    }

    private static bool TryAppendDelimitedBlock(List<string> lines, Table table, MainDocumentPart mainPart)
    {
        var rows = table.Elements<TableRow>().ToArray();
        if (rows.Length != 1)
        {
            return false;
        }

        var cells = rows[0].Elements<TableCell>().ToArray();
        if (cells.Length != 1)
        {
            return false;
        }

        var row = rows[0];
        var cell = cells[0];

        var paragraphs = cell.Elements<Paragraph>().ToArray();
        if (paragraphs.Length == 0)
        {
            return false;
        }

        if (TryAppendAdmonitionBlock(lines, table, cell, paragraphs, mainPart))
        {
            return true;
        }

        return TryAppendCodeLikeDelimitedBlock(lines, table, cell, paragraphs, mainPart);
    }

    private static bool TryAppendAdmonitionBlock(List<string> lines, Table table, TableCell cell, Paragraph[] paragraphs, MainDocumentPart mainPart)
    {
        if (paragraphs.Length < 2)
        {
            return false;
        }

        var label = paragraphs[0].InnerText.Trim();
        var fill = cell.TableCellProperties?.Shading?.Fill?.Value;
        var border = table.GetFirstChild<TableProperties>()?.TableBorders?.TopBorder?.Color?.Value;
        var matches = label switch
        {
            "NOTE" => string.Equals(fill, "E8F1FB", StringComparison.OrdinalIgnoreCase) && string.Equals(border, "4F81BD", StringComparison.OrdinalIgnoreCase),
            "IMPORTANT" => string.Equals(fill, "FFF2CC", StringComparison.OrdinalIgnoreCase) && string.Equals(border, "C9A227", StringComparison.OrdinalIgnoreCase),
            "WARNING" => string.Equals(fill, "FDE9E7", StringComparison.OrdinalIgnoreCase) && string.Equals(border, "C0504D", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        if (!matches)
        {
            return false;
        }

        lines.Add($"[{label}]");
        lines.Add("====");
        foreach (var paragraph in paragraphs.Skip(1))
        {
            lines.Add(ExtractInlineText(paragraph, mainPart).TrimEnd());
        }

        lines.Add("====");
        AppendBlankLine(lines);
        return true;
    }

    private static bool TryAppendCodeLikeDelimitedBlock(List<string> lines, Table table, TableCell cell, Paragraph[] paragraphs, MainDocumentPart mainPart)
    {
        var fill = cell.TableCellProperties?.Shading?.Fill?.Value;
        var border = table.GetFirstChild<TableProperties>()?.TableBorders?.TopBorder?.Color?.Value;
        if (!string.Equals(fill, "F5F5F5", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(border, "808080", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var startIndex = 0;
        string? language = null;
        if (paragraphs.Length > 1 && IsItalicParagraph(paragraphs[0]) && !IsCodeParagraph(paragraphs[0]))
        {
            language = ExtractInlineText(paragraphs[0], mainPart, suppressItalic: true).Trim();
            startIndex = 1;
        }

        var contentParagraphs = paragraphs[startIndex..];
        if (contentParagraphs.Length == 0 || !contentParagraphs.All(IsCodeParagraph))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            lines.Add($"[source,{language}]");
            lines.Add("----");
        }
        else
        {
            lines.Add("....");
        }

        foreach (var paragraph in contentParagraphs)
        {
            lines.Add(ExtractInlineText(paragraph, mainPart, suppressCode: true).TrimEnd());
        }

        lines.Add(string.IsNullOrWhiteSpace(language) ? "...." : "----");
        AppendBlankLine(lines);
        return true;
    }

    private static bool IsItalicParagraph(Paragraph paragraph) =>
        paragraph.Descendants<RunProperties>().Any(static properties => properties.Italic is not null);

    private static bool IsCodeParagraph(Paragraph paragraph) =>
        paragraph.Descendants<RunFonts>().Any(static fonts =>
            string.Equals(fonts.Ascii?.Value, "Consolas", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fonts.Ascii?.Value, "Courier New", StringComparison.OrdinalIgnoreCase));

    private static bool HasHeaderRow(TableRow[] rows)
    {
        if (rows.Length == 0)
        {
            return false;
        }

        var headerCells = rows[0].Elements<TableCell>().ToArray();
        return headerCells.Length > 0
            && headerCells.All(static cell => string.Equals(cell.TableCellProperties?.Shading?.Fill?.Value, "EAEAEA", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveTableColumnSpecification(Table table)
    {
        var widths = table.Elements<TableGrid>()
            .SelectMany(static grid => grid.Elements<GridColumn>())
            .Select(static column => column.Width?.Value)
            .Where(static width => int.TryParse(width, out var parsed) && parsed > 0)
            .Select(static width => int.Parse(width!, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (widths.Length == 0)
        {
            return null;
        }

        var minWidth = widths.Min();
        if (minWidth <= 0)
        {
            return null;
        }

        var weights = widths
            .Select(width => Math.Max(1, (int)Math.Round(width / (double)minWidth, MidpointRounding.AwayFromZero)))
            .ToArray();
        return weights.All(static weight => weight == 1)
            ? null
            : string.Join(',', weights);
    }

    private static NumberFormatValues? ResolveNumberFormat(int? numberId, int level, MainDocumentPart mainPart)
    {
        if (numberId is null)
        {
            return null;
        }

        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering is null)
        {
            return null;
        }

        var numberingInstance = numbering.Elements<NumberingInstance>()
            .FirstOrDefault(instance => instance.NumberID?.Value == numberId.Value);
        var abstractNumberId = numberingInstance?.AbstractNumId?.Val?.Value;
        var abstractNumber = numbering.Elements<AbstractNum>()
            .FirstOrDefault(candidate => candidate.AbstractNumberId?.Value == abstractNumberId);
        var levelDefinition = abstractNumber?.Elements<Level>()
            .FirstOrDefault(candidate => candidate.LevelIndex?.Value == level);
        return levelDefinition?.NumberingFormat?.Val?.Value;
    }

    private static void AppendBlankLine(List<string> lines)
    {
        if (lines.Count == 0 || lines[^1].Length != 0)
        {
            lines.Add(string.Empty);
        }
    }

    private static void TrimBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    [GeneratedRegex("^Heading([1-6])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingStylePattern();

    [GeneratedRegex("^\\d+\\.\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListPrefixPattern();
}