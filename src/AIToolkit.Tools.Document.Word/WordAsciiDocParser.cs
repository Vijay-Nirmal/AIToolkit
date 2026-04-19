using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Parses a focused subset of AsciiDoc into a block model for Word rendering.
/// </summary>
internal static partial class WordAsciiDocParser
{
    public static WordAsciiDocDocumentModel Parse(string asciiDoc)
    {
        var normalized = WordAsciiDocTextUtilities.NormalizeLineEndings(asciiDoc);
        var lines = normalized.Split('\n');
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var blocks = new List<WordAsciiDocBlockModel>();
        var index = 0;
        string? title = null;

        ParseHeader(lines, ref index, attributes, ref title);

        var pendingMetadata = new PendingBlockMetadata();
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            if (TryParseDocumentAttribute(line, out var attributeName, out var attributeValue))
            {
                attributes[attributeName] = attributeValue;
                index++;
                continue;
            }

            if (TryParseBlockMetadataLine(line, pendingMetadata))
            {
                index++;
                continue;
            }

            if (TryParseBlockTitle(line, out var blockTitle))
            {
                pendingMetadata.Title = blockTitle;
                index++;
                continue;
            }

            if (TryParseHeading(line, out var level, out var headingText))
            {
                blocks.Add(new WordAsciiDocHeadingBlockModel(level, ExpandAttributes(headingText, attributes), pendingMetadata.ToMetadata()));
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (TryParseAdmonitionParagraph(line, out var admonitionKind, out var admonitionText))
            {
                blocks.Add(new WordAsciiDocDelimitedBlockModel(
                    WordAsciiDocDelimitedBlockKind.Admonition,
                    admonitionKind,
                    null,
                    ExpandAttributes(admonitionText, attributes),
                    pendingMetadata.ToMetadata()));
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (TryParseBlockMacro(line, out var macroKind, out var macroTarget, out var macroLabel))
            {
                blocks.Add(new WordAsciiDocMacroBlockModel(macroKind, macroTarget, ExpandAttributes(macroLabel, attributes), pendingMetadata.ToMetadata()));
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (string.Equals(line, "<<<", StringComparison.Ordinal))
            {
                blocks.Add(new WordAsciiDocPageBreakBlockModel(pendingMetadata.ToMetadata()));
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (string.Equals(line, "'''", StringComparison.Ordinal))
            {
                blocks.Add(new WordAsciiDocThematicBreakBlockModel(pendingMetadata.ToMetadata()));
                pendingMetadata = new PendingBlockMetadata();
                index++;
                continue;
            }

            if (TryParseTable(lines, ref index, attributes, pendingMetadata, out var tableBlock))
            {
                blocks.Add(tableBlock);
                pendingMetadata = new PendingBlockMetadata();
                continue;
            }

            if (TryParseDelimitedBlock(lines, ref index, attributes, pendingMetadata, out var delimitedBlock))
            {
                blocks.Add(delimitedBlock);
                pendingMetadata = new PendingBlockMetadata();
                continue;
            }

            if (TryParseList(lines, ref index, attributes, pendingMetadata, out var listBlock))
            {
                blocks.Add(listBlock);
                pendingMetadata = new PendingBlockMetadata();
                continue;
            }

            blocks.Add(ParseParagraph(lines, ref index, attributes, pendingMetadata));
            pendingMetadata = new PendingBlockMetadata();
        }

        return new WordAsciiDocDocumentModel(title, attributes, blocks);
    }

    private static void ParseHeader(
        string[] lines,
        ref int index,
        Dictionary<string, string?> attributes,
        ref string? title)
    {
        if (lines.Length == 0 || !TryParseHeading(lines[0], out var level, out var headingText) || level != 1)
        {
            return;
        }

        title = headingText.Trim();
        index = 1;
        var consumedHeaderTextLine = false;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                break;
            }

            if (TryParseDocumentAttribute(line, out var attributeName, out var attributeValue))
            {
                attributes[attributeName] = attributeValue;
                index++;
                continue;
            }

            if (!consumedHeaderTextLine)
            {
                consumedHeaderTextLine = true;
                index++;
                continue;
            }

            break;
        }
    }

    private static bool TryParseDocumentAttribute(string line, out string name, out string? value)
    {
        var match = DocumentAttributePattern().Match(line);
        if (!match.Success)
        {
            name = string.Empty;
            value = null;
            return false;
        }

        name = match.Groups[1].Value.Trim();
        value = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
        return true;
    }

    private static bool TryParseBlockMetadataLine(string line, PendingBlockMetadata metadata)
    {
        var match = BlockMetadataPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var attrText = match.Groups[1].Value;
        foreach (var segment in SplitCommaSeparated(attrText))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment[0] == '.')
            {
                AddShorthandRoles(segment, metadata);
                continue;
            }

            if (segment[0] == '#')
            {
                metadata.NamedAttributes["id"] = segment[1..].Trim();
                continue;
            }

            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex > 0)
            {
                var key = segment[..equalsIndex].Trim();
                var rawValue = segment[(equalsIndex + 1)..].Trim();
                var normalizedValue = TrimQuotes(rawValue);
                if (string.Equals(key, "role", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var role in ParseRoleAttributeValue(normalizedValue))
                    {
                        metadata.Roles.Add(role);
                    }

                    continue;
                }

                if (string.Equals(key, "id", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.NamedAttributes["id"] = normalizedValue;
                    continue;
                }

                metadata.NamedAttributes[key] = normalizedValue;
                continue;
            }

            metadata.PositionalAttributes.Add(segment.Trim());
        }

        return true;
    }

    private static bool TryParseBlockTitle(string line, out string title)
    {
        if (line.Length > 1 && line[0] == '.' && !IsListItemLine(line) && !line.StartsWith("..", StringComparison.Ordinal))
        {
            title = line[1..].Trim();
            return true;
        }

        title = string.Empty;
        return false;
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        var match = HeadingPattern().Match(line);
        if (!match.Success)
        {
            level = 0;
            text = string.Empty;
            return false;
        }

        level = match.Groups[1].Value.Length;
        text = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool TryParseAdmonitionParagraph(string line, out string kind, out string text)
    {
        var match = AdmonitionParagraphPattern().Match(line);
        if (!match.Success)
        {
            kind = string.Empty;
            text = string.Empty;
            return false;
        }

        kind = match.Groups[1].Value.ToUpperInvariant();
        text = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool TryParseBlockMacro(string line, out WordAsciiDocMacroKind kind, out string target, out string? label)
    {
        var match = BlockMacroPattern().Match(line);
        if (!match.Success)
        {
            kind = default;
            target = string.Empty;
            label = null;
            return false;
        }

        kind = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "image" => WordAsciiDocMacroKind.Image,
            "audio" => WordAsciiDocMacroKind.Audio,
            _ => WordAsciiDocMacroKind.Video,
        };
        target = match.Groups[2].Value.Trim();
        label = SplitCommaSeparated(match.Groups[3].Value).FirstOrDefault();
        return true;
    }

    private static bool TryParseTable(
        string[] lines,
        ref int index,
        IReadOnlyDictionary<string, string?> attributes,
        PendingBlockMetadata pendingMetadata,
        out WordAsciiDocTableBlockModel tableBlock)
    {
        var metadata = pendingMetadata.ToMetadata();
        var usesExplicitDelimiters = string.Equals(lines[index], "|===", StringComparison.Ordinal);
        if (!usesExplicitDelimiters && !LooksLikeImplicitTableStart(lines, index, attributes, metadata))
        {
            tableBlock = null!;
            return false;
        }

        if (usesExplicitDelimiters)
        {
            index++;
        }

        var cellBuffer = new List<string>();
        var inferredColumnCount = 0;
        while (index < lines.Length)
        {
            if (usesExplicitDelimiters && string.Equals(lines[index], "|===", StringComparison.Ordinal))
            {
                break;
            }

            var expandedLine = ExpandAttributes(lines[index], attributes);
            if (string.IsNullOrWhiteSpace(expandedLine))
            {
                if (!usesExplicitDelimiters && !HasImplicitTableContinuation(lines, index + 1, attributes))
                {
                    break;
                }

                index++;
                continue;
            }

            var parsedCells = ParseTableCells(expandedLine);
            if (parsedCells.Count > 0)
            {
                inferredColumnCount = Math.Max(inferredColumnCount, parsedCells.Count);
                cellBuffer.AddRange(parsedCells);
            }
            else if (cellBuffer.Count > 0)
            {
                if (!usesExplicitDelimiters && IsBlockBoundaryLine(lines[index]))
                {
                    break;
                }

                cellBuffer[^1] = cellBuffer[^1] + "\n" + expandedLine.Trim();
            }
            else if (!usesExplicitDelimiters)
            {
                break;
            }

            index++;
        }

        if (usesExplicitDelimiters && index < lines.Length && string.Equals(lines[index], "|===", StringComparison.Ordinal))
        {
            index++;
        }

        var hasHeader = metadata.TryGetNamedAttribute("options", out var options)
            && options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static option => string.Equals(option, "header", StringComparison.OrdinalIgnoreCase));

        var configuredColumnCount = GetConfiguredTableColumnCount(metadata);
        var columnCount = Math.Max(configuredColumnCount, inferredColumnCount);
        if (columnCount <= 0)
        {
            columnCount = 1;
        }

        tableBlock = new WordAsciiDocTableBlockModel(hasHeader, BuildTableRows(cellBuffer, columnCount), metadata);
        return true;
    }

    private static bool LooksLikeImplicitTableStart(
        string[] lines,
        int index,
        IReadOnlyDictionary<string, string?> attributes,
        WordAsciiDocBlockMetadata metadata)
    {
        if (!HasImplicitTableMetadata(metadata))
        {
            return false;
        }

        var currentLine = ExpandAttributes(lines[index], attributes);
        var parsedCells = ParseTableCells(currentLine);
        return parsedCells.Count > 0;
    }

    private static bool HasImplicitTableMetadata(WordAsciiDocBlockMetadata metadata) =>
        metadata.NamedAttributes.ContainsKey("cols")
        || metadata.NamedAttributes.ContainsKey("options")
        || metadata.NamedAttributes.ContainsKey("format");

    private static bool HasImplicitTableContinuation(string[] lines, int index, IReadOnlyDictionary<string, string?> attributes)
    {
        while (index < lines.Length)
        {
            var expandedLine = ExpandAttributes(lines[index], attributes);
            if (string.IsNullOrWhiteSpace(expandedLine))
            {
                index++;
                continue;
            }

            return ParseTableCells(expandedLine).Count > 0;
        }

        return false;
    }

    private static List<string> ParseTableCells(string line)
    {
        if (!line.StartsWith('|'))
        {
            return [];
        }

        var cells = line.Split('|', StringSplitOptions.None)
            .Skip(1)
            .Select(static cell => cell.Trim())
            .ToList();

        if (line.Length > 0 && line[^1] == '|' && cells.Count > 0 && cells[^1].Length == 0)
        {
            cells.RemoveAt(cells.Count - 1);
        }

        for (var index = 0; index < cells.Count; index++)
        {
            cells[index] = NormalizeMalformedTableCellSyntax(cells[index]);
        }

        return cells;
    }

    private static List<IReadOnlyList<string>> BuildTableRows(List<string> cells, int columnCount)
    {
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < cells.Count; index += columnCount)
        {
            var row = new string[columnCount];
            for (var offset = 0; offset < columnCount; offset++)
            {
                row[offset] = index + offset < cells.Count ? cells[index + offset] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static bool TryParseDelimitedBlock(
        string[] lines,
        ref int index,
        IReadOnlyDictionary<string, string?> attributes,
        PendingBlockMetadata pendingMetadata,
        out WordAsciiDocDelimitedBlockModel block)
    {
        if (!TryGetDelimiterKind(lines[index], pendingMetadata, out var kind, out var label, out var language))
        {
            block = null!;
            return false;
        }

        var delimiter = lines[index];
        index++;
        var builder = new StringBuilder();
        while (index < lines.Length && !string.Equals(lines[index], delimiter, StringComparison.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(ExpandAttributes(lines[index], attributes));
            index++;
        }

        if (index < lines.Length && string.Equals(lines[index], delimiter, StringComparison.Ordinal))
        {
            index++;
        }

        block = new WordAsciiDocDelimitedBlockModel(kind, label, language, builder.ToString(), pendingMetadata.ToMetadata());
        return true;
    }

    private static bool TryGetDelimiterKind(
        string line,
        PendingBlockMetadata metadata,
        out WordAsciiDocDelimitedBlockKind kind,
        out string? label,
        out string? language)
    {
        label = null;
        language = null;
        if (string.Equals(line, "----", StringComparison.Ordinal))
        {
            kind = WordAsciiDocDelimitedBlockKind.Source;
            if (metadata.PositionalAttributes.Count > 0 && string.Equals(metadata.PositionalAttributes[0], "source", StringComparison.OrdinalIgnoreCase))
            {
                language = metadata.PositionalAttributes.Count > 1 ? metadata.PositionalAttributes[1] : null;
            }

            return true;
        }

        if (string.Equals(line, "....", StringComparison.Ordinal))
        {
            kind = WordAsciiDocDelimitedBlockKind.Literal;
            return true;
        }

        if (string.Equals(line, "====", StringComparison.Ordinal))
        {
            if (metadata.PositionalAttributes.Count > 0 && IsAdmonition(metadata.PositionalAttributes[0]))
            {
                kind = WordAsciiDocDelimitedBlockKind.Admonition;
                label = metadata.PositionalAttributes[0].ToUpperInvariant();
                return true;
            }

            kind = WordAsciiDocDelimitedBlockKind.Example;
            return true;
        }

        if (string.Equals(line, "____", StringComparison.Ordinal))
        {
            var style = metadata.PositionalAttributes.FirstOrDefault();
            kind = string.Equals(style, "verse", StringComparison.OrdinalIgnoreCase)
                ? WordAsciiDocDelimitedBlockKind.Verse
                : WordAsciiDocDelimitedBlockKind.Quote;
            return true;
        }

        if (string.Equals(line, "****", StringComparison.Ordinal))
        {
            kind = WordAsciiDocDelimitedBlockKind.Example;
            return true;
        }

        if (string.Equals(line, "--", StringComparison.Ordinal))
        {
            kind = WordAsciiDocDelimitedBlockKind.Open;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryParseList(
        string[] lines,
        ref int index,
        IReadOnlyDictionary<string, string?> attributes,
        PendingBlockMetadata pendingMetadata,
        out WordAsciiDocListBlockModel listBlock)
    {
        if (!TryParseListItem(lines[index], attributes, out var firstItem))
        {
            listBlock = null!;
            return false;
        }

        var items = new List<WordAsciiDocListItemModel> { firstItem };
        index++;

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                break;
            }

            if (string.Equals(lines[index], "+", StringComparison.Ordinal) && items.Count > 0)
            {
                index++;
                var continuation = ParseParagraphText(lines, ref index, attributes);
                var previous = items[^1];
                items[^1] = previous with
                {
                    ContinuationText = string.IsNullOrWhiteSpace(previous.ContinuationText)
                        ? continuation
                        : previous.ContinuationText + "\n" + continuation,
                };
                continue;
            }

            if (!TryParseListItem(lines[index], attributes, out var item))
            {
                break;
            }

            items.Add(item);
            index++;
        }

        listBlock = new WordAsciiDocListBlockModel(items, pendingMetadata.ToMetadata());
        return true;
    }

    private static bool TryParseListItem(
        string line,
        IReadOnlyDictionary<string, string?> attributes,
        out WordAsciiDocListItemModel item)
    {
        var match = ListItemPattern().Match(line);
        if (!match.Success)
        {
            item = null!;
            return false;
        }

        var marker = match.Groups[1].Value;
        var text = ExpandAttributes(match.Groups[2].Value.Trim(), attributes);
        var kind = marker[0] switch
        {
            '<' => WordAsciiDocListKind.Callout,
            '.' => WordAsciiDocListKind.Ordered,
            _ => WordAsciiDocListKind.Unordered,
        };

        if (kind == WordAsciiDocListKind.Unordered)
        {
            var checklistMatch = ChecklistPattern().Match(text);
            if (checklistMatch.Success)
            {
                kind = string.Equals(checklistMatch.Groups[1].Value, "x", StringComparison.OrdinalIgnoreCase)
                    ? WordAsciiDocListKind.ChecklistChecked
                    : WordAsciiDocListKind.ChecklistUnchecked;
                text = checklistMatch.Groups[2].Value.Trim();
            }
        }

        var level = kind == WordAsciiDocListKind.Callout
            ? 1
            : Math.Max(1, marker.Trim('<', '>').Length);

        item = new WordAsciiDocListItemModel(kind, level, text);
        return true;
    }

    private static bool IsListItemLine(string line) =>
        ListItemPattern().IsMatch(line);

    private static bool IsBlockBoundaryLine(string line) =>
        string.IsNullOrWhiteSpace(line)
        || line.StartsWith("//", StringComparison.Ordinal)
        || TryParseDocumentAttribute(line, out _, out _)
        || BlockMetadataPattern().IsMatch(line)
        || TryParseBlockTitle(line, out _)
        || TryParseHeading(line, out _, out _)
        || TryParseAdmonitionParagraph(line, out _, out _)
        || TryParseBlockMacro(line, out _, out _, out _)
        || string.Equals(line, "<<<", StringComparison.Ordinal)
        || string.Equals(line, "'''", StringComparison.Ordinal)
        || string.Equals(line, "|===", StringComparison.Ordinal)
        || TryGetDelimiterKind(line, new PendingBlockMetadata(), out _, out _, out _)
        || IsListItemLine(line);

    private static WordAsciiDocParagraphBlockModel ParseParagraph(
        string[] lines,
        ref int index,
        IReadOnlyDictionary<string, string?> attributes,
        PendingBlockMetadata pendingMetadata)
    {
        var text = ParseParagraphText(lines, ref index, attributes);
        return new WordAsciiDocParagraphBlockModel(text, pendingMetadata.ToMetadata());
    }

    private static string ParseParagraphText(string[] lines, ref int index, IReadOnlyDictionary<string, string?> attributes)
    {
        var builder = new StringBuilder();
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("//", StringComparison.Ordinal)
                || TryParseDocumentAttribute(line, out _, out _)
                || BlockMetadataPattern().IsMatch(line)
                || TryParseBlockTitle(line, out _)
                || TryParseHeading(line, out _, out _)
                || TryParseAdmonitionParagraph(line, out _, out _)
                || TryParseBlockMacro(line, out _, out _, out _)
                || string.Equals(line, "<<<", StringComparison.Ordinal)
                || string.Equals(line, "'''", StringComparison.Ordinal)
                || string.Equals(line, "|===", StringComparison.Ordinal)
                || TryGetDelimiterKind(line, new PendingBlockMetadata(), out _, out _, out _)
                || IsListItemLine(line))
            {
                break;
            }

            var expanded = ExpandAttributes(line.TrimEnd(), attributes);
            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append(expanded.EndsWith(" +", StringComparison.Ordinal) ? '\n' : ' ');
            }

            if (expanded.EndsWith(" +", StringComparison.Ordinal))
            {
                builder.Append(expanded[..^2].TrimEnd());
                builder.Append('\n');
            }
            else
            {
                builder.Append(expanded.Trim());
            }

            index++;
        }

        return builder.ToString();
    }

    private static string ExpandAttributes(string? value, IReadOnlyDictionary<string, string?> attributes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return AttributeReferencePattern().Replace(
            value,
            match => attributes.TryGetValue(match.Groups[1].Value, out var attributeValue) && attributeValue is not null
                ? attributeValue
                : match.Value);
    }

    private static bool IsAdmonition(string value) =>
        value is "NOTE" or "TIP" or "IMPORTANT" or "WARNING" or "CAUTION";

    private static int GetConfiguredTableColumnCount(WordAsciiDocBlockMetadata metadata)
    {
        if (!metadata.TryGetNamedAttribute("cols", out var columnSpecification) || string.IsNullOrWhiteSpace(columnSpecification))
        {
            return 0;
        }

        return SplitCommaSeparated(columnSpecification).Count(static segment => !string.IsNullOrWhiteSpace(segment));
    }

    private static void AddShorthandRoles(string segment, PendingBlockMetadata metadata)
    {
        foreach (var role in ParseRoleList(segment[1..]))
        {
            metadata.Roles.Add(role);
        }

        var anchorSeparator = segment.IndexOf('#');
        if (anchorSeparator < 0 || anchorSeparator >= segment.Length - 1)
        {
            return;
        }

        metadata.NamedAttributes["id"] = segment[(anchorSeparator + 1)..].Trim();
    }

    private static string[] ParseRoleList(string rawRoles)
    {
        var anchorSeparator = rawRoles.IndexOf('#');
        var rolePortion = anchorSeparator >= 0 ? rawRoles[..anchorSeparator] : rawRoles;
        return rolePortion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] ParseRoleAttributeValue(string rawRoles) =>
        rawRoles
            .Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeMalformedTableCellSyntax(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return cell;
        }

        if (TryNormalizeMalformedTableCellRolePrefix(cell, out var normalizedCell))
        {
            return normalizedCell;
        }

        if (cell.StartsWith(":.", StringComparison.Ordinal))
        {
            var separator = cell.IndexOf(' ');
            if (separator > 2 && separator < cell.Length - 1)
            {
                var roleToken = cell[2..separator].Trim();
                var content = cell[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(roleToken) && !string.IsNullOrWhiteSpace(content))
                {
                    return $"[.{roleToken}]#{content}#";
                }
            }
        }

        if (cell.StartsWith("[.", StringComparison.Ordinal) && cell.Contains("]#", StringComparison.Ordinal))
        {
            var hashCount = cell.Count(static character => character == '#');
            if (hashCount % 2 != 0)
            {
                return cell + "#";
            }
        }

        return cell;
    }

    private static bool TryNormalizeMalformedTableCellRolePrefix(string cell, out string normalizedCell)
    {
        if (cell.Length <= 3 || cell[1] != '.' || !IsMalformedTableCellRolePrefix(cell[0]))
        {
            normalizedCell = string.Empty;
            return false;
        }

        var separator = cell.IndexOf(' ');
        if (separator <= 2 || separator >= cell.Length - 1)
        {
            normalizedCell = string.Empty;
            return false;
        }

        var roleToken = cell[2..separator].Trim();
        var content = cell[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(roleToken) || string.IsNullOrWhiteSpace(content))
        {
            normalizedCell = string.Empty;
            return false;
        }

        normalizedCell = $"[.{roleToken}]#{content}#";
        return true;
    }

    private static bool IsMalformedTableCellRolePrefix(char value) =>
        value is ':' or '=' or '<' or '>' or '^';

    private static List<string> SplitCommaSeparated(string input)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        foreach (var current in input)
        {
            if (current == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(current);
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                values.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            values.Add(builder.ToString().Trim());
        }

        return values;
    }

    private static string TrimQuotes(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;

    [GeneratedRegex(@"^:([^:]+):(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentAttributePattern();

    [GeneratedRegex(@"^\[(.*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockMetadataPattern();

    [GeneratedRegex(@"^(=+)\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(NOTE|TIP|IMPORTANT|WARNING|CAUTION):\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AdmonitionParagraphPattern();

    [GeneratedRegex(@"^(image|audio|video)::([^\[]+)\[(.*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockMacroPattern();

    [GeneratedRegex(@"^(\*+|-+|\.+|<\d+>)\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^\[(x| )\]\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChecklistPattern();

    [GeneratedRegex(@"\{([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeReferencePattern();

    /// <summary>
    /// Accumulates block metadata lines until the next block is parsed.
    /// </summary>
    private sealed class PendingBlockMetadata
    {
        public string? Title { get; set; }

        public List<string> Roles { get; } = [];

        public List<string> PositionalAttributes { get; } = [];

        public Dictionary<string, string> NamedAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WordAsciiDocBlockMetadata ToMetadata() =>
            new(Title, [.. Roles], [.. PositionalAttributes], new Dictionary<string, string>(NamedAttributes, StringComparer.OrdinalIgnoreCase));
    }
}