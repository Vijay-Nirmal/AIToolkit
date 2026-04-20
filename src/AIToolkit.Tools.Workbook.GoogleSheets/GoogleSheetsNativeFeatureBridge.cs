using AIToolkit.Tools.Workbook.Excel;
using Google.Apis.Sheets.v4.Data;
using System.Globalization;
using System.Text;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

internal static class GoogleSheetsNativeFeatureBridge
{
    public static IList<Request> CreateSyncRequests(Spreadsheet spreadsheet, WorkbookDocModel model)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        ArgumentNullException.ThrowIfNull(model);

        var requests = new List<Request>();
        var sheetByName = (spreadsheet.Sheets ?? [])
            .Where(static sheet => !string.IsNullOrWhiteSpace(sheet.Properties?.Title) && sheet.Properties?.SheetId is not null)
            .ToDictionary(static sheet => sheet.Properties!.Title, StringComparer.Ordinal);
        var tableSources = BuildTableSources(model);

        foreach (var sheet in spreadsheet.Sheets ?? [])
        {
            foreach (var chart in sheet.Charts ?? [])
            {
                if (chart.ChartId is int chartId)
                {
                    requests.Add(new Request
                    {
                        DeleteEmbeddedObject = new DeleteEmbeddedObjectRequest
                        {
                            ObjectId = chartId,
                        },
                    });
                }
            }

            if (sheet.Properties?.SheetId is int existingSheetId && sheet.ConditionalFormats is not null)
            {
                for (var index = sheet.ConditionalFormats.Count - 1; index >= 0; index--)
                {
                    requests.Add(new Request
                    {
                        DeleteConditionalFormatRule = new DeleteConditionalFormatRuleRequest
                        {
                            SheetId = existingSheetId,
                            Index = index,
                        },
                    });
                }
            }
        }

        foreach (var namedRange in spreadsheet.NamedRanges ?? [])
        {
            if (!string.IsNullOrWhiteSpace(namedRange.NamedRangeId))
            {
                requests.Add(new Request
                {
                    DeleteNamedRange = new DeleteNamedRangeRequest
                    {
                        NamedRangeId = namedRange.NamedRangeId,
                    },
                });
            }
        }

        foreach (var namedItem in model.Names)
        {
            if (!TryCreateNamedRangeRequest(sheetByName, namedItem, out var request))
            {
                continue;
            }

            requests.Add(request);
        }

        foreach (var sheetModel in model.Sheets)
        {
            if (!sheetByName.TryGetValue(sheetModel.Name, out var nativeSheet) || nativeSheet.Properties?.SheetId is not int sheetId)
            {
                continue;
            }

            requests.AddRange(CreateSheetPropertyRequests(sheetId, sheetModel));

            foreach (var line in sheetModel.ConditionalFormattingLines)
            {
                if (TryCreateConditionalFormattingRequest(sheetByName, sheetModel.Name, sheetId, line, out var cfRequest))
                {
                    requests.Add(cfRequest);
                }
            }

            foreach (var chart in ParseCharts(sheetModel))
            {
                if (TryCreateChartRequest(sheetByName, sheetModel.Name, sheetId, chart, out var chartRequest))
                {
                    requests.Add(chartRequest);
                }
            }

            foreach (var pivot in ParsePivots(sheetModel, tableSources))
            {
                if (TryCreatePivotRequest(sheetByName, model, sheetModel.Name, sheetId, pivot, out var pivotRequest))
                {
                    requests.Add(pivotRequest);
                }
            }

            foreach (var sparkline in ParseSparklines(sheetModel))
            {
                if (TryCreateSparklineRequest(sheetByName, sheetModel.Name, sheetId, sparkline, out var sparkRequest))
                {
                    requests.Add(sparkRequest);
                }
            }
        }

        return requests;
    }

    public static GoogleSheetsNativeFeatureMetadata ExtractMetadata(Spreadsheet spreadsheet)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);

        var metadata = new GoogleSheetsNativeFeatureMetadata();

        foreach (var namedRange in spreadsheet.NamedRanges ?? [])
        {
            if (string.IsNullOrWhiteSpace(namedRange.Name) || namedRange.Range is null)
            {
                continue;
            }

            if (TryFormatRange(spreadsheet, namedRange.Range, currentSheetName: null, out var rangeText))
            {
                metadata.WorkbookDirectives.Add($"[name {namedRange.Name} = {rangeText}]");
            }
        }

        foreach (var sheet in spreadsheet.Sheets ?? [])
        {
            var sheetName = sheet.Properties?.Title;
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                continue;
            }

            var additions = metadata.SheetAdditions.GetValueOrDefault(sheetName) ?? [];

            foreach (var rule in BuildConditionalFormattingLines(spreadsheet, sheet))
            {
                additions.Add(rule);
            }

            foreach (var block in BuildChartBlocks(spreadsheet, sheet))
            {
                additions.Add(block);
            }

            foreach (var block in BuildPivotBlocks(spreadsheet, sheet))
            {
                additions.Add(block);
            }

            foreach (var line in BuildSparkLines(spreadsheet, sheet))
            {
                additions.Add(line);
            }

            if (additions.Count > 0)
            {
                metadata.SheetAdditions[sheetName] = additions;
            }
        }

        return metadata;
    }

    private static Dictionary<string, (string SheetName, string RangeText)> BuildTableSources(WorkbookDocModel model)
    {
        var sources = new Dictionary<string, (string SheetName, string RangeText)>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in model.Sheets)
        {
            foreach (var line in sheet.TableLines)
            {
                if (!TryGetDirectiveTokens(line, out var tokens) || tokens.Count < 3 || !string.Equals(tokens[0], "table", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sources[tokens[1]] = (sheet.Name, tokens[2]);
            }
        }

        return sources;
    }

    private static List<Request> CreateSheetPropertyRequests(int sheetId, WorkbookDocSheetModel sheetModel)
    {
        var requests = new List<Request>();
        var properties = new SheetProperties
        {
            SheetId = sheetId,
            Hidden = sheetModel.Hidden,
        };
        var fields = new List<string> { "hidden" };

        if (sheetModel.View.FreezeRows is not null || sheetModel.View.FreezeColumns is not null || sheetModel.View.ShowGridLines || sheetModel.View.Zoom is not null)
        {
            properties.GridProperties = new GridProperties
            {
                FrozenRowCount = sheetModel.View.FreezeRows,
                FrozenColumnCount = sheetModel.View.FreezeColumns,
                HideGridlines = sheetModel.View.ShowGridLines ? false : (bool?)null,
            };
            fields.Add("gridProperties.frozenRowCount");
            fields.Add("gridProperties.frozenColumnCount");
            if (sheetModel.View.ShowGridLines)
            {
                fields.Add("gridProperties.hideGridlines");
            }
        }

        if (sheetModel.View.Zoom is not null)
        {
            properties.GridProperties ??= new GridProperties();
            properties.GridProperties.ColumnCount ??= 26;
        }

        requests.Add(new Request
        {
            UpdateSheetProperties = new UpdateSheetPropertiesRequest
            {
                Properties = properties,
                Fields = string.Join(',', fields),
            },
        });

        return requests;
    }

    private static bool TryCreateNamedRangeRequest(
        Dictionary<string, Sheet> sheetByName,
        WorkbookDocNamedItem namedItem,
        out Request request)
    {
        request = null!;
        if (!TryResolveSheetRange(sheetByName, currentSheetName: null, namedItem.Target, out var range))
        {
            return false;
        }

        request = new Request
        {
            AddNamedRange = new AddNamedRangeRequest
            {
                NamedRange = new NamedRange
                {
                    Name = namedItem.Name,
                    Range = range,
                },
            },
        };
        return true;
    }

    private static bool TryCreateConditionalFormattingRequest(
        Dictionary<string, Sheet> sheetByName,
        string currentSheetName,
        int defaultSheetId,
        string line,
        out Request request)
    {
        request = null!;
        if (!TryGetDirectiveTokens(line, out var tokens) || tokens.Count < 3)
        {
            return false;
        }

        if (!TryResolveSheetRange(sheetByName, currentSheetName, tokens[1], out var range))
        {
            range = new GridRange { SheetId = defaultSheetId };
        }

        ConditionalFormatRule? rule = null;
        if (tokens[2].StartsWith("scale(", StringComparison.OrdinalIgnoreCase))
        {
            rule = CreateScaleRule(range, tokens[2]);
        }
        else if (string.Equals(tokens[2], "color-scale", StringComparison.OrdinalIgnoreCase))
        {
            rule = CreateScaleRule(range, BuildCompatibilityScaleToken(tokens.Skip(3)));
        }
        else if (tokens[2].StartsWith("data-bar(", StringComparison.OrdinalIgnoreCase))
        {
            // Google Sheets has no native data-bar rule; approximate it with a two-point color scale.
            rule = CreateDataBarApproximation(range, tokens[2]);
        }
        else if (string.Equals(tokens[2], "data-bar", StringComparison.OrdinalIgnoreCase))
        {
            rule = CreateDataBarApproximation(range, BuildCompatibilityDataBarToken(tokens.Skip(3)));
        }
        else if (tokens[2].StartsWith("icon-set(", StringComparison.OrdinalIgnoreCase))
        {
            rule = CreateIconSetApproximation(range, tokens[2]);
        }
        else if (TryParseCompatibilityCellCondition(tokens, out var compatibilityType, out var compatibilityValue, out var compatibilityStyleTokens))
        {
            rule = CreateBooleanRule(range, compatibilityType, compatibilityValue, compatibilityStyleTokens);
        }
        else if (tokens[2].StartsWith("cell(", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseCellCondition(GetInnerFunctionText(tokens[2]), out var op, out var value))
            {
                var type = op switch
                {
                    ">" => "NUMBER_GREATER",
                    "<" => "NUMBER_LESS",
                    ">=" => "NUMBER_GREATER_THAN_EQ",
                    "<=" => "NUMBER_LESS_THAN_EQ",
                    "!=" => "NUMBER_NOT_EQ",
                    _ => "NUMBER_EQ",
                };
                rule = CreateBooleanRule(range, type, value, tokens.Skip(3));
            }
        }
        else if (string.Equals(tokens[2], "when", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 5)
        {
            if (string.Equals(tokens[3], "formula", StringComparison.OrdinalIgnoreCase) || tokens[3].StartsWith("formula(", StringComparison.OrdinalIgnoreCase))
            {
                var formulaToken = tokens[3].StartsWith("formula(", StringComparison.OrdinalIgnoreCase) ? tokens[3] : tokens[4];
                rule = CreateBooleanRule(range, "CUSTOM_FORMULA", GetInnerFunctionText(formulaToken), tokens.Skip(formulaToken == tokens[3] ? 4 : 5));
            }
            else if (string.Equals(tokens[3], "text", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 6 && string.Equals(tokens[4], "contains", StringComparison.OrdinalIgnoreCase))
            {
                rule = CreateBooleanRule(range, "TEXT_CONTAINS", UnquoteToken(tokens[5]), tokens.Skip(6));
            }
            else if (string.Equals(tokens[3], "cell", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 6)
            {
                var type = tokens[4] switch
                {
                    ">" => "NUMBER_GREATER",
                    "<" => "NUMBER_LESS",
                    ">=" => "NUMBER_GREATER_THAN_EQ",
                    "<=" => "NUMBER_LESS_THAN_EQ",
                    "!=" => "NUMBER_NOT_EQ",
                    _ => "NUMBER_EQ",
                };
                rule = CreateBooleanRule(range, type, tokens[5], tokens.Skip(6));
            }
        }

        if (rule is null)
        {
            return false;
        }

        request = new Request
        {
            AddConditionalFormatRule = new AddConditionalFormatRuleRequest
            {
                Index = 0,
                Rule = rule,
            },
        };
        return true;
    }

    private static ConditionalFormatRule CreateScaleRule(GridRange range, string token)
    {
        var points = SplitListArguments(GetInnerFunctionText(token));
        var interpolationPoints = new List<InterpolationPoint>();
        foreach (var point in points)
        {
            var separator = point.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var label = point[..separator];
            var color = point[(separator + 1)..];
            interpolationPoints.Add(CreateInterpolationPoint(label, color));
        }

        return new ConditionalFormatRule
        {
            Ranges = [range],
            GradientRule = new GradientRule
            {
                Minpoint = interpolationPoints.ElementAtOrDefault(0),
                Midpoint = interpolationPoints.Count == 3 ? interpolationPoints[1] : null,
                Maxpoint = interpolationPoints.Count == 3 ? interpolationPoints[2] : interpolationPoints.ElementAtOrDefault(1),
            },
        };
    }

    private static ConditionalFormatRule CreateDataBarApproximation(GridRange range, string token)
    {
        var colorToken = SplitDirectiveTokens(GetInnerFunctionText(token)).FirstOrDefault(static item => item.StartsWith("color=", StringComparison.OrdinalIgnoreCase));
        var color = colorToken is null ? "#5B9BD5" : colorToken["color=".Length..];
        return new ConditionalFormatRule
        {
            Ranges = [range],
            GradientRule = new GradientRule
            {
                Minpoint = CreateInterpolationPoint("min", "#FFFFFF"),
                Maxpoint = CreateInterpolationPoint("max", color),
            },
        };
    }

    private static ConditionalFormatRule CreateIconSetApproximation(GridRange range, string token)
    {
        var iconSet = GetInnerFunctionText(token).Trim().ToLowerInvariant();
        var colors = iconSet switch
        {
            "3-arrows" => ("#F8696B", "#FFEB84", "#63BE7B"),
            "3-traffic-lights" => ("#F8696B", "#FFEB84", "#63BE7B"),
            "4-arrows" => ("#F8696B", "#FFB366", "#63BE7B"),
            _ => ("#F8696B", "#FFEB84", "#63BE7B"),
        };

        return new ConditionalFormatRule
        {
            Ranges = [range],
            GradientRule = new GradientRule
            {
                Minpoint = CreateInterpolationPoint("min", colors.Item1),
                Midpoint = CreateInterpolationPoint("50%", colors.Item2),
                Maxpoint = CreateInterpolationPoint("max", colors.Item3),
            },
        };
    }

    private static ConditionalFormatRule CreateBooleanRule(GridRange range, string type, string? value, IEnumerable<string> styleTokens) =>
        new()
        {
            Ranges = [range],
            BooleanRule = new BooleanRule
            {
                Condition = new BooleanCondition
                {
                    Type = type,
                    Values = string.IsNullOrWhiteSpace(value) ? null : [new ConditionValue { UserEnteredValue = value }],
                },
                Format = CreateConditionalCellFormat(styleTokens),
            },
        };

    private static CellFormat CreateConditionalCellFormat(IEnumerable<string> tokens)
    {
        var format = new CellFormat();
        var styleTokens = tokens.ToArray();
        foreach (var token in styleTokens)
        {
            if (token.StartsWith("fill=", StringComparison.OrdinalIgnoreCase))
            {
                format.BackgroundColor = ParseColor(token["fill=".Length..]);
            }
            else if (token.StartsWith("fg=", StringComparison.OrdinalIgnoreCase))
            {
                format.TextFormat ??= new TextFormat();
                format.TextFormat.ForegroundColor = ParseColor(token["fg=".Length..]);
            }
            else if (string.Equals(token, "bold", StringComparison.OrdinalIgnoreCase))
            {
                format.TextFormat ??= new TextFormat();
                format.TextFormat.Bold = true;
            }
            else if (string.Equals(token, "italic", StringComparison.OrdinalIgnoreCase))
            {
                format.TextFormat ??= new TextFormat();
                format.TextFormat.Italic = true;
            }
            else if (string.Equals(token, "underline", StringComparison.OrdinalIgnoreCase))
            {
                format.TextFormat ??= new TextFormat();
                format.TextFormat.Underline = true;
            }
            else if (string.Equals(token, "strike", StringComparison.OrdinalIgnoreCase))
            {
                format.TextFormat ??= new TextFormat();
                format.TextFormat.Strikethrough = true;
            }
        }

        return format;
    }

    private static bool TryCreateChartRequest(
        Dictionary<string, Sheet> sheetByName,
        string currentSheetName,
        int sheetId,
        ParsedChart chart,
        out Request request)
    {
        request = null!;
        if (chart.Series.Count == 0)
        {
            return false;
        }

        var position = new EmbeddedObjectPosition
        {
            OverlayPosition = new OverlayPosition
            {
                AnchorCell = new GridCoordinate
                {
                    SheetId = sheetId,
                    RowIndex = chart.At.RowNumber - 1,
                    ColumnIndex = chart.At.ColumnNumber - 1,
                },
                WidthPixels = chart.WidthPx,
                HeightPixels = chart.HeightPx,
                OffsetXPixels = 0,
                OffsetYPixels = 0,
            },
        };

        ChartSpec spec;
        if (string.Equals(chart.Type, "pie", StringComparison.OrdinalIgnoreCase))
        {
            var firstSeries = chart.Series[0];
            if (firstSeries.CategoryRange is null || firstSeries.ValueRange is null
                || !TryCreateChartData(sheetByName, currentSheetName, firstSeries.CategoryRange, out var domainData)
                || !TryCreateChartData(sheetByName, currentSheetName, firstSeries.ValueRange, out var seriesData))
            {
                return false;
            }

            spec = new ChartSpec
            {
                Title = chart.Title,
                PieChart = new PieChartSpec
                {
                    Domain = domainData,
                    Series = seriesData,
                    LegendPosition = "RIGHT_LEGEND",
                },
            };
        }
        else
        {
            var basicChart = new BasicChartSpec
            {
                ChartType = string.Equals(chart.Type, "combo", StringComparison.OrdinalIgnoreCase) ? "COMBO" : ToBasicChartType(chart.Type),
                LegendPosition = "RIGHT_LEGEND",
                HeaderCount = 0,
                Axis = [
                    new BasicChartAxis { Position = "BOTTOM_AXIS" },
                    new BasicChartAxis { Position = "LEFT_AXIS" }
                ],
            };

            var domainSeries = chart.Series.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item.CategoryRange));
            if (domainSeries?.CategoryRange is not null && TryCreateChartData(sheetByName, currentSheetName, domainSeries.CategoryRange, out var domain))
            {
                basicChart.Domains = [new BasicChartDomain { Domain = domain }];
            }

            var seriesList = new List<BasicChartSeries>();
            foreach (var series in chart.Series)
            {
                if (string.IsNullOrWhiteSpace(series.ValueRange)
                    || !TryCreateChartData(sheetByName, currentSheetName, series.ValueRange, out var valueData))
                {
                    continue;
                }

                seriesList.Add(new BasicChartSeries
                {
                    Series = valueData,
                    Type = ToBasicChartType(series.Type),
                    TargetAxis = string.Equals(series.Axis, "secondary", StringComparison.OrdinalIgnoreCase) ? "RIGHT_AXIS" : "LEFT_AXIS",
                });
            }

            if (seriesList.Count == 0)
            {
                return false;
            }

            if (seriesList.Any(static item => string.Equals(item.TargetAxis, "RIGHT_AXIS", StringComparison.OrdinalIgnoreCase)))
            {
                basicChart.Axis.Add(new BasicChartAxis { Position = "RIGHT_AXIS" });
            }

            basicChart.Series = seriesList;
            spec = new ChartSpec
            {
                Title = chart.Title,
                BasicChart = basicChart,
            };
        }

        request = new Request
        {
            AddChart = new AddChartRequest
            {
                Chart = new EmbeddedChart
                {
                    Position = position,
                    Spec = spec,
                },
            },
        };
        return true;
    }

    private static bool TryCreateChartData(
        Dictionary<string, Sheet> sheetByName,
        string currentSheetName,
        string rangeText,
        out ChartData data)
    {
        data = null!;
        if (!TryResolveSheetRange(sheetByName, currentSheetName, rangeText, out var range))
        {
            return false;
        }

        data = new ChartData
        {
            SourceRange = new ChartSourceRange
            {
                Sources = [range],
            },
        };
        return true;
    }

    private static bool TryCreatePivotRequest(
        Dictionary<string, Sheet> sheetByName,
        WorkbookDocModel model,
        string currentSheetName,
        int sheetId,
        ParsedPivot pivot,
        out Request request)
    {
        request = null!;
        if (!TryResolveSheetRange(sheetByName, currentSheetName, pivot.SourceRange, out var sourceRange))
        {
            return false;
        }

        var nativePivot = new PivotTable
        {
            Source = sourceRange,
            Rows = pivot.Rows
                .Select(field => TryResolvePivotColumnOffset(model, currentSheetName, pivot.SourceRange, sourceRange, field, out var offset) ? new PivotGroup
                {
                    SourceColumnOffset = offset,
                    ShowTotals = true,
                    SortOrder = "ASCENDING",
                } : null)
                .Where(static item => item is not null)
                .Select(static item => item!)
                .ToList(),
            Columns = pivot.Columns
                .Select(field => TryResolvePivotColumnOffset(model, currentSheetName, pivot.SourceRange, sourceRange, field, out var offset) ? new PivotGroup
                {
                    SourceColumnOffset = offset,
                    ShowTotals = true,
                    SortOrder = "ASCENDING",
                } : null)
                .Where(static item => item is not null)
                .Select(static item => item!)
                .ToList(),
            Values = pivot.Values
                .Select(value => TryResolvePivotColumnOffset(model, currentSheetName, pivot.SourceRange, sourceRange, value.Field, out var offset) ? new PivotValue
                {
                    SourceColumnOffset = offset,
                    SummarizeFunction = ToPivotSummarizeFunction(value.Function),
                    Name = value.Name,
                } : null)
                .Where(static item => item is not null)
                .Select(static item => item!)
                .ToList(),
        };

        request = new Request
        {
            UpdateCells = new UpdateCellsRequest
            {
                Start = new GridCoordinate
                {
                    SheetId = sheetId,
                    RowIndex = pivot.At.RowNumber - 1,
                    ColumnIndex = pivot.At.ColumnNumber - 1,
                },
                Rows =
                [
                    new RowData
                    {
                        Values =
                        [
                            new CellData
                            {
                                PivotTable = nativePivot,
                            }
                        ]
                    }
                ],
                Fields = "pivotTable",
            },
        };

        if (pivot.Filters.Count > 0)
        {
            nativePivot.FilterSpecs = pivot.Filters
                .Select(field => TryResolvePivotColumnOffset(model, currentSheetName, pivot.SourceRange, sourceRange, field, out var offset) ? new PivotFilterSpec
                {
                    ColumnOffsetIndex = offset,
                    FilterCriteria = new PivotFilterCriteria(),
                } : null)
                .Where(static item => item is not null)
                .Select(static item => item!)
                .ToList();
        }

        return true;
    }

    private static bool TryResolvePivotColumnOffset(
        WorkbookDocModel model,
        string currentSheetName,
        string sourceRangeText,
        GridRange sourceRange,
        string fieldToken,
        out int columnOffset)
    {
        columnOffset = 0;
        if (TryParseColumnToken(fieldToken, out var columnToken))
        {
            columnOffset = ResolveColumnOffset(sourceRange, columnToken);
            return true;
        }

        if (!TryResolvePivotColumnFromHeader(model, currentSheetName, sourceRangeText, fieldToken, out columnToken))
        {
            return false;
        }

        columnOffset = ResolveColumnOffset(sourceRange, columnToken);
        return true;
    }

    private static int ResolveColumnOffset(GridRange sourceRange, string columnToken)
    {
        var columnNumber = WorkbookAddressing.ToColumnNumber(columnToken);
        var startColumn = (sourceRange.StartColumnIndex ?? 0) + 1;
        return Math.Max(0, columnNumber - startColumn);
    }

    private static string ToPivotSummarizeFunction(string function) =>
        function.ToLowerInvariant() switch
        {
            "count" => "COUNTA",
            "avg" => "AVERAGE",
            "average" => "AVERAGE",
            "min" => "MIN",
            "max" => "MAX",
            _ => "SUM",
        };

    private static IEnumerable<string> BuildConditionalFormattingLines(Spreadsheet spreadsheet, Sheet sheet)
    {
        var sheetName = sheet.Properties?.Title;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            yield break;
        }

        foreach (var rule in sheet.ConditionalFormats ?? [])
        {
            var range = rule.Ranges?.FirstOrDefault();
            if (range is null || !TryFormatRange(spreadsheet, range, sheetName, out var rangeText))
            {
                continue;
            }

            if (rule.GradientRule is not null)
            {
                var parts = new List<string>();
                if (rule.GradientRule.Minpoint is not null)
                {
                    parts.Add(FormatInterpolationPoint(rule.GradientRule.Minpoint));
                }
                if (rule.GradientRule.Midpoint is not null)
                {
                    parts.Add(FormatInterpolationPoint(rule.GradientRule.Midpoint));
                }
                if (rule.GradientRule.Maxpoint is not null)
                {
                    parts.Add(FormatInterpolationPoint(rule.GradientRule.Maxpoint));
                }

                if (parts.Count >= 2)
                {
                    yield return $"[cf {rangeText} scale({string.Join(',', parts)})]";
                }
            }
            else if (rule.BooleanRule?.Condition is BooleanCondition condition)
            {
                var styleText = FormatConditionalStyle(rule.BooleanRule.Format);
                var prefix = condition.Type switch
                {
                    "CUSTOM_FORMULA" => $"[cf {rangeText} when formula({condition.Values?.FirstOrDefault()?.UserEnteredValue})",
                    "TEXT_CONTAINS" => $"[cf {rangeText} when text contains {QuoteIfNeeded(condition.Values?.FirstOrDefault()?.UserEnteredValue ?? string.Empty)}",
                    "NUMBER_GREATER" => $"[cf {rangeText} when cell > {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                    "NUMBER_LESS" => $"[cf {rangeText} when cell < {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                    "NUMBER_GREATER_THAN_EQ" => $"[cf {rangeText} when cell >= {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                    "NUMBER_LESS_THAN_EQ" => $"[cf {rangeText} when cell <= {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                    "NUMBER_NOT_EQ" => $"[cf {rangeText} when cell != {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                    _ => $"[cf {rangeText} when cell = {condition.Values?.FirstOrDefault()?.UserEnteredValue}",
                };
                yield return prefix + (styleText.Length == 0 ? "]" : " " + styleText + "]");
            }
        }
    }

    private static string FormatInterpolationPoint(InterpolationPoint point)
    {
        var label = point.Type switch
        {
            "MIN" => "min",
            "MAX" => "max",
            "PERCENTILE" when !string.IsNullOrWhiteSpace(point.Value) => point.Value + "%",
            _ when !string.IsNullOrWhiteSpace(point.Value) => point.Value,
            _ => "mid",
        };

        return label + ":" + FormatColor(point.Color);
    }

    private static IEnumerable<string> BuildChartBlocks(Spreadsheet spreadsheet, Sheet sheet)
    {
        var sheetName = sheet.Properties?.Title;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            yield break;
        }

        foreach (var chart in sheet.Charts ?? [])
        {
            var spec = chart.Spec;
            if (spec is null)
            {
                continue;
            }

            var anchor = chart.Position?.OverlayPosition?.AnchorCell;
            var at = new WorkbookCellReference((anchor?.RowIndex ?? 0) + 1, (anchor?.ColumnIndex ?? 0) + 1);
            var width = chart.Position?.OverlayPosition?.WidthPixels ?? 480;
            var height = chart.Position?.OverlayPosition?.HeightPixels ?? 280;
            var block = new List<string>();

            if (spec.PieChart is not null)
            {
                if (!TryFormatChartData(spreadsheet, spec.PieChart.Domain, sheetName, out var domainRange)
                    || !TryFormatChartData(spreadsheet, spec.PieChart.Series, sheetName, out var valueRange))
                {
                    continue;
                }

                block.Add($"[chart {QuoteIfNeeded(spec.Title ?? "Chart")} type=pie at={at} size={width}x{height}px]");
                block.Add($"- series pie {QuoteIfNeeded(spec.Title ?? "Series")} cat={domainRange} val={valueRange}");
                block.Add("[end]");
                yield return string.Join('\n', block);
                continue;
            }

            if (spec.BasicChart is null)
            {
                continue;
            }

            var basicChart = spec.BasicChart;
            var chartType = DetermineChartType(basicChart);
            block.Add($"[chart {QuoteIfNeeded(spec.Title ?? "Chart")} type={chartType} at={at} size={width}x{height}px]");
            var domainRangeText = basicChart.Domains?.FirstOrDefault() is BasicChartDomain domain
                && TryFormatChartData(spreadsheet, domain.Domain, sheetName, out var domainRangeValue)
                    ? domainRangeValue
                    : null;

            foreach (var series in basicChart.Series ?? [])
            {
                if (!TryFormatChartData(spreadsheet, series.Series, sheetName, out var valueRange))
                {
                    continue;
                }

                var parts = new List<string>
                {
                    "- series",
                    ToWorkbookChartType(series.Type, chartType),
                    QuoteIfNeeded(spec.Title ?? "Series"),
                };
                if (domainRangeText is not null)
                {
                    parts.Add("cat=" + domainRangeText);
                }

                parts.Add("val=" + valueRange);
                if (string.Equals(series.TargetAxis, "RIGHT_AXIS", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add("axis=secondary");
                }

                block.Add(string.Join(' ', parts));
            }

            block.Add("[end]");
            yield return string.Join('\n', block);
        }
    }

    private static bool TryFormatChartData(Spreadsheet spreadsheet, ChartData? data, string currentSheetName, out string rangeText)
    {
        rangeText = string.Empty;
        var range = data?.SourceRange?.Sources?.FirstOrDefault();
        return range is not null && TryFormatRange(spreadsheet, range, currentSheetName, out rangeText);
    }

    private static string DetermineChartType(BasicChartSpec chart)
    {
        var distinctSeriesTypes = (chart.Series ?? []).Select(static series => series.Type).Where(static type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (string.Equals(chart.ChartType, "COMBO", StringComparison.OrdinalIgnoreCase) || distinctSeriesTypes.Length > 1)
        {
            return "combo";
        }

        return ToWorkbookChartType(distinctSeriesTypes.FirstOrDefault(), chart.ChartType);
    }

    private static string ToWorkbookChartType(string? apiType, string? fallback) =>
        (apiType ?? fallback ?? "COLUMN").ToUpperInvariant() switch
        {
            "LINE" => "line",
            "BAR" => "bar",
            "AREA" => "area",
            "SCATTER" => "scatter",
            _ => "column",
        };

    private static IEnumerable<string> BuildPivotBlocks(Spreadsheet spreadsheet, Sheet sheet)
    {
        var sheetName = sheet.Properties?.Title;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            yield break;
        }

        foreach (var (cell, rowIndex, columnIndex) in EnumerateCells(sheet))
        {
            if (cell.PivotTable is not PivotTable pivotTable || pivotTable.Source is null)
            {
                continue;
            }

            if (!TryFormatRange(spreadsheet, pivotTable.Source, sheetName, out var sourceRange))
            {
                continue;
            }

            var at = new WorkbookCellReference(rowIndex + 1, columnIndex + 1);
            var block = new List<string>
            {
                $"[pivot {QuoteIfNeeded("Pivot")} source={sourceRange} at={at}]"
            };

            foreach (var row in pivotTable.Rows ?? [])
            {
                block.Add("- row " + ResolvePivotFieldName(spreadsheet, pivotTable.Source, row.SourceColumnOffset));
            }

            foreach (var column in pivotTable.Columns ?? [])
            {
                block.Add("- col " + ResolvePivotFieldName(spreadsheet, pivotTable.Source, column.SourceColumnOffset));
            }

            foreach (var value in pivotTable.Values ?? [])
            {
                var columnName = ResolvePivotFieldName(spreadsheet, pivotTable.Source, value.SourceColumnOffset);
                block.Add($"- val {columnName} {ToWorkbookPivotFunction(value.SummarizeFunction)}" + (string.Equals(value.Name, columnName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value.Name) ? string.Empty : $" as {QuoteIfNeeded(value.Name)}"));
            }

            foreach (var filter in pivotTable.FilterSpecs ?? [])
            {
                block.Add("- filter " + ResolvePivotFieldName(spreadsheet, pivotTable.Source, filter.ColumnOffsetIndex));
            }

            block.Add("[end]");
            yield return string.Join('\n', block);
        }
    }

    private static string ToWorkbookPivotFunction(string? summarizeFunction) =>
        summarizeFunction?.ToUpperInvariant() switch
        {
            "COUNTA" => "count",
            "AVERAGE" => "average",
            "MIN" => "min",
            "MAX" => "max",
            _ => "sum",
        };

    private static IEnumerable<(CellData Cell, int RowIndex, int ColumnIndex)> EnumerateCells(Sheet sheet)
    {
        foreach (var gridData in sheet.Data ?? [])
        {
            var startRow = gridData.StartRow ?? 0;
            var startColumn = gridData.StartColumn ?? 0;
            if (gridData.RowData is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < gridData.RowData.Count; rowIndex++)
            {
                var row = gridData.RowData[rowIndex];
                if (row.Values is null)
                {
                    continue;
                }

                for (var columnIndex = 0; columnIndex < row.Values.Count; columnIndex++)
                {
                    yield return (row.Values[columnIndex], startRow + rowIndex, startColumn + columnIndex);
                }
            }
        }
    }

    private static IEnumerable<string> BuildSparkLines(Spreadsheet spreadsheet, Sheet sheet)
    {
        var sheetName = sheet.Properties?.Title;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            yield break;
        }

        foreach (var (cell, rowIndex, columnIndex) in EnumerateCells(sheet))
        {
            if (!TryParseSparklineFormula(cell.UserEnteredValue?.FormulaValue, sheetName, out var sparkline))
            {
                continue;
            }

            var targetCell = new WorkbookCellReference(rowIndex + 1, columnIndex + 1);
            var parts = new List<string>
            {
                $"[spark {targetCell} source={sparkline.SourceRange} type={sparkline.Type}"
            };
            if (!string.IsNullOrWhiteSpace(sparkline.Color))
            {
                parts.Add("color=" + sparkline.Color);
            }
            if (!string.IsNullOrWhiteSpace(sparkline.NegativeColor))
            {
                parts.Add("negative=" + sparkline.NegativeColor);
            }

            yield return string.Join(' ', parts) + "]";
        }
    }

    private static bool TryResolveSheetRange(
        Dictionary<string, Sheet> sheetByName,
        string? currentSheetName,
        string rangeText,
        out GridRange range)
    {
        range = null!;
        var sheetName = currentSheetName;
        var normalizedRangeText = rangeText;
        var bangIndex = rangeText.IndexOf('!');
        if (bangIndex >= 0)
        {
            sheetName = UnquoteToken(rangeText[..bangIndex]);
            normalizedRangeText = rangeText[(bangIndex + 1)..];
        }

        if (sheetName is null || !sheetByName.TryGetValue(sheetName, out var sheet) || sheet.Properties?.SheetId is not int sheetId)
        {
            return false;
        }

        if (!WorkbookAddressing.TryParseRange(normalizedRangeText, out var parsedRange))
        {
            return false;
        }

        range = ToGridRange(sheetId, parsedRange);
        return true;
    }

    private static GridRange ToGridRange(int sheetId, WorkbookRangeReference range) =>
        new()
        {
            SheetId = sheetId,
            StartRowIndex = range.Start.RowNumber - 1,
            EndRowIndex = range.End.RowNumber,
            StartColumnIndex = range.Start.ColumnNumber - 1,
            EndColumnIndex = range.End.ColumnNumber,
        };

    private static bool TryFormatRange(Spreadsheet spreadsheet, GridRange range, string? currentSheetName, out string rangeText)
    {
        rangeText = string.Empty;
        if (range.SheetId is null || range.StartRowIndex is null || range.EndRowIndex is null || range.StartColumnIndex is null || range.EndColumnIndex is null)
        {
            return false;
        }

        var sheetName = spreadsheet.Sheets?.FirstOrDefault(sheet => sheet.Properties?.SheetId == range.SheetId)?.Properties?.Title;
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return false;
        }

        var start = new WorkbookCellReference(range.StartRowIndex.Value + 1, range.StartColumnIndex.Value + 1);
        var end = new WorkbookCellReference(range.EndRowIndex.Value, range.EndColumnIndex.Value);
        rangeText = start.Equals(end) ? start.ToString() : $"{start}:{end}";
        if (!string.Equals(sheetName, currentSheetName, StringComparison.Ordinal))
        {
            rangeText = QuoteSheetName(sheetName) + "!" + rangeText;
        }

        return true;
    }

    private static string QuoteSheetName(string sheetName) =>
        "'" + sheetName.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ToBasicChartType(string? chartType) =>
        (chartType ?? "column").ToLowerInvariant() switch
        {
            "line" => "LINE",
            "bar" => "BAR",
            "area" => "AREA",
            "scatter" => "SCATTER",
            "combo" => "COMBO",
            _ => "COLUMN",
        };

    private static InterpolationPoint CreateInterpolationPoint(string label, string color) =>
        new()
        {
            Type = label.ToLowerInvariant() switch
            {
                "min" => "MIN",
                "max" => "MAX",
                _ when label.EndsWith('%') => "PERCENTILE",
                _ => "NUMBER",
            },
            Value = label is "min" or "max" ? null : label.TrimEnd('%'),
            Color = ParseColor(color),
        };

    private static Color ParseColor(string color)
    {
        var normalized = color.Trim();
        if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length != 6)
        {
            return new Color();
        }

        return new Color
        {
            Red = int.Parse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
            Green = int.Parse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
            Blue = int.Parse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
            Alpha = 1f,
        };
    }

    private static string FormatColor(Color? color)
    {
        if (color is null)
        {
            return "#000000";
        }

        var red = (int)Math.Round((color.Red ?? 0f) * 255f, MidpointRounding.AwayFromZero);
        var green = (int)Math.Round((color.Green ?? 0f) * 255f, MidpointRounding.AwayFromZero);
        var blue = (int)Math.Round((color.Blue ?? 0f) * 255f, MidpointRounding.AwayFromZero);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static string FormatConditionalStyle(CellFormat? format)
    {
        if (format is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (format.BackgroundColor is not null)
        {
            parts.Add("fill=" + FormatColor(format.BackgroundColor));
        }
        if (format.TextFormat?.ForegroundColor is not null)
        {
            parts.Add("fg=" + FormatColor(format.TextFormat.ForegroundColor));
        }
        if (format.TextFormat?.Bold == true)
        {
            parts.Add("bold");
        }
        if (format.TextFormat?.Italic == true)
        {
            parts.Add("italic");
        }
        if (format.TextFormat?.Underline == true)
        {
            parts.Add("underline");
        }
        if (format.TextFormat?.Strikethrough == true)
        {
            parts.Add("strike");
        }

        return string.Join(' ', parts);
    }

    private static List<ParsedChart> ParseCharts(WorkbookDocSheetModel sheetModel)
    {
        var charts = new List<ParsedChart>();
        foreach (var block in sheetModel.ChartBlocks)
        {
            var lines = NormalizeLineEndings(block)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0 || !TryGetDirectiveTokens(lines[0], out var headerTokens) || headerTokens.Count < 2)
            {
                continue;
            }

            var chart = new ParsedChart
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

                var series = new ParsedChartSeries
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

                chart.Series.Add(series);
            }

            charts.Add(chart);
        }

        return charts;
    }

    private static IEnumerable<ParsedPivot> ParsePivots(WorkbookDocSheetModel sheetModel, Dictionary<string, (string SheetName, string RangeText)> tableSources)
    {
        foreach (var block in sheetModel.PivotBlocks)
        {
            var lines = NormalizeLineEndings(block)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0 || !TryGetDirectiveTokens(lines[0], out var headerTokens) || headerTokens.Count < 2)
            {
                continue;
            }

            var pivot = new ParsedPivot
            {
                Title = UnquoteToken(headerTokens[1]),
                SourceRange = string.Empty,
                At = new WorkbookCellReference(1, 1),
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
                if (string.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                {
                    if (tableSources.TryGetValue(value, out var tableSource))
                    {
                        pivot.SourceRange = tableSource.RangeText.Contains('!', StringComparison.Ordinal)
                            ? tableSource.RangeText
                            : tableSource.SheetName + "!" + tableSource.RangeText;
                    }
                    else
                    {
                        pivot.SourceRange = value;
                    }
                }
                else if (string.Equals(key, "at", StringComparison.OrdinalIgnoreCase))
                {
                    pivot.At = WorkbookAddressing.ParseCell(value);
                }
            }

            foreach (var line in lines.Skip(1))
            {
                if (line.Length == 0 || line[0] != '-')
                {
                    continue;
                }

                var entry = line[1..].Trim();
                if (TryReadDirectiveSuffix(entry, "row", out var rowValue)
                    || TryReadDirectiveSuffix(entry, "rows", out rowValue))
                {
                    pivot.Rows.Add(UnquoteToken(rowValue));
                    continue;
                }

                if (TryReadDirectiveSuffix(entry, "col", out var columnValue)
                    || TryReadDirectiveSuffix(entry, "column", out columnValue)
                    || TryReadDirectiveSuffix(entry, "columns", out columnValue))
                {
                    pivot.Columns.Add(UnquoteToken(columnValue));
                    continue;
                }

                if (TryReadDirectiveSuffix(entry, "filter", out var filterValue)
                    || TryReadDirectiveSuffix(entry, "filters", out filterValue))
                {
                    pivot.Filters.Add(UnquoteToken(filterValue));
                    continue;
                }

                if ((TryReadDirectiveSuffix(entry, "val", out var valueBody)
                    || TryReadDirectiveSuffix(entry, "value", out valueBody)
                    || TryReadDirectiveSuffix(entry, "values", out valueBody))
                    && TryParsePivotValue(valueBody, out var parsedValue))
                {
                    pivot.Values.Add(parsedValue);
                }
            }

            if (!string.IsNullOrWhiteSpace(pivot.SourceRange))
            {
                yield return pivot;
            }
        }
    }

    private static IEnumerable<ParsedSparkline> ParseSparklines(WorkbookDocSheetModel sheetModel)
    {
        foreach (var line in sheetModel.SparkLines)
        {
            if (!TryGetDirectiveTokens(line, out var tokens) || tokens.Count < 4 || !string.Equals(tokens[0], "spark", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sparkline = new ParsedSparkline
            {
                Cell = WorkbookAddressing.ParseCell(tokens[1]),
                SourceRange = string.Empty,
                Type = "line",
            };

            foreach (var token in tokens.Skip(2))
            {
                var separator = token.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = token[..separator];
                var value = UnquoteToken(token[(separator + 1)..]);
                switch (key.ToLowerInvariant())
                {
                    case "source":
                        sparkline.SourceRange = value;
                        break;
                    case "type":
                        sparkline.Type = value;
                        break;
                    case "color":
                        sparkline.Color = value;
                        break;
                    case "negative":
                        sparkline.NegativeColor = value;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(sparkline.SourceRange))
            {
                yield return sparkline;
            }
        }
    }

    private static bool TryCreateSparklineRequest(
        Dictionary<string, Sheet> sheetByName,
        string currentSheetName,
        int defaultSheetId,
        ParsedSparkline sparkline,
        out Request request)
    {
        request = null!;
        var formula = BuildSparklineFormula(currentSheetName, sparkline);
        request = new Request
        {
            UpdateCells = new UpdateCellsRequest
            {
                Start = new GridCoordinate
                {
                    SheetId = defaultSheetId,
                    RowIndex = sparkline.Cell.RowNumber - 1,
                    ColumnIndex = sparkline.Cell.ColumnNumber - 1,
                },
                Rows =
                [
                    new RowData
                    {
                        Values =
                        [
                            new CellData
                            {
                                UserEnteredValue = new ExtendedValue
                                {
                                    FormulaValue = formula,
                                },
                            }
                        ]
                    }
                ],
                Fields = "userEnteredValue",
            },
        };
        return true;
    }

    private static string BuildSparklineFormula(string currentSheetName, ParsedSparkline sparkline)
    {
        var options = new List<(string Key, string Value)>
        {
            ("charttype", sparkline.Type),
        };
        if (!string.IsNullOrWhiteSpace(sparkline.Color))
        {
            options.Add(("color", sparkline.Color));
        }
        if (!string.IsNullOrWhiteSpace(sparkline.NegativeColor)
            && (string.Equals(sparkline.Type, "column", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sparkline.Type, "winloss", StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(("negcolor", sparkline.NegativeColor));
        }

        var optionsText = string.Join(";", options.Select(static option => $"\"{option.Key}\",\"{option.Value}\""));
        return $"=SPARKLINE({sparkline.SourceRange}, {{{optionsText}}})";
    }

    private static bool TryParseSparklineFormula(string? formula, string currentSheetName, out ParsedSparkline sparkline)
    {
        sparkline = null!;
        if (string.IsNullOrWhiteSpace(formula))
        {
            return false;
        }

        var trimmed = formula.Trim();
        if (!trimmed.StartsWith("=SPARKLINE(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
        {
            return false;
        }

        var inner = trimmed["=SPARKLINE(".Length..^1];
        var arguments = SplitFunctionArguments(inner);
        if (arguments.Count == 0)
        {
            return false;
        }

        sparkline = new ParsedSparkline
        {
            Cell = new WorkbookCellReference(1, 1),
            SourceRange = NormalizeSparklineSource(arguments[0].Trim(), currentSheetName),
            Type = "line",
        };

        if (arguments.Count < 2)
        {
            return true;
        }

        foreach (var pair in SplitSparklineOptions(arguments[1]))
        {
            switch (pair.Key.ToLowerInvariant())
            {
                case "charttype":
                    sparkline.Type = pair.Value;
                    break;
                case "color":
                    sparkline.Color = pair.Value;
                    break;
                case "negcolor":
                    sparkline.NegativeColor = pair.Value;
                    break;
            }
        }

        return true;
    }

    private static List<string> SplitFunctionArguments(string text)
    {
        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var braceDepth = 0;
        var parenDepth = 0;
        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                switch (ch)
                {
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        braceDepth--;
                        break;
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        parenDepth--;
                        break;
                    case ',' when braceDepth == 0 && parenDepth == 0:
                        arguments.Add(builder.ToString().Trim());
                        builder.Clear();
                        continue;
                }
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString().Trim());
        }

        return arguments;
    }

    private static Dictionary<string, string> SplitSparklineOptions(string text)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            trimmed = trimmed[1..^1];
        }

        foreach (var entry in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            options[UnquoteToken(parts[0])] = UnquoteToken(parts[1]);
        }

        return options;
    }

    private static string NormalizeSparklineSource(string sourceRange, string currentSheetName)
    {
        var bangIndex = sourceRange.IndexOf('!');
        if (bangIndex < 0)
        {
            return sourceRange;
        }

        var sourceSheetName = UnquoteToken(sourceRange[..bangIndex]);
        return string.Equals(sourceSheetName, currentSheetName, StringComparison.Ordinal)
            ? sourceRange[(bangIndex + 1)..]
            : sourceRange;
    }

    private static bool TryReadDirectiveSuffix(string text, string keyword, out string value)
    {
        value = string.Empty;
        if (!text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == keyword.Length)
        {
            return false;
        }

        var suffix = text[keyword.Length..];
        if (!char.IsWhiteSpace(suffix[0]))
        {
            return false;
        }

        value = suffix.Trim();
        return value.Length > 0;
    }

    private static bool TryParsePivotValue(string valueBody, out ParsedPivotValue value)
    {
        value = null!;
        var asIndex = valueBody.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        var coreText = asIndex >= 0 ? valueBody[..asIndex].Trim() : valueBody.Trim();
        var displayName = asIndex >= 0 ? UnquoteToken(valueBody[(asIndex + 4)..].Trim()) : null;
        var tokens = SplitDirectiveTokens(coreText);
        if (displayName is null && tokens.Count >= 3 && IsQuotedToken(tokens[^1]))
        {
            displayName = UnquoteToken(tokens[^1]);
            tokens = tokens.Take(tokens.Count - 1).ToList();
        }
        if (tokens.Count < 2)
        {
            return false;
        }

        string function;
        string field;
        if (IsPivotAggregate(tokens[0]))
        {
            function = tokens[0];
            field = string.Join(" ", tokens.Skip(1));
        }
        else if (IsPivotAggregate(tokens[^1]))
        {
            function = tokens[^1];
            field = string.Join(" ", tokens.Take(tokens.Count - 1));
        }
        else
        {
            return false;
        }

        field = UnquoteToken(field);
        value = new ParsedPivotValue(function, field, displayName ?? field);
        return true;
    }

    private static bool IsPivotAggregate(string token) =>
        token is "sum" or "count" or "avg" or "average" or "min" or "max";

    private static bool IsQuotedToken(string token)
    {
        var trimmed = token.Trim();
        return trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''));
    }

    private static string BuildCompatibilityDataBarToken(IEnumerable<string> tokens)
    {
        var color = tokens.FirstOrDefault(static token => token.StartsWith("color=", StringComparison.OrdinalIgnoreCase));
        return color is null ? "data-bar()" : $"data-bar({color})";
    }

    private static string BuildCompatibilityScaleToken(IEnumerable<string> tokens)
    {
        var entries = new List<string>();
        foreach (var token in tokens)
        {
            var separator = token.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = token[..separator].Trim();
            var value = token[(separator + 1)..].Trim();
            if (string.Equals(key, "mid", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add($"50%:{value}");
            }
            else
            {
                entries.Add($"{key}:{value}");
            }
        }

        return $"scale({string.Join(',', entries)})";
    }

    private static bool TryParseCompatibilityCellCondition(
        List<string> tokens,
        out string type,
        out string value,
        out IEnumerable<string> styleTokens)
    {
        type = string.Empty;
        value = string.Empty;
        styleTokens = [];

        if (tokens.Count < 4)
        {
            return false;
        }

        type = tokens[2].ToLowerInvariant() switch
        {
            "gt" => "NUMBER_GREATER",
            "lt" => "NUMBER_LESS",
            "gte" or "ge" => "NUMBER_GREATER_THAN_EQ",
            "lte" or "le" => "NUMBER_LESS_THAN_EQ",
            "ne" or "neq" => "NUMBER_NOT_EQ",
            "eq" => "NUMBER_EQ",
            _ => string.Empty,
        };
        if (type.Length == 0)
        {
            return false;
        }

        value = tokens[3];
        styleTokens = tokens.Skip(4);
        return true;
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

    private static bool TryParseColumnToken(string fieldToken, out string columnToken)
    {
        columnToken = fieldToken.Trim();
        if (columnToken.Length == 0 || columnToken.Any(static character => !char.IsLetter(character)))
        {
            return false;
        }

        columnToken = columnToken.ToUpperInvariant();
        return true;
    }

    private static bool TryResolvePivotColumnFromHeader(
        WorkbookDocModel model,
        string currentSheetName,
        string sourceRangeText,
        string fieldToken,
        out string columnToken)
    {
        columnToken = string.Empty;
        if (!TryResolveWorkbookRange(currentSheetName, sourceRangeText, out var sourceSheetName, out var sourceRange))
        {
            return false;
        }

        var sourceSheet = model.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, sourceSheetName, StringComparison.Ordinal));
        if (sourceSheet is null)
        {
            return false;
        }

        var headerRow = sourceRange.Start.RowNumber;
        for (var columnNumber = sourceRange.Start.ColumnNumber; columnNumber <= sourceRange.End.ColumnNumber; columnNumber++)
        {
            if (!TryGetCellRawContent(sourceSheet, headerRow, columnNumber, out var headerText))
            {
                continue;
            }

            if (string.Equals(headerText.Trim(), fieldToken.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                columnToken = WorkbookAddressing.ToColumnName(columnNumber);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveWorkbookRange(
        string currentSheetName,
        string rangeText,
        out string sheetName,
        out WorkbookRangeReference range)
    {
        sheetName = currentSheetName;
        var normalizedRangeText = rangeText;
        var bangIndex = rangeText.IndexOf('!');
        if (bangIndex >= 0)
        {
            sheetName = UnquoteToken(rangeText[..bangIndex]);
            normalizedRangeText = rangeText[(bangIndex + 1)..];
        }

        return WorkbookAddressing.TryParseRange(normalizedRangeText, out range);
    }

    private static bool TryGetCellRawContent(WorkbookDocSheetModel sheetModel, int rowNumber, int columnNumber, out string value)
    {
        value = string.Empty;
        var explicitAddress = new WorkbookCellReference(rowNumber, columnNumber).ToString();
        if (sheetModel.ExplicitCells.TryGetValue(explicitAddress, out var explicitCell))
        {
            value = explicitCell.RawContent;
            return true;
        }

        foreach (var row in sheetModel.Rows)
        {
            if (row.Anchor.RowNumber != rowNumber)
            {
                continue;
            }

            var cell = row.Cells.FirstOrDefault(cell => cell.Address.ColumnNumber == columnNumber);
            if (cell is not null)
            {
                value = cell.RawContent;
                return true;
            }
        }

        return false;
    }

    private static string ResolvePivotFieldName(Spreadsheet spreadsheet, GridRange sourceRange, int? sourceColumnOffset)
    {
        var columnNumber = (sourceRange.StartColumnIndex ?? 0) + (sourceColumnOffset ?? 0) + 1;
        if (TryGetSheetCellText(spreadsheet, sourceRange.SheetId, (sourceRange.StartRowIndex ?? 0), columnNumber - 1, out var text))
        {
            return text;
        }

        return WorkbookAddressing.ToColumnName(columnNumber);
    }

    private static bool TryGetSheetCellText(Spreadsheet spreadsheet, int? sheetId, int rowIndex, int columnIndex, out string value)
    {
        value = string.Empty;
        var sheet = spreadsheet.Sheets?.FirstOrDefault(item => item.Properties?.SheetId == sheetId);
        if (sheet is null)
        {
            return false;
        }

        foreach (var (cell, currentRowIndex, currentColumnIndex) in EnumerateCells(sheet))
        {
            if (currentRowIndex != rowIndex || currentColumnIndex != columnIndex)
            {
                continue;
            }

            value = cell.FormattedValue
                ?? cell.UserEnteredValue?.StringValue
                ?? cell.UserEnteredValue?.FormulaValue
                ?? string.Empty;
            return value.Length > 0;
        }

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
        char quoteCharacter = '\0';
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
            }

            if (ch == ',' && !inQuotes && parenDepth == 0)
            {
                items.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            items.Add(builder.ToString().Trim());
        }

        return items;
    }

    private static string UnquoteToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\|", "|", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
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

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed class ParsedChart
    {
        public required string Title { get; set; }
        public required string Type { get; set; }
        public required WorkbookCellReference At { get; set; }
        public required int WidthPx { get; set; }
        public required int HeightPx { get; set; }
        public List<ParsedChartSeries> Series { get; } = [];
    }

    private sealed class ParsedChartSeries
    {
        public required string Type { get; set; }
        public required string Name { get; set; }
        public string? CategoryRange { get; set; }
        public string? ValueRange { get; set; }
        public string? Axis { get; set; }
        public bool Labels { get; set; }
    }

    private sealed class ParsedPivot
    {
        public required string Title { get; set; }
        public required string SourceRange { get; set; }
        public required WorkbookCellReference At { get; set; }
        public List<string> Rows { get; } = [];
        public List<string> Columns { get; } = [];
        public List<string> Filters { get; } = [];
        public List<ParsedPivotValue> Values { get; } = [];
    }

    private sealed record ParsedPivotValue(string Function, string Field, string Name);

    private sealed class ParsedSparkline
    {
        public required WorkbookCellReference Cell { get; set; }
        public required string SourceRange { get; set; }
        public required string Type { get; set; }
        public string? Color { get; set; }
        public string? NegativeColor { get; set; }
    }
}
