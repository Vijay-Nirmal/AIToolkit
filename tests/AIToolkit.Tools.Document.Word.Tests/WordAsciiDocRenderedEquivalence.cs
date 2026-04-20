using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;

namespace AIToolkit.Tools.Document.Word.Tests;

/// <summary>
/// Compares AsciiDoc inputs by the Word content they render rather than their exact source syntax.
/// </summary>
internal static class WordAsciiDocRenderedEquivalence
{
    public static void AssertEquivalent(string expectedAsciiDoc, string actualAsciiDoc, string caseName)
    {
        var expectedFingerprint = CreateFingerprint(expectedAsciiDoc);
        var actualFingerprint = CreateFingerprint(actualAsciiDoc);

        Assert.AreEqual(
            expectedFingerprint,
            actualFingerprint,
            CreateMismatchMessage(caseName, expectedAsciiDoc, actualAsciiDoc));
    }

    private static string CreateFingerprint(string asciiDoc)
    {
        using var stream = new MemoryStream();
        using var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        WordAsciiDocRenderer.Write(mainPart, asciiDoc);
        return CreateDocumentFingerprint(mainPart);
    }

    private static string CreateDocumentFingerprint(MainDocumentPart mainPart)
    {
        var body = mainPart.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var child in body.ChildElements)
        {
            builder.AppendLine(child switch
            {
                Paragraph paragraph => CreateParagraphFingerprint(paragraph, mainPart),
                Table table => CreateTableFingerprint(table, mainPart),
                _ => "E|" + child.LocalName,
            });
        }

        return builder.ToString();
    }

    private static string CreateParagraphFingerprint(Paragraph paragraph, MainDocumentPart mainPart)
    {
        var builder = new StringBuilder("P");
        AppendProperty(builder, "style", paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        AppendProperty(builder, "just", paragraph.ParagraphProperties?.Justification?.Val?.Value.ToString());
        AppendProperty(builder, "outline", paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value is int outlineLevel
            ? outlineLevel.ToString(CultureInfo.InvariantCulture)
            : null);
        AppendProperty(builder, "indent", paragraph.ParagraphProperties?.Indentation?.Left?.Value);
        AppendProperty(builder, "border", paragraph.ParagraphProperties?.ParagraphBorders?.BottomBorder?.Color?.Value);
        builder.Append('|');

        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    if (ShouldSkipRun(run))
                    {
                        break;
                    }

                    builder.Append(CreateRunFingerprint(run));
                    break;
                case Hyperlink hyperlink:
                    builder.Append(CreateHyperlinkFingerprint(hyperlink, mainPart));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string CreateHyperlinkFingerprint(Hyperlink hyperlink, MainDocumentPart mainPart)
    {
        var builder = new StringBuilder("H");
        AppendProperty(builder, "target", ResolveHyperlinkTarget(hyperlink, mainPart));
        builder.Append('|');
        foreach (var child in hyperlink.ChildElements)
        {
            if (child is Run run && !ShouldSkipRun(run))
            {
                builder.Append(CreateRunFingerprint(run));
            }
        }

        return builder.ToString();
    }

    private static string CreateRunFingerprint(Run run)
    {
        var builder = new StringBuilder("R");
        var properties = run.RunProperties;
        AppendProperty(builder, "b", Bool(properties?.Bold is not null));
        AppendProperty(builder, "i", Bool(properties?.Italic is not null));
        AppendProperty(builder, "u", Bool(properties?.Underline is not null));
        AppendProperty(builder, "h", Bool(properties?.Highlight is not null));
        AppendProperty(builder, "color", properties?.Color?.Val?.Value);
        AppendProperty(builder, "font", properties?.RunFonts?.Ascii?.Value);
        AppendProperty(builder, "style", properties?.RunStyle?.Val?.Value);
        builder.Append('|');

        foreach (var child in run.ChildElements)
        {
            builder.Append(child switch
            {
                Text text => "T[" + Escape(text.Text) + "]",
                Break lineBreak => "BR[" + (lineBreak.Type?.Value.ToString() ?? "TextWrapping") + "]",
                TabChar => "TAB",
                _ => child.LocalName,
            });
        }

        return builder.ToString();
    }

    private static string CreateTableFingerprint(Table table, MainDocumentPart mainPart)
    {
        var builder = new StringBuilder("TB");
        var properties = table.GetFirstChild<TableProperties>();
        AppendProperty(builder, "top", properties?.TableBorders?.TopBorder?.Color?.Value);
        AppendProperty(builder, "insideH", properties?.TableBorders?.InsideHorizontalBorder?.Color?.Value);
        AppendProperty(builder, "insideV", properties?.TableBorders?.InsideVerticalBorder?.Color?.Value);
        AppendProperty(builder, "grid", string.Join(',', table.Elements<TableGrid>().SelectMany(static grid => grid.Elements<GridColumn>()).Select(static column => column.Width?.Value ?? string.Empty)));
        builder.Append('|');

        foreach (var row in table.Elements<TableRow>())
        {
            builder.Append("ROW[");
            foreach (var cell in row.Elements<TableCell>())
            {
                builder.Append(CreateCellFingerprint(cell, mainPart));
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    private static string CreateCellFingerprint(TableCell cell, MainDocumentPart mainPart)
    {
        var builder = new StringBuilder("CELL");
        AppendProperty(builder, "fill", cell.TableCellProperties?.Shading?.Fill?.Value);
        AppendProperty(builder, "width", cell.TableCellProperties?.TableCellWidth?.Width?.Value);
        builder.Append('{');
        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            builder.Append(CreateParagraphFingerprint(paragraph, mainPart));
            builder.Append(';');
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static bool ShouldSkipRun(Run run)
    {
        var properties = run.RunProperties;
        if (properties is not null
            && (properties.Bold is not null
                || properties.Italic is not null
                || properties.Underline is not null
                || properties.Highlight is not null
                || properties.Color is not null
                || properties.RunFonts is not null
                || properties.RunStyle is not null))
        {
            return false;
        }

        var text = string.Concat(run.ChildElements.Select(GetInlineTextValue));
        return string.IsNullOrWhiteSpace(text);
    }

    private static string GetInlineTextValue(OpenXmlElement element) =>
        element switch
        {
            Text text => text.Text,
            Break => "\n",
            TabChar => "\t",
            _ => element.InnerText,
        };

    private static string ResolveHyperlinkTarget(Hyperlink hyperlink, MainDocumentPart mainPart)
    {
        var relationshipId = hyperlink.Id?.Value;
        if (!string.IsNullOrWhiteSpace(relationshipId))
        {
            var relationship = mainPart.HyperlinkRelationships.FirstOrDefault(candidate => candidate.Id == relationshipId);
            if (relationship is not null)
            {
                return relationship.Uri.AbsoluteUri;
            }
        }

        return hyperlink.Anchor?.Value ?? string.Empty;
    }

    private static void AppendProperty(StringBuilder builder, string name, string? value)
    {
        builder.Append('|');
        builder.Append(name);
        builder.Append('=');
        builder.Append(Escape(value ?? string.Empty));
    }

    private static string Bool(bool value) => value ? "1" : "0";

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal);

    private static string CreateMismatchMessage(string caseName, string expectedAsciiDoc, string actualAsciiDoc)
    {
        var builder = new StringBuilder();
        builder.Append("Best-effort rendered equivalence mismatch for '");
        builder.Append(caseName);
        builder.AppendLine("'.");
        builder.AppendLine("Original AsciiDoc:");
        builder.AppendLine(expectedAsciiDoc);
        builder.AppendLine("Imported AsciiDoc:");
        builder.Append(actualAsciiDoc);
        return builder.ToString();
    }
}