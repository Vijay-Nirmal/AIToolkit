using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Imports external .pptx content into a best-effort DeckDoc view.
/// </summary>
internal static class PowerPointDeckImporter
{
    private const long SlideWidth = 12_192_000L;
    private const long SlideHeight = 6_858_000L;
    private const int DefaultGridWidth = 32;
    private const int DefaultGridHeight = 18;

    /// <summary>
    /// Imports the supplied presentation into canonical DeckDoc.
    /// </summary>
    public static string Import(PresentationDocument presentationDocument, string resolvedReference)
    {
        ArgumentNullException.ThrowIfNull(presentationDocument);

        var title = presentationDocument.PackageProperties.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(resolvedReference);
        }

        var lines = new List<string>
        {
            $"= {title}",
            string.Empty,
        };

        var slideParts = GetOrderedSlides(presentationDocument).ToList();
        for (var slideIndex = 0; slideIndex < slideParts.Count; slideIndex++)
        {
            var slidePart = slideParts[slideIndex];
            var titleText = GetSlideTitle(slidePart) ?? $"Slide {slideIndex + 1}";
            var notesText = GetSpeakerNotes(slidePart);
            var transitionDirective = GetTransitionDirective(slidePart);
            var tables = GetTables(slidePart, slideIndex + 1);
            lines.Add($"== {titleText}");
            lines.Add($"@title | {titleText}");

            if (IsHidden(slidePart))
            {
                lines.Add("[state hidden]");
            }

            if (!string.IsNullOrWhiteSpace(notesText))
            {
                lines.Add($"[notes {Quote(notesText)}]");
            }

            if (!string.IsNullOrWhiteSpace(transitionDirective))
            {
                lines.Add(transitionDirective);
            }

            var bodyLines = GetBodyLines(slidePart, titleText);
            foreach (var bodyLine in bodyLines)
            {
                lines.Add($"@body [text .body] | {bodyLine}");
            }

            foreach (var table in tables)
            {
                var entries = new List<string>();
                if (table.HasHeader)
                {
                    entries.Add("header");
                }

                if (table.IsBanded)
                {
                    entries.Add("banded");
                }

                var entrySuffix = entries.Count == 0 ? string.Empty : " " + string.Join(' ', entries);
                lines.Add($"[table {table.Name} at={table.Anchor} size={table.Size}{entrySuffix}]");
                foreach (var row in table.Rows)
                {
                    lines.Add("| " + string.Join(" | ", row.Select(FormatInlineValue)) + " |");
                }

                lines.Add("[end]");
            }

            var pictureCount = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<P.Picture>().Count() ?? 0;
            if (bodyLines.Count == 0 && tables.Count == 0 && pictureCount > 0)
            {
                lines.Add($"@body [text .caption] | Slide contains {pictureCount} image object(s) that were not expanded during best-effort import.");
            }

            lines.Add(string.Empty);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines);
    }

    private static bool IsHidden(SlidePart slidePart) =>
        slidePart.Slide?.Show?.Value == false;

    private static string? GetTransitionDirective(SlidePart slidePart)
    {
        var slideXml = slidePart.Slide?.OuterXml;
        if (string.IsNullOrWhiteSpace(slideXml))
        {
            return null;
        }

        var transitionMatch = Regex.Match(
            slideXml,
            "<(?:\\w+:)?transition\\b(?<attrs>[^>]*)>(?<content>.*?)</(?:\\w+:)?transition>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!transitionMatch.Success)
        {
            return null;
        }

        var transitionAttributes = transitionMatch.Groups["attrs"].Value;
        var transitionContent = transitionMatch.Groups["content"].Value;

        var type = transitionContent.Contains("<p:fade", StringComparison.OrdinalIgnoreCase) ? "fade"
            : transitionContent.Contains("<p:push", StringComparison.OrdinalIgnoreCase) ? "push"
            : transitionContent.Contains("<p:wipe", StringComparison.OrdinalIgnoreCase) ? "wipe"
            : transitionContent.Contains(":morph", StringComparison.OrdinalIgnoreCase) ? "morph"
            : slideXml.Contains("<p:transition", StringComparison.OrdinalIgnoreCase) ? "cut"
            : null;

        if (type is null)
        {
            return null;
        }

        var entries = new List<string> { type };

        var directionMatch = Regex.Match(transitionContent, "\\bdir=\"(?<dir>[lrud])\"", RegexOptions.IgnoreCase);
        if (directionMatch.Success)
        {
            entries.Add($"dir={directionMatch.Groups["dir"].Value.ToLowerInvariant() switch
            {
                "r" => "right",
                "u" => "up",
                "d" => "down",
                _ => "left",
            }}");
        }

        var durationMatch = Regex.Match(transitionAttributes, "(?:p14:)?dur=\"(?<dur>\\d+)\"", RegexOptions.IgnoreCase);
        if (durationMatch.Success && int.TryParse(durationMatch.Groups["dur"].Value, CultureInfo.InvariantCulture, out var durationMs))
        {
            entries.Add($"dur={FormatMillisecondsAsSeconds(durationMs)}");
        }

        var advanceTimeMatch = Regex.Match(transitionAttributes, "advTm=\"(?<tm>\\d+)\"", RegexOptions.IgnoreCase);
        if (advanceTimeMatch.Success && int.TryParse(advanceTimeMatch.Groups["tm"].Value, CultureInfo.InvariantCulture, out var advanceMs))
        {
            entries.Add($"advance=after({FormatMillisecondsAsSeconds(advanceMs)})");
        }
        else if (Regex.IsMatch(transitionAttributes, "advClick=\"1\"", RegexOptions.IgnoreCase))
        {
            entries.Add("advance=click");
        }

        return $"[transition {string.Join(' ', entries)}]";
    }

    private static string FormatMillisecondsAsSeconds(int milliseconds) =>
        $"{(milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture)}s";

    private static string? GetSpeakerNotes(SlidePart slidePart)
    {
        var shapes = slidePart.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree?.Elements<P.Shape>()
            ?? Enumerable.Empty<P.Shape>();
        var notes = string.Join(
            "\n",
            shapes
                .Select(ReadShapeText)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));

        return string.IsNullOrWhiteSpace(notes) ? null : notes;
    }

    private static List<ImportedTable> GetTables(SlidePart slidePart, int slideNumber)
    {
        var tables = new List<ImportedTable>();
        var frames = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<P.GraphicFrame>()
            ?? Enumerable.Empty<P.GraphicFrame>();

        var index = 1;
        foreach (var frame in frames)
        {
            var drawingTable = frame.Graphic?.GraphicData?.GetFirstChild<A.Table>();
            if (drawingTable is null)
            {
                continue;
            }

            var properties = drawingTable.GetFirstChild<A.TableProperties>();
            var rows = drawingTable.Elements<A.TableRow>()
                .Select(row => row.Elements<A.TableCell>()
                    .Select(static cell => ReadTextBody(cell.TextBody))
                    .ToArray())
                .Where(static row => row.Length > 0)
                .ToArray();
            if (rows.Length == 0)
            {
                index++;
                continue;
            }

            var offset = frame.Transform?.Offset;
            var extents = frame.Transform?.Extents;
            tables.Add(new ImportedTable(
                Name: NormalizeIdentifier(frame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Name?.Value, $"ImportedTable{slideNumber}_{index}"),
                Anchor: FormatAnchor(offset?.X?.Value ?? 0L, offset?.Y?.Value ?? 0L),
                Size: FormatSpan(extents?.Cx?.Value ?? 0L, extents?.Cy?.Value ?? 0L),
                Rows: rows,
                HasHeader: properties?.FirstRow?.Value == true,
                IsBanded: properties?.BandRow?.Value == true));
            index++;
        }

        return tables;
    }

    private static IEnumerable<SlidePart> GetOrderedSlides(PresentationDocument presentationDocument)
    {
        var presentationPart = presentationDocument.PresentationPart;
        var slideIdList = presentationPart?.Presentation?.SlideIdList;
        if (presentationPart is null || slideIdList is null)
        {
            yield break;
        }

        foreach (var slideId in slideIdList.Elements<P.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not string relationshipId)
            {
                continue;
            }

            if (presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                yield return slidePart;
            }
        }
    }

    private static string? GetSlideTitle(SlidePart slidePart)
    {
        var shapes = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<P.Shape>() ?? Enumerable.Empty<P.Shape>();
        foreach (var shape in shapes)
        {
            var placeholder = shape.NonVisualShapeProperties?
                .ApplicationNonVisualDrawingProperties?
                .GetFirstChild<P.PlaceholderShape>();
            var placeholderType = placeholder?.Type?.Value;
            if (placeholderType == P.PlaceholderValues.Title || placeholderType == P.PlaceholderValues.CenteredTitle)
            {
                var titleText = ReadShapeText(shape);
                if (!string.IsNullOrWhiteSpace(titleText))
                {
                    return titleText;
                }
            }
        }

        return shapes
            .Select(ReadShapeText)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
    }

    private static List<string> GetBodyLines(SlidePart slidePart, string titleText)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shapes = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<P.Shape>() ?? Enumerable.Empty<P.Shape>();
        foreach (var shape in shapes)
        {
            var text = ReadShapeText(shape);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (string.Equals(text, titleText, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var paragraph in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = paragraph.Trim();
                if (normalized.Length == 0 || !seen.Add(normalized))
                {
                    continue;
                }

                lines.Add(normalized);
            }
        }

        return lines;
    }

    private static string ReadShapeText(P.Shape shape)
    {
        var paragraphs = shape.TextBody?.Elements<A.Paragraph>() ?? Enumerable.Empty<A.Paragraph>();
        return string.Join(
            "\n",
            paragraphs
                .Select(ReadParagraphText)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string ReadTextBody(A.TextBody? textBody)
    {
        var paragraphs = textBody?.Elements<A.Paragraph>() ?? Enumerable.Empty<A.Paragraph>();
        return string.Join(
            "\n",
            paragraphs
                .Select(ReadParagraphText)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string ReadParagraphText(A.Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<A.Text>().Select(static text => text.Text)).Trim();

    private static string Quote(string value) =>
        '"' + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
        + '"';

    private static string FormatInlineValue(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length == 0
            || normalized[0] == ' '
            || normalized[^1] == ' '
            || normalized.Contains('|')
            || normalized.Contains('"')
            || normalized.Contains('\n')
            || normalized[0] == '['
            || normalized[0] == '@')
        {
            return Quote(normalized);
        }

        return normalized;
    }

    private static string NormalizeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = new string(value
            .Trim()
            .Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray())
            .Trim('-');
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string FormatAnchor(long x, long y)
    {
        var column = Math.Clamp((int)Math.Floor((x / (double)SlideWidth) * DefaultGridWidth) + 1, 1, DefaultGridWidth);
        var row = Math.Clamp((int)Math.Floor((y / (double)SlideHeight) * DefaultGridHeight) + 1, 1, DefaultGridHeight);
        return ToColumnName(column) + row;
    }

    private static string FormatSpan(long cx, long cy)
    {
        var width = Math.Max(1, (int)Math.Round((cx / (double)SlideWidth) * DefaultGridWidth));
        var height = Math.Max(1, (int)Math.Round((cy / (double)SlideHeight) * DefaultGridHeight));
        return $"{width}x{height}";
    }

    private static string ToColumnName(int columnNumber)
    {
        var value = columnNumber;
        var letters = new Stack<char>();
        while (value > 0)
        {
            value--;
            letters.Push((char)('A' + (value % 26)));
            value /= 26;
        }

        return new string(letters.ToArray());
    }

    /// <summary>
    /// Represents one imported PowerPoint table.
    /// </summary>
    private sealed record ImportedTable(
        string Name,
        string Anchor,
        string Size,
        string[][] Rows,
        bool HasHeader,
        bool IsBanded);
}
