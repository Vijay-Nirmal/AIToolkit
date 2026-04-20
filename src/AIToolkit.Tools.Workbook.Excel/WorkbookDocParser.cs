using System.Globalization;
using System.Text;

namespace AIToolkit.Tools.Workbook.Excel;

internal static class WorkbookDocParser
{
    public static WorkbookDocModel Parse(string workbookDoc)
    {
        ArgumentNullException.ThrowIfNull(workbookDoc);

        var normalizedWorkbookDoc = DecodeJsonEscapes(workbookDoc)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedWorkbookDoc.Split('\n');
        WorkbookDocModel? workbook = null;
        WorkbookDocSheetModel? currentSheet = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (workbook is null)
            {
                if (!line.StartsWith("= ", StringComparison.Ordinal))
                {
                    throw new FormatException("WorkbookDoc must start with '= Workbook Name'.");
                }

                workbook = new WorkbookDocModel
                {
                    Title = line[2..].Trim(),
                };
                continue;
            }

            if (line.StartsWith(':'))
            {
                var separator = line.IndexOf(':', 1);
                if (separator <= 1)
                {
                    continue;
                }

                workbook.Attributes[line[1..separator].Trim()] = line[(separator + 1)..].Trim();
                continue;
            }

            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                currentSheet = new WorkbookDocSheetModel
                {
                    Name = line[3..].Trim(),
                };
                workbook.Sheets.Add(currentSheet);
                continue;
            }

            if (currentSheet is null)
            {
                ParseWorkbookDirective(workbook, line);
                continue;
            }

            if (line[0] == '@')
            {
                currentSheet.Rows.Add(ParseRow(line));
                continue;
            }

            if (line.StartsWith("[chart ", StringComparison.Ordinal)
                || line.StartsWith("[pivot ", StringComparison.Ordinal))
            {
                var block = new StringBuilder();
                block.Append(rawLine);
                while (index + 1 < lines.Length)
                {
                    index++;
                    block.Append('\n');
                    block.Append(lines[index]);
                    if (string.Equals(lines[index].Trim(), "[end]", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                if (line.StartsWith("[chart ", StringComparison.Ordinal))
                {
                    currentSheet.ChartBlocks.Add(block.ToString());
                }
                else
                {
                    currentSheet.PivotBlocks.Add(block.ToString());
                }

                continue;
            }

            ParseSheetDirective(currentSheet, line);
        }

        if (workbook is null)
        {
            throw new FormatException("WorkbookDoc is empty.");
        }

        return workbook;
    }

    private static string DecodeJsonEscapes(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch != '\\' || index + 1 >= value.Length)
            {
                builder.Append(ch);
                continue;
            }

            var next = value[index + 1];
            switch (next)
            {
                case '"':
                    builder.Append('"');
                    index++;
                    break;
                case '\\':
                    builder.Append('\\');
                    index++;
                    break;
                case '/':
                    builder.Append('/');
                    index++;
                    break;
                case 'b':
                    builder.Append('\b');
                    index++;
                    break;
                case 'f':
                    builder.Append('\f');
                    index++;
                    break;
                case 'n':
                    builder.Append('\n');
                    index++;
                    break;
                case 'r':
                    builder.Append('\r');
                    index++;
                    break;
                case 't':
                    builder.Append('\t');
                    index++;
                    break;
                case 'u' or 'U' when index + 5 < value.Length
                    && ushort.TryParse(value.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint):
                    builder.Append((char)codePoint);
                    index += 5;
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void ParseWorkbookDirective(WorkbookDocModel workbook, string line)
    {
        if (!line.StartsWith('[') || !line.EndsWith(']'))
        {
            return;
        }

        var body = line[1..^1].Trim();
        var tokens = SplitTokens(body);
        if (tokens.Count == 0)
        {
            return;
        }

        if (string.Equals(tokens[0], "style", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 2)
        {
            var style = new WorkbookDocCellStyle();
            ParseStyleTokens(tokens.Skip(2), style);
            workbook.Styles[tokens[1]] = style;
            return;
        }

        if (string.Equals(tokens[0], "name", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 4 && tokens[2] == "=")
        {
            workbook.Names.Add(new WorkbookDocNamedItem
            {
                Name = tokens[1],
                Target = string.Join(" ", tokens.Skip(3)),
            });
        }
    }

    private static void ParseSheetDirective(WorkbookDocSheetModel sheet, string line)
    {
        if (line.StartsWith("[cell ", StringComparison.Ordinal))
        {
            var close = line.IndexOf(']');
            if (close < 0)
            {
                return;
            }

            var cellBody = line[1..close].Trim();
            var cellTokens = SplitTokens(cellBody);
            if (cellTokens.Count < 2)
            {
                return;
            }

            var cell = new WorkbookDocCellModel
            {
                Address = WorkbookAddressing.ParseCell(cellTokens[1]),
                RawContent = line[(close + 1)..].Trim()
            };
            ParseStyleTokens(cellTokens.Skip(2), cell.Style);
            sheet.ExplicitCells[cell.Address.ToString()] = cell;
            return;
        }

        if (!line.StartsWith('[') || !line.EndsWith(']'))
        {
            return;
        }

        var directiveBody = line[1..^1].Trim();
        var directiveTokens = SplitTokens(directiveBody);
        if (directiveTokens.Count == 0)
        {
            return;
        }

        switch (directiveTokens[0].ToLowerInvariant())
        {
            case "state" when directiveTokens.Count >= 2 && string.Equals(directiveTokens[1], "hidden", StringComparison.OrdinalIgnoreCase):
                sheet.Hidden = true;
                break;
            case "view":
                foreach (var token in directiveTokens.Skip(1))
                {
                    if (token.StartsWith("freeze=", StringComparison.OrdinalIgnoreCase))
                    {
                        var pair = token["freeze=".Length..].Split(',', StringSplitOptions.TrimEntries);
                        if (pair.Length == 2
                            && int.TryParse(pair[0], CultureInfo.InvariantCulture, out var rows)
                            && int.TryParse(pair[1], CultureInfo.InvariantCulture, out var columns))
                        {
                            sheet.View.FreezeRows = rows;
                            sheet.View.FreezeColumns = columns;
                        }
                    }
                    else if (token.StartsWith("zoom=", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(token["zoom=".Length..], CultureInfo.InvariantCulture, out var zoom))
                    {
                        sheet.View.Zoom = zoom;
                    }
                    else if (string.Equals(token, "grid", StringComparison.OrdinalIgnoreCase))
                    {
                        sheet.View.ShowGridLines = true;
                    }
                }

                break;
            case "used" when directiveTokens.Count >= 2:
                sheet.UsedRange = directiveTokens[1];
                break;
            case "type" when directiveTokens.Count >= 3:
                sheet.TypeRanges.Add(new WorkbookDocTypeRange { RangeText = directiveTokens[1], Kind = directiveTokens[2] });
                break;
            case "fmt" when directiveTokens.Count >= 2:
                var fmtStyle = new WorkbookDocCellStyle();
                ParseStyleTokens(directiveTokens.Skip(2), fmtStyle);
                sheet.FormatRanges.Add(new WorkbookDocFormatRange { RangeText = directiveTokens[1], Style = fmtStyle });
                break;
            case "merge" when directiveTokens.Count >= 2:
                sheet.Merges.Add(new WorkbookDocMerge
                {
                    RangeText = directiveTokens[1],
                    Alignment = directiveTokens.Skip(2)
                        .FirstOrDefault(static token => token.StartsWith("align=", StringComparison.OrdinalIgnoreCase))
                        ?.Split('=', 2)[1],
                });
                break;
            case "validate":
                sheet.ValidationLines.Add(line);
                break;
            case "cf":
                sheet.ConditionalFormattingLines.Add(line);
                break;
            case "table":
                sheet.TableLines.Add(line);
                break;
            case "spark":
                sheet.SparkLines.Add(line);
                break;
            case "x":
                sheet.ExtensionLines.Add(line);
                break;
        }
    }

    private static WorkbookDocRowModel ParseRow(string line)
    {
        var index = 1;
        while (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] != '|')
        {
            index++;
        }

        var anchor = WorkbookAddressing.ParseCell(line[1..index]);
        var row = new WorkbookDocRowModel { Anchor = anchor };
        var remaining = line[index..].TrimStart();
        if (remaining.StartsWith('['))
        {
            var close = FindBracketClose(remaining, 0);
            if (close > 0)
            {
                ParseAttrlist(remaining[1..close], row.RowStyle);
                remaining = remaining[(close + 1)..].TrimStart();
            }
        }

        if (!remaining.StartsWith('|'))
        {
            return row;
        }

        var cells = SplitTopLevel(remaining[1..], '|');
        var currentColumn = anchor.ColumnNumber;
        foreach (var cellToken in cells)
        {
            var cell = ParseCellToken(cellToken, anchor.RowNumber, currentColumn);
            row.Cells.Add(cell);
            var span = 1;
            if (cell.Style.Values.TryGetValue("span", out var spanValue)
                && int.TryParse(spanValue, CultureInfo.InvariantCulture, out var parsedSpan)
                && parsedSpan > 1)
            {
                span = parsedSpan;
            }

            currentColumn += span;
        }

        return row;
    }

    private static WorkbookDocCellModel ParseCellToken(string token, int rowNumber, int columnNumber)
    {
        var trimmed = token.Trim();
        var cell = new WorkbookDocCellModel
        {
            Address = new WorkbookCellReference(rowNumber, columnNumber),
            RawContent = "blank",
        };

        if (trimmed.Length == 0)
        {
            return cell;
        }

        if (trimmed.StartsWith('['))
        {
            var close = FindBracketClose(trimmed, 0);
            if (close > 0)
            {
                ParseAttrlist(trimmed[1..close], cell.Style);
                trimmed = trimmed[(close + 1)..].TrimStart();
            }
        }

        cell.RawContent = trimmed.Length == 0 ? "blank" : trimmed;
        return cell;
    }

    private static void ParseAttrlist(string body, WorkbookDocCellStyle style)
    {
        ParseStyleTokens(SplitTokens(body), style);
    }

    private static void ParseStyleTokens(IEnumerable<string> tokens, WorkbookDocCellStyle style)
    {
        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            if (token[0] == '.')
            {
                style.StyleNames.Add(token[1..]);
                continue;
            }

            var separator = token.IndexOf('=');
            if (separator > 0)
            {
                var key = token[..separator];
                var value = token[(separator + 1)..];
                if (string.Equals(key, "numfmt", StringComparison.OrdinalIgnoreCase))
                {
                    key = "fmt";
                }

                if (string.Equals(key, "style", StringComparison.OrdinalIgnoreCase))
                {
                    style.StyleNames.Add(Unquote(value));
                }
                else
                {
                    style.Values[key] = Unquote(value);
                }

                continue;
            }

            style.Flags.Add(token);
        }
    }

    private static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        char quoteCharacter = '\0';
        var escape = false;
        foreach (var ch in text)
        {
            if (escape)
            {
                builder.Append(ch);
                escape = false;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(ch);
                escape = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                builder.Append(ch);
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteCharacter = ch;
                }
                else if (quoteCharacter == ch)
                {
                    inQuotes = false;
                    quoteCharacter = '\0';
                }
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        char quoteCharacter = '\0';
        var escape = false;
        var bracketDepth = 0;
        foreach (var ch in text)
        {
            if (escape)
            {
                builder.Append(ch);
                escape = false;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(ch);
                escape = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                builder.Append(ch);
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteCharacter = ch;
                }
                else if (quoteCharacter == ch)
                {
                    inQuotes = false;
                    quoteCharacter = '\0';
                }
                continue;
            }

            if (!inQuotes)
            {
                if (ch == '[')
                {
                    bracketDepth++;
                }
                else if (ch == ']' && bracketDepth > 0)
                {
                    bracketDepth--;
                }
                else if (ch == separator && bracketDepth == 0)
                {
                    parts.Add(builder.ToString());
                    builder.Clear();
                    continue;
                }
            }

            builder.Append(ch);
        }

        parts.Add(builder.ToString());
        return parts;
    }

    private static int FindBracketClose(string text, int startIndex)
    {
        var depth = 0;
        var inQuotes = false;
        char quoteCharacter = '\0';
        var escape = false;
        for (var index = startIndex; index < text.Length; index++)
        {
            var ch = text[index];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\')
            {
                escape = true;
                continue;
            }

            if (ch is '"' or '\'')
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteCharacter = ch;
                }
                else if (quoteCharacter == ch)
                {
                    inQuotes = false;
                    quoteCharacter = '\0';
                }
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return value;
        }

        var builder = new StringBuilder();
        var inner = value[1..^1];
        for (var index = 0; index < inner.Length; index++)
        {
            var ch = inner[index];
            if (ch == '\\' && index + 1 < inner.Length)
            {
                index++;
                builder.Append(inner[index] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    '|' => '|',
                    var other => other,
                });
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
