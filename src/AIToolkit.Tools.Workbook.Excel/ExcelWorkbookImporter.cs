using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AIToolkit.Tools.Workbook.Excel;

internal static class ExcelWorkbookImporter
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    public static string Import(SpreadsheetDocument spreadsheet, string reference)
    {
        var workbookPart = spreadsheet.WorkbookPart ?? throw new InvalidOperationException("Workbook part was not found.");
        var sharedStrings = LoadSharedStrings(workbookPart);
        var styles = ImportedStyleCatalog.Create(workbookPart.WorkbookStylesPart);
        var title = Path.GetFileNameWithoutExtension(reference);
        var lines = new List<string>
        {
            $"= {(string.IsNullOrWhiteSpace(title) ? "Workbook" : title)}",
            ":wbdoc: 4",
            $":date-system: {(workbookPart.Workbook?.WorkbookProperties?.Date1904?.Value == true ? "1904" : "1900")}",
        };

        var activeTab = workbookPart.Workbook?.BookViews?.Elements<WorkbookView>().FirstOrDefault()?.ActiveTab?.Value;
        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
        if (activeTab is not null && activeTab < sheets.Count)
        {
            lines.Add($":active: {sheets[(int)activeTab].Name!.Value}");
        }

        for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            if (sheet.Name is null)
            {
                continue;
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"== {sheet.Name.Value}");
            if (sheet.State?.Value == SheetStateValues.Hidden)
            {
                lines.Add("[state hidden]");
            }

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var worksheet = worksheetPart.Worksheet;
            var viewLine = BuildViewLine(worksheet);
            if (viewLine is not null)
            {
                lines.Add(viewLine);
            }

            var usedRange = worksheet?.SheetDimension?.Reference?.Value;
            if (!string.IsNullOrWhiteSpace(usedRange))
            {
                lines.Add($"[used {usedRange}]");
            }

            var importedCells = ReadCells(worksheetPart, sharedStrings, styles);
            foreach (var typeLine in BuildTypeLines(importedCells))
            {
                lines.Add(typeLine);
            }

            foreach (var row in BuildRows(importedCells))
            {
                lines.Add(row);
            }

            foreach (var merge in worksheet?.Elements<MergeCells>().SelectMany(static collection => collection.Elements<MergeCell>()) ?? Enumerable.Empty<MergeCell>())
            {
                if (merge.Reference?.Value is string mergeReference)
                {
                    var align = importedCells.TryGetValue(mergeReference.Split(':')[0], out var topLeft)
                        && topLeft.Style.TryGetAttrText(includeType: false) is string attrText
                        && attrText.Contains("align=", StringComparison.OrdinalIgnoreCase)
                            ? " " + string.Join(' ', topLeft.Style.GetAlignmentTokens())
                            : string.Empty;
                    lines.Add($"[merge {mergeReference}{align}]");
                }
            }

            foreach (var tablePart in worksheetPart.TableDefinitionParts)
            {
                if (tablePart.Table is Table table && table.Reference is not null && table.Name is not null)
                {
                    var tableEntries = new List<string>();
                    if (table.AutoFilter is not null)
                    {
                        tableEntries.Add("filter");
                    }

                    if (table.TotalsRowShown?.Value == true)
                    {
                        tableEntries.Add("totals");
                    }

                    if (table.TableStyleInfo?.ShowRowStripes?.Value == true)
                    {
                        tableEntries.Add("banded");
                    }

                    lines.Add($"[table {table.Name.Value} {table.Reference.Value}{(tableEntries.Count > 0 ? " " + string.Join(' ', tableEntries) : string.Empty)}]");
                }
            }

            foreach (var validationLine in BuildValidationLines(worksheet))
            {
                lines.Add(validationLine);
            }

            foreach (var cfLine in BuildConditionalFormattingLines(worksheetPart, styles))
            {
                lines.Add(cfLine);
            }

            foreach (var chartBlock in BuildChartBlocks(worksheetPart))
            {
                lines.AddRange(chartBlock.Split('\n'));
            }
        }

        return string.Join('\n', lines);
    }

    private static Dictionary<string, ImportedCell> ReadCells(
        WorksheetPart worksheetPart,
        IReadOnlyList<string> sharedStrings,
        ImportedStyleCatalog styles)
    {
        var result = new Dictionary<string, ImportedCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in worksheetPart.Worksheet?.Descendants<Cell>() ?? Enumerable.Empty<Cell>())
        {
            if (cell.CellReference?.Value is not string reference)
            {
                continue;
            }

            var address = WorkbookAddressing.ParseCell(reference);
            var style = styles.GetStyle(cell.StyleIndex?.Value ?? 0U).Clone();
            var imported = new ImportedCell
            {
                Address = address,
                Style = style,
                RawContent = ConvertCellContent(cell, sharedStrings, style),
            };
            result[reference] = imported;
        }

        return result;
    }

    private static string ConvertCellContent(Cell cell, IReadOnlyList<string> sharedStrings, ImportedCellStyle style)
    {
        var rawValue = cell.CellValue?.Text ?? cell.InlineString?.InnerText ?? string.Empty;
        if (cell.CellFormula is not null)
        {
            var formula = "=" + cell.CellFormula.Text;
            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                style.Values["result"] = ConvertRawScalar(rawValue, cell.DataType?.Value, sharedStrings, style.Kind);
            }

            return formula;
        }

        return ConvertRawScalar(rawValue, cell.DataType?.Value, sharedStrings, style.Kind);
    }

    private static string ConvertRawScalar(string rawValue, CellValues? dataType, IReadOnlyList<string> sharedStrings, string? kind)
    {
        if (dataType == CellValues.SharedString && int.TryParse(rawValue, CultureInfo.InvariantCulture, out var sharedStringIndex) && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
        {
            return QuoteIfNeeded(sharedStrings[sharedStringIndex]);
        }

        if (dataType == CellValues.Boolean)
        {
            return rawValue == "1" ? "true" : "false";
        }

        if (dataType == CellValues.InlineString || dataType == CellValues.String)
        {
            return QuoteIfNeeded(rawValue);
        }

        if (kind == "date" && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var dateNumber))
        {
            return DateTime.FromOADate(dateNumber).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (kind == "time" && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var timeNumber))
        {
            return TimeOnly.FromTimeSpan(TimeSpan.FromDays(timeNumber)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (kind == "datetime" && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var dateTimeNumber))
        {
            return DateTime.FromOADate(dateTimeNumber).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        return rawValue.Length == 0 ? "blank" : rawValue;
    }

    private static IEnumerable<string> BuildTypeLines(Dictionary<string, ImportedCell> cells)
    {
        var groups = cells.Values
            .Where(static cell => cell.Style.Kind is "date" or "time" or "datetime")
            .GroupBy(static cell => (cell.Address.ColumnNumber, Kind: cell.Style.Kind!), static cell => cell)
            .OrderBy(static group => group.Key.ColumnNumber);

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(static cell => cell.Address.RowNumber).ToList();
            var start = ordered[0];
            var previous = ordered[0];
            var runLength = 1;
            for (var index = 1; index < ordered.Count; index++)
            {
                var current = ordered[index];
                if (current.Address.RowNumber == previous.Address.RowNumber + 1)
                {
                    previous = current;
                    runLength++;
                    continue;
                }

                if (runLength > 1)
                {
                    yield return $"[type {start.Address}:{previous.Address} {group.Key.Kind}]";
                    foreach (var cell in ordered.Where(cell => cell.Address.RowNumber >= start.Address.RowNumber && cell.Address.RowNumber <= previous.Address.RowNumber))
                    {
                        cell.HoistedType = true;
                    }
                }

                start = current;
                previous = current;
                runLength = 1;
            }

            if (runLength > 1)
            {
                yield return $"[type {start.Address}:{previous.Address} {group.Key.Kind}]";
                foreach (var cell in ordered.Where(cell => cell.Address.RowNumber >= start.Address.RowNumber && cell.Address.RowNumber <= previous.Address.RowNumber))
                {
                    cell.HoistedType = true;
                }
            }
        }
    }

    private static IEnumerable<string> BuildRows(Dictionary<string, ImportedCell> cells)
    {
        foreach (var group in cells.Values.GroupBy(static cell => cell.Address.RowNumber).OrderBy(static group => group.Key))
        {
            var ordered = group.OrderBy(static cell => cell.Address.ColumnNumber).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }

            var rowStyle = TryGetSharedRowStyle(ordered);
            var builder = new StringBuilder();
            builder.Append('@');
            builder.Append(ordered[0].Address);
            if (rowStyle is not null)
            {
                builder.Append(' ');
                builder.Append(rowStyle);
            }

            var currentColumn = ordered[0].Address.ColumnNumber;
            for (var index = 0; index < ordered.Count; index++)
            {
                var cell = ordered[index];
                while (currentColumn < cell.Address.ColumnNumber)
                {
                    builder.Append(" | blank");
                    currentColumn++;
                }

                builder.Append(" | ");
                var inlineStyle = cell.Style.TryGetAttrText(includeType: !cell.HoistedType);
                if (rowStyle is not null && inlineStyle == rowStyle)
                {
                    inlineStyle = null;
                }

                if (!string.IsNullOrWhiteSpace(inlineStyle))
                {
                    builder.Append(inlineStyle);
                    builder.Append(' ');
                }

                builder.Append(cell.HoistedType ? cell.RawContent : cell.GetSerializedContent());
                currentColumn = cell.Address.ColumnNumber + 1;
            }

            yield return builder.ToString();
        }
    }

    private static string? TryGetSharedRowStyle(List<ImportedCell> ordered)
    {
        if (ordered.Count < 2)
        {
            return null;
        }

        var first = ordered[0].Style.TryGetAttrText(includeType: false);
        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        return ordered.All(cell => string.Equals(cell.Style.TryGetAttrText(includeType: false), first, StringComparison.Ordinal))
            ? first
            : null;
    }

    private static string? BuildViewLine(Worksheet? worksheet)
    {
        var sheetView = worksheet?.SheetViews?.Elements<SheetView>().FirstOrDefault();
        if (sheetView is null)
        {
            return null;
        }

        var entries = new List<string>();
        var pane = sheetView.Elements<Pane>().FirstOrDefault();
        if (pane is not null)
        {
            var frozenRows = (int)(pane.HorizontalSplit?.Value ?? 0D);
            var frozenColumns = (int)(pane.VerticalSplit?.Value ?? 0D);
            if (frozenRows > 0 || frozenColumns > 0)
            {
                entries.Add($"freeze={frozenRows},{frozenColumns}");
            }
        }

        if (sheetView.ZoomScale?.Value is uint zoom)
        {
            entries.Add($"zoom={zoom.ToString(CultureInfo.InvariantCulture)}");
        }

        if (sheetView.ShowGridLines?.Value == true)
        {
            entries.Add("grid");
        }

        return entries.Count == 0 ? null : $"[view {string.Join(' ', entries)}]";
    }

    private static IEnumerable<string> BuildConditionalFormattingLines(WorksheetPart worksheetPart, ImportedStyleCatalog styles)
    {
        if (worksheetPart is null)
        {
            yield break;
        }

        var document = XDocument.Load(worksheetPart.GetStream(FileMode.Open, FileAccess.Read));
        foreach (var conditionalFormatting in document.Root?.Elements(SpreadsheetNs + "conditionalFormatting") ?? [])
        {
            var sqref = conditionalFormatting.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
            {
                continue;
            }

            foreach (var rule in conditionalFormatting.Elements(SpreadsheetNs + "cfRule"))
            {
                var type = rule.Attribute("type")?.Value;
                if (string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase))
                {
                    var op = rule.Attribute("operator")?.Value switch
                    {
                        "greaterThan" => ">",
                        "lessThan" => "<",
                        "equal" => "=",
                        "notEqual" => "!=",
                        "greaterThanOrEqual" => ">=",
                        "lessThanOrEqual" => "<=",
                        _ => "=",
                    };
                    var formula = rule.Element(SpreadsheetNs + "formula")?.Value;
                    var style = styles.GetDifferentialStyle(rule.Attribute("dxfId")?.Value);
                    var formatEntries = style.TryGetAttrText(includeType: false);
                    yield return $"[cf {sqref} when cell {op} {formula}{(string.IsNullOrWhiteSpace(formatEntries) ? string.Empty : " " + formatEntries[1..^1].Replace("bg=", "fill=", StringComparison.Ordinal))}]";
                }
                else if (string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase))
                {
                    var colorScale = rule.Element(SpreadsheetNs + "colorScale");
                    if (colorScale is null)
                    {
                        continue;
                    }

                    var points = colorScale.Elements(SpreadsheetNs + "cfvo").ToList();
                    var colors = colorScale.Elements(SpreadsheetNs + "color").Select(static color => NormalizeColor(color.Attribute("rgb")?.Value)).ToList();
                    if (points.Count == colors.Count && points.Count >= 2)
                    {
                        var entries = new List<string>();
                        for (var index = 0; index < points.Count; index++)
                        {
                            var point = points[index];
                            var label = point.Attribute("type")?.Value switch
                            {
                                "min" => "min",
                                "max" => "max",
                                var typeText when !string.IsNullOrWhiteSpace(typeText) && point.Attribute("val") is not null => $"{point.Attribute("val")!.Value}{(typeText == "percentile" ? "%" : string.Empty)}",
                                _ => "mid",
                            };
                            entries.Add($"{label}:{colors[index]}");
                        }

                        yield return $"[cf {sqref} scale({string.Join(',', entries)})]";
                    }
                }
                else if (string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase))
                {
                    var color = NormalizeColor(rule.Element(SpreadsheetNs + "dataBar")?.Element(SpreadsheetNs + "color")?.Attribute("rgb")?.Value) ?? "#5B9BD5";
                    yield return $"[cf {sqref} data-bar(color={color})]";
                }
                else if (string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase))
                {
                    var formula = rule.Element(SpreadsheetNs + "formula")?.Value;
                    if (string.IsNullOrWhiteSpace(formula))
                    {
                        continue;
                    }

                    var style = styles.GetDifferentialStyle(rule.Attribute("dxfId")?.Value);
                    var formatEntries = style.TryGetAttrText(includeType: false);
                    var normalizedFormula = formula.Length > 0 && formula[0] == '=' ? formula : "=" + formula;
                    if (TryNormalizeContainsTextFormula(normalizedFormula, out var containsText))
                    {
                        yield return $"[cf {sqref} when text contains {containsText}{(string.IsNullOrWhiteSpace(formatEntries) ? string.Empty : " " + formatEntries[1..^1].Replace("bg=", "fill=", StringComparison.Ordinal))}]";
                        continue;
                    }

                    yield return $"[cf {sqref} when formula({normalizedFormula}){(string.IsNullOrWhiteSpace(formatEntries) ? string.Empty : " " + formatEntries[1..^1].Replace("bg=", "fill=", StringComparison.Ordinal))}]";
                }
                else if (string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase))
                {
                    var iconSet = rule.Element(SpreadsheetNs + "iconSet");
                    var iconSetName = iconSet?.Attribute("iconSet")?.Value switch
                    {
                        "3Arrows" => "3-arrows",
                        "3TrafficLights1" => "3-traffic-lights",
                        _ => "3-arrows",
                    };

                    yield return $"[cf {sqref} icon-set({iconSetName})]";
                }
            }
        }
    }

    private static bool TryNormalizeContainsTextFormula(string formula, out string text)
    {
        var match = Regex.Match(formula, "^=NOT\\(ISERROR\\(SEARCH\\((?<text>.+?),(?<cell>\\$?[A-Z]+\\$?\\d+)\\)\\)\\)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            text = string.Empty;
            return false;
        }

        text = match.Groups["text"].Value;
        return true;
    }

    private static IEnumerable<string> BuildValidationLines(Worksheet? worksheet)
    {
        foreach (var dataValidation in worksheet?.Elements<DataValidations>().SelectMany(static group => group.Elements<DataValidation>()) ?? Enumerable.Empty<DataValidation>())
        {
            var range = dataValidation.SequenceOfReferences?.InnerText;
            if (string.IsNullOrWhiteSpace(range) || dataValidation.Type is null)
            {
                continue;
            }

            var behaviors = new List<string>();
            if (dataValidation.ErrorStyle?.Value == DataValidationErrorStyleValues.Warning)
            {
                behaviors.Add("warn");
            }
            else
            {
                behaviors.Add("reject");
            }

            var formula1 = dataValidation.Elements<Formula1>().FirstOrDefault()?.Text ?? string.Empty;
            var formula2 = dataValidation.Elements<Formula2>().FirstOrDefault()?.Text ?? string.Empty;
            var isCheckbox = string.Equals(formula1.Trim(), "\"TRUE,FALSE\"", StringComparison.OrdinalIgnoreCase);
            if (dataValidation.Type?.Value == DataValidationValues.List && dataValidation.ShowDropDown?.Value == false && !isCheckbox)
            {
                behaviors.Add("dropdown");
            }

            var validationType = dataValidation.Type?.Value;
            if (validationType is null)
            {
                continue;
            }
            string rule;
            if (validationType == DataValidationValues.List)
            {
                rule = NormalizeListValidation(formula1);
            }
            else if (validationType == DataValidationValues.Decimal || validationType == DataValidationValues.Whole)
            {
                rule = string.IsNullOrWhiteSpace(formula2)
                    ? $"number gt({formula1})"
                    : $"number between({formula1},{formula2})";
            }
            else if (validationType == DataValidationValues.Date)
            {
                rule = $"date between({NormalizeDateValidationValue(formula1)},{NormalizeDateValidationValue(formula2)})";
            }
            else if (validationType == DataValidationValues.TextLength)
            {
                rule = $"text-len between({formula1},{formula2})";
            }
            else if (validationType == DataValidationValues.Custom)
            {
                rule = NormalizeCustomValidation(formula1);
            }
            else
            {
                rule = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            yield return $"[validate {range} {rule}{(behaviors.Count > 0 ? " " + string.Join(' ', behaviors) : string.Empty)}]";
        }
    }

    private static string NormalizeListValidation(string formula1)
    {
        var trimmed = formula1.Trim();
        if (string.Equals(trimmed, "\"TRUE,FALSE\"", StringComparison.OrdinalIgnoreCase))
        {
            return "checkbox";
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            var items = trimmed[1..^1]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(QuoteIfNeeded);
            return $"list({string.Join(',', items)})";
        }

        return $"list({trimmed})";
    }

    private static string NormalizeDateValidationValue(string formula)
    {
        if (double.TryParse(formula, NumberStyles.Float, CultureInfo.InvariantCulture, out var oaDate))
        {
            return $"date(\"{DateTime.FromOADate(oaDate):yyyy-MM-dd}\")";
        }

        return formula;
    }

    private static string NormalizeCustomValidation(string formula1)
    {
        var trimmed = formula1.Trim();
        return trimmed.Length > 0 && trimmed[0] == '=' ? $"formula({trimmed})" : $"formula(={trimmed})";
    }

    private static IEnumerable<string> BuildChartBlocks(WorksheetPart worksheetPart)
    {
        if (worksheetPart.DrawingsPart is null)
        {
            yield break;
        }

        var drawingXml = XDocument.Load(worksheetPart.DrawingsPart.GetStream(FileMode.Open, FileAccess.Read));
        var anchors = drawingXml.Root?.Elements(SpreadsheetDrawingNs + "twoCellAnchor").ToList() ?? [];
        foreach (var anchor in anchors)
        {
            var graphicFrame = anchor.Element(SpreadsheetDrawingNs + "graphicFrame");
            var chartReference = graphicFrame?
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "chart")
                ?.Attribute(RelationshipNs + "id")
                ?.Value;
            if (string.IsNullOrWhiteSpace(chartReference))
            {
                continue;
            }

            var chartPart = (ChartPart?)worksheetPart.DrawingsPart.GetPartById(chartReference);
            if (chartPart is null)
            {
                continue;
            }

            var chartXml = XDocument.Load(chartPart.GetStream(FileMode.Open, FileAccess.Read));
            var plotArea = chartXml.Root?.Descendants(DrawingNs + "plotArea").FirstOrDefault();
            if (plotArea is null)
            {
                continue;
            }

            var chartTypeElements = plotArea.Elements().Where(static element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)).ToList();
            var chartType = chartTypeElements.Count > 1
                ? "combo"
                : NormalizeChartType(chartTypeElements.FirstOrDefault());
            var from = anchor.Element(SpreadsheetDrawingNs + "from");
            var to = anchor.Element(SpreadsheetDrawingNs + "to");
            var startCell = new WorkbookCellReference(
                RowNumber: int.Parse(from?.Element(SpreadsheetDrawingNs + "row")?.Value ?? "0", CultureInfo.InvariantCulture) + 1,
                ColumnNumber: int.Parse(from?.Element(SpreadsheetDrawingNs + "col")?.Value ?? "0", CultureInfo.InvariantCulture) + 1);
            var endColumn = int.Parse(to?.Element(SpreadsheetDrawingNs + "col")?.Value ?? "0", CultureInfo.InvariantCulture) + 1;
            var endRow = int.Parse(to?.Element(SpreadsheetDrawingNs + "row")?.Value ?? "0", CultureInfo.InvariantCulture) + 1;
            var width = Math.Max(240, (endColumn - startCell.ColumnNumber + 1) * 64);
            var height = Math.Max(160, (endRow - startCell.RowNumber + 1) * 20);
            var title = chartXml.Root?
                .Descendants(DrawingNs + "title")
                .Descendants()
                .FirstOrDefault(static element => element.Name.LocalName == "t")
                ?.Value ?? "Chart";

            var block = new List<string>
            {
                $"[chart {QuoteIfNeeded(title)} type={chartType} at={startCell} size={width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}px]"
            };
            foreach (var chartTypeElement in chartTypeElements)
            {
                var seriesType = NormalizeChartType(chartTypeElement);
                foreach (var series in chartTypeElement.Elements(DrawingNs + "ser"))
                {
                    var label = series.Descendants(DrawingNs + "tx").Descendants().FirstOrDefault(static element => element.Name.LocalName == "v")?.Value ?? "Series";
                    var categoryFormula = series.Descendants(DrawingNs + "cat").Descendants(DrawingNs + "f").FirstOrDefault()?.Value;
                    var valueFormula = series.Descendants(DrawingNs + "val").Descendants(DrawingNs + "f").FirstOrDefault()?.Value;
                    var sizeFormula = series.Descendants(DrawingNs + "bubbleSize").Descendants(DrawingNs + "f").FirstOrDefault()?.Value;
                    var parts = new List<string> { "- series", seriesType, QuoteIfNeeded(label) };
                    if (!string.IsNullOrWhiteSpace(categoryFormula))
                    {
                        parts.Add("cat=" + MinimizeReference(categoryFormula));
                    }

                    if (!string.IsNullOrWhiteSpace(valueFormula))
                    {
                        parts.Add("val=" + MinimizeReference(valueFormula));
                    }

                    if (!string.IsNullOrWhiteSpace(sizeFormula))
                    {
                        parts.Add("size=" + MinimizeReference(sizeFormula));
                    }

                    block.Add(string.Join(' ', parts));
                }
            }

            block.Add("[end]");
            yield return string.Join('\n', block);
        }
    }

    private static List<string> LoadSharedStrings(WorkbookPart workbookPart)
    {
        if (workbookPart.SharedStringTablePart?.SharedStringTable is not SharedStringTable table)
        {
            return [];
        }

        return table.Elements<SharedStringItem>()
            .Select(static item => item.InnerText)
            .ToList();
    }

    private static string NormalizeChartType(XElement? chartElement)
    {
        if (chartElement is null)
        {
            return "column";
        }

        return chartElement.Name.LocalName switch
        {
            "barChart" => chartElement.Elements(DrawingNs + "barDir").FirstOrDefault()?.Attribute("val")?.Value == "bar" ? "bar" : "column",
            "lineChart" => "line",
            "areaChart" => "area",
            "pieChart" => "pie",
            "doughnutChart" => "doughnut",
            "scatterChart" => "scatter",
            "bubbleChart" => "bubble",
            "radarChart" => "radar",
            _ => "column",
        };
    }

    private static string MinimizeReference(string formula) =>
        formula.TrimStart('=');

    private static string NormalizeColor(string? rgb)
    {
        if (string.IsNullOrWhiteSpace(rgb))
        {
            return "#000000";
        }

        var value = rgb.Length == 8 ? rgb[2..] : rgb;
        return "#" + value.ToUpperInvariant();
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0
            || value.StartsWith('=')
            || value.Contains(' ')
            || value.Contains('|')
            || value.StartsWith(' ')
            || value.EndsWith(' ')
            || string.Equals(value, "blank", StringComparison.OrdinalIgnoreCase))
        {
            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal) + "\"";
        }

        return value;
    }

    private sealed class ImportedCell
    {
        public required WorkbookCellReference Address { get; init; }

        public required ImportedCellStyle Style { get; init; }

        public required string RawContent { get; init; }

        public bool HoistedType { get; set; }

        public string GetSerializedContent()
        {
            return Style.Kind switch
            {
                "date" when !HoistedType => $"date({QuoteIfNeeded(RawContent)})",
                "time" when !HoistedType => $"time({QuoteIfNeeded(RawContent)})",
                "datetime" when !HoistedType => $"datetime({QuoteIfNeeded(RawContent)})",
                _ => RawContent,
            };
        }
    }
}

internal sealed class ImportedCellStyle
{
    public string? Kind { get; set; }

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ImportedCellStyle Clone()
    {
        var clone = new ImportedCellStyle
        {
            Kind = Kind,
        };
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

    public IEnumerable<string> GetAlignmentTokens()
    {
        if (Values.TryGetValue("align", out var align))
        {
            yield return "align=" + align;
        }
    }

    public string? TryGetAttrText(bool includeType)
    {
        var parts = new List<string>();
        foreach (var pair in Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.Equals(pair.Key, "__type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts.Add(pair.Value.IndexOfAny([' ', '"']) >= 0
                ? $"{pair.Key}=\"{pair.Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : $"{pair.Key}={pair.Value}");
        }

        parts.AddRange(Flags.OrderBy(static flag => flag, StringComparer.Ordinal));
        return parts.Count == 0 ? null : "[" + string.Join(' ', parts) + "]";
    }
}

internal sealed class ImportedStyleCatalog
{
    private readonly IReadOnlyList<ImportedCellStyle> _styles;
    private readonly IReadOnlyList<ImportedCellStyle> _differentialStyles;

    private ImportedStyleCatalog(IReadOnlyList<ImportedCellStyle> styles, IReadOnlyList<ImportedCellStyle> differentialStyles)
    {
        _styles = styles;
        _differentialStyles = differentialStyles;
    }

    public static ImportedStyleCatalog Create(WorkbookStylesPart? workbookStylesPart)
    {
        if (workbookStylesPart?.Stylesheet is not Stylesheet stylesheet)
        {
            return new ImportedStyleCatalog([new ImportedCellStyle()], []);
        }

        var fonts = stylesheet.Fonts?.Elements<Font>().ToList() ?? [];
        var fills = stylesheet.Fills?.Elements<Fill>().ToList() ?? [];
        var numberingFormats = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
            .Where(static format => format.NumberFormatId is not null)
            .ToDictionary(static format => format.NumberFormatId!.Value, static format => format.FormatCode?.Value ?? string.Empty)
            ?? new Dictionary<uint, string>();

        var styles = new List<ImportedCellStyle>();
        foreach (var cellFormat in stylesheet.CellFormats?.Elements<CellFormat>() ?? [])
        {
            styles.Add(BuildCellStyle(cellFormat, fonts, fills, numberingFormats));
        }

        var differentialStyles = new List<ImportedCellStyle>();
        foreach (var differentialFormat in stylesheet.DifferentialFormats?.Elements<DifferentialFormat>() ?? [])
        {
            differentialStyles.Add(BuildDifferentialStyle(differentialFormat));
        }

        if (styles.Count == 0)
        {
            styles.Add(new ImportedCellStyle());
        }

        return new ImportedStyleCatalog(styles, differentialStyles);
    }

    public ImportedCellStyle GetStyle(uint styleIndex) =>
        styleIndex < _styles.Count ? _styles[(int)styleIndex] : new ImportedCellStyle();

    public ImportedCellStyle GetDifferentialStyle(string? indexText) =>
        uint.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index < _differentialStyles.Count
            ? _differentialStyles[(int)index]
            : new ImportedCellStyle();

    private static ImportedCellStyle BuildCellStyle(
        CellFormat cellFormat,
        List<Font> fonts,
        List<Fill> fills,
        Dictionary<uint, string> numberingFormats)
    {
        var style = new ImportedCellStyle();
        if (cellFormat.FontId?.Value is uint fontId && fontId < fonts.Count)
        {
            ApplyFont(fonts[(int)fontId], style);
        }

        if (cellFormat.FillId?.Value is uint fillId && fillId < fills.Count)
        {
            ApplyFill(fills[(int)fillId], style);
        }

        if (cellFormat.Alignment is Alignment alignment)
        {
            var horizontal = "left";
            if (alignment.Horizontal?.Value == HorizontalAlignmentValues.Center)
            {
                horizontal = "center";
            }
            else if (alignment.Horizontal?.Value == HorizontalAlignmentValues.Right)
            {
                horizontal = "right";
            }
            else if (alignment.Horizontal?.Value == HorizontalAlignmentValues.Justify)
            {
                horizontal = "justify";
            }

            var vertical = "middle";
            if (alignment.Vertical?.Value == VerticalAlignmentValues.Top)
            {
                vertical = "top";
            }
            else if (alignment.Vertical?.Value == VerticalAlignmentValues.Bottom)
            {
                vertical = "bottom";
            }

            if (!(horizontal == "left" && vertical == "middle"))
            {
                style.Values["align"] = $"{horizontal}/{vertical}";
            }

            if (alignment.WrapText?.Value == true)
            {
                style.Flags.Add("wrap");
            }
        }

        var formatCode = ResolveNumberFormat(cellFormat.NumberFormatId?.Value ?? 0U, numberingFormats);
        if (!string.IsNullOrWhiteSpace(formatCode) && !IsDefaultGeneralFormat(formatCode))
        {
            style.Values["fmt"] = formatCode;
        }

        var inferredKind = InferKind(formatCode);
        if (inferredKind is not null)
        {
            style.Kind = inferredKind;
            style.Values["__type"] = inferredKind;
        }
        return style;
    }

    private static ImportedCellStyle BuildDifferentialStyle(DifferentialFormat differentialFormat)
    {
        var style = new ImportedCellStyle();
        if (differentialFormat.Font is Font font)
        {
            ApplyFont(font, style);
        }

        if (differentialFormat.Fill is Fill fill)
        {
            ApplyFill(fill, style);
        }

        return style;
    }

    private static void ApplyFont(Font font, ImportedCellStyle style)
    {
        if (font.Bold is not null)
        {
            style.Flags.Add("bold");
        }

        if (font.Italic is not null)
        {
            style.Flags.Add("italic");
        }

        if (font.Underline is not null)
        {
            style.Flags.Add("underline");
        }

        if (font.Strike is not null)
        {
            style.Flags.Add("strike");
        }

        if (font.FontName?.Val?.Value is string fontName && !string.IsNullOrWhiteSpace(fontName) && !string.Equals(fontName, "Calibri", StringComparison.OrdinalIgnoreCase) && !string.Equals(fontName, "Aptos", StringComparison.OrdinalIgnoreCase))
        {
            style.Values["font"] = fontName;
        }

        if (font.FontSize?.Val?.Value is double fontSize && Math.Abs(fontSize - 11D) > 0.01D)
        {
            style.Values["size"] = fontSize.ToString("0.##", CultureInfo.InvariantCulture);
        }

        var rgb = font.Color?.Rgb?.Value;
        if (!string.IsNullOrWhiteSpace(rgb))
        {
            var normalized = NormalizeColor(rgb);
            if (!string.Equals(normalized, "#000000", StringComparison.OrdinalIgnoreCase) && !string.Equals(normalized, "#1F1F1F", StringComparison.OrdinalIgnoreCase))
            {
                style.Values["fg"] = normalized;
            }
        }
    }

    private static void ApplyFill(Fill fill, ImportedCellStyle style)
    {
        var rgb = fill.PatternFill?.ForegroundColor?.Rgb?.Value;
        if (!string.IsNullOrWhiteSpace(rgb))
        {
            var normalized = NormalizeColor(rgb);
            if (!string.Equals(normalized, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                style.Values["bg"] = normalized;
            }
        }
    }

    private static string ResolveNumberFormat(uint numberFormatId, Dictionary<uint, string> numberingFormats) =>
        numberingFormats.TryGetValue(numberFormatId, out var formatCode)
            ? formatCode
            : numberFormatId switch
            {
                14 => "m/d/yyyy",
                15 => "d-mmm-yy",
                16 => "d-mmm",
                17 => "mmm-yy",
                18 => "h:mm AM/PM",
                19 => "h:mm:ss AM/PM",
                20 => "h:mm",
                21 => "h:mm:ss",
                22 => "m/d/yyyy h:mm",
                _ => "General",
            };

    private static bool IsDefaultGeneralFormat(string formatCode) =>
        string.Equals(formatCode, "General", StringComparison.OrdinalIgnoreCase);

    private static string? InferKind(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return null;
        }

        var normalized = formatCode.ToLowerInvariant();
        var hasDate = normalized.Contains('y') || normalized.Contains("m/") || normalized.Contains("-m") || normalized.Contains('d');
        var hasTime = normalized.Contains('h') || normalized.Contains("ss");
        return (hasDate, hasTime) switch
        {
            (true, true) => "datetime",
            (true, false) => "date",
            (false, true) => "time",
            _ => null,
        };
    }

    private static string NormalizeColor(string? rgb)
    {
        if (string.IsNullOrWhiteSpace(rgb))
        {
            return "#000000";
        }

        var value = rgb.Length == 8 ? rgb[2..] : rgb;
        return "#" + value.ToUpperInvariant();
    }
}
