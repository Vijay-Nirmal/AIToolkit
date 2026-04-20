using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Workbook.Excel;

internal sealed class WorkbookDocModel
{
    public required string Title { get; init; }

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, WorkbookDocCellStyle> Styles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<WorkbookDocNamedItem> Names { get; } = [];

    public List<WorkbookDocSheetModel> Sheets { get; } = [];
}

internal sealed class WorkbookDocNamedItem
{
    public required string Name { get; init; }

    public required string Target { get; init; }
}

internal sealed class WorkbookDocSheetModel
{
    public required string Name { get; init; }

    public bool Hidden { get; set; }

    public WorkbookDocViewSettings View { get; } = new();

    public string? UsedRange { get; set; }

    public List<WorkbookDocTypeRange> TypeRanges { get; } = [];

    public List<WorkbookDocFormatRange> FormatRanges { get; } = [];

    public List<WorkbookDocRowModel> Rows { get; } = [];

    public Dictionary<string, WorkbookDocCellModel> ExplicitCells { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<WorkbookDocMerge> Merges { get; } = [];

    public List<string> ValidationLines { get; } = [];

    public List<string> ConditionalFormattingLines { get; } = [];

    public List<string> TableLines { get; } = [];

    public List<string> PivotBlocks { get; } = [];

    public List<string> ChartBlocks { get; } = [];

    public List<string> SparkLines { get; } = [];

    public List<string> ExtensionLines { get; } = [];
}

internal sealed class WorkbookDocViewSettings
{
    public int? FreezeRows { get; set; }

    public int? FreezeColumns { get; set; }

    public int? Zoom { get; set; }

    public bool ShowGridLines { get; set; }
}

internal sealed class WorkbookDocTypeRange
{
    public required string RangeText { get; init; }

    public required string Kind { get; init; }
}

internal sealed class WorkbookDocFormatRange
{
    public required string RangeText { get; init; }

    public required WorkbookDocCellStyle Style { get; init; }
}

internal sealed class WorkbookDocRowModel
{
    public required WorkbookCellReference Anchor { get; init; }

    public WorkbookDocCellStyle RowStyle { get; } = new();

    public List<WorkbookDocCellModel> Cells { get; } = [];
}

internal sealed class WorkbookDocCellModel
{
    public required WorkbookCellReference Address { get; init; }

    public WorkbookDocCellStyle Style { get; } = new();

    public required string RawContent { get; set; }
}

internal sealed class WorkbookDocMerge
{
    public required string RangeText { get; init; }

    public string? Alignment { get; init; }
}

internal sealed class WorkbookDocCellStyle
{
    public List<string> StyleNames { get; } = [];

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => StyleNames.Count == 0 && Values.Count == 0 && Flags.Count == 0;

    public WorkbookDocCellStyle Clone()
    {
        var clone = new WorkbookDocCellStyle();
        clone.StyleNames.AddRange(StyleNames);
        foreach (var pair in Values)
        {
            clone.Values[pair.Key] = pair.Value;
        }

        foreach (var flag in Flags)
        {
            clone.Flags.Add(flag);
        }

        return clone;
    }

    public void Apply(WorkbookDocCellStyle? other)
    {
        if (other is null)
        {
            return;
        }

        foreach (var styleName in other.StyleNames)
        {
            if (!StyleNames.Contains(styleName, StringComparer.OrdinalIgnoreCase))
            {
                StyleNames.Add(styleName);
            }
        }

        foreach (var pair in other.Values)
        {
            Values[pair.Key] = pair.Value;
        }

        foreach (var flag in other.Flags)
        {
            Flags.Add(flag);
        }
    }
}

internal readonly record struct WorkbookCellReference(int RowNumber, int ColumnNumber)
{
    public override string ToString() => $"{WorkbookAddressing.ToColumnName(ColumnNumber)}{RowNumber.ToString(CultureInfo.InvariantCulture)}";
}

internal readonly record struct WorkbookRangeReference(WorkbookCellReference Start, WorkbookCellReference End)
{
    public bool Contains(WorkbookCellReference cell) =>
        cell.RowNumber >= Start.RowNumber
        && cell.RowNumber <= End.RowNumber
        && cell.ColumnNumber >= Start.ColumnNumber
        && cell.ColumnNumber <= End.ColumnNumber;

    public override string ToString() =>
        Start.Equals(End) ? Start.ToString() : $"{Start}:{End}";
}

internal static partial class WorkbookAddressing
{
    public static WorkbookCellReference ParseCell(string text)
    {
        var match = CellReferenceRegex().Match(text.Trim());
        if (!match.Success)
        {
            throw new FormatException($"'{text}' is not a supported A1 cell reference.");
        }

        return new WorkbookCellReference(
            RowNumber: int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture),
            ColumnNumber: ToColumnNumber(match.Groups["col"].Value));
    }

    public static bool TryParseRange(string text, out WorkbookRangeReference range)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            range = default;
            return false;
        }

        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            try
            {
                var cell = ParseCell(parts[0]);
                range = new WorkbookRangeReference(cell, cell);
                return true;
            }
            catch (FormatException)
            {
                range = default;
                return false;
            }
        }

        if (parts.Length == 2)
        {
            try
            {
                var start = ParseCell(parts[0]);
                var end = ParseCell(parts[1]);
                range = new WorkbookRangeReference(
                    new WorkbookCellReference(Math.Min(start.RowNumber, end.RowNumber), Math.Min(start.ColumnNumber, end.ColumnNumber)),
                    new WorkbookCellReference(Math.Max(start.RowNumber, end.RowNumber), Math.Max(start.ColumnNumber, end.ColumnNumber)));
                return true;
            }
            catch (FormatException)
            {
                range = default;
                return false;
            }
        }

        range = default;
        return false;
    }

    public static int ToColumnNumber(string columnName)
    {
        var total = 0;
        foreach (var ch in columnName.ToUpperInvariant())
        {
            total *= 26;
            total += (ch - 'A') + 1;
        }

        return total;
    }

    public static string ToColumnName(int columnNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columnNumber, 1);

        var builder = new StringBuilder();
        var current = columnNumber;
        while (current > 0)
        {
            current--;
            builder.Insert(0, (char)('A' + (current % 26)));
            current /= 26;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^\$?(?<col>[A-Za-z]+)\$?(?<row>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CellReferenceRegex();
}
