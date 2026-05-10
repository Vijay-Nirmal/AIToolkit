using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Parses canonical DeckDoc into a complete internal syntax model.
/// </summary>
internal static class DeckDocSyntaxParser
{
    /// <summary>
    /// Parses canonical DeckDoc text.
    /// </summary>
    public static DeckDocDocument Parse(string deckDoc)
    {
        var normalized = DeckSupport.NormalizeLineEndings(deckDoc ?? string.Empty);
        var lines = normalized.Split('\n');
        var titleLineIndex = FindFirstMeaningfulLine(lines);
        if (titleLineIndex < 0 || !lines[titleLineIndex].StartsWith("= ", StringComparison.Ordinal))
        {
            throw new DeckDocParseException("DeckDoc text must start with '= Presentation Name'.", Math.Max(1, titleLineIndex + 1), queryHint: "document header");
        }

        var document = new DeckDocDocument
        {
            Title = lines[titleLineIndex][2..].Trim(),
            Lines = lines,
        };

        var parser = new Parser(lines, document, titleLineIndex + 1);
        parser.ParseDocument();
        if (document.Slides.Count == 0)
        {
            throw new DeckDocParseException("DeckDoc text must contain at least one slide starting with '== Slide Title'.", titleLineIndex + 1, queryHint: "slides");
        }

        return document;
    }

    private sealed class Parser(string[] lines, DeckDocDocument document, int startIndex)
    {
        private readonly string[] _lines = lines;
        private readonly DeckDocDocument _document = document;
        private int _index = startIndex;
        private bool _enteredSlides;

        public void ParseDocument()
        {
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (trimmed.Length == 0)
                {
                    _index++;
                    continue;
                }

                if (!_enteredSlides && IsAttributeLine(trimmed))
                {
                    ParseAttribute(trimmed, _index + 1);
                    _index++;
                    continue;
                }

                if (!_enteredSlides && IsLayoutStart(trimmed, out var layoutName))
                {
                    ParseLayoutBlock(layoutName);
                    continue;
                }

                if (trimmed.StartsWith("== ", StringComparison.Ordinal))
                {
                    _enteredSlides = true;
                    ParseSlide(trimmed[3..].Trim());
                    continue;
                }

                if (!_enteredSlides)
                {
                    ParseSharedDirective(trimmed, _index + 1);
                    _index++;
                    continue;
                }

                throw new DeckDocParseException("Unexpected content after slide parsing completed.", _index + 1, queryHint: "slides");
            }
        }

        private void ParseAttribute(string trimmed, int lineNumber)
        {
            var secondColon = trimmed.IndexOf(':', 1);
            if (secondColon <= 1)
            {
                throw new DeckDocParseException("Document attributes must use ':name: value' syntax.", lineNumber, queryHint: "document-header");
            }

            var name = trimmed[1..secondColon].Trim();
            var value = trimmed[(secondColon + 1)..].Trim();
            if (name.Length == 0)
            {
                throw new DeckDocParseException("Document attribute names cannot be empty.", lineNumber, queryHint: "document-header");
            }

            _document.Attributes[name] = value;
        }

        private void ParseSharedDirective(string trimmed, int lineNumber)
        {
            var directive = ParseBracketDirective(trimmed, lineNumber);
            switch (directive.Kind)
            {
                case "theme":
                    foreach (var pair in directive.Arguments.Values)
                    {
                        _document.ThemeTokens[pair.Key] = pair.Value;
                    }
                    break;

                case "style":
                {
                    var (styleName, styleArguments) = ParseStyleDefinition(directive.Arguments, lineNumber);
                    _document.Styles[styleName] = styleArguments;
                    break;
                }

                case "asset":
                {
                    var assetName = RequireBareToken(directive.Arguments, 0, lineNumber, "shared-asset");
                    var assetReference = RequireBareToken(directive.Arguments, 1, lineNumber, "shared-asset");
                    _document.SharedAssets[assetName] = assetReference;
                    break;
                }

                case "motion":
                {
                    var motionName = RequireBareToken(directive.Arguments, 0, lineNumber, "shared-motion");
                    _document.Motions[motionName] = SliceArguments(directive.Arguments, 1);
                    break;
                }

                case "x":
                    _document.SharedExtensions.Add(new DeckExtensionDirective
                    {
                        Provider = directive.Arguments.BareTokens.FirstOrDefault(),
                        Arguments = SliceArguments(directive.Arguments, directive.Arguments.BareTokens.Count > 0 ? 1 : 0),
                    });
                    break;

                default:
                    if (string.Equals(directive.Kind, "shared", StringComparison.OrdinalIgnoreCase)
                        && directive.Arguments.BareTokens.Count > 0)
                    {
                        var sharedKind = directive.Arguments.BareTokens[0];
                        var message = string.Equals(sharedKind, "motion", StringComparison.OrdinalIgnoreCase)
                            ? "Unsupported shared directive '[shared motion ...]'. Define reusable animation presets with '[motion <name> <entry>...]'."
                            : $"Unsupported shared directive '[shared {sharedKind} ...]'. Use the canonical '[{sharedKind} ...]' form instead.";

                        throw new DeckDocParseException(message, lineNumber, queryHint: $"shared-{sharedKind}");
                    }

                    throw new DeckDocParseException($"Unsupported shared directive '[{directive.Kind} ...]'.", lineNumber, queryHint: directive.Kind);
            }
        }

        private void ParseLayoutBlock(string layoutName)
        {
            var layout = new DeckDocLayout
            {
                Name = layoutName,
                StartLineNumber = _index + 1,
            };

            if (_document.LayoutMap.ContainsKey(layoutName))
            {
                throw new DeckDocParseException($"The layout '{layoutName}' is declared more than once.", _index + 1, queryHint: "layout-block");
            }

            _document.LayoutMap[layoutName] = layout;
            _document.Layouts.Add(layout);

            _index++;
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (trimmed.Length == 0)
                {
                    _index++;
                    continue;
                }

                if (string.Equals(trimmed, "[end]", StringComparison.OrdinalIgnoreCase))
                {
                    layout.EndLineNumber = _index + 1;
                    _index++;
                    return;
                }

                if (trimmed.StartsWith("== ", StringComparison.Ordinal))
                {
                    throw new DeckDocParseException("Slide headings cannot appear inside an open 'layout' block.", _index + 1, queryHint: "layout-block");
                }

                if (trimmed.StartsWith('!'))
                {
                    layout.FixedObjects.Add(ParseFixedObject(trimmed, _index + 1));
                    _index++;
                    continue;
                }

                if (trimmed.StartsWith('@'))
                {
                    if (HasLayoutSlotBinding(trimmed))
                    {
                        layout.Slots.Add(ParseSlotDefinition(trimmed, _index + 1));
                    }
                    else
                    {
                        layout.FixedObjects.Add(ParseFixedObject(trimmed, _index + 1));
                    }

                    _index++;
                    continue;
                }

                var (directive, trailingContent) = ParseBracketDirectiveLine(trimmed, _index + 1);
                if (!string.IsNullOrWhiteSpace(trailingContent) && !string.Equals(directive.Kind, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeckDocParseException("Only '[obj ...]' directives can carry trailing compatibility content on the same line.", _index + 1, queryHint: "explicit-object-overrides");
                }

                switch (directive.Kind)
                {
                    case "background":
                        layout.Background = directive.Arguments;
                        break;

                    case "line":
                        layout.FixedObjects.Add(ParseLayoutFixedDirective(directive, _index + 1));
                        break;

                    case "transition":
                        layout.Transition = ParseTransition(directive, _index + 1);
                        break;

                    case "grid" when IsGridSizeDirective(directive.Arguments):
                        layout.GridOverride = ParseGridSize(RequireBareToken(directive.Arguments, 0, _index + 1, "layout-block"), _index + 1, "layout-block");
                        break;

                    case "area":
                    case "split":
                    case "grid":
                    case "stack":
                        layout.Targets.Add(ParseTargetDefinition(directive, _index + 1));
                        break;

                    case "x":
                        layout.Extensions.Add(new DeckExtensionDirective
                        {
                            Provider = directive.Arguments.BareTokens.FirstOrDefault(),
                            Arguments = SliceArguments(directive.Arguments, directive.Arguments.BareTokens.Count > 0 ? 1 : 0),
                        });
                        break;

                    default:
                        throw new DeckDocParseException($"Unsupported directive '[{directive.Kind} ...]' inside a layout block.", _index + 1, queryHint: "layout-block");
                }

                _index++;
            }

            throw new DeckDocParseException("The 'layout' block does not have a matching '[end]'.", layout.StartLineNumber, queryHint: "layout-block");
        }

        private void ParseSlide(string title)
        {
            var slide = new DeckDocSlide
            {
                SlideNumber = _document.Slides.Count + 1,
                Title = title,
                StartLineNumber = _index + 1,
            };

            _index++;
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (trimmed.StartsWith("== ", StringComparison.Ordinal))
                {
                    break;
                }

                if (trimmed.Length == 0)
                {
                    _index++;
                    continue;
                }

                if (string.Equals(trimmed, "[end]", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeckDocParseException("Found '[end]' without a matching block opener inside the slide.", _index + 1, queryHint: "slides");
                }

                if (trimmed.StartsWith("[table ", StringComparison.OrdinalIgnoreCase))
                {
                    slide.Tables.Add(ParseTableBlock());
                    continue;
                }

                if (trimmed.StartsWith("[chart ", StringComparison.OrdinalIgnoreCase))
                {
                    slide.Charts.Add(ParseChartBlock());
                    continue;
                }

                if (trimmed.StartsWith('@') || trimmed.StartsWith('!'))
                {
                    slide.Objects.Add(ParseObjectLineWithContinuations(trimmed, _index + 1));
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    throw new DeckDocParseException("Series rows must appear inside a standalone '[chart ...]' block and follow a chart opener directly.", _index + 1, queryHint: "chart-block");
                }

                if (trimmed.StartsWith("[obj", StringComparison.OrdinalIgnoreCase))
                {
                    slide.ObjectOverrides.Add(ParseObjectOverrideLine(trimmed, _index + 1));
                    _index++;
                    continue;
                }

                var (directive, trailingContent) = ParseBracketDirectiveLine(trimmed, _index + 1);
                if (!string.IsNullOrWhiteSpace(trailingContent) && !string.Equals(directive.Kind, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeckDocParseException("Only '[obj ...]' directives can carry trailing compatibility content on the same line.", _index + 1, queryHint: "explicit-object-overrides");
                }

                switch (directive.Kind)
                {
                    case "use":
                        slide.LayoutName = RequireBareToken(directive.Arguments, 0, _index + 1, "slide-use");
                        break;

                    case "section":
                        slide.SectionName = RequireBareToken(directive.Arguments, 0, _index + 1, "slides");
                        break;

                    case "state":
                        slide.Hidden = directive.Arguments.HasToken("hidden");
                        break;

                    case "background":
                        slide.Background = directive.Arguments;
                        break;

                    case "notes":
                        slide.Notes = RequireBareToken(directive.Arguments, 0, _index + 1, "slide-notes");
                        break;

                    case "transition":
                        slide.Transition = ParseTransition(directive, _index + 1);
                        break;

                    case "area":
                    case "split":
                    case "grid":
                    case "stack":
                        slide.Targets.Add(ParseTargetDefinition(directive, _index + 1));
                        break;

                    case "obj":
                        slide.ObjectOverrides.Add(ParseObjectOverride(directive, trailingContent, _index + 1));
                        break;

                    case "group":
                        slide.Groups.Add(ParseGroup(directive, _index + 1));
                        break;

                    case "animate":
                        slide.Animations.Add(ParseAnimate(directive, _index + 1));
                        break;

                    case "x":
                        slide.Extensions.Add(new DeckExtensionDirective
                        {
                            Provider = directive.Arguments.BareTokens.FirstOrDefault(),
                            Arguments = SliceArguments(directive.Arguments, directive.Arguments.BareTokens.Count > 0 ? 1 : 0),
                        });
                        break;

                    default:
                        throw new DeckDocParseException($"Unsupported slide directive '[{directive.Kind} ...]'.", _index + 1, queryHint: directive.Kind);
                }

                _index++;
            }

            slide.EndLineNumber = _index == 0 ? slide.StartLineNumber : Math.Max(slide.StartLineNumber, _index);
            if (_index == _lines.Length)
            {
                slide.EndLineNumber = _lines.Length;
            }

            _document.Slides.Add(slide);
        }

        private DeckObjectDefinition ParseObjectLineWithContinuations(string line, int lineNumber)
        {
            var definition = ParseObjectLine(line, lineNumber);
            var payloadSegments = definition.PayloadSegments.ToList();

            _index++;
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (!IsObjectPayloadContinuation(trimmed, definition.Arguments))
                {
                    break;
                }

                payloadSegments.Add(trimmed[1..].TrimStart());
                _index++;
            }

            return new DeckObjectDefinition
            {
                LineNumber = definition.LineNumber,
                Placement = definition.Placement,
                Arguments = definition.Arguments,
                PayloadSegments = payloadSegments,
                RawLine = definition.RawLine,
            };
        }

        private DeckTableBlock ParseTableBlock()
        {
            var startLineNumber = _index + 1;
            var directive = ParseBracketDirective(_lines[_index].Trim(), startLineNumber);
            var name = RequireBareToken(directive.Arguments, 0, startLineNumber, "table-block");
            var placement = ParseStandaloneBlockPlacement(
                RequireNamedValue(directive.Arguments, "at", startLineNumber, "table-block"),
                directive.Arguments.GetValue("size"),
                startLineNumber,
                "table-block");
            var table = new DeckTableBlock
            {
                Name = name,
                StartLineNumber = startLineNumber,
                Anchor = placement.Anchor,
                Size = placement.Size,
                TargetName = placement.TargetName,
                TargetIndex = placement.TargetIndex,
                Arguments = SliceArguments(directive.Arguments, 1, ["at", "size"]),
            };

            _index++;
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (string.Equals(trimmed, "[end]", StringComparison.OrdinalIgnoreCase))
                {
                    table.EndLineNumber = _index + 1;
                    _index++;
                    return table;
                }

                if (trimmed.Length == 0)
                {
                    _index++;
                    continue;
                }

                var cells = ParseTableRow(trimmed, _index + 1);
                if (IsMarkdownTableSeparator(cells))
                {
                    table.HasHeaderSeparator = true;
                }
                else
                {
                    table.Rows.Add(cells);
                }

                _index++;
            }

            throw new DeckDocParseException("The 'table' block does not have a matching '[end]'.", startLineNumber, queryHint: "table-block");
        }

        private DeckChartBlock ParseChartBlock()
        {
            var startLineNumber = _index + 1;
            var directive = ParseBracketDirective(_lines[_index].Trim(), startLineNumber);
            var name = RequireBareToken(directive.Arguments, 0, startLineNumber, "chart-block");
            var type = RequireNamedValue(directive.Arguments, "type", startLineNumber, "chart-block");
            var placement = ParseStandaloneBlockPlacement(
                RequireNamedValue(directive.Arguments, "at", startLineNumber, "chart-block"),
                directive.Arguments.GetValue("size"),
                startLineNumber,
                "chart-block");
            var chart = new DeckChartBlock
            {
                Name = name,
                StartLineNumber = startLineNumber,
                Type = type,
                Anchor = placement.Anchor,
                Size = placement.Size,
                TargetName = placement.TargetName,
                TargetIndex = placement.TargetIndex,
                Arguments = SliceArguments(directive.Arguments, 1, ["type", "at", "size"]),
            };

            _index++;
            while (_index < _lines.Length)
            {
                var trimmed = _lines[_index].Trim();
                if (string.Equals(trimmed, "[end]", StringComparison.OrdinalIgnoreCase))
                {
                    chart.EndLineNumber = _index + 1;
                    _index++;
                    return chart;
                }

                if (trimmed.Length == 0)
                {
                    _index++;
                    continue;
                }

                chart.Series.Add(ParseChartSeries(trimmed, _index + 1));
                _index++;
            }

            throw new DeckDocParseException("The 'chart' block does not have a matching '[end]'.", startLineNumber, queryHint: "chart-block");
        }
    }

    private static ParsedDirective ParseBracketDirective(string line, int lineNumber)
    {
        if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
        {
            throw new DeckDocParseException("Directives must use '[name ...]' syntax.", lineNumber, queryHint: "slides");
        }

        var content = line[1..^1].Trim();
        var tokens = Tokenize(content, lineNumber);
        if (tokens.Count == 0)
        {
            throw new DeckDocParseException("Directive blocks cannot be empty.", lineNumber, queryHint: "slides");
        }

        return new ParsedDirective(tokens[0], ParseArguments(tokens.Skip(1)));
    }

    private static (ParsedDirective Directive, string? TrailingContent) ParseBracketDirectiveLine(string line, int lineNumber)
    {
        if (line.Length < 2 || line[0] != '[')
        {
            throw new DeckDocParseException("Directives must use '[name ...]' syntax.", lineNumber, queryHint: "slides");
        }

        var closingBracketIndex = FindDirectiveClosingBracket(line, lineNumber);
        var directive = ParseBracketDirective(line[..(closingBracketIndex + 1)], lineNumber);
        var trailingContent = line[(closingBracketIndex + 1)..].Trim();
        return (directive, trailingContent.Length == 0 ? null : trailingContent);
    }

    private static DeckObjectOverrideDefinition ParseObjectOverrideLine(string line, int lineNumber)
    {
        var (directive, trailingContent) = ParseBracketDirectiveLine(line, lineNumber);
        if (!string.Equals(directive.Kind, "obj", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeckDocParseException("Explicit object overrides must use '[obj ...]' syntax.", lineNumber, queryHint: "explicit-object-overrides");
        }

        if (string.IsNullOrWhiteSpace(trailingContent))
        {
            var closingBracketIndex = FindDirectiveClosingBracket(line, lineNumber);
            var innerContent = line[1..closingBracketIndex].Trim();
            var directiveBody = innerContent.Length > 3 ? innerContent[3..].TrimStart() : string.Empty;
            if (directiveBody.Length > 0)
            {
                _ = ExtractTrailingBracketBlock(directiveBody, lineNumber, out var headerWithoutAttrlist);
                if (headerWithoutAttrlist.Length < directiveBody.Length)
                {
                    trailingContent = directiveBody[headerWithoutAttrlist.Length..].Trim();
                }
            }
        }

        return ParseObjectOverride(directive, trailingContent, lineNumber);
    }

    private static DeckObjectDefinition ParseObjectLine(string line, int lineNumber)
    {
        var prefix = line[0];
        var body = line[1..].Trim();
        var pipeIndex = FindPipeOutsideQuotes(body);
        var header = pipeIndex >= 0 ? body[..pipeIndex].TrimEnd() : body;
        var payload = pipeIndex >= 0 ? body[(pipeIndex + 1)..] : string.Empty;
        var attrlist = TryExtractInlineAttrlistWithTrailingPlacement(header, lineNumber, out var headerWithoutAttrlist, out var trailingPlacementTokens)
            ? ParseArguments(Tokenize(GetAttrlistContent(header, lineNumber), lineNumber))
            : ExtractTrailingBracketBlock(header, lineNumber, out headerWithoutAttrlist);
        var headerTokens = Tokenize(headerWithoutAttrlist, lineNumber);
        if (headerTokens.Count == 0)
        {
            throw new DeckDocParseException("Object lines must specify an anchor or target reference.", lineNumber, queryHint: "slides");
        }

        var objectArguments = attrlist;

        if (prefix == '@')
        {
            if (header.Contains("[chart ", StringComparison.OrdinalIgnoreCase))
            {
                throw new DeckDocParseException("'chart' is a standalone block directive, not an object attrlist. Use '[chart ...]' on its own line and close it with '[end]'.", lineNumber, queryHint: "chart-block");
            }

            if (header.Contains("[table ", StringComparison.OrdinalIgnoreCase))
            {
                throw new DeckDocParseException("'table' is a standalone block directive, not an object attrlist. Use '[table ...]' on its own line and close it with '[end]'.", lineNumber, queryHint: "table-block");
            }
        }

        if (prefix == '@' && attrlist.BareTokens.Count > 0)
        {
            var objectKind = attrlist.BareTokens[0];
            if (string.Equals(objectKind, "chart", StringComparison.OrdinalIgnoreCase)
                || string.Equals(objectKind, "table", StringComparison.OrdinalIgnoreCase))
            {
                throw new DeckDocParseException($"'{objectKind}' is a standalone block directive, not an object attrlist. Use '[{objectKind} ...]' on its own line and close it with '[end]'.", lineNumber, queryHint: $"{objectKind}-block");
            }
        }

        DeckObjectPlacement placement;
        if (prefix == '!')
        {
            if (headerTokens.Count == 1 && LooksLikeRange(headerTokens[0]))
            {
                placement = ParsePlacementReference(headerTokens, lineNumber, "layout-block");
            }
            else if (headerTokens.Count == 1 && !LooksLikeAnchor(headerTokens[0]) && !LooksLikeRange(headerTokens[0]))
            {
                placement = ParseTargetReference(headerTokens[0], lineNumber, "layout-block");
            }
            else if (TryParseNamedFixedPlacement(headerTokens, lineNumber, out var namedFixedPlacement))
            {
                placement = namedFixedPlacement;
                objectArguments = attrlist.Clone();
                objectArguments.Values.TryAdd("name", headerTokens[0]);
            }
            else if (headerTokens.Count < 2)
            {
                throw new DeckDocParseException("Fixed layout lines must specify an anchor and span.", lineNumber, queryHint: "layout-block");
            }
            else if (headerTokens.Count > 2)
            {
                throw new DeckDocParseException("Fixed layout lines only allow a target reference or an anchor and span before the attrlist or payload.", lineNumber, queryHint: "layout-block");
            }
            else
            {
                placement = new DeckObjectPlacement
                {
                    Mode = DeckObjectAddressingMode.Geometry,
                    Anchor = ParseAnchor(headerTokens[0], lineNumber, "layout-block"),
                    Span = ParseSpan(headerTokens[1], lineNumber, "layout-block"),
                };
            }
        }
        else
        {
            if (trailingPlacementTokens is not null && TryParseTrailingPlacementGeometry(headerTokens, attrlist, trailingPlacementTokens, lineNumber, out var trailingPlacement, out var trailingArguments))
            {
                placement = trailingPlacement;
                objectArguments = trailingArguments;
            }
            else if (trailingPlacementTokens is not null)
            {
                throw new DeckDocParseException("Trailing placement after an object attrlist only supports geometry-addressed named objects such as '@box [shape ...] B8 10x4' or '@caption [text ...] at=V16:AM17'.", lineNumber, queryHint: "slides");
            }
            else if (TryParseNamedGeometryPlacement(headerTokens, lineNumber, out var namedGeometryPlacement))
            {
                placement = namedGeometryPlacement;
                objectArguments = attrlist.Clone();
                objectArguments.Values.TryAdd("name", headerTokens[0]);
            }
            else if (TryParseNamedTargetPlacement(headerTokens, lineNumber, out var namedTargetPlacement))
            {
                placement = namedTargetPlacement;
                objectArguments = attrlist.Clone();
                objectArguments.Values.TryAdd("name", headerTokens[0]);
            }
            else
            {
            var placementTokenCount = GetPlacementTokenCount(headerTokens, lineNumber, "slides");
            if (headerTokens.Count > placementTokenCount)
            {
                if (headerTokens.Count > 1 && string.Equals(headerTokens[1], "=", StringComparison.Ordinal))
                {
                    throw new DeckDocParseException("Slide object lines do not use '=' slot-binding syntax. Reserve '@name = ...' for layout slot lines and use '@target [attrlist] | payload' inside slides.", lineNumber, queryHint: "layout-slot-lines");
                }

                throw new DeckDocParseException("Compact object lines only allow a placement reference before the attrlist or payload.", lineNumber, queryHint: "slides");
            }

            placement = ParsePlacementReference(headerTokens, lineNumber, "slides");
            }
        }

        return new DeckObjectDefinition
        {
            LineNumber = lineNumber,
            Placement = placement,
            Arguments = objectArguments,
            PayloadSegments = SplitPayloadSegments(payload, lineNumber),
            RawLine = line,
        };
    }

    private static bool TryParseTrailingPlacementGeometry(List<string> headerTokens, DeckDirectiveArguments attrlist, List<string> trailingPlacementTokens, int lineNumber, out DeckObjectPlacement placement, out DeckDirectiveArguments arguments)
    {
        placement = null!;
        arguments = attrlist;
        if (headerTokens.Count != 1 || trailingPlacementTokens.Count == 0)
        {
            return false;
        }

        placement = ParsePlacementReference(trailingPlacementTokens, lineNumber, "slides");
        if (placement.Mode != DeckObjectAddressingMode.Geometry)
        {
            return false;
        }

        arguments = attrlist.Clone();
        arguments.Values.TryAdd("name", headerTokens[0]);
        return true;
    }

    private static bool TryExtractInlineAttrlistWithTrailingPlacement(string header, int lineNumber, out string headerWithoutAttrlist, out List<string>? trailingPlacementTokens)
    {
        headerWithoutAttrlist = header;
        trailingPlacementTokens = null;

        var openIndex = FindInlineAttrlistStartIndex(header);
        if (openIndex < 0)
        {
            return false;
        }

        var closeIndex = FindClosingBracket(header, openIndex);
        if (closeIndex < 0)
        {
            throw new DeckDocParseException("Attribute lists must use balanced '[' and ']'.", lineNumber, queryHint: "slides");
        }

        var suffix = header[(closeIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        headerWithoutAttrlist = header[..openIndex].TrimEnd();
        trailingPlacementTokens = TokenizeTrailingPlacementSuffix(suffix, lineNumber);
        return true;
    }

    private static string GetAttrlistContent(string header, int lineNumber)
    {
        var openIndex = FindInlineAttrlistStartIndex(header);
        var closeIndex = FindClosingBracket(header, openIndex);
        if (openIndex < 0 || closeIndex < 0)
        {
            throw new DeckDocParseException("Attribute lists must use balanced '[' and ']'.", lineNumber, queryHint: "slides");
        }

        return header[(openIndex + 1)..closeIndex].Trim();
    }

    private static List<string> TokenizeTrailingPlacementSuffix(string suffix, int lineNumber)
    {
        var tokens = Tokenize(suffix, lineNumber);
        if (tokens.Count == 0)
        {
            throw new DeckDocParseException("Trailing placement syntax must include an anchor, range, or target reference after the attrlist.", lineNumber, queryHint: "slides");
        }

        var placementTokens = new List<string>
        {
            tokens[0].StartsWith("at=", StringComparison.OrdinalIgnoreCase) ? tokens[0][3..] : tokens[0],
        };
        if (tokens.Count >= 2)
        {
            placementTokens.Add(tokens[1].StartsWith("size=", StringComparison.OrdinalIgnoreCase) ? tokens[1][5..] : tokens[1]);
        }

        if (tokens.Count > 2)
        {
            throw new DeckDocParseException("Trailing placement syntax only supports '<anchor-or-range>' or 'at=<anchor-or-range>' with an optional span token.", lineNumber, queryHint: "slides");
        }

        return placementTokens;
    }

    private static int FindInlineAttrlistStartIndex(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '[' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindClosingBracket(string value, int openIndex)
    {
        var inQuotes = false;
        var escaping = false;
        for (var index = openIndex + 1; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == ']')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsObjectPayloadContinuation(string line, DeckDirectiveArguments arguments)
    {
        if (line.StartsWith('|'))
        {
            return true;
        }

        if (line.StartsWith("- series ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal)
            || line.StartsWith("* ", StringComparison.Ordinal)
            || line.StartsWith("x ", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(line, @"^\d+\.\s", RegexOptions.CultureInvariant))
        {
            return true;
        }

        return false;
    }

    private static DeckFixedObjectDefinition ParseFixedObject(string line, int lineNumber)
    {
        var parsed = ParseObjectLine(line, lineNumber);
        return new DeckFixedObjectDefinition
        {
            LineNumber = lineNumber,
            Placement = parsed.Placement,
            Arguments = parsed.Arguments,
        };
    }

    private static DeckSlotDefinition ParseSlotDefinition(string line, int lineNumber)
    {
        var withoutMarker = line[1..].Trim();
        var equalsIndex = FindLayoutSlotBindingEqualsIndex(withoutMarker);
        if (equalsIndex < 0)
        {
            throw new DeckDocParseException("Layout slot lines must use '@slot = ...' syntax.", lineNumber, queryHint: "layout-block");
        }

        var name = withoutMarker[..equalsIndex].Trim();
        if (name.Length == 0)
        {
            throw new DeckDocParseException("Layout slot names cannot be empty.", lineNumber, queryHint: "layout-block");
        }

        var right = withoutMarker[(equalsIndex + 1)..].Trim();
        var attrlist = ExtractTrailingBracketBlock(right, lineNumber, out var headerWithoutAttrlist);
        var tokens = Tokenize(headerWithoutAttrlist, lineNumber);
        if (tokens.Count == 0)
        {
            throw new DeckDocParseException("Layout slots must bind to geometry or a named target.", lineNumber, queryHint: "layout-block");
        }

        return new DeckSlotDefinition
        {
            Name = name,
            LineNumber = lineNumber,
            Placement = ParsePlacementReference(tokens, lineNumber, "layout-block"),
            Arguments = attrlist,
        };
    }

    private static bool HasLayoutSlotBinding(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return FindLayoutSlotBindingEqualsIndex(line[1..].Trim()) >= 0;
    }

    private static int FindLayoutSlotBindingEqualsIndex(string value)
    {
        var inQuotes = false;
        var escaping = false;
        var bracketDepth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }

            if (ch == '=' && bracketDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static DeckObjectOverrideDefinition ParseObjectOverride(ParsedDirective directive, string? trailingContent, int lineNumber)
    {
        var placementTokenCount = GetPlacementTokenCount(directive.Arguments.BareTokens, lineNumber, "slides");
        var arguments = SliceArguments(directive.Arguments, placementTokenCount);
        string? payload = null;

        if (!string.IsNullOrWhiteSpace(trailingContent))
        {
            if (TryParseObjectOverrideTrailingArguments(trailingContent, lineNumber, out var trailingArguments))
            {
                arguments = MergeArguments(arguments, trailingArguments);
            }
            else
            {
                payload = ParseObjectOverrideTrailingPayload(trailingContent, lineNumber);
            }
        }

        return new DeckObjectOverrideDefinition
        {
            LineNumber = lineNumber,
            Placement = ParsePlacementReference([.. directive.Arguments.BareTokens.Take(placementTokenCount)], lineNumber, "slides"),
            Arguments = arguments,
            Payload = payload,
        };
    }

    private static bool TryParseObjectOverrideTrailingArguments(string trailingContent, int lineNumber, out DeckDirectiveArguments arguments)
    {
        arguments = new DeckDirectiveArguments();
        if (trailingContent.Length < 2 || trailingContent[0] != '[' || trailingContent[^1] != ']')
        {
            return false;
        }

        var innerContent = trailingContent[1..^1].Trim();
        if (innerContent.Length == 0)
        {
            return false;
        }

        var tokens = Tokenize(innerContent, lineNumber);
        if (!LooksLikeObjectOverrideArgumentTokens(tokens))
        {
            return false;
        }

        arguments = ParseArguments(tokens);
        return true;
    }

    private static string ParseObjectOverrideTrailingPayload(string trailingContent, int lineNumber)
    {
        if (trailingContent.Length >= 2 && trailingContent[0] == '[' && trailingContent[^1] == ']')
        {
            var innerContent = trailingContent[1..^1].Trim();
            var tokens = Tokenize(innerContent, lineNumber);
            return tokens.Count == 1 ? tokens[0] : innerContent;
        }

        return trailingContent;
    }

    private static bool LooksLikeObjectOverrideArgumentTokens(List<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Any(static token => (token.Length > 0 && token[0] == '.') || token.Contains('=', StringComparison.Ordinal)))
        {
            return true;
        }

        return IsObjectKindToken(tokens[0])
            || (tokens.Count == 1 && IsObjectFlagToken(tokens[0]));
    }

    private static bool IsObjectKindToken(string token) =>
        token.Equals("text", StringComparison.OrdinalIgnoreCase)
        || token.Equals("list", StringComparison.OrdinalIgnoreCase)
        || token.Equals("image", StringComparison.OrdinalIgnoreCase)
        || token.Equals("icon", StringComparison.OrdinalIgnoreCase)
        || token.Equals("shape", StringComparison.OrdinalIgnoreCase)
        || token.Equals("line", StringComparison.OrdinalIgnoreCase);

    private static bool IsObjectFlagToken(string token) =>
        token.Equals("bold", StringComparison.OrdinalIgnoreCase)
        || token.Equals("italic", StringComparison.OrdinalIgnoreCase)
        || token.Equals("underline", StringComparison.OrdinalIgnoreCase)
        || token.Equals("strike", StringComparison.OrdinalIgnoreCase)
        || token.Equals("wrap", StringComparison.OrdinalIgnoreCase)
        || token.Equals("hidden", StringComparison.OrdinalIgnoreCase)
        || token.Equals("locked", StringComparison.OrdinalIgnoreCase);

    private static DeckDirectiveArguments MergeArguments(DeckDirectiveArguments source, DeckDirectiveArguments additional)
    {
        var merged = source.Clone();
        merged.Roles.AddRange(additional.Roles);
        merged.BareTokens.AddRange(additional.BareTokens);
        foreach (var pair in additional.Values)
        {
            merged.Values[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static DeckFixedObjectDefinition ParseLayoutFixedDirective(ParsedDirective directive, int lineNumber)
    {
        var placementTokenCount = GetPlacementTokenCount(directive.Arguments.BareTokens, lineNumber, "layout-block");
        var arguments = SliceArguments(directive.Arguments, placementTokenCount);
        arguments.BareTokens.Insert(0, directive.Kind);

        return new DeckFixedObjectDefinition
        {
            LineNumber = lineNumber,
            Placement = ParsePlacementReference([.. directive.Arguments.BareTokens.Take(placementTokenCount)], lineNumber, "layout-block"),
            Arguments = arguments,
        };
    }

    private static DeckGroupDirective ParseGroup(ParsedDirective directive, int lineNumber)
    {
        var name = RequireBareToken(directive.Arguments, 0, lineNumber, "group-block");
        var members = directive.Arguments.BareTokens.Skip(1).ToArray();
        if (members.Length == 0)
        {
            if (directive.Arguments.Values.Count > 0)
            {
                throw new DeckDocParseException("Grouping is a single directive, not a geometry or nested block form. Create named objects first, then group them with '[group name member1 member2 ...]'.", lineNumber, queryHint: "group-block");
            }

            throw new DeckDocParseException("Group directives must specify at least one member reference.", lineNumber, queryHint: "group-block");
        }

        return new DeckGroupDirective
        {
            Name = name,
            Members = members,
        };
    }

    private static DeckAnimationDirective ParseAnimate(ParsedDirective directive, int lineNumber)
    {
        var targetText = RequireBareToken(directive.Arguments, 0, lineNumber, "animate");
        var reference = ParseTargetReference(targetText, lineNumber, "animate");
        return new DeckAnimationDirective
        {
            TargetName = reference.TargetName!,
            TargetIndex = reference.TargetIndex,
            Arguments = SliceArguments(directive.Arguments, 1),
        };
    }

    private static DeckTransitionDirective ParseTransition(ParsedDirective directive, int lineNumber) =>
        new()
        {
            Type = RequireBareToken(directive.Arguments, 0, lineNumber, "slide-transition"),
            Arguments = SliceArguments(directive.Arguments, 1),
        };

    private static DeckLayoutTargetDefinition ParseTargetDefinition(ParsedDirective directive, int lineNumber) =>
        directive.Kind switch
        {
            "area" => ParseArea(directive.Arguments, lineNumber),
            "split" => ParseSplit(directive.Arguments, lineNumber),
            "grid" => ParseGridTarget(directive.Arguments, lineNumber),
            "stack" => ParseStack(directive.Arguments, lineNumber),
            _ => throw new DeckDocParseException($"Unsupported target definition '[{directive.Kind} ...]'.", lineNumber, queryHint: directive.Kind),
        };

    private static DeckAreaTargetDefinition ParseArea(DeckDirectiveArguments arguments, int lineNumber)
    {
        var name = RequireBareToken(arguments, 0, lineNumber, "layout-surfaces");
        var hasOptionalEquals = arguments.BareTokens.Count > 2 && string.Equals(arguments.BareTokens[1], "=", StringComparison.Ordinal);
        var rangeText = hasOptionalEquals
            ? arguments.BareTokens[2]
            : RequireBareToken(arguments, 1, lineNumber, "layout-surfaces");
        return new DeckAreaTargetDefinition
        {
            LineNumber = lineNumber,
            Name = name,
            Range = ParseRange(rangeText, lineNumber, "layout-surfaces"),
            Arguments = SliceArguments(arguments, hasOptionalEquals ? 3 : 2),
        };
    }

    private static DeckSplitTargetDefinition ParseSplit(DeckDirectiveArguments arguments, int lineNumber)
    {
        var source = arguments.BareTokens.Count > 0
            ? arguments.BareTokens[0]
            : "__grid";
        var partsText = arguments.GetValue("rows") ?? arguments.GetValue("cols")
            ?? throw new DeckDocParseException("Split directives must specify either rows=(...) or cols=(...).", lineNumber, queryHint: "layout-surfaces");
        var namesText = RequireNamedValue(arguments, "as", lineNumber, "layout-surfaces");
        var outputs = ParseListValue(namesText);
        if (outputs.Count == 0)
        {
            throw new DeckDocParseException("Split directives must name their output targets with as=name,... .", lineNumber, queryHint: "layout-surfaces");
        }

        return new DeckSplitTargetDefinition
        {
            LineNumber = lineNumber,
            Source = source,
            IsRows = arguments.GetValue("rows") is not null,
            Parts = ParseListValue(partsText),
            OutputNames = outputs,
            Gap = ParseOptionalDouble(arguments.GetValue("gap")),
        };
    }

    private static DeckGridTargetDefinition ParseGridTarget(DeckDirectiveArguments arguments, int lineNumber)
    {
        var name = RequireBareToken(arguments, 0, lineNumber, "layout-surfaces");
        var source = RequireNamedValue(arguments, "in", lineNumber, "layout-surfaces");
        var columns = ParseRequiredInt(arguments.GetValue("cols"), lineNumber, "layout-surfaces", "cols");
        int? rows = arguments.GetValue("rows") is string rowsText
            ? ParseRequiredInt(rowsText, lineNumber, "layout-surfaces", "rows")
            : null;
        ParseGap(arguments.GetValue("gap"), out var horizontalGap, out var verticalGap);
        return new DeckGridTargetDefinition
        {
            LineNumber = lineNumber,
            Name = name,
            Source = source,
            Columns = columns,
            Rows = rows,
            HorizontalGap = horizontalGap,
            VerticalGap = verticalGap,
            Order = arguments.GetValue("order") ?? "row",
            Arguments = SliceArguments(arguments, 1, ["in", "cols", "rows", "gap", "order"]),
        };
    }

    private static DeckStackTargetDefinition ParseStack(DeckDirectiveArguments arguments, int lineNumber)
    {
        var name = RequireBareToken(arguments, 0, lineNumber, "layout-surfaces");
        return new DeckStackTargetDefinition
        {
            LineNumber = lineNumber,
            Name = name,
            Source = RequireNamedValue(arguments, "in", lineNumber, "layout-surfaces"),
            Direction = RequireNamedValue(arguments, "dir", lineNumber, "layout-surfaces"),
            Count = ParseRequiredInt(arguments.GetValue("count"), lineNumber, "layout-surfaces", "count"),
            Gap = ParseOptionalDouble(arguments.GetValue("gap")),
            Arguments = SliceArguments(arguments, 1, ["in", "dir", "count", "gap"]),
        };
    }

    private static DeckChartSeries ParseChartSeries(string line, int lineNumber)
    {
        if (!line.StartsWith('-'))
        {
            throw new DeckDocParseException("Chart blocks must contain '- series ...' lines.", lineNumber, queryHint: "chart-block");
        }

        var tokens = Tokenize(line[1..].Trim(), lineNumber);
        if (tokens.Count < 5 || !string.Equals(tokens[0], "series", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeckDocParseException("Chart series lines must use '- series <type> <label> ...' syntax.", lineNumber, queryHint: "chart-block");
        }

        var arguments = ParseArguments(tokens.Skip(3));
        return new DeckChartSeries
        {
            Type = tokens[1],
            Label = tokens[2],
            Categories = ParseListValue(RequireNamedValue(arguments, "cat", lineNumber, "chart-block")),
            Values = ParseListValue(RequireNamedValue(arguments, "val", lineNumber, "chart-block")),
            Axis = arguments.GetValue("axis") ?? "primary",
            Color = arguments.GetValue("color"),
            Labels = arguments.HasToken("labels"),
        };
    }

    private static List<string> ParseTableRow(string line, int lineNumber)
    {
        if (!line.StartsWith('|'))
        {
            throw new DeckDocParseException("Table rows must start with '|'.", lineNumber, queryHint: "table-block");
        }

        var cells = SplitPipes(line[1..], lineNumber, trimUnquoted: false);
        if (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[^1]))
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells;
    }

    private static bool IsMarkdownTableSeparator(IReadOnlyList<string> cells)
    {
        var hasSeparatorCell = false;
        foreach (var cell in cells)
        {
            var trimmed = cell.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            hasSeparatorCell = true;
            if (!trimmed.All(static ch => ch == '-'))
            {
                return false;
            }
        }

        return hasSeparatorCell;
    }

    private static DeckObjectPlacement ParsePlacementReference(List<string> tokens, int lineNumber, string queryHint)
    {
        if (tokens.Count == 0)
        {
            throw new DeckDocParseException("Expected a placement reference.", lineNumber, queryHint: queryHint);
        }

        if (LooksLikeRange(tokens[0]))
        {
            var range = ParseRange(tokens[0], lineNumber, queryHint);
            return new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Geometry,
                Anchor = range.Start,
                Span = CreateSpanFromRange(range),
            };
        }

        if (LooksLikeAnchor(tokens[0]))
        {
            if (tokens.Count < 2)
            {
                throw new DeckDocParseException("Geometry references must include both an anchor and a span.", lineNumber, queryHint: queryHint);
            }

            return new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Geometry,
                Anchor = ParseAnchor(tokens[0], lineNumber, queryHint),
                Span = ParseSpan(tokens[1], lineNumber, queryHint),
            };
        }

        return ParseTargetReference(tokens[0], lineNumber, queryHint);
    }

    private static int GetPlacementTokenCount(List<string> tokens, int lineNumber, string queryHint)
    {
        if (tokens.Count == 0)
        {
            throw new DeckDocParseException("Expected a placement reference.", lineNumber, queryHint: queryHint);
        }

        if (LooksLikeRange(tokens[0]))
        {
            return 1;
        }

        if (LooksLikeAnchor(tokens[0]))
        {
            if (tokens.Count < 2)
            {
                throw new DeckDocParseException("Geometry references must include both an anchor and a span.", lineNumber, queryHint: queryHint);
            }

            return 2;
        }

        return 1;
    }

    private static bool TryParseNamedGeometryPlacement(List<string> tokens, int lineNumber, out DeckObjectPlacement placement)
    {
        placement = null!;
        if (tokens.Count < 2 || LooksLikeRange(tokens[0]) || LooksLikeAnchor(tokens[0]))
        {
            return false;
        }

        var geometryTokens = tokens.Skip(1).ToList();
        var placementTokenCount = GetPlacementTokenCount(geometryTokens, lineNumber, "slides");
        if (placementTokenCount != geometryTokens.Count || placementTokenCount == 1)
        {
            return false;
        }

        placement = ParsePlacementReference(geometryTokens, lineNumber, "slides");
        return placement.Mode == DeckObjectAddressingMode.Geometry;
    }

    private static bool TryParseNamedTargetPlacement(List<string> tokens, int lineNumber, out DeckObjectPlacement placement)
    {
        placement = null!;
        if (tokens.Count < 2 || LooksLikeRange(tokens[0]) || LooksLikeAnchor(tokens[0]))
        {
            return false;
        }

        var placementTokens = tokens.Skip(1).ToList();
        var placementTokenCount = GetPlacementTokenCount(placementTokens, lineNumber, "slides");
        if (placementTokenCount != placementTokens.Count || placementTokenCount != 1)
        {
            return false;
        }

        placement = ParsePlacementReference(placementTokens, lineNumber, "slides");
        return placement.Mode == DeckObjectAddressingMode.Target;
    }

    private static bool TryParseNamedFixedPlacement(List<string> tokens, int lineNumber, out DeckObjectPlacement placement)
    {
        placement = null!;
        if (tokens.Count < 2 || LooksLikeRange(tokens[0]) || LooksLikeAnchor(tokens[0]))
        {
            return false;
        }

        var placementTokens = tokens.Skip(1).ToList();
        var placementTokenCount = GetPlacementTokenCount(placementTokens, lineNumber, "layout-block");
        if (placementTokenCount != placementTokens.Count)
        {
            return false;
        }

        placement = ParsePlacementReference(placementTokens, lineNumber, "layout-block");
        return placement.Mode == DeckObjectAddressingMode.Geometry || placement.Mode == DeckObjectAddressingMode.Target;
    }

    private static DeckObjectPlacement ParseTargetReference(string token, int lineNumber, string queryHint)
    {
        if (token.Length == 0)
        {
            throw new DeckDocParseException("Target references cannot be empty.", lineNumber, queryHint: queryHint);
        }

        var bracketIndex = token.IndexOf('[', StringComparison.Ordinal);
        if (bracketIndex < 0)
        {
            return new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Target,
                TargetName = token,
            };
        }

        if (!token.EndsWith(']'))
        {
            throw new DeckDocParseException("Indexed target references must end with ']'.", lineNumber, queryHint: queryHint);
        }

        return new DeckObjectPlacement
        {
            Mode = DeckObjectAddressingMode.Target,
            TargetName = token[..bracketIndex],
            TargetIndex = token[(bracketIndex + 1)..^1],
        };
    }

    private static DeckGridAnchor ParseAnchor(string text, int lineNumber, string queryHint)
    {
        if (!LooksLikeAnchor(text))
        {
            throw new DeckDocParseException($"'{text}' is not a valid grid anchor.", lineNumber, queryHint: queryHint);
        }

        var splitIndex = 0;
        while (splitIndex < text.Length && char.IsLetter(text[splitIndex]))
        {
            splitIndex++;
        }

        var column = 0;
        foreach (var ch in text[..splitIndex].ToUpperInvariant())
        {
            column = (column * 26) + (ch - 'A' + 1);
        }

        var row = int.Parse(text[splitIndex..], CultureInfo.InvariantCulture);
        return new DeckGridAnchor(column, row);
    }

    private static DeckGridRange ParseRange(string text, int lineNumber, string queryHint)
    {
        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new DeckDocParseException($"'{text}' is not a valid grid range.", lineNumber, queryHint: queryHint);
        }

        var start = ParseAnchor(parts[0], lineNumber, queryHint);
        var end = ParseAnchor(parts[1], lineNumber, queryHint);
        if (end.RowNumber < start.RowNumber && end.ColumnNumber >= start.ColumnNumber)
        {
            // Tolerate AI-authored shorthand like B4:AD3 meaning anchor B4 spanning to column AD for 3 rows.
            end = new DeckGridAnchor(end.ColumnNumber, start.RowNumber + end.RowNumber - 1);
        }

        return new DeckGridRange(start, end);
    }

    private static bool LooksLikeRange(string text)
    {
        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && LooksLikeAnchor(parts[0]) && LooksLikeAnchor(parts[1]);
    }

    private static DeckGridSize CreateSpanFromRange(DeckGridRange range) =>
        new(
            range.End.ColumnNumber - range.Start.ColumnNumber + 1,
            range.End.RowNumber - range.Start.RowNumber + 1);

    private static DeckGridSize ParseSpan(string text, int lineNumber, string queryHint)
    {
        var separatorIndex = text.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (separatorIndex <= 0 || separatorIndex >= text.Length - 1)
        {
            throw new DeckDocParseException($"'{text}' is not a valid span expression.", lineNumber, queryHint: queryHint);
        }

        var width = ParseRequiredDouble(text[..separatorIndex], lineNumber, queryHint, "span width");
        var height = ParseRequiredDouble(text[(separatorIndex + 1)..], lineNumber, queryHint, "span height");

        if (width <= 0)
        {
            throw new DeckDocParseException("The 'span width' entry must be greater than zero.", lineNumber, queryHint: queryHint);
        }

        if (height <= 0)
        {
            throw new DeckDocParseException("The 'span height' entry must be greater than zero.", lineNumber, queryHint: queryHint);
        }

        return new DeckGridSize(width, height);
    }

    private static DeckGridSize ParseGridSize(string text, int lineNumber, string queryHint) =>
        ParseSpan(text, lineNumber, queryHint);

    private static (DeckGridAnchor Anchor, DeckGridSize Size) ParseAnchorAndSize(string atValue, string? sizeValue, int lineNumber, string queryHint)
    {
        if (LooksLikeRange(atValue))
        {
            var range = ParseRange(atValue, lineNumber, queryHint);
            return (range.Start, CreateSpanFromRange(range));
        }

        if (sizeValue is null)
        {
            throw new DeckDocParseException("Required 'size=...' syntax entry is missing.", lineNumber, queryHint: queryHint);
        }

        return (ParseAnchor(atValue, lineNumber, queryHint), ParseSpan(sizeValue, lineNumber, queryHint));
    }

    private static (DeckGridAnchor? Anchor, DeckGridSize? Size, string? TargetName, string? TargetIndex) ParseStandaloneBlockPlacement(string atValue, string? sizeValue, int lineNumber, string queryHint)
    {
        if (LooksLikeRange(atValue))
        {
            var range = ParseRange(atValue, lineNumber, queryHint);
            return (range.Start, CreateSpanFromRange(range), null, null);
        }

        if (LooksLikeAnchor(atValue))
        {
            if (sizeValue is null)
            {
                throw new DeckDocParseException("Required 'size=...' syntax entry is missing.", lineNumber, queryHint: queryHint);
            }

            return (ParseAnchor(atValue, lineNumber, queryHint), ParseSpan(sizeValue, lineNumber, queryHint), null, null);
        }

        var targetReference = ParseTargetReference(atValue, lineNumber, queryHint);
        return (null, null, targetReference.TargetName, targetReference.TargetIndex);
    }

    private static bool IsAttributeLine(string line)
    {
        if (line.Length == 0 || line[0] != ':')
        {
            return false;
        }

        var secondColon = line.IndexOf(':', 1);
        return secondColon > 1;
    }

    private static bool IsLayoutStart(string line, out string layoutName)
    {
        layoutName = string.Empty;
        if (!line.StartsWith("[layout ", StringComparison.OrdinalIgnoreCase) || !line.EndsWith(']'))
        {
            return false;
        }

        layoutName = line[8..^1].Trim();
        return layoutName.Length > 0;
    }

    private static bool IsGridSizeDirective(DeckDirectiveArguments arguments) =>
        arguments.BareTokens.Count == 1
        && arguments.Values.Count == 0
        && arguments.Roles.Count == 0
        && arguments.BareTokens[0].Contains('x', StringComparison.OrdinalIgnoreCase)
        && !arguments.BareTokens[0].Contains('[', StringComparison.Ordinal);

    private static string RequireBareToken(DeckDirectiveArguments arguments, int index, int lineNumber, string queryHint)
    {
        if (index < arguments.BareTokens.Count)
        {
            return arguments.BareTokens[index];
        }

        throw new DeckDocParseException("Required syntax token is missing.", lineNumber, queryHint: queryHint);
    }

    private static string RequireStyleName(DeckDirectiveArguments arguments, int lineNumber)
    {
        if (arguments.Roles.Count > 0)
        {
            return arguments.Roles[0];
        }

        if (arguments.BareTokens.Count > 0)
        {
            return arguments.BareTokens[0].TrimStart('.');
        }

        throw new DeckDocParseException("Required syntax token is missing.", lineNumber, queryHint: "style");
    }

    private static (string Name, DeckDirectiveArguments Arguments) ParseStyleDefinition(DeckDirectiveArguments arguments, int lineNumber)
    {
        if (arguments.Roles.Count > 0)
        {
            var styleArguments = new DeckDirectiveArguments();
            foreach (var role in arguments.Roles.Skip(1))
            {
                styleArguments.Roles.Add(role);
            }

            foreach (var token in arguments.BareTokens)
            {
                styleArguments.BareTokens.Add(token);
            }

            foreach (var pair in arguments.Values)
            {
                styleArguments.Values[pair.Key] = pair.Value;
            }

            return (arguments.Roles[0], styleArguments);
        }

        if (arguments.BareTokens.Count > 0)
        {
            return (arguments.BareTokens[0].TrimStart('.'), SliceArguments(arguments, 1));
        }

        throw new DeckDocParseException("Required syntax token is missing.", lineNumber, queryHint: "style");
    }

    private static string RequireNamedValue(DeckDirectiveArguments arguments, string name, int lineNumber, string queryHint) =>
        arguments.GetValue(name)
        ?? throw new DeckDocParseException($"Required '{name}=...' syntax entry is missing.", lineNumber, queryHint: queryHint);

    private static DeckDirectiveArguments SliceArguments(DeckDirectiveArguments source, int bareTokenSkip, IReadOnlyCollection<string>? omittedNames = null)
    {
        var slice = new DeckDirectiveArguments();
        slice.Roles.AddRange(source.Roles);
        foreach (var token in source.BareTokens.Skip(bareTokenSkip))
        {
            slice.BareTokens.Add(token);
        }

        foreach (var pair in source.Values)
        {
            if (omittedNames is not null && omittedNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            slice.Values[pair.Key] = pair.Value;
        }

        return slice;
    }

    private static DeckDirectiveArguments ExtractTrailingBracketBlock(string text, int lineNumber, out string headerWithoutAttrlist)
    {
        headerWithoutAttrlist = text.Trim();
        if (!headerWithoutAttrlist.EndsWith(']'))
        {
            return new DeckDirectiveArguments();
        }

        var startIndex = FindMatchingOpeningBracket(headerWithoutAttrlist);
        if (startIndex < 0)
        {
            throw new DeckDocParseException("Attribute lists must use balanced '[' and ']'.", lineNumber, queryHint: "slides");
        }

        if (startIndex > 0 && !char.IsWhiteSpace(headerWithoutAttrlist[startIndex - 1]))
        {
            return new DeckDirectiveArguments();
        }

        var attrContent = headerWithoutAttrlist[(startIndex + 1)..^1].Trim();
        headerWithoutAttrlist = headerWithoutAttrlist[..startIndex].TrimEnd();
        return ParseArguments(Tokenize(attrContent, lineNumber));
    }

    private static int FindMatchingOpeningBracket(string value)
    {
        var inQuotes = false;
        var escaping = false;
        for (var index = value.Length - 1; index >= 0; index--)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '[')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindDirectiveClosingBracket(string value, int lineNumber)
    {
        var depth = 0;
        var inQuotes = false;
        var escaping = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '[')
            {
                depth++;
                continue;
            }

            if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        throw new DeckDocParseException("Attribute lists must use balanced '[' and ']'.", lineNumber, queryHint: "slides");
    }

    private static int FindPipeOutsideQuotes(string value)
    {
        var inQuotes = false;
        var escaping = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '|')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindEqualsOutsideQuotes(string value)
    {
        var inQuotes = false;
        var escaping = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuotes)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '=')
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> Tokenize(string value, int lineNumber)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        var parenthesisDepth = 0;

        void Flush()
        {
            if (builder.Length == 0)
            {
                return;
            }

            tokens.Add(builder.ToString());
            builder.Clear();
        }

        foreach (var ch in value)
        {
            if (inQuotes)
            {
                if (escaping)
                {
                    builder.Append(ch switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '"' => '"',
                        '|' => '|',
                        '\\' => '\\',
                        _ => ch,
                    });
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                    continue;
                }

                builder.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (parenthesisDepth > 0)
                {
                    builder.Append(ch);
                }
                else
                {
                    Flush();
                }
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == '(')
            {
                parenthesisDepth++;
                builder.Append(ch);
                continue;
            }

            if (ch == ')')
            {
                if (parenthesisDepth > 0)
                {
                    parenthesisDepth--;
                }

                builder.Append(ch);
                continue;
            }

            builder.Append(ch);
        }

        if (inQuotes)
        {
            throw new DeckDocParseException("Quoted strings must end with a closing double quote.", lineNumber, queryHint: "slides");
        }

        if (escaping)
        {
            builder.Append('\\');
        }

        Flush();
        return tokens;
    }

    private static DeckDirectiveArguments ParseArguments(IEnumerable<string> tokens)
    {
        var arguments = new DeckDirectiveArguments();
        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            if (LooksLikeRoleToken(token))
            {
                arguments.Roles.Add(token[1..]);
                continue;
            }

            var equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex > 0)
            {
                arguments.Values[token[..equalsIndex]] = token[(equalsIndex + 1)..];
                continue;
            }

            arguments.BareTokens.Add(token);
        }

        return arguments;
    }

    private static bool LooksLikeRoleToken(string token) =>
        token.Length > 1
        && token[0] == '.'
        && char.IsLetter(token[1])
        && token.IndexOfAny(['/', '\\']) < 0;

    private static List<string> SplitPayloadSegments(string value, int lineNumber, bool trimUnquoted = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return SplitPipes(value, lineNumber, trimUnquoted);
    }

    private static List<string> SplitPipes(string value, int lineNumber, bool trimUnquoted)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        var segmentQuoted = false;

        void Flush()
        {
            var raw = builder.ToString();
            builder.Clear();
            var text = segmentQuoted ? raw : (trimUnquoted ? raw.Trim() : raw.Trim());
            segments.Add(text);
            segmentQuoted = false;
        }

        foreach (var ch in value)
        {
            if (inQuotes)
            {
                if (escaping)
                {
                    builder.Append(ch switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '"' => '"',
                        '|' => '|',
                        '\\' => '\\',
                        _ => ch,
                    });
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = false;
                    continue;
                }

                builder.Append(ch);
                continue;
            }

            if (ch == '"' && builder.Length == 0)
            {
                inQuotes = true;
                segmentQuoted = true;
                continue;
            }

            if (ch == '|')
            {
                Flush();
                continue;
            }

            builder.Append(ch);
        }

        if (inQuotes)
        {
            throw new DeckDocParseException("Quoted payload strings must end with a closing double quote.", lineNumber, queryHint: "slides");
        }

        Flush();
        return segments;
    }

    private static List<string> ParseListValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void ParseGap(string? gapText, out double horizontalGap, out double verticalGap)
    {
        horizontalGap = 0D;
        verticalGap = 0D;
        if (string.IsNullOrWhiteSpace(gapText))
        {
            return;
        }

        var parts = gapText.Split('x', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        horizontalGap = ParseOptionalDouble(parts[0]);
        verticalGap = parts.Length == 2 ? ParseOptionalDouble(parts[1]) : horizontalGap;
    }

    private static int ParseRequiredInt(string? value, int lineNumber, string queryHint, string entryName)
    {
        if (value is null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DeckDocParseException($"The '{entryName}' entry must be an integer.", lineNumber, queryHint: queryHint);
        }

        return parsed;
    }

    private static double ParseRequiredDouble(string value, int lineNumber, string queryHint, string entryName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DeckDocParseException($"The '{entryName}' entry must be numeric.", lineNumber, queryHint: queryHint);
        }

        return parsed;
    }

    private static double ParseOptionalDouble(string? value) =>
        value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0D;

    private static bool LooksLikeAnchor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var index = 0;
        while (index < text.Length && char.IsLetter(text[index]))
        {
            index++;
        }

        if (index == 0 || index == text.Length)
        {
            return false;
        }

        if (index > 3 || !text[index..].All(char.IsDigit))
        {
            return false;
        }

        return text[..index].All(static ch => ch is >= 'A' and <= 'Z');
    }

    private static int FindFirstMeaningfulLine(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Holds the parsed name and arguments for one directive line.
    /// </summary>
    private readonly record struct ParsedDirective(string Kind, DeckDirectiveArguments Arguments);
}