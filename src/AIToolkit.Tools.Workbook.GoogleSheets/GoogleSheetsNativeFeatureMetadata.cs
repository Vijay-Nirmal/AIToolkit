using AIToolkit.Tools.Workbook.Excel;
using System.Text;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

internal sealed class GoogleSheetsNativeFeatureMetadata
{
    public List<string> WorkbookDirectives { get; } = [];

    public Dictionary<string, List<string>> SheetAdditions { get; } = new(StringComparer.Ordinal);

    public static GoogleSheetsNativeFeatureMetadata FromWorkbookDoc(string workbookDoc) =>
        FromModel(WorkbookDocParser.Parse(workbookDoc));

    public static GoogleSheetsNativeFeatureMetadata FromModel(WorkbookDocModel workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var metadata = new GoogleSheetsNativeFeatureMetadata();
        foreach (var name in workbook.Names)
        {
            metadata.WorkbookDirectives.Add($"[name {name.Name} = {name.Target}]");
        }

        foreach (var sheet in workbook.Sheets)
        {
            var additions = metadata.GetOrCreateSheetAdditions(sheet.Name);
            additions.AddRange(sheet.TableLines);
            additions.AddRange(sheet.ValidationLines);
            additions.AddRange(sheet.ConditionalFormattingLines);
            additions.AddRange(sheet.SparkLines);
            additions.AddRange(sheet.ChartBlocks);
            additions.AddRange(sheet.PivotBlocks);
        }

        return metadata;
    }

    public string ApplyTo(string workbookDoc)
    {
        ArgumentNullException.ThrowIfNull(workbookDoc);

        var normalized = NormalizeLineEndings(workbookDoc);
        var parsed = ParsedWorkbookDoc.Parse(normalized);
        foreach (var directive in WorkbookDirectives)
        {
            if (!parsed.Preface.Contains(directive, StringComparer.Ordinal))
            {
                parsed.Preface.Add(directive);
            }
        }

        foreach (var section in parsed.Sections)
        {
            if (!SheetAdditions.TryGetValue(section.Name, out var additions))
            {
                continue;
            }

            var bodyText = string.Join('\n', section.Body);
            foreach (var addition in additions)
            {
                var normalizedAddition = NormalizeLineEndings(addition);
                if (bodyText.Contains(normalizedAddition, StringComparison.Ordinal))
                {
                    continue;
                }

                section.Body.AddRange(normalizedAddition.Split('\n'));
                bodyText = string.Join('\n', section.Body);
            }
        }

        return parsed.ToText();
    }

    private List<string> GetOrCreateSheetAdditions(string sheetName)
    {
        if (!SheetAdditions.TryGetValue(sheetName, out var additions))
        {
            additions = [];
            SheetAdditions[sheetName] = additions;
        }

        return additions;
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed class ParsedWorkbookDoc
    {
        public List<string> Preface { get; } = [];

        public List<ParsedSheetSection> Sections { get; } = [];

        public static ParsedWorkbookDoc Parse(string workbookDoc)
        {
            var parsed = new ParsedWorkbookDoc();
            ParsedSheetSection? currentSection = null;
            foreach (var line in workbookDoc.Split('\n'))
            {
                if (line.StartsWith("== ", StringComparison.Ordinal))
                {
                    currentSection = new ParsedSheetSection(line[3..].Trim(), line);
                    parsed.Sections.Add(currentSection);
                    continue;
                }

                if (currentSection is null)
                {
                    parsed.Preface.Add(line);
                }
                else
                {
                    currentSection.Body.Add(line);
                }
            }

            return parsed;
        }

        public string ToText()
        {
            var builder = new StringBuilder();
            var wroteAny = false;
            foreach (var line in Preface)
            {
                if (wroteAny)
                {
                    builder.Append('\n');
                }

                builder.Append(line);
                wroteAny = true;
            }

            foreach (var section in Sections)
            {
                if (wroteAny)
                {
                    builder.Append('\n');
                }

                builder.Append(section.HeadingLine);
                wroteAny = true;
                foreach (var bodyLine in section.Body)
                {
                    builder.Append('\n');
                    builder.Append(bodyLine);
                }
            }

            return builder.ToString();
        }
    }

    private sealed record ParsedSheetSection(string Name, string HeadingLine)
    {
        public List<string> Body { get; } = [];
    }
}
