using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace AIToolkit.Tools.Workbook.Excel;

internal static class ExcelWorkbookWriter
{
    public static void Write(SpreadsheetDocument spreadsheet, WorkbookDocModel model)
    {
        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

        var styleRepository = new ExcelStyleRepository();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        var workbookView = new WorkbookView();
        if (model.Attributes.TryGetValue("active", out var activeSheetName))
        {
            var activeIndex = model.Sheets.FindIndex(sheet => string.Equals(sheet.Name, activeSheetName, StringComparison.Ordinal));
            if (activeIndex >= 0)
            {
                workbookView.ActiveTab = (uint)activeIndex;
            }
        }

        workbookPart.Workbook.BookViews = new BookViews(workbookView);
        workbookPart.Workbook.WorkbookProperties = new WorkbookProperties
        {
            Date1904 = string.Equals(model.Attributes.GetValueOrDefault("date-system"), "1904", StringComparison.Ordinal),
        };
        workbookPart.Workbook.CalculationProperties = new CalculationProperties
        {
            CalculationMode = string.Equals(model.Attributes.GetValueOrDefault("recalc"), "manual", StringComparison.OrdinalIgnoreCase)
                ? CalculateModeValues.Manual
                : CalculateModeValues.Auto,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true,
        };
        workbookPart.Workbook.DefinedNames = new DefinedNames();
        foreach (var name in model.Names)
        {
            workbookPart.Workbook.DefinedNames.AppendChild(new DefinedName
            {
                Name = name.Name,
                Text = name.Target,
            });
        }

        for (var index = 0; index < model.Sheets.Count; index++)
        {
            var sheetModel = model.Sheets[index];
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            WriteWorksheet(worksheetPart, model, sheetModel, styleRepository);

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = (uint)(index + 1),
                Name = sheetModel.Name,
                State = sheetModel.Hidden ? SheetStateValues.Hidden : null,
            });
        }

        workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = styleRepository.BuildStylesheet();
        workbookPart.Workbook.Save();
    }

    private static void WriteWorksheet(
        WorksheetPart worksheetPart,
        WorkbookDocModel workbook,
        WorkbookDocSheetModel sheetModel,
        ExcelStyleRepository styleRepository)
    {
        var sheetData = new SheetData();
        var worksheet = new Worksheet();
        worksheet.Append(CreateSheetViews(sheetModel.View));
        worksheet.Append(sheetData);

        var cellMap = BuildCellMap(workbook, sheetModel);
        var rowGroups = cellMap.Values
            .GroupBy(static cell => cell.Address.RowNumber)
            .OrderBy(static group => group.Key);
        var hyperlinks = new List<(string CellReference, string Uri)>();

        foreach (var rowGroup in rowGroups)
        {
            var row = new Row { RowIndex = (uint)rowGroup.Key };
            foreach (var cell in rowGroup.OrderBy(static candidate => candidate.Address.ColumnNumber))
            {
                row.Append(CreateCell(cell, styleRepository, hyperlinks));
            }

            sheetData.Append(row);
        }

        var mergeReferences = BuildMergeReferences(sheetModel, cellMap);
        if (mergeReferences.Count > 0)
        {
            var mergeCells = new MergeCells();
            foreach (var mergeReference in mergeReferences)
            {
                mergeCells.Append(new MergeCell { Reference = mergeReference });
            }

            worksheet.Append(mergeCells);
        }

        var usedRange = !string.IsNullOrWhiteSpace(sheetModel.UsedRange)
            ? sheetModel.UsedRange
            : TryComputeUsedRange(cellMap);
        if (!string.IsNullOrWhiteSpace(usedRange))
        {
            worksheet.SheetDimension = new SheetDimension { Reference = usedRange };
        }

        AppendConditionalFormatting(worksheet, sheetModel, styleRepository);
        AppendDataValidations(worksheet, sheetModel);
        worksheetPart.Worksheet = worksheet;
        AppendHyperlinks(worksheetPart, worksheet, hyperlinks);
        AppendCharts(worksheetPart, worksheet, sheetModel);
        worksheetPart.Worksheet.Save();
    }

    private static SheetViews CreateSheetViews(WorkbookDocViewSettings view)
    {
        var sheetView = new SheetView { WorkbookViewId = 0U };
        if (view.Zoom is int zoom)
        {
            sheetView.ZoomScale = (uint)Math.Clamp(zoom, 10, 400);
        }

        if (view.ShowGridLines)
        {
            sheetView.ShowGridLines = true;
        }

        if ((view.FreezeRows ?? 0) > 0 || (view.FreezeColumns ?? 0) > 0)
        {
            var freezeRows = Math.Max(0, view.FreezeRows ?? 0);
            var freezeColumns = Math.Max(0, view.FreezeColumns ?? 0);
            var topLeft = new WorkbookCellReference(freezeRows + 1, freezeColumns + 1).ToString();
            var pane = new Pane
            {
                TopLeftCell = topLeft,
                State = PaneStateValues.Frozen,
                VerticalSplit = freezeColumns == 0 ? null : freezeColumns,
                HorizontalSplit = freezeRows == 0 ? null : freezeRows,
                ActivePane = freezeRows > 0 && freezeColumns > 0
                    ? PaneValues.BottomRight
                    : freezeRows > 0 ? PaneValues.BottomLeft : PaneValues.TopRight,
            };
            sheetView.Append(pane);
        }

        return new SheetViews(sheetView);
    }

    private static Dictionary<string, PlacedCell> BuildCellMap(WorkbookDocModel workbook, WorkbookDocSheetModel sheetModel)
    {
        var cellMap = new Dictionary<string, PlacedCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheetModel.Rows)
        {
            foreach (var cell in row.Cells)
            {
                cellMap[cell.Address.ToString()] = new PlacedCell(cell.Address, ResolveStyle(workbook, sheetModel, row.RowStyle, cell.Style, cell.Address), cell.RawContent);
            }
        }

        foreach (var overrideCell in sheetModel.ExplicitCells.Values)
        {
            cellMap[overrideCell.Address.ToString()] = new PlacedCell(overrideCell.Address, ResolveStyle(workbook, sheetModel, null, overrideCell.Style, overrideCell.Address), overrideCell.RawContent);
        }

        foreach (var merge in sheetModel.Merges)
        {
            if (string.IsNullOrWhiteSpace(merge.Alignment) || !WorkbookAddressing.TryParseRange(merge.RangeText, out var range))
            {
                continue;
            }

            var topLeft = range.Start.ToString();
            if (!cellMap.TryGetValue(topLeft, out var cell))
            {
                continue;
            }

            var style = cell.Style.Clone();
            if (!style.Values.ContainsKey("align"))
            {
                style.Values["align"] = merge.Alignment;
                cellMap[topLeft] = cell with { Style = style };
            }
        }

        return cellMap;
    }

    private static List<string> BuildMergeReferences(WorkbookDocSheetModel sheetModel, Dictionary<string, PlacedCell> cellMap)
    {
        var merges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var merge in sheetModel.Merges)
        {
            merges.Add(merge.RangeText);
        }

        foreach (var cell in cellMap.Values)
        {
            if (cell.Style.Values.TryGetValue("span", out var spanValue)
                && int.TryParse(spanValue, CultureInfo.InvariantCulture, out var span)
                && span > 1)
            {
                var end = new WorkbookCellReference(cell.Address.RowNumber, cell.Address.ColumnNumber + span - 1);
                merges.Add($"{cell.Address}:{end}");
            }
        }

        return merges.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? TryComputeUsedRange(Dictionary<string, PlacedCell> cellMap)
    {
        if (cellMap.Count == 0)
        {
            return null;
        }

        var minRow = cellMap.Values.Min(static cell => cell.Address.RowNumber);
        var maxRow = cellMap.Values.Max(static cell => cell.Address.RowNumber);
        var minColumn = cellMap.Values.Min(static cell => cell.Address.ColumnNumber);
        var maxColumn = cellMap.Values.Max(static cell => cell.Address.ColumnNumber);
        return $"{new WorkbookCellReference(minRow, minColumn)}:{new WorkbookCellReference(maxRow, maxColumn)}";
    }

    private static WorkbookDocCellStyle ResolveStyle(
        WorkbookDocModel workbook,
        WorkbookDocSheetModel sheetModel,
        WorkbookDocCellStyle? rowStyle,
        WorkbookDocCellStyle cellStyle,
        WorkbookCellReference address)
    {
        var resolved = new WorkbookDocCellStyle();
        ApplyStyles(workbook, resolved, rowStyle);

        foreach (var formatRange in sheetModel.FormatRanges)
        {
            if (WorkbookAddressing.TryParseRange(formatRange.RangeText, out var range) && range.Contains(address))
            {
                ApplyStyles(workbook, resolved, formatRange.Style);
            }
        }

        foreach (var typeRange in sheetModel.TypeRanges)
        {
            if (WorkbookAddressing.TryParseRange(typeRange.RangeText, out var range) && range.Contains(address))
            {
                resolved.Values["__type"] = typeRange.Kind;
            }
        }

        ApplyStyles(workbook, resolved, cellStyle);
        return resolved;
    }

    private static void ApplyStyles(WorkbookDocModel workbook, WorkbookDocCellStyle target, WorkbookDocCellStyle? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var styleName in source.StyleNames)
        {
            if (workbook.Styles.TryGetValue(styleName, out var styleDefinition))
            {
                target.Apply(styleDefinition);
            }
        }

        target.Apply(source);
    }

    private static Cell CreateCell(PlacedCell cell, ExcelStyleRepository styleRepository, List<(string CellReference, string Uri)> hyperlinks)
    {
        var parsedValue = WorkbookDocValueParser.Parse(cell.RawContent, cell.Style);
        var styleIndex = styleRepository.GetStyleIndex(cell.Style, parsedValue.Kind);
        var spreadsheetCell = new Cell
        {
            CellReference = cell.Address.ToString(),
            StyleIndex = styleIndex,
        };

        switch (parsedValue.Kind)
        {
            case WorkbookValueKind.Blank:
                break;
            case WorkbookValueKind.String:
            case WorkbookValueKind.RichText:
            case WorkbookValueKind.Error:
                spreadsheetCell.DataType = parsedValue.Kind == WorkbookValueKind.Error ? CellValues.Error : CellValues.InlineString;
                if (parsedValue.Kind == WorkbookValueKind.Error)
                {
                    spreadsheetCell.CellValue = new CellValue(parsedValue.RawValue);
                }
                else
                {
                    spreadsheetCell.InlineString = new InlineString(new Text(parsedValue.RawValue) { Space = SpaceProcessingModeValues.Preserve });
                }

                break;
            case WorkbookValueKind.Boolean:
                spreadsheetCell.DataType = CellValues.Boolean;
                spreadsheetCell.CellValue = new CellValue(parsedValue.RawValue);
                break;
            case WorkbookValueKind.Number:
                spreadsheetCell.CellValue = new CellValue(parsedValue.RawValue);
                break;
            case WorkbookValueKind.Date:
            case WorkbookValueKind.Time:
            case WorkbookValueKind.DateTime:
                spreadsheetCell.CellValue = new CellValue(parsedValue.NumericValue!.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case WorkbookValueKind.Formula:
                spreadsheetCell.CellFormula = new CellFormula(parsedValue.RawValue.TrimStart('='));
                if (cell.Style.Values.TryGetValue("result", out var cachedResult))
                {
                    var cached = WorkbookDocValueParser.Parse(cachedResult, new WorkbookDocCellStyle());
                    spreadsheetCell.CellValue = new CellValue(cached.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? cached.RawValue);
                    if (cached.Kind is WorkbookValueKind.String or WorkbookValueKind.RichText)
                    {
                        spreadsheetCell.DataType = CellValues.String;
                    }
                    else if (cached.Kind == WorkbookValueKind.Boolean)
                    {
                        spreadsheetCell.DataType = CellValues.Boolean;
                    }
                }

                break;
        }

        if (cell.Style.Values.TryGetValue("link", out var hyperlink))
        {
            hyperlinks.Add((cell.Address.ToString(), hyperlink));
        }

        return spreadsheetCell;
    }

    private static void AppendHyperlinks(
        WorksheetPart worksheetPart,
        Worksheet worksheet,
        List<(string CellReference, string Uri)> hyperlinks)
    {
        if (hyperlinks.Count == 0)
        {
            return;
        }

        var container = new Hyperlinks();
        foreach (var hyperlink in hyperlinks)
        {
            var relationship = worksheetPart.AddHyperlinkRelationship(new Uri(hyperlink.Uri, UriKind.Absolute), true);
            container.Append(new Hyperlink
            {
                Reference = hyperlink.CellReference,
                Id = relationship.Id,
            });
        }

        worksheet.Append(container);
    }

    private static void AppendDataValidations(Worksheet worksheet, WorkbookDocSheetModel sheetModel)
    {
        if (sheetModel.ValidationLines.Count == 0)
        {
            return;
        }

        var validations = new DataValidations();
        foreach (var line in sheetModel.ValidationLines)
        {
            if (TryCreateDataValidation(line, out var validation))
            {
                validations.Append(validation);
            }
        }

        if (validations.ChildElements.Count > 0)
        {
            validations.Count = (uint)validations.ChildElements.Count;
            worksheet.Append(validations);
        }
    }

    private static bool TryCreateDataValidation(string line, out DataValidation validation)
    {
        validation = null!;
        if (!TryGetDirectiveTokens(line, out var tokens) || tokens.Count < 3 || !string.Equals(tokens[0], "validate", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rangeText = tokens[1];
        var validationRule = tokens[2];
        var behaviors = new List<string>();
        var type = DataValidationValues.Custom;
        string? formula1 = null;
        string? formula2 = null;

        if (validationRule.StartsWith("list(", StringComparison.OrdinalIgnoreCase))
        {
            type = DataValidationValues.List;
            formula1 = CreateListFormula(GetInnerFunctionText(validationRule));
            behaviors.AddRange(tokens.Skip(3));
        }
        else if (validationRule.StartsWith("formula(", StringComparison.OrdinalIgnoreCase))
        {
            type = DataValidationValues.Custom;
            formula1 = GetInnerFunctionText(validationRule);
            behaviors.AddRange(tokens.Skip(3));
        }
        else if (string.Equals(validationRule, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            type = DataValidationValues.List;
            formula1 = "\"TRUE,FALSE\"";
            behaviors.AddRange(tokens.Skip(3));
        }
        else if (tokens.Count >= 4)
        {
            var ruleKind = validationRule.ToLowerInvariant();
            var ruleOp = tokens[3];
            behaviors.AddRange(tokens.Skip(4));
            switch (ruleKind)
            {
                case "number":
                    CreateNumericValidation(ruleOp, out type, out formula1, out formula2);
                    break;
                case "date":
                    CreateDateValidation(ruleOp, out type, out formula1, out formula2);
                    break;
                case "text-len":
                    CreateTextLengthValidation(ruleOp, out type, out formula1, out formula2);
                    break;
                default:
                    return false;
            }
        }

        if (formula1 is null)
        {
            return false;
        }

        validation = new DataValidation
        {
            Type = type,
            SequenceOfReferences = new ListValue<StringValue> { InnerText = rangeText },
            AllowBlank = true,
            ErrorStyle = behaviors.Contains("warn", StringComparer.OrdinalIgnoreCase)
                ? DataValidationErrorStyleValues.Warning
                : DataValidationErrorStyleValues.Stop,
        };

        if (type == DataValidationValues.List)
        {
            validation.ShowDropDown = !behaviors.Contains("dropdown", StringComparer.OrdinalIgnoreCase);
        }

        validation.Append(new Formula1(formula1));
        if (!string.IsNullOrWhiteSpace(formula2))
        {
            validation.Append(new Formula2(formula2));
        }

        return true;
    }

    private static void AppendConditionalFormatting(Worksheet worksheet, WorkbookDocSheetModel sheetModel, ExcelStyleRepository styleRepository)
    {
        if (sheetModel.ConditionalFormattingLines.Count == 0)
        {
            return;
        }

        var priority = 1;
        foreach (var line in sheetModel.ConditionalFormattingLines)
        {
            if (TryCreateConditionalFormatting(line, styleRepository, ref priority, out var conditionalFormatting))
            {
                worksheet.Append(conditionalFormatting);
            }
        }
    }

    private static bool TryCreateConditionalFormatting(
        string line,
        ExcelStyleRepository styleRepository,
        ref int priority,
        out ConditionalFormatting conditionalFormatting)
    {
        conditionalFormatting = null!;
        if (!TryGetDirectiveTokens(line, out var tokens) || tokens.Count < 3 || !string.Equals(tokens[0], "cf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rangeText = tokens[1];
        var cfRule = new ConditionalFormatting
        {
            SequenceOfReferences = new ListValue<StringValue> { InnerText = rangeText },
        };

        var rule = new ConditionalFormattingRule
        {
            Priority = priority++,
        };

        if (tokens[2].StartsWith("data-bar(", StringComparison.OrdinalIgnoreCase))
        {
            rule.Type = ConditionalFormatValues.DataBar;
            var color = ParseKeyValueEntries(GetInnerFunctionText(tokens[2])).GetValueOrDefault("color") ?? "#5B9BD5";
            var dataBar = new DataBar();
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min });
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max });
            dataBar.Append(new Color { Rgb = HexBinaryValue.FromString(ExcelStyleRepository.NormalizeColor(color)!) });
            rule.Append(dataBar);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (string.Equals(tokens[2], "data-bar", StringComparison.OrdinalIgnoreCase))
        {
            rule.Type = ConditionalFormatValues.DataBar;
            var color = tokens.Skip(3)
                .FirstOrDefault(static token => token.StartsWith("color=", StringComparison.OrdinalIgnoreCase))?["color=".Length..]
                ?? "#5B9BD5";
            var dataBar = new DataBar();
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min });
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max });
            dataBar.Append(new Color { Rgb = HexBinaryValue.FromString(ExcelStyleRepository.NormalizeColor(color)!) });
            rule.Append(dataBar);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (tokens[2].StartsWith("scale(", StringComparison.OrdinalIgnoreCase))
        {
            rule.Type = ConditionalFormatValues.ColorScale;
            var colorScale = new ColorScale();
            var entries = ParseScaleEntries(GetInnerFunctionText(tokens[2])).ToArray();
            foreach (var entry in entries)
            {
                colorScale.Append(new ConditionalFormatValueObject
                {
                    Type = entry.Type,
                    Val = entry.Value,
                });
            }

            foreach (var entry in entries)
            {
                colorScale.Append(new Color
                {
                    Rgb = HexBinaryValue.FromString(ExcelStyleRepository.NormalizeColor(entry.Color)!)
                });
            }

            rule.Append(colorScale);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (string.Equals(tokens[2], "color-scale", StringComparison.OrdinalIgnoreCase))
        {
            rule.Type = ConditionalFormatValues.ColorScale;
            var colorScale = new ColorScale();
            var entries = ParseCompatibilityScaleEntries(tokens.Skip(3)).ToArray();
            foreach (var entry in entries)
            {
                colorScale.Append(new ConditionalFormatValueObject
                {
                    Type = entry.Type,
                    Val = entry.Value,
                });
            }

            foreach (var entry in entries)
            {
                colorScale.Append(new Color
                {
                    Rgb = HexBinaryValue.FromString(ExcelStyleRepository.NormalizeColor(entry.Color)!)
                });
            }

            rule.Append(colorScale);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (tokens[2].StartsWith("icon-set(", StringComparison.OrdinalIgnoreCase))
        {
            rule.Type = ConditionalFormatValues.IconSet;
            var iconSetName = GetInnerFunctionText(tokens[2]).Trim();
            var iconSet = new IconSet
            {
                IconSetValue = iconSetName switch
                {
                    "3-arrows" => IconSetValues.ThreeArrows,
                    "3-traffic-lights" => IconSetValues.ThreeTrafficLights1,
                    _ => IconSetValues.ThreeArrows,
                },
            };
            iconSet.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "0" });
            iconSet.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "33" });
            iconSet.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "67" });
            rule.Append(iconSet);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (tokens[2].StartsWith("cell(", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCellCondition(GetInnerFunctionText(tokens[2]), out var op, out var value))
            {
                return false;
            }

            rule.Type = ConditionalFormatValues.CellIs;
            rule.Operator = op switch
            {
                ">" => ConditionalFormattingOperatorValues.GreaterThan,
                "<" => ConditionalFormattingOperatorValues.LessThan,
                ">=" => ConditionalFormattingOperatorValues.GreaterThanOrEqual,
                "<=" => ConditionalFormattingOperatorValues.LessThanOrEqual,
                "!=" => ConditionalFormattingOperatorValues.NotEqual,
                _ => ConditionalFormattingOperatorValues.Equal,
            };
            rule.Append(new Formula(value));
            var style = ParseConditionalStyle(tokens.Skip(3));
            if (!style.IsEmpty)
            {
                rule.FormatId = styleRepository.GetDifferentialFormatIndex(style);
            }

            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (TryParseCompatibilityCellCondition(tokens, out var compatibilityOperator, out var compatibilityValue, out var compatibilityStyleTokens))
        {
            rule.Type = ConditionalFormatValues.CellIs;
            rule.Operator = compatibilityOperator;
            rule.Append(new Formula(compatibilityValue));
            var style = ParseConditionalStyle(compatibilityStyleTokens);
            if (!style.IsEmpty)
            {
                rule.FormatId = styleRepository.GetDifferentialFormatIndex(style);
            }

            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        if (tokens.Count >= 4 && string.Equals(tokens[2], "when", StringComparison.OrdinalIgnoreCase))
        {
            WorkbookDocCellStyle? style = null;
            if (string.Equals(tokens[3], "formula", StringComparison.OrdinalIgnoreCase) || tokens[3].StartsWith("formula(", StringComparison.OrdinalIgnoreCase))
            {
                var formulaToken = tokens[3].StartsWith("formula(", StringComparison.OrdinalIgnoreCase) ? tokens[3] : tokens[4];
                rule.Type = ConditionalFormatValues.Expression;
                rule.Append(new Formula(GetInnerFunctionText(formulaToken)));
                var styleStartIndex = tokens[3].StartsWith("formula(", StringComparison.OrdinalIgnoreCase) ? 4 : 5;
                style = ParseConditionalStyle(tokens.Skip(styleStartIndex));
            }
            else if (tokens.Count >= 6
                && string.Equals(tokens[3], "text", StringComparison.OrdinalIgnoreCase)
                && string.Equals(tokens[4], "contains", StringComparison.OrdinalIgnoreCase))
            {
                if (!WorkbookAddressing.TryParseRange(rangeText, out var range))
                {
                    return false;
                }

                var textToken = tokens[5];
                rule.Type = ConditionalFormatValues.Expression;
                rule.Append(new Formula($"NOT(ISERROR(SEARCH({textToken},{range.Start})))"));
                style = ParseConditionalStyle(tokens.Skip(6));
            }
            else if (tokens.Count >= 6 && string.Equals(tokens[3], "cell", StringComparison.OrdinalIgnoreCase))
            {
                rule.Type = ConditionalFormatValues.CellIs;
                rule.Operator = tokens[4] switch
                {
                    ">" => ConditionalFormattingOperatorValues.GreaterThan,
                    "<" => ConditionalFormattingOperatorValues.LessThan,
                    ">=" => ConditionalFormattingOperatorValues.GreaterThanOrEqual,
                    "<=" => ConditionalFormattingOperatorValues.LessThanOrEqual,
                    "!=" => ConditionalFormattingOperatorValues.NotEqual,
                    _ => ConditionalFormattingOperatorValues.Equal,
                };
                rule.Append(new Formula(tokens[5]));
                style = ParseConditionalStyle(tokens.Skip(6));
            }

            if (style is not null && !style.IsEmpty)
            {
                rule.FormatId = styleRepository.GetDifferentialFormatIndex(style);
            }

            rule.StopIfTrue = tokens.Contains("stop", StringComparer.OrdinalIgnoreCase);
            cfRule.Append(rule);
            conditionalFormatting = cfRule;
            return true;
        }

        return false;
    }

    private static void AppendCharts(WorksheetPart worksheetPart, Worksheet worksheet, WorkbookDocSheetModel sheetModel)
    {
        var charts = ParseCharts(sheetModel);
        if (charts.Count == 0)
        {
            return;
        }

        var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
        var anchors = new List<XElement>();
        for (var index = 0; index < charts.Count; index++)
        {
            var chart = charts[index];
            var chartPart = drawingsPart.AddNewPart<ChartPart>();
            WriteChartPart(chartPart, sheetModel.Name, chart, index);
            anchors.Add(CreateChartAnchor(drawingsPart.GetIdOfPart(chartPart), chart, index));
        }

        worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var document = new XDocument(
            new XElement(
                xdr + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", xdr),
                new XAttribute(XNamespace.Xmlns + "a", a),
                new XAttribute(XNamespace.Xmlns + "c", c),
                new XAttribute(XNamespace.Xmlns + "r", r),
                anchors));

        using var stream = drawingsPart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static List<WorkbookDocChart> ParseCharts(WorkbookDocSheetModel sheetModel)
    {
        var charts = new List<WorkbookDocChart>();
        foreach (var block in sheetModel.ChartBlocks)
        {
            var lines = block
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0 || !TryGetDirectiveTokens(lines[0], out var headerTokens) || headerTokens.Count < 2)
            {
                continue;
            }

            var chart = new WorkbookDocChart
            {
                Title = UnquoteToken(headerTokens[1]),
                Type = "column",
                At = new WorkbookCellReference(1, 1),
                WidthPx = 480,
                HeightPx = 280,
            };

            foreach (var token in headerTokens.Skip(2))
            {
                var separator = token.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = token[..separator];
                var value = UnquoteToken(token[(separator + 1)..]);
                if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase))
                {
                    chart.Type = value;
                }
                else if (string.Equals(key, "at", StringComparison.OrdinalIgnoreCase))
                {
                    chart.At = WorkbookAddressing.ParseCell(value);
                }
                else if (string.Equals(key, "size", StringComparison.OrdinalIgnoreCase))
                {
                    var size = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
                    var parts = size.Split('x', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var width)
                        && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var height))
                    {
                        chart.WidthPx = Math.Max(240, width);
                        chart.HeightPx = Math.Max(160, height);
                    }
                }
            }

            foreach (var line in lines.Skip(1))
            {
                if (line.Length == 0 || line[0] != '-')
                {
                    continue;
                }

                var tokens = SplitDirectiveTokens(line[1..].Trim());
                if (tokens.Count < 3 || !string.Equals(tokens[0], "series", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var series = new WorkbookDocChartSeries
                {
                    Type = tokens[1],
                    Name = UnquoteToken(tokens[2]),
                };

                foreach (var token in tokens.Skip(3))
                {
                    if (string.Equals(token, "labels", StringComparison.OrdinalIgnoreCase))
                    {
                        series.Labels = true;
                        continue;
                    }

                    var separator = token.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = token[..separator];
                    var value = UnquoteToken(token[(separator + 1)..]);
                    switch (key.ToLowerInvariant())
                    {
                        case "cat":
                            series.CategoryRange = value;
                            break;
                        case "val":
                            series.ValueRange = value;
                            break;
                        case "axis":
                            series.Axis = value;
                            break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(series.ValueRange))
                {
                    chart.Series.Add(series);
                }
            }

            if (chart.Series.Count > 0)
            {
                charts.Add(chart);
            }
        }

        return charts;
    }

    private static void WriteChartPart(ChartPart chartPart, string sheetName, WorkbookDocChart chart, int chartIndex)
    {
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var categoryAxisId = 48650000U + (uint)(chartIndex * 10);
        var primaryValueAxisId = categoryAxisId + 1;
        var secondaryValueAxisId = categoryAxisId + 2;

        var plotAreaChildren = new List<object> { new XElement(c + "layout") };
        var seriesGroups = chart.Series.GroupBy(static series => NormalizeChartSeriesType(series.Type)).ToList();
        var hasSecondaryAxis = chart.Series.Any(static series => string.Equals(series.Axis, "secondary", StringComparison.OrdinalIgnoreCase));
        var seriesIndex = 0;

        foreach (var seriesGroup in seriesGroups)
        {
            var chartElementName = seriesGroup.Key switch
            {
                "line" => c + "lineChart",
                "pie" => c + "pieChart",
                "bar" => c + "barChart",
                _ => c + "barChart",
            };

            var chartElement = new XElement(chartElementName);
            if (chartElementName == c + "barChart")
            {
                chartElement.Add(
                    new XElement(c + "barDir", new XAttribute("val", seriesGroup.Key == "bar" ? "bar" : "col")),
                    new XElement(c + "grouping", new XAttribute("val", "clustered")),
                    new XElement(c + "varyColors", new XAttribute("val", 0)));
            }
            else if (chartElementName == c + "lineChart")
            {
                chartElement.Add(
                    new XElement(c + "grouping", new XAttribute("val", "standard")),
                    new XElement(c + "varyColors", new XAttribute("val", 0)));
            }
            else if (chartElementName == c + "pieChart")
            {
                chartElement.Add(new XElement(c + "varyColors", new XAttribute("val", 1)));
            }

            foreach (var series in seriesGroup)
            {
                chartElement.Add(CreateChartSeriesElement(c, sheetName, series, seriesIndex++));
            }

            if (seriesGroup.Any(static item => item.Labels))
            {
                chartElement.Add(
                    new XElement(
                        c + "dLbls",
                        new XElement(c + "showLegendKey", new XAttribute("val", 0)),
                        new XElement(c + "showVal", new XAttribute("val", 1)),
                        new XElement(c + "showCatName", new XAttribute("val", 0)),
                        new XElement(c + "showSerName", new XAttribute("val", 0)),
                        new XElement(c + "showPercent", new XAttribute("val", 0)),
                        new XElement(c + "showBubbleSize", new XAttribute("val", 0))));
            }

            if (chartElementName != c + "pieChart")
            {
                chartElement.Add(new XElement(c + "axId", new XAttribute("val", categoryAxisId)));
                var axisId = seriesGroup.Any(static item => string.Equals(item.Axis, "secondary", StringComparison.OrdinalIgnoreCase))
                    ? secondaryValueAxisId
                    : primaryValueAxisId;
                chartElement.Add(new XElement(c + "axId", new XAttribute("val", axisId)));
            }

            plotAreaChildren.Add(chartElement);
        }

        if (seriesGroups.Any(static group => group.Key != "pie"))
        {
            plotAreaChildren.Add(CreateCategoryAxis(c, categoryAxisId, primaryValueAxisId));
            plotAreaChildren.Add(CreateValueAxis(c, primaryValueAxisId, categoryAxisId, false));
            if (hasSecondaryAxis)
            {
                plotAreaChildren.Add(CreateValueAxis(c, secondaryValueAxisId, categoryAxisId, true));
            }
        }

        var document = new XDocument(
            new XElement(
                c + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", c),
                new XAttribute(XNamespace.Xmlns + "a", a),
                new XAttribute(XNamespace.Xmlns + "r", r),
                new XElement(c + "lang", new XAttribute("val", "en-US")),
                new XElement(
                    c + "chart",
                    CreateChartTitle(c, a, chart.Title),
                    new XElement(c + "autoTitleDeleted", new XAttribute("val", 0)),
                    new XElement(c + "plotArea", plotAreaChildren),
                    new XElement(
                        c + "legend",
                        new XElement(c + "legendPos", new XAttribute("val", "r")),
                        new XElement(c + "layout")),
                    new XElement(c + "plotVisOnly", new XAttribute("val", 1)),
                    new XElement(c + "dispBlanksAs", new XAttribute("val", "gap")))));

        using var stream = chartPart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static XElement CreateChartSeriesElement(XNamespace c, string sheetName, WorkbookDocChartSeries series, int seriesIndex)
    {
        var element = new XElement(
            c + "ser",
            new XElement(c + "idx", new XAttribute("val", seriesIndex)),
            new XElement(c + "order", new XAttribute("val", seriesIndex)),
            new XElement(c + "tx", new XElement(c + "v", series.Name)));

        if (!string.IsNullOrWhiteSpace(series.CategoryRange))
        {
            element.Add(
                new XElement(
                    c + "cat",
                    new XElement(
                        c + "strRef",
                        new XElement(c + "f", BuildChartFormula(sheetName, series.CategoryRange!)))));
        }

        element.Add(
            new XElement(
                c + "val",
                new XElement(
                    c + "numRef",
                    new XElement(c + "f", BuildChartFormula(sheetName, series.ValueRange!)))));
        return element;
    }

    private static XElement CreateCategoryAxis(XNamespace c, uint axisId, uint crossAxisId) =>
        new(
            c + "catAx",
            new XElement(c + "axId", new XAttribute("val", axisId)),
            new XElement(c + "scaling", new XElement(c + "orientation", new XAttribute("val", "minMax"))),
            new XElement(c + "delete", new XAttribute("val", 0)),
            new XElement(c + "axPos", new XAttribute("val", "b")),
            new XElement(c + "tickLblPos", new XAttribute("val", "nextTo")),
            new XElement(c + "crossAx", new XAttribute("val", crossAxisId)),
            new XElement(c + "crosses", new XAttribute("val", "autoZero")),
            new XElement(c + "auto", new XAttribute("val", 1)),
            new XElement(c + "lblAlgn", new XAttribute("val", "ctr")),
            new XElement(c + "lblOffset", new XAttribute("val", 100)));

    private static XElement CreateValueAxis(XNamespace c, uint axisId, uint crossAxisId, bool secondary) =>
        new(
            c + "valAx",
            new XElement(c + "axId", new XAttribute("val", axisId)),
            new XElement(c + "scaling", new XElement(c + "orientation", new XAttribute("val", "minMax"))),
            new XElement(c + "delete", new XAttribute("val", 0)),
            new XElement(c + "axPos", new XAttribute("val", secondary ? "r" : "l")),
            new XElement(c + "majorGridlines"),
            new XElement(c + "numFmt", new XAttribute("formatCode", "General"), new XAttribute("sourceLinked", 1)),
            new XElement(c + "tickLblPos", new XAttribute("val", "nextTo")),
            new XElement(c + "crossAx", new XAttribute("val", crossAxisId)),
            new XElement(c + "crosses", new XAttribute("val", "autoZero")));

    private static XElement CreateChartTitle(XNamespace c, XNamespace a, string title) =>
        new(
            c + "title",
            new XElement(
                c + "tx",
                new XElement(
                    c + "rich",
                    new XElement(a + "bodyPr"),
                    new XElement(a + "lstStyle"),
                    new XElement(
                        a + "p",
                        new XElement(
                            a + "r",
                            new XElement(a + "t", title))))),
            new XElement(c + "layout"));

    private static XElement CreateChartAnchor(string relationshipId, WorkbookDocChart chart, int index)
    {
        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var columnSpan = Math.Max(4, (int)Math.Ceiling(chart.WidthPx / 64D));
        var rowSpan = Math.Max(8, (int)Math.Ceiling(chart.HeightPx / 20D));
        var endColumn = chart.At.ColumnNumber + columnSpan - 1;
        var endRow = chart.At.RowNumber + rowSpan - 1;

        return new XElement(
            xdr + "twoCellAnchor",
            new XElement(
                xdr + "from",
                new XElement(xdr + "col", chart.At.ColumnNumber - 1),
                new XElement(xdr + "colOff", 0),
                new XElement(xdr + "row", chart.At.RowNumber - 1),
                new XElement(xdr + "rowOff", 0)),
            new XElement(
                xdr + "to",
                new XElement(xdr + "col", endColumn - 1),
                new XElement(xdr + "colOff", 0),
                new XElement(xdr + "row", endRow - 1),
                new XElement(xdr + "rowOff", 0)),
            new XElement(
                xdr + "graphicFrame",
                new XAttribute("macro", string.Empty),
                new XElement(
                    xdr + "nvGraphicFramePr",
                    new XElement(xdr + "cNvPr", new XAttribute("id", index + 2), new XAttribute("name", $"Chart {index + 1}")),
                    new XElement(xdr + "cNvGraphicFramePr")),
                new XElement(
                    xdr + "xfrm",
                    new XElement(a + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(a + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0))),
                new XElement(
                    a + "graphic",
                    new XElement(
                        a + "graphicData",
                        new XAttribute("uri", c.NamespaceName),
                        new XElement(c + "chart", new XAttribute(r + "id", relationshipId))))),
            new XElement(xdr + "clientData"));
    }

    private static string BuildChartFormula(string sheetName, string rangeText)
    {
        if (rangeText.Contains('!', StringComparison.Ordinal))
        {
            var parts = rangeText.Split('!', 2);
            return QuoteSheetName(parts[0]) + "!" + ToAbsoluteRange(parts[1]);
        }

        return QuoteSheetName(sheetName) + "!" + ToAbsoluteRange(rangeText);
    }

    private static string ToAbsoluteRange(string rangeText)
    {
        if (!WorkbookAddressing.TryParseRange(rangeText, out var range))
        {
            return rangeText;
        }

        static string Format(WorkbookCellReference cell) =>
            $"${WorkbookAddressing.ToColumnName(cell.ColumnNumber)}${cell.RowNumber.ToString(CultureInfo.InvariantCulture)}";

        return range.Start.Equals(range.End)
            ? Format(range.Start)
            : $"{Format(range.Start)}:{Format(range.End)}";
    }

    private static string QuoteSheetName(string sheetName)
    {
        var trimmed = sheetName.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed;
        }

        return "'" + trimmed.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string NormalizeChartSeriesType(string type) =>
        type.ToLowerInvariant() switch
        {
            "line" => "line",
            "pie" => "pie",
            "bar" => "bar",
            _ => "column",
        };

    private static WorkbookDocCellStyle ParseConditionalStyle(IEnumerable<string> tokens)
    {
        var style = new WorkbookDocCellStyle();
        foreach (var token in tokens)
        {
            if (token.StartsWith("fill=", StringComparison.OrdinalIgnoreCase))
            {
                style.Values["bg"] = token["fill=".Length..];
            }
            else if (token.Contains('='))
            {
                var separator = token.IndexOf('=');
                style.Values[token[..separator]] = token[(separator + 1)..];
            }
            else
            {
                style.Flags.Add(token);
            }
        }

        return style;
    }

    private static bool TryParseCellCondition(string text, out string op, out string value)
    {
        foreach (var candidate in new[] { ">=", "<=", "!=", ">", "<", "=" })
        {
            if (text.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                value = text[candidate.Length..].Trim();
                return value.Length > 0;
            }
        }

        op = string.Empty;
        value = string.Empty;
        return false;
    }

    private static bool TryGetDirectiveTokens(string line, out List<string> tokens)
    {
        tokens = [];
        if (!line.StartsWith('[') || !line.EndsWith(']'))
        {
            return false;
        }

        tokens = SplitDirectiveTokens(line[1..^1]);
        return tokens.Count > 0;
    }

    private static List<string> SplitDirectiveTokens(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var escape = false;
        var parenDepth = 0;
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

            if (ch == '"')
            {
                builder.Append(ch);
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes)
            {
                if (ch == '(')
                {
                    parenDepth++;
                }
                else if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                }
            }

            if (char.IsWhiteSpace(ch) && !inQuotes && parenDepth == 0)
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

    private static string GetInnerFunctionText(string token)
    {
        var open = token.IndexOf('(');
        var close = token.LastIndexOf(')');
        return open >= 0 && close > open ? token[(open + 1)..close] : string.Empty;
    }

    private static string CreateListFormula(string innerText)
    {
        if (!innerText.Contains('"', StringComparison.Ordinal))
        {
            return innerText;
        }

        var items = SplitListArguments(innerText)
            .Select(UnquoteToken)
            .ToArray();
        return "\"" + string.Join(',', items) + "\"";
    }

    private static void CreateNumericValidation(string operationToken, out DataValidationValues type, out string? formula1, out string? formula2)
    {
        type = DataValidationValues.Decimal;
        formula1 = null;
        formula2 = null;
        if (operationToken.StartsWith("between(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitListArguments(GetInnerFunctionText(operationToken));
            if (args.Count == 2)
            {
                formula1 = args[0];
                formula2 = args[1];
                return;
            }
        }
        else if (operationToken.StartsWith("gt(", StringComparison.OrdinalIgnoreCase))
        {
            formula1 = GetInnerFunctionText(operationToken);
            formula2 = null;
            return;
        }
    }

    private static void CreateDateValidation(string operationToken, out DataValidationValues type, out string? formula1, out string? formula2)
    {
        type = DataValidationValues.Date;
        formula1 = null;
        formula2 = null;
        if (operationToken.StartsWith("between(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitListArguments(GetInnerFunctionText(operationToken));
            if (args.Count == 2)
            {
                formula1 = ConvertValidationLiteral(args[0]);
                formula2 = ConvertValidationLiteral(args[1]);
            }
        }
    }

    private static void CreateTextLengthValidation(string operationToken, out DataValidationValues type, out string? formula1, out string? formula2)
    {
        type = DataValidationValues.TextLength;
        formula1 = null;
        formula2 = null;
        if (operationToken.StartsWith("between(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitListArguments(GetInnerFunctionText(operationToken));
            if (args.Count == 2)
            {
                formula1 = args[0];
                formula2 = args[1];
            }
        }
    }

    private static string ConvertValidationLiteral(string token)
    {
        if (TryParseDateLiteral(token, out var oaDate))
        {
            return oaDate.ToString(CultureInfo.InvariantCulture);
        }

        return token;
    }

    private static bool TryParseDateLiteral(string token, out double oaDate)
    {
        oaDate = 0;
        if (!token.StartsWith("date(", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = UnquoteToken(GetInnerFunctionText(token));
        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        oaDate = date.ToDateTime(TimeOnly.MinValue).ToOADate();
        return true;
    }

    private static List<string> SplitListArguments(string text)
    {
        var items = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var escape = false;
        var parenDepth = 0;
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

            if (ch == '"')
            {
                builder.Append(ch);
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes)
            {
                if (ch == '(')
                {
                    parenDepth++;
                }
                else if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                }
                else if (ch == ',' && parenDepth == 0)
                {
                    items.Add(builder.ToString().Trim());
                    builder.Clear();
                    continue;
                }
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            items.Add(builder.ToString().Trim());
        }

        return items;
    }

    private static string UnquoteToken(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            : value;

    private static Dictionary<string, string> ParseKeyValueEntries(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SplitListArguments(text))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            result[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
        }

        return result;
    }

    private static bool TryParseCompatibilityCellCondition(
        List<string> tokens,
        out ConditionalFormattingOperatorValues operatorValue,
        out string value,
        out IEnumerable<string> styleTokens)
    {
        operatorValue = ConditionalFormattingOperatorValues.Equal;
        value = string.Empty;
        styleTokens = [];

        if (tokens.Count < 4)
        {
            return false;
        }

        switch (tokens[2].ToLowerInvariant())
        {
            case "gt":
                operatorValue = ConditionalFormattingOperatorValues.GreaterThan;
                break;
            case "lt":
                operatorValue = ConditionalFormattingOperatorValues.LessThan;
                break;
            case "gte":
            case "ge":
                operatorValue = ConditionalFormattingOperatorValues.GreaterThanOrEqual;
                break;
            case "lte":
            case "le":
                operatorValue = ConditionalFormattingOperatorValues.LessThanOrEqual;
                break;
            case "ne":
            case "neq":
                operatorValue = ConditionalFormattingOperatorValues.NotEqual;
                break;
            case "eq":
                operatorValue = ConditionalFormattingOperatorValues.Equal;
                break;
            default:
                return false;
        }

        if (string.IsNullOrWhiteSpace(tokens[3]))
        {
            return false;
        }

        value = tokens[3];
        styleTokens = tokens.Skip(4);
        return true;
    }

    private static IEnumerable<(ConditionalFormatValueObjectValues Type, string? Value, string Color)> ParseScaleEntries(string text)
    {
        foreach (var entry in SplitListArguments(text))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = entry[..separator].Trim();
            var color = entry[(separator + 1)..].Trim();
            if (string.Equals(label, "min", StringComparison.OrdinalIgnoreCase))
            {
                yield return (ConditionalFormatValueObjectValues.Min, null, color);
            }
            else if (string.Equals(label, "max", StringComparison.OrdinalIgnoreCase))
            {
                yield return (ConditionalFormatValueObjectValues.Max, null, color);
            }
            else if (label.Length > 0 && label[^1] == '%')
            {
                yield return (ConditionalFormatValueObjectValues.Percentile, label[..^1], color);
            }
        }
    }

    private static IEnumerable<(ConditionalFormatValueObjectValues Type, string? Value, string Color)> ParseCompatibilityScaleEntries(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            var separator = token.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = token[..separator].Trim();
            var color = token[(separator + 1)..].Trim();
            if (string.Equals(key, "min", StringComparison.OrdinalIgnoreCase))
            {
                yield return (ConditionalFormatValueObjectValues.Min, null, color);
            }
            else if (string.Equals(key, "max", StringComparison.OrdinalIgnoreCase))
            {
                yield return (ConditionalFormatValueObjectValues.Max, null, color);
            }
            else if (string.Equals(key, "mid", StringComparison.OrdinalIgnoreCase))
            {
                yield return (ConditionalFormatValueObjectValues.Percentile, "50", color);
            }
        }
    }

    private sealed record PlacedCell(WorkbookCellReference Address, WorkbookDocCellStyle Style, string RawContent);

    private sealed class WorkbookDocChart
    {
        public required string Title { get; set; }
        public required string Type { get; set; }
        public required WorkbookCellReference At { get; set; }
        public required int WidthPx { get; set; }
        public required int HeightPx { get; set; }
        public List<WorkbookDocChartSeries> Series { get; } = [];
    }

    private sealed class WorkbookDocChartSeries
    {
        public required string Type { get; set; }
        public required string Name { get; set; }
        public string? CategoryRange { get; set; }
        public string? ValueRange { get; set; }
        public string? Axis { get; set; }
        public bool Labels { get; set; }
    }
}

internal enum WorkbookValueKind
{
    Blank,
    String,
    Number,
    Boolean,
    Formula,
    Date,
    Time,
    DateTime,
    Error,
    RichText,
}

internal sealed record ParsedWorkbookValue(WorkbookValueKind Kind, string RawValue, double? NumericValue = null);

internal static class WorkbookDocValueParser
{
    public static ParsedWorkbookValue Parse(string rawContent, WorkbookDocCellStyle style)
    {
        var trimmed = rawContent.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "blank", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Blank, string.Empty);
        }

        if (trimmed.StartsWith('='))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Formula, trimmed);
        }

        if (TryParseTypedLiteral(trimmed, "date", out var dateText)
            && DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Date, dateText, date.ToDateTime(TimeOnly.MinValue).ToOADate());
        }

        if (TryParseTypedLiteral(trimmed, "time", out var timeText)
            && TimeOnly.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Time, timeText, time.ToTimeSpan().TotalDays);
        }

        if (TryParseTypedLiteral(trimmed, "datetime", out var dateTimeText)
            && DateTime.TryParse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.DateTime, dateTimeText, dateTime.ToOADate());
        }

        if (TryGetRangeType(style, out var typeKind))
        {
            if (typeKind == WorkbookValueKind.Date
                && DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var typedDate))
            {
                return new ParsedWorkbookValue(WorkbookValueKind.Date, trimmed, typedDate.ToDateTime(TimeOnly.MinValue).ToOADate());
            }

            if (typeKind == WorkbookValueKind.Time
                && TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var typedTime))
            {
                return new ParsedWorkbookValue(WorkbookValueKind.Time, trimmed, typedTime.ToTimeSpan().TotalDays);
            }

            if (typeKind == WorkbookValueKind.DateTime
                && DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var typedDateTime))
            {
                return new ParsedWorkbookValue(WorkbookValueKind.DateTime, trimmed, typedDateTime.ToOADate());
            }
        }

        if (bool.TryParse(trimmed, out var booleanValue))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Boolean, booleanValue ? "1" : "0");
        }

        if (trimmed.StartsWith('#'))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Error, trimmed);
        }

        if (TryParseTypedLiteral(trimmed, "rich", out var richText))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.RichText, richText);
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericValue))
        {
            return new ParsedWorkbookValue(WorkbookValueKind.Number, numericValue.ToString(CultureInfo.InvariantCulture), numericValue);
        }

        return new ParsedWorkbookValue(WorkbookValueKind.String, Unquote(trimmed));
    }

    private static bool TryGetRangeType(WorkbookDocCellStyle style, out WorkbookValueKind kind)
    {
        if (style.Values.TryGetValue("__type", out var typeValue))
        {
            kind = typeValue.ToLowerInvariant() switch
            {
                "date" => WorkbookValueKind.Date,
                "time" => WorkbookValueKind.Time,
                "datetime" => WorkbookValueKind.DateTime,
                _ => WorkbookValueKind.String,
            };

            return kind is WorkbookValueKind.Date or WorkbookValueKind.Time or WorkbookValueKind.DateTime;
        }

        kind = WorkbookValueKind.String;
        return false;
    }

    private static bool TryParseTypedLiteral(string text, string kind, out string value)
    {
        var prefix = kind + "(";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            value = Unquote(text[prefix.Length..^1]);
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\|", "|", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
            : value;
}

internal sealed class ExcelStyleRepository
{
    private readonly List<Font> _fonts = [new Font()];
    private readonly List<Fill> _fills =
    [
        new Fill(new PatternFill { PatternType = PatternValues.None }),
        new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
    ];
    private readonly List<Border> _borders = [new Border()];
    private readonly List<CellFormat> _cellFormats = [new CellFormat()];
    private readonly Dictionary<string, uint> _styleIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _customNumberFormats = new(StringComparer.Ordinal);
    private readonly List<DifferentialFormat> _differentialFormats = [];
    private readonly Dictionary<string, uint> _differentialFormatIndices = new(StringComparer.Ordinal);
    private uint _nextNumberFormatId = 164;

    public Stylesheet BuildStylesheet()
    {
        var stylesheet = new Stylesheet();
        var numberingFormats = BuildNumberingFormats();
        if (numberingFormats is not null)
        {
            stylesheet.Append(numberingFormats);
        }

        stylesheet.Append(
            new Fonts(_fonts) { Count = (uint)_fonts.Count },
            new Fills(_fills) { Count = (uint)_fills.Count },
            new Borders(_borders) { Count = (uint)_borders.Count },
            new CellStyleFormats(new CellFormat()),
            new CellFormats(_cellFormats) { Count = (uint)_cellFormats.Count },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1U },
            new DifferentialFormats(_differentialFormats) { Count = (uint)_differentialFormats.Count },
            new TableStyles { Count = 0U, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" });
        return stylesheet;
    }

    public uint GetStyleIndex(WorkbookDocCellStyle style, WorkbookValueKind valueKind)
    {
        var normalized = NormalizeStyle(style, valueKind);
        if (normalized.Key.Length == 0)
        {
            return 0;
        }

        if (_styleIndices.TryGetValue(normalized.Key, out var existing))
        {
            return existing;
        }

        var fontId = AddFont(normalized);
        var fillId = AddFill(normalized);
        var borderId = AddBorder(normalized);
        var numberFormatId = AddNumberFormat(normalized.NumberFormat);
        var format = new CellFormat
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyFont = fontId != 0U,
            ApplyFill = fillId > 1U,
            ApplyBorder = borderId != 0U,
        };

        if (numberFormatId is not null)
        {
            format.NumberFormatId = numberFormatId.Value;
            format.ApplyNumberFormat = true;
        }

        if (normalized.Alignment is not null)
        {
            format.Alignment = normalized.Alignment;
            format.ApplyAlignment = true;
        }

        var styleIndex = (uint)_cellFormats.Count;
        _cellFormats.Add(format);
        _styleIndices[normalized.Key] = styleIndex;
        return styleIndex;
    }

    public uint GetDifferentialFormatIndex(WorkbookDocCellStyle style)
    {
        var normalized = NormalizeStyle(style, WorkbookValueKind.String);
        if (_differentialFormatIndices.TryGetValue(normalized.Key, out var existing))
        {
            return existing;
        }

        var differential = new DifferentialFormat();
        if (normalized.FontKey.Length > 0)
        {
            var font = new Font();
            if (normalized.Bold)
            {
                font.Append(new Bold());
            }

            if (normalized.Italic)
            {
                font.Append(new Italic());
            }

            if (normalized.Underline)
            {
                font.Append(new Underline());
            }

            if (normalized.Strike)
            {
                font.Append(new Strike());
            }

            if (!string.IsNullOrWhiteSpace(normalized.ForegroundColor))
            {
                font.Append(new Color { Rgb = HexBinaryValue.FromString(normalized.ForegroundColor) });
            }

            differential.Append(font);
        }

        if (!string.IsNullOrWhiteSpace(normalized.BackgroundColor))
        {
            var fill = new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = HexBinaryValue.FromString(normalized.BackgroundColor) },
                BackgroundColor = new BackgroundColor { Indexed = 64U },
            });
            differential.Append(fill);
        }

        var index = (uint)_differentialFormats.Count;
        _differentialFormats.Add(differential);
        _differentialFormatIndices[normalized.Key] = index;
        return index;
    }

    private uint AddFont(NormalizedExcelStyle style)
    {
        if (style.FontKey.Length == 0)
        {
            return 0U;
        }

        for (var index = 0; index < _fonts.Count; index++)
        {
            if (string.Equals(_fonts[index].OuterXml, style.FontKey, StringComparison.Ordinal))
            {
                return (uint)index;
            }
        }

        var font = new Font();
        if (style.Bold)
        {
            font.Append(new Bold());
        }

        if (style.Italic)
        {
            font.Append(new Italic());
        }

        if (style.Underline)
        {
            font.Append(new Underline());
        }

        if (style.Strike)
        {
            font.Append(new Strike());
        }

        if (!string.IsNullOrWhiteSpace(style.FontName))
        {
            font.Append(new FontName { Val = style.FontName });
        }

        if (style.FontSize is double fontSize)
        {
            font.Append(new FontSize { Val = fontSize });
        }

        if (!string.IsNullOrWhiteSpace(style.ForegroundColor))
        {
            font.Append(new Color { Rgb = HexBinaryValue.FromString(style.ForegroundColor) });
        }

        _fonts.Add(font);
        return (uint)(_fonts.Count - 1);
    }

    private uint AddFill(NormalizedExcelStyle style)
    {
        if (string.IsNullOrWhiteSpace(style.BackgroundColor))
        {
            return 0U;
        }

        var patternFill = new PatternFill { PatternType = PatternValues.Solid };
        patternFill.ForegroundColor = new ForegroundColor { Rgb = HexBinaryValue.FromString(style.BackgroundColor) };
        patternFill.BackgroundColor = new BackgroundColor { Indexed = 64U };
        var fill = new Fill(patternFill);

        for (var index = 0; index < _fills.Count; index++)
        {
            if (string.Equals(_fills[index].OuterXml, fill.OuterXml, StringComparison.Ordinal))
            {
                return (uint)index;
            }
        }

        _fills.Add(fill);
        return (uint)(_fills.Count - 1);
    }

    private uint AddBorder(NormalizedExcelStyle style)
    {
        if (style.Border is null)
        {
            return 0U;
        }

        for (var index = 0; index < _borders.Count; index++)
        {
            if (string.Equals(_borders[index].OuterXml, style.Border.OuterXml, StringComparison.Ordinal))
            {
                return (uint)index;
            }
        }

        _borders.Add(style.Border);
        return (uint)(_borders.Count - 1);
    }

    private uint? AddNumberFormat(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return null;
        }

        if (_customNumberFormats.TryGetValue(formatCode, out var existing))
        {
            return existing;
        }

        var id = _nextNumberFormatId++;
        _customNumberFormats[formatCode] = id;
        return id;
    }

    private NumberingFormats? BuildNumberingFormats()
    {
        if (_customNumberFormats.Count == 0)
        {
            return null;
        }

        var numberingFormats = new NumberingFormats { Count = (uint)_customNumberFormats.Count };
        foreach (var pair in _customNumberFormats.OrderBy(static pair => pair.Value))
        {
            numberingFormats.Append(new NumberingFormat
            {
                NumberFormatId = pair.Value,
                FormatCode = StringValue.FromString(pair.Key),
            });
        }

        return numberingFormats;
    }

    private static NormalizedExcelStyle NormalizeStyle(WorkbookDocCellStyle style, WorkbookValueKind valueKind)
    {
        var foreground = NormalizeColor(style.Values.GetValueOrDefault("fg"));
        var background = NormalizeColor(style.Values.GetValueOrDefault("bg"));
        var fontName = style.Values.GetValueOrDefault("font");
        var fontSize = style.Values.TryGetValue("size", out var sizeText)
            && double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSizeValue)
                ? fontSizeValue
                : (double?)null;
        var numberFormat = style.Values.GetValueOrDefault("fmt") ?? valueKind switch
        {
            WorkbookValueKind.Date => "yyyy-mm-dd",
            WorkbookValueKind.Time => "hh:mm:ss",
            WorkbookValueKind.DateTime => "yyyy-mm-dd hh:mm:ss",
            _ => null,
        };

        Alignment? alignment = null;
        if (style.Values.TryGetValue("align", out var align))
        {
            var parts = align.Split('/', StringSplitOptions.TrimEntries);
            alignment = new Alignment();
            if (parts.Length >= 1)
            {
                alignment.Horizontal = parts[0].ToLowerInvariant() switch
                {
                    "center" => HorizontalAlignmentValues.Center,
                    "right" => HorizontalAlignmentValues.Right,
                    "justify" => HorizontalAlignmentValues.Justify,
                    _ => HorizontalAlignmentValues.Left,
                };
            }

            if (parts.Length >= 2)
            {
                alignment.Vertical = parts[1].ToLowerInvariant() switch
                {
                    "top" => VerticalAlignmentValues.Top,
                    "bottom" => VerticalAlignmentValues.Bottom,
                    _ => VerticalAlignmentValues.Center,
                };
            }
        }

        if (style.Flags.Contains("wrap"))
        {
            alignment ??= new Alignment();
            alignment.WrapText = true;
        }

        var border = CreateBorder(style.Values.GetValueOrDefault("border"));

        var fontKeyBuilder = new List<string>();
        if (!string.IsNullOrWhiteSpace(fontName))
        {
            fontKeyBuilder.Add("font=" + fontName);
        }

        if (fontSize is not null)
        {
            fontKeyBuilder.Add("size=" + fontSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(foreground))
        {
            fontKeyBuilder.Add("fg=" + foreground);
        }

        if (style.Flags.Contains("bold"))
        {
            fontKeyBuilder.Add("bold");
        }

        if (style.Flags.Contains("italic"))
        {
            fontKeyBuilder.Add("italic");
        }

        if (style.Flags.Contains("underline"))
        {
            fontKeyBuilder.Add("underline");
        }

        if (style.Flags.Contains("strike"))
        {
            fontKeyBuilder.Add("strike");
        }

        var keyParts = new List<string>();
        if (fontKeyBuilder.Count > 0)
        {
            keyParts.AddRange(fontKeyBuilder);
        }

        if (!string.IsNullOrWhiteSpace(background))
        {
            keyParts.Add("bg=" + background);
        }

        if (!string.IsNullOrWhiteSpace(numberFormat))
        {
            keyParts.Add("fmt=" + numberFormat);
        }

        if (border is not null)
        {
            keyParts.Add("border=" + border.OuterXml);
        }

        if (alignment is not null)
        {
            keyParts.Add("align=" + alignment.OuterXml);
        }

        return new NormalizedExcelStyle(
            Key: string.Join(';', keyParts),
            FontKey: fontKeyBuilder.Count == 0 ? string.Empty : string.Join(';', fontKeyBuilder),
            ForegroundColor: foreground,
            BackgroundColor: background,
            FontName: fontName,
            FontSize: fontSize,
            Bold: style.Flags.Contains("bold"),
            Italic: style.Flags.Contains("italic"),
            Underline: style.Flags.Contains("underline"),
            Strike: style.Flags.Contains("strike"),
            NumberFormat: numberFormat,
            Border: border,
            Alignment: alignment);
    }

    private static Border? CreateBorder(string? borderText)
    {
        if (string.IsNullOrWhiteSpace(borderText))
        {
            return null;
        }

        var border = new Border();
        var segments = borderText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 1 && !segments[0].Contains(':', StringComparison.Ordinal))
        {
            var allSide = CreateBorderProperties(segments[0], null);
            border.LeftBorder = allSide.CloneNode(true) as LeftBorder;
            border.RightBorder = CreateRightBorder(segments[0], null);
            border.TopBorder = CreateTopBorder(segments[0], null);
            border.BottomBorder = CreateBottomBorder(segments[0], null);
            border.DiagonalBorder = new DiagonalBorder();
            return border;
        }

        foreach (var segment in segments)
        {
            var parts = segment.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var side = parts[0].ToLowerInvariant();
            var lineStyle = parts[1];
            var color = parts.Length >= 3 ? parts[2] : null;
            switch (side)
            {
                case "left":
                    border.LeftBorder = CreateBorderProperties(lineStyle, color);
                    break;
                case "right":
                    border.RightBorder = CreateRightBorder(lineStyle, color);
                    break;
                case "top":
                    border.TopBorder = CreateTopBorder(lineStyle, color);
                    break;
                case "bottom":
                    border.BottomBorder = CreateBottomBorder(lineStyle, color);
                    break;
                case "all":
                    border.LeftBorder = CreateBorderProperties(lineStyle, color);
                    border.RightBorder = CreateRightBorder(lineStyle, color);
                    border.TopBorder = CreateTopBorder(lineStyle, color);
                    border.BottomBorder = CreateBottomBorder(lineStyle, color);
                    break;
            }
        }

        border.LeftBorder ??= new LeftBorder();
        border.RightBorder ??= new RightBorder();
        border.TopBorder ??= new TopBorder();
        border.BottomBorder ??= new BottomBorder();
        border.DiagonalBorder ??= new DiagonalBorder();
        return border;
    }

    private static LeftBorder CreateBorderProperties(string lineStyle, string? color)
    {
        var border = new LeftBorder
        {
            Style = ParseBorderStyle(lineStyle),
        };
        AppendBorderColor(border, color);
        return border;
    }

    private static RightBorder CreateRightBorder(string lineStyle, string? color)
    {
        var border = new RightBorder
        {
            Style = ParseBorderStyle(lineStyle),
        };
        AppendBorderColor(border, color);
        return border;
    }

    private static TopBorder CreateTopBorder(string lineStyle, string? color)
    {
        var border = new TopBorder
        {
            Style = ParseBorderStyle(lineStyle),
        };
        AppendBorderColor(border, color);
        return border;
    }

    private static BottomBorder CreateBottomBorder(string lineStyle, string? color)
    {
        var border = new BottomBorder
        {
            Style = ParseBorderStyle(lineStyle),
        };
        AppendBorderColor(border, color);
        return border;
    }

    private static void AppendBorderColor(OpenXmlCompositeElement border, string? color)
    {
        var normalized = NormalizeColor(color);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            border.Append(new Color { Rgb = HexBinaryValue.FromString(normalized) });
        }
    }

    private static BorderStyleValues ParseBorderStyle(string lineStyle) =>
        lineStyle.Trim().ToLowerInvariant() switch
        {
            "thin" => BorderStyleValues.Thin,
            "medium" => BorderStyleValues.Medium,
            "thick" => BorderStyleValues.Thick,
            "dashed" => BorderStyleValues.Dashed,
            "dotted" => BorderStyleValues.Dotted,
            "double" => BorderStyleValues.Double,
            _ => BorderStyleValues.Thin,
        };

    internal static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var trimmed = color.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length == 6 ? "FF" + trimmed.ToUpperInvariant() : trimmed.ToUpperInvariant();
    }

    private sealed record NormalizedExcelStyle(
        string Key,
        string FontKey,
        string? ForegroundColor,
        string? BackgroundColor,
        string? FontName,
        double? FontSize,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strike,
        string? NumberFormat,
        Border? Border,
        Alignment? Alignment);
}
