using AIToolkit.Tools.Deck;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Renders the shared DeckDoc syntax model into PowerPoint slide content.
/// </summary>
internal static class PowerPointDeckRenderer
{
    private const long SlideWidth = 12_192_000L;
    private const long SlideHeight = 6_858_000L;

    /// <summary>
    /// Builds one PowerPoint slide from the parsed DeckDoc model.
    /// </summary>
    public static P.Slide CreateSlide(
        SlidePart slidePart,
        DeckDocDocument document,
        DeckDocSlide slide,
        IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages)
    {
        ArgumentNullException.ThrowIfNull(slidePart);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(resolvedImages);

        var layout = slide.LayoutName is not null && document.LayoutMap.TryGetValue(slide.LayoutName, out var matchedLayout)
            ? matchedLayout
            : null;
        var grid = ResolveGrid(document, layout);
        var targets = ResolveTargets(document, layout, slide, grid);
        if (layout is not null)
        {
            MaterializeIndexedSlotTargets(document, layout, slide, targets);
        }

        var (resolvedObjects, standaloneOverrides) = ResolveObjectOverrides(document, slide, grid, targets);

        var renderedElements = new List<RenderedElement>();
        var renderedTargetIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var sequence = 0;
        uint nextShapeId = 2;
        var hasVisibleTitle = false;

        var background = slide.Background ?? layout?.Background;
        var backgroundShapeId = nextShapeId++;
        if (background is not null && TryCreateBackground(slidePart, document, background, grid, resolvedImages, backgroundShapeId, out var backgroundElement))
        {
            renderedElements.Add(new RenderedElement(-10_000, sequence++, backgroundShapeId, backgroundElement));
        }

        if (layout is not null)
        {
            foreach (var fixedObject in layout.FixedObjects)
            {
                var shapeId = nextShapeId++;
                if (TryCreateObjectElement(slidePart, document, fixedObject.Placement, fixedObject.Arguments, Array.Empty<string>(), grid, targets, resolvedImages, shapeId, fixedObject.LineNumber, out var element, out var layer, out _))
                {
                    RegisterRenderedTarget(renderedTargetIds, fixedObject.Placement, fixedObject.Arguments, shapeId);
                    renderedElements.Add(new RenderedElement(layer, sequence++, shapeId, element));
                }
            }
        }

        foreach (var deckObject in resolvedObjects)
        {
            var shapeId = nextShapeId++;
            if (!TryCreateObjectElement(slidePart, document, deckObject.Placement, deckObject.Arguments, deckObject.PayloadSegments, grid, targets, resolvedImages, shapeId, deckObject.LineNumber, out var element, out var layer, out var isVisibleTitle))
            {
                continue;
            }

            hasVisibleTitle |= isVisibleTitle;
            RegisterRenderedTarget(renderedTargetIds, deckObject.Placement, deckObject.Arguments, shapeId);
            renderedElements.Add(new RenderedElement(layer, sequence++, shapeId, element));
        }

        foreach (var deckObject in standaloneOverrides)
        {
            var shapeId = nextShapeId++;
            if (!TryCreateObjectOverrideElement(slidePart, document, deckObject, grid, targets, resolvedImages, shapeId, out var element, out var layer))
            {
                continue;
            }

            RegisterRenderedTarget(renderedTargetIds, deckObject.Placement, deckObject.Arguments, shapeId);
            renderedElements.Add(new RenderedElement(layer, sequence++, shapeId, element));
        }

        foreach (var table in slide.Tables)
        {
            if (!TryResolveStandaloneBlockRect(table.Anchor, table.Size, table.TargetName, table.TargetIndex, grid, targets, out var tableRect))
            {
                continue;
            }

            var shapeId = nextShapeId++;
            renderedElements.Add(new RenderedElement(
                ResolveLayer(table.Arguments),
                sequence++,
                shapeId,
                CreateTableFrame(document, table, tableRect, grid, shapeId)));
        }

        foreach (var chart in slide.Charts)
        {
            if (!TryResolveStandaloneBlockRect(chart.Anchor, chart.Size, chart.TargetName, chart.TargetIndex, grid, targets, out var chartRect))
            {
                continue;
            }

            var shapeId = nextShapeId++;
            renderedElements.Add(new RenderedElement(
                ResolveLayer(chart.Arguments),
                sequence++,
                shapeId,
                CreateChartFrame(slidePart, document, chart, chartRect, grid, shapeId)));
        }

        if (!hasVisibleTitle)
        {
            RegisterRenderedTarget(
                renderedTargetIds,
                new DeckObjectPlacement { Mode = DeckObjectAddressingMode.Target, TargetName = "title" },
                new DeckDirectiveArguments(),
                nextShapeId);
            renderedElements.Add(new RenderedElement(
                0,
                sequence,
                nextShapeId,
                CreateTextShape(
                    nextShapeId,
                    $"Title {slide.SlideNumber}",
                    ToEmuRect(new DeckGridRect(1, 1, Math.Min(24, grid.Width - 2), 2), grid),
                    [slide.Title],
                    ResolveDefaultFontSize("title"),
                    bold: true,
                    fillHex: null,
                    strokeHex: null,
                    textColorHex: ResolveColor(document, document.ThemeTokens.TryGetValue("ink", out var ink) ? ink : "#0F172A"),
                    shapeKind: A.ShapeTypeValues.Rectangle)));
        }

        if (slide.Groups.Count > 0)
        {
            renderedElements = ApplyGroups(slide, renderedElements, renderedTargetIds, ref nextShapeId);
        }

        var shapeTree = CreateShapeTree();
        foreach (var rendered in renderedElements
            .OrderBy(static item => item.Layer)
            .ThenBy(static item => item.Sequence))
        {
            shapeTree.Append(rendered.Element);
        }

        var slideDocument = new P.Slide();
        slideDocument.Append(new P.CommonSlideData(shapeTree));
        slideDocument.Append(new P.ColorMapOverride(new A.MasterColorMapping()));
        TryAppendTransition(slideDocument, slide.Transition ?? layout?.Transition);

        if (slide.Hidden)
        {
            slideDocument.Show = false;
        }

        if (!string.IsNullOrWhiteSpace(slide.Notes))
        {
            TryWriteNotes(slidePart, slide.Notes!);
        }

        if (slide.Animations.Count > 0)
        {
            TryApplyAnimations(slideDocument, document, slide, renderedTargetIds);
        }

        return slideDocument;
    }

    private static (IReadOnlyList<DeckObjectDefinition> Objects, IReadOnlyList<DeckObjectOverrideDefinition> StandaloneOverrides) ResolveObjectOverrides(
        DeckDocDocument document,
        DeckDocSlide slide,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets)
    {
        var resolvedObjects = slide.Objects.ToList();
        var standaloneOverrides = new List<DeckObjectOverrideDefinition>();

        foreach (var deckObjectOverride in slide.ObjectOverrides)
        {
            if (!TryFindOverriddenObjectIndex(resolvedObjects, deckObjectOverride, out var objectIndex))
            {
                standaloneOverrides.Add(deckObjectOverride);
                continue;
            }

            resolvedObjects[objectIndex] = ApplyObjectOverride(document, slide, resolvedObjects[objectIndex], deckObjectOverride, grid, targets);
        }

        return (resolvedObjects, standaloneOverrides);
    }

    private static bool TryFindOverriddenObjectIndex(List<DeckObjectDefinition> objects, DeckObjectOverrideDefinition deckObjectOverride, out int objectIndex)
    {
        objectIndex = -1;
        if (deckObjectOverride.Placement.Mode != DeckObjectAddressingMode.Target || string.IsNullOrWhiteSpace(deckObjectOverride.Placement.TargetName))
        {
            return false;
        }

        var overrideKey = BuildPlacementKey(deckObjectOverride.Placement.TargetName, deckObjectOverride.Placement.TargetIndex);
        for (var index = objects.Count - 1; index >= 0; index--)
        {
            var candidate = objects[index];
            var explicitName = candidate.Arguments.GetValue("name");
            if (!string.IsNullOrWhiteSpace(explicitName)
                && (string.Equals(explicitName, overrideKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(explicitName, deckObjectOverride.Placement.TargetName, StringComparison.OrdinalIgnoreCase)))
            {
                objectIndex = index;
                return true;
            }

            if (candidate.Placement.Mode == DeckObjectAddressingMode.Target
                && !string.IsNullOrWhiteSpace(candidate.Placement.TargetName)
                && string.Equals(BuildPlacementKey(candidate.Placement.TargetName, candidate.Placement.TargetIndex), overrideKey, StringComparison.OrdinalIgnoreCase))
            {
                objectIndex = index;
                return true;
            }
        }

        return false;
    }

    private static DeckObjectDefinition ApplyObjectOverride(
        DeckDocDocument document,
        DeckDocSlide slide,
        DeckObjectDefinition originalObject,
        DeckObjectOverrideDefinition deckObjectOverride,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets)
    {
        DeckDirectiveArguments? inheritedArguments = null;
        if (targets is not null && TryResolvePlacementRect(originalObject.Placement, grid, targets, out _, out var resolvedInheritedArguments))
        {
            inheritedArguments = resolvedInheritedArguments;
        }

        var slotArguments = ResolveLayoutSlotArguments(document, slide, originalObject.Placement);
        var mergedArguments = MergeArguments(document, inheritedArguments, slotArguments, originalObject.Arguments, deckObjectOverride.Arguments);
        mergedArguments.Values.Remove("at");
        mergedArguments.Values.Remove("size");

        return new DeckObjectDefinition
        {
            LineNumber = deckObjectOverride.LineNumber,
            Placement = ResolveObjectOverridePlacement(originalObject.Placement, deckObjectOverride.Arguments),
            Arguments = mergedArguments,
            PayloadSegments = deckObjectOverride.Payload is not null ? [deckObjectOverride.Payload] : originalObject.PayloadSegments,
            RawLine = originalObject.RawLine,
        };
    }

    private static DeckDirectiveArguments? ResolveLayoutSlotArguments(DeckDocDocument document, DeckDocSlide slide, DeckObjectPlacement placement)
    {
        if (placement.Mode != DeckObjectAddressingMode.Target
            || string.IsNullOrWhiteSpace(placement.TargetName)
            || string.IsNullOrWhiteSpace(slide.LayoutName)
            || document.Layouts.FirstOrDefault(layout => string.Equals(layout.Name, slide.LayoutName, StringComparison.OrdinalIgnoreCase)) is not DeckDocLayout layout)
        {
            return null;
        }

        return layout.Slots.FirstOrDefault(slot => string.Equals(slot.Name, placement.TargetName, StringComparison.OrdinalIgnoreCase))?.Arguments;
    }

    private static DeckObjectPlacement ResolveObjectOverridePlacement(DeckObjectPlacement originalPlacement, DeckDirectiveArguments overrideArguments)
    {
        var atValue = overrideArguments.GetValue("at");
        if (string.IsNullOrWhiteSpace(atValue))
        {
            return originalPlacement;
        }

        if (TryParsePlacementOverride(atValue, overrideArguments.GetValue("size"), out var overridePlacement))
        {
            return overridePlacement;
        }

        return originalPlacement;
    }

    private static bool TryParsePlacementOverride(string atValue, string? sizeValue, out DeckObjectPlacement placement)
    {
        placement = null!;

        if (TryParseRendererRange(atValue, out var range))
        {
            placement = new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Geometry,
                Anchor = range.Start,
                Span = new DeckGridSize(range.End.ColumnNumber - range.Start.ColumnNumber + 1, range.End.RowNumber - range.Start.RowNumber + 1),
            };
            return true;
        }

        if (TryParseRendererAnchor(atValue, out var anchor))
        {
            if (string.IsNullOrWhiteSpace(sizeValue))
            {
                return false;
            }

            placement = new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Geometry,
                Anchor = anchor,
                Span = ParseGridSize(sizeValue),
            };
            return true;
        }

        placement = ParseRendererTargetPlacement(atValue);
        return true;
    }

    private static bool TryParseRendererRange(string value, out DeckGridRange range)
    {
        range = default;
        var parts = value.Split(":", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseRendererAnchor(parts[0], out var start) || !TryParseRendererAnchor(parts[1], out var end))
        {
            return false;
        }

        if (end.RowNumber < start.RowNumber && end.ColumnNumber >= start.ColumnNumber)
        {
            end = new DeckGridAnchor(end.ColumnNumber, start.RowNumber + end.RowNumber - 1);
        }

        range = new DeckGridRange(start, end);
        return true;
    }

    private static bool TryParseRendererAnchor(string value, out DeckGridAnchor anchor)
    {
        anchor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var splitIndex = 0;
        while (splitIndex < value.Length && char.IsLetter(value[splitIndex]))
        {
            splitIndex++;
        }

        if (splitIndex == 0 || splitIndex >= value.Length)
        {
            return false;
        }

        if (!int.TryParse(value[splitIndex..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) || row <= 0)
        {
            return false;
        }

        var column = 0;
        foreach (var ch in value[..splitIndex].ToUpperInvariant())
        {
            if (ch < 'A' || ch > 'Z')
            {
                return false;
            }

            column = (column * 26) + (ch - 'A' + 1);
        }

        anchor = new DeckGridAnchor(column, row);
        return true;
    }

    private static DeckObjectPlacement ParseRendererTargetPlacement(string value)
    {
        var trimmed = value.Trim();
        var bracketIndex = trimmed.IndexOf('[');
        if (bracketIndex > 0 && trimmed.EndsWith(']'))
        {
            return new DeckObjectPlacement
            {
                Mode = DeckObjectAddressingMode.Target,
                TargetName = trimmed[..bracketIndex],
                TargetIndex = trimmed[(bracketIndex + 1)..^1],
            };
        }

        return new DeckObjectPlacement
        {
            Mode = DeckObjectAddressingMode.Target,
            TargetName = trimmed,
        };
    }

    private static string BuildPlacementKey(string targetName, string? targetIndex) =>
        targetIndex is null ? targetName : $"{targetName}[{targetIndex}]";

    private static void RegisterRenderedTarget(Dictionary<string, uint> renderedTargetIds, DeckObjectPlacement placement, DeckDirectiveArguments arguments, uint shapeId)
    {
        if (arguments.GetValue("name") is string explicitName && !string.IsNullOrWhiteSpace(explicitName))
        {
            renderedTargetIds[explicitName] = shapeId;
        }

        if (placement.Mode == DeckObjectAddressingMode.Geometry && placement.Anchor is DeckGridAnchor anchor)
        {
            renderedTargetIds[FormatAnchorReference(anchor)] = shapeId;
        }

        if (string.IsNullOrWhiteSpace(placement.TargetName))
        {
            return;
        }

        var targetKey = placement.TargetIndex is null
            ? placement.TargetName
            : $"{placement.TargetName}[{placement.TargetIndex}]";
        renderedTargetIds[targetKey] = shapeId;
    }

    private static string FormatAnchorReference(DeckGridAnchor anchor)
    {
        var columnNumber = anchor.ColumnNumber;
        Span<char> buffer = stackalloc char[8];
        var index = buffer.Length;
        while (columnNumber > 0)
        {
            columnNumber--;
            buffer[--index] = (char)('A' + (columnNumber % 26));
            columnNumber /= 26;
        }

        return string.Concat(new string(buffer[index..]), anchor.RowNumber.ToString(CultureInfo.InvariantCulture));
    }

    private static void TryApplyAnimations(P.Slide slideDocument, DeckDocDocument document, DeckDocSlide slide, IReadOnlyDictionary<string, uint> renderedTargetIds)
    {
        var animationPlan = slide.Animations
            .Select(animation =>
            {
                var mergedArguments = animation.Arguments.GetValue("preset") is string presetName && document.Motions.TryGetValue(presetName, out var presetArguments)
                    ? MergeArguments(document, presetArguments, animation.Arguments)
                    : animation.Arguments;
                return new PlannedAnimation(animation, mergedArguments);
            })
            .Select(candidate =>
            {
                var effect = ResolveAnimationEffect(candidate.Arguments);
                var hasTarget = TryResolveAnimationTargetId(candidate.Animation, renderedTargetIds, out var shapeId);
                return new ResolvedAnimation(candidate.Animation, candidate.Arguments, effect, hasTarget ? shapeId : null);
            })
            .Where(static candidate => candidate.Effect is not null && candidate.ShapeId is not null)
            .OrderBy(static candidate => ResolveAnimationOrder(candidate.Arguments))
            .ThenBy(static candidate => candidate.Animation.TargetName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (animationPlan.Length == 0)
        {
            return;
        }

        EnsureTimingTree(slideDocument, out var mainSequenceNode, out var buildList);
        var timing = slideDocument.GetFirstChild<P.Timing>()!;
        var nextTimingId = GetMaxTimingId(timing) + 1;
        var nextGroupId = GetMaxGroupId(timing);
        var hasExistingAnimations = false;

        foreach (var planned in animationPlan)
        {
            var shapeId = planned.ShapeId!.Value;
            var effect = planned.Effect!.Value;

            var trigger = ResolveAnimationTrigger(planned.Arguments, hasExistingAnimations);
            var clickGroup = BuildClickGroup(
                shapeId.ToString(CultureInfo.InvariantCulture),
                effect.PresetId,
                effect.PresetClass,
                trigger == AnimationTrigger.OnClick ? P.TimeNodeValues.ClickEffect : P.TimeNodeValues.AfterEffect,
                ResolveAnimationDurationMilliseconds(planned.Arguments),
                effect.Filter,
                nextGroupId++,
                trigger == AnimationTrigger.OnClick ? "indefinite" : "0",
                effect.PresetSubtype,
                ref nextTimingId,
                ResolveAnimationDelayMilliseconds(planned.Arguments));

            mainSequenceNode.ChildTimeNodeList!.AppendChild(clickGroup);
            buildList ??= timing.BuildList ?? new P.BuildList();
            timing.BuildList = buildList;
            if (!buildList.Elements<P.BuildParagraph>().Any(candidate => string.Equals(candidate.ShapeId?.Value, shapeId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            {
                buildList.AppendChild(new P.BuildParagraph
                {
                    ShapeId = shapeId.ToString(CultureInfo.InvariantCulture),
                    GroupId = new UInt32Value((uint)(nextGroupId - 1)),
                });
            }

            hasExistingAnimations = true;
        }
    }

    private static bool TryResolveAnimationTargetId(DeckAnimationDirective animation, IReadOnlyDictionary<string, uint> renderedTargetIds, out uint shapeId)
    {
        var key = animation.TargetIndex is null
            ? animation.TargetName
            : $"{animation.TargetName}[{animation.TargetIndex}]";
        return renderedTargetIds.TryGetValue(key, out shapeId) || renderedTargetIds.TryGetValue(animation.TargetName, out shapeId);
    }

    private static List<RenderedElement> ApplyGroups(DeckDocSlide slide, List<RenderedElement> renderedElements, Dictionary<string, uint> renderedTargetIds, ref uint nextShapeId)
    {
        var elementsByShapeId = renderedElements.ToDictionary(static element => element.ShapeId);
        var groupedShapeIds = new HashSet<uint>();
        var groupedElements = new List<RenderedElement>();

        foreach (var group in slide.Groups)
        {
            var memberShapeIds = group.Members
                .SelectMany(member => ResolveGroupMemberShapeIds(member, renderedTargetIds))
                .Distinct()
                .Where(shapeId => !groupedShapeIds.Contains(shapeId) && elementsByShapeId.ContainsKey(shapeId))
                .ToArray();
            if (memberShapeIds.Length == 0)
            {
                continue;
            }

            var members = memberShapeIds
                .Select(shapeId => elementsByShapeId[shapeId])
                .OrderBy(static element => element.Layer)
                .ThenBy(static element => element.Sequence)
                .ToArray();
            var groupShapeId = nextShapeId++;
            var groupShape = CreateGroupShape(groupShapeId, group.Name, members.Select(static element => element.Element));
            if (groupShape is null)
            {
                continue;
            }

            foreach (var shapeId in memberShapeIds)
            {
                groupedShapeIds.Add(shapeId);
            }

            var groupedElement = new RenderedElement(
                members.Min(static element => element.Layer),
                members.Min(static element => element.Sequence),
                groupShapeId,
                groupShape);
            elementsByShapeId[groupShapeId] = groupedElement;
            renderedTargetIds[group.Name] = groupShapeId;
            groupedElements.Add(groupedElement);
        }

        if (groupedElements.Count == 0)
        {
            return renderedElements;
        }

        return renderedElements
            .Where(element => !groupedShapeIds.Contains(element.ShapeId))
            .Concat(groupedElements)
            .ToList();
    }

    private static IEnumerable<uint> ResolveGroupMemberShapeIds(string member, Dictionary<string, uint> renderedTargetIds)
    {
        if (renderedTargetIds.TryGetValue(member, out var exactShapeId))
        {
            yield return exactShapeId;
            yield break;
        }

        var prefix = member + "[";
        foreach (var pair in renderedTargetIds)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return pair.Value;
            }
        }
    }

    private static P.GroupShape? CreateGroupShape(uint groupShapeId, string groupName, IEnumerable<OpenXmlElement> members)
    {
        var memberArray = members.ToArray();
        var memberBounds = memberArray
            .Select(TryGetElementBounds)
            .Where(static bounds => bounds is not null)
            .Select(static bounds => bounds!.Value)
            .ToArray();
        if (memberArray.Length == 0 || memberBounds.Length == 0)
        {
            return null;
        }

        var left = memberBounds.Min(static bounds => bounds.X);
        var top = memberBounds.Min(static bounds => bounds.Y);
        var right = memberBounds.Max(static bounds => bounds.X + bounds.Width);
        var bottom = memberBounds.Max(static bounds => bounds.Y + bounds.Height);

        var groupShape = new P.GroupShape(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = groupShapeId, Name = groupName },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(
                new A.TransformGroup(
                    new A.Offset { X = left, Y = top },
                    new A.Extents { Cx = right - left, Cy = bottom - top },
                    new A.ChildOffset { X = left, Y = top },
                    new A.ChildExtents { Cx = right - left, Cy = bottom - top })));

        foreach (var member in memberArray)
        {
            groupShape.Append(member);
        }

        return groupShape;
    }

    private static EmuRect? TryGetElementBounds(OpenXmlElement element) =>
        element switch
        {
            P.Shape shape => TryGetTransformBounds(shape.ShapeProperties?.Transform2D),
            P.Picture picture => TryGetTransformBounds(picture.ShapeProperties?.Transform2D),
            P.GraphicFrame frame when frame.Transform?.Offset is not null && frame.Transform.Extents is not null =>
                new EmuRect(
                    frame.Transform.Offset.X?.Value ?? 0L,
                    frame.Transform.Offset.Y?.Value ?? 0L,
                    frame.Transform.Extents.Cx?.Value ?? 0L,
                    frame.Transform.Extents.Cy?.Value ?? 0L),
            P.GroupShape groupShape => TryGetTransformBounds(groupShape.GroupShapeProperties?.TransformGroup),
            _ => null,
        };

    private static EmuRect? TryGetTransformBounds(A.Transform2D? transform) =>
        transform?.Offset is not null && transform.Extents is not null
            ? new EmuRect(
                transform.Offset.X?.Value ?? 0L,
                transform.Offset.Y?.Value ?? 0L,
                transform.Extents.Cx?.Value ?? 0L,
                transform.Extents.Cy?.Value ?? 0L)
            : null;

    private static EmuRect? TryGetTransformBounds(A.TransformGroup? transform) =>
        transform?.Offset is not null && transform.Extents is not null
            ? new EmuRect(
                transform.Offset.X?.Value ?? 0L,
                transform.Offset.Y?.Value ?? 0L,
                transform.Extents.Cx?.Value ?? 0L,
                transform.Extents.Cy?.Value ?? 0L)
            : null;

    private static AnimationEffect? ResolveAnimationEffect(DeckDirectiveArguments arguments)
    {
        if (arguments.GetValue("enter") is string enter && !string.IsNullOrWhiteSpace(enter))
        {
            return CreateAnimationEffect(enter.Trim(), P.TimeNodePresetClassValues.Entrance, direction: arguments.GetValue("dir"));
        }

        if (arguments.GetValue("exit") is string exit && !string.IsNullOrWhiteSpace(exit))
        {
            return CreateAnimationEffect(exit.Trim(), P.TimeNodePresetClassValues.Exit, direction: arguments.GetValue("dir"));
        }

        if (arguments.GetValue("emphasis") is string emphasis && !string.IsNullOrWhiteSpace(emphasis))
        {
            var mapped = emphasis.Trim().ToLowerInvariant() switch
            {
                "pulse" => "grow",
                "color" => "fade",
                _ => emphasis.Trim(),
            };
            return CreateAnimationEffect(mapped, P.TimeNodePresetClassValues.Emphasis, direction: null);
        }

        if (arguments.GetValue("motion") is string motion && !string.IsNullOrWhiteSpace(motion))
        {
            var normalizedMotion = motion.Trim().ToLowerInvariant();
            var direction = normalizedMotion switch
            {
                "from-left" => "left",
                "from-right" => "right",
                "from-top" => "up",
                "from-bottom" => "down",
                _ => null,
            };

            if (direction is not null)
            {
                return CreateAnimationEffect("fly", P.TimeNodePresetClassValues.Entrance, direction);
            }
        }

        return null;
    }

    private static AnimationEffect? CreateAnimationEffect(string effectName, P.TimeNodePresetClassValues presetClass, string? direction)
    {
        var normalizedEffect = effectName.Trim().ToLowerInvariant();
        (int PresetId, string? Filter)? preset;
        if (presetClass == P.TimeNodePresetClassValues.Entrance || presetClass == P.TimeNodePresetClassValues.Exit)
        {
            preset = normalizedEffect switch
            {
                "appear" => (1, null),
                "fade" => (10, "fade"),
                "wipe" => (20, "wipe(left)"),
                "zoom" => (21, null),
                "fly" => (2, null),
                _ => null,
            };
        }
        else if (presetClass == P.TimeNodePresetClassValues.Emphasis)
        {
            preset = normalizedEffect switch
            {
                "grow" => (26, null),
                "spin" => (27, null),
                "fade" => (10, "fade"),
                _ => null,
            };
        }
        else
        {
            preset = null;
        }

        if (preset is null)
        {
            return null;
        }

        return new AnimationEffect(
            preset.Value.PresetId,
            presetClass,
            preset.Value.Filter,
            ResolveAnimationSubtype(normalizedEffect, direction));
    }

    private static int ResolveAnimationSubtype(string effectName, string? direction)
    {
        if (!string.IsNullOrWhiteSpace(direction))
        {
            return direction.Trim().ToLowerInvariant() switch
            {
                "left" => 8,
                "right" => 2,
                "up" or "top" => 1,
                "down" or "bottom" => 4,
                _ => 0,
            };
        }

        return effectName switch
        {
            "fly" => 4,
            "wipe" => 1,
            _ => 0,
        };
    }

    private static int ResolveAnimationOrder(DeckDirectiveArguments arguments)
    {
        if (arguments.GetValue("order") is string value && int.TryParse(value, out var order))
        {
            return order;
        }

        return int.MaxValue;
    }

    private static int ResolveAnimationDurationMilliseconds(DeckDirectiveArguments arguments) =>
        ParseTimeMilliseconds(arguments.GetValue("dur"), defaultValue: 400);

    private static int ResolveAnimationDelayMilliseconds(DeckDirectiveArguments arguments) =>
        ParseTimeMilliseconds(arguments.GetValue("delay"), defaultValue: 0);

    private static int ParseTimeMilliseconds(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var candidate = value.Trim();
        if (candidate.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            return (int)Math.Round(ParseDouble(candidate[..^2]), MidpointRounding.AwayFromZero);
        }

        if (candidate.EndsWith('s'))
        {
            return (int)Math.Round(ParseDouble(candidate[..^1]) * 1000d, MidpointRounding.AwayFromZero);
        }

        return int.TryParse(candidate, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static AnimationTrigger ResolveAnimationTrigger(DeckDirectiveArguments arguments, bool hasExistingAnimations)
    {
        var on = arguments.GetValue("on")?.Trim().ToLowerInvariant();
        return on switch
        {
            "click" => AnimationTrigger.OnClick,
            "auto" => AnimationTrigger.AfterPrevious,
            not null when on.StartsWith("after(", StringComparison.Ordinal) => AnimationTrigger.AfterPrevious,
            _ => hasExistingAnimations ? AnimationTrigger.AfterPrevious : AnimationTrigger.OnClick,
        };
    }

    private static void EnsureTimingTree(P.Slide slideDocument, out P.CommonTimeNode mainSequenceNode, out P.BuildList? buildList)
    {
        var timing = slideDocument.GetFirstChild<P.Timing>();
        if (timing is null)
        {
            timing = new P.Timing();
            slideDocument.Append(timing);
        }

        var timeNodeList = timing.TimeNodeList;
        if (timeNodeList is null)
        {
            timeNodeList = new P.TimeNodeList();
            timing.TimeNodeList = timeNodeList;
        }

        var rootParallelNode = timeNodeList.GetFirstChild<P.ParallelTimeNode>();
        if (rootParallelNode is null)
        {
            rootParallelNode = new P.ParallelTimeNode();
            timeNodeList.Append(rootParallelNode);
        }

        var rootCommonNode = rootParallelNode.CommonTimeNode;
        if (rootCommonNode is null)
        {
            rootCommonNode = new P.CommonTimeNode
            {
                Id = 1U,
                Duration = "indefinite",
                Restart = P.TimeNodeRestartValues.Never,
                NodeType = P.TimeNodeValues.TmingRoot,
            };
            rootParallelNode.CommonTimeNode = rootCommonNode;
        }

        rootCommonNode.ChildTimeNodeList ??= new P.ChildTimeNodeList();

        var sequenceNode = rootCommonNode.ChildTimeNodeList.GetFirstChild<P.SequenceTimeNode>();
        if (sequenceNode is null)
        {
            sequenceNode = new P.SequenceTimeNode
            {
                Concurrent = true,
                NextAction = P.NextActionValues.Seek,
            };
            rootCommonNode.ChildTimeNodeList.AppendChild(sequenceNode);
            sequenceNode.CommonTimeNode = new P.CommonTimeNode
            {
                Id = 2U,
                Duration = "indefinite",
                NodeType = P.TimeNodeValues.MainSequence,
                ChildTimeNodeList = new P.ChildTimeNodeList(),
            };

            sequenceNode.PreviousConditionList = new P.PreviousConditionList(
                new P.Condition
                {
                    Event = P.TriggerEventValues.OnPrevious,
                    Delay = "0",
                    TargetElement = new P.TargetElement(new P.SlideTarget()),
                });
            sequenceNode.NextConditionList = new P.NextConditionList(
                new P.Condition
                {
                    Event = P.TriggerEventValues.OnNext,
                    Delay = "0",
                    TargetElement = new P.TargetElement(new P.SlideTarget()),
                });
        }

        mainSequenceNode = sequenceNode.CommonTimeNode!;
        mainSequenceNode.ChildTimeNodeList ??= new P.ChildTimeNodeList();
        buildList = timing.BuildList;
    }

    private static P.ParallelTimeNode BuildClickGroup(string shapeId, int presetId, P.TimeNodePresetClassValues presetClass, P.TimeNodeValues nodeType, int durationMs, string? filter, int groupId, string outerDelay, int presetSubtype, ref uint nextTimingId, int delayMs)
    {
        var isEntrance = presetClass == P.TimeNodePresetClassValues.Entrance;
        var isEmphasis = presetClass == P.TimeNodePresetClassValues.Emphasis;
        var transition = isEntrance || isEmphasis ? P.AnimateEffectTransitionValues.In : P.AnimateEffectTransitionValues.Out;

        var effectId = nextTimingId++;
        var setVisibilityId = nextTimingId++;
        var animationEffectId = nextTimingId++;
        var effectChildren = new P.ChildTimeNodeList();
        effectChildren.AppendChild(new P.SetBehavior(
            new P.CommonBehavior(
                new P.CommonTimeNode
                {
                    Id = setVisibilityId,
                    Duration = "1",
                    Fill = P.TimeNodeFillValues.Hold,
                    StartConditionList = new P.StartConditionList(new P.Condition { Delay = "0" }),
                },
                new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId }),
                new P.AttributeNameList(new P.AttributeName("style.visibility"))),
            new P.ToVariantValue(new P.StringVariantValue { Val = isEntrance || isEmphasis ? "visible" : "hidden" })));

        if (presetId == 2)
        {
            BuildFlyAnimation(effectChildren, shapeId, durationMs, presetSubtype, isEntrance, ref nextTimingId);
        }
        else if (presetId == 21)
        {
            var animateScale = new P.AnimateScale
            {
                ZoomContents = true,
                CommonBehavior = new P.CommonBehavior(
                        new P.CommonTimeNode { Id = animationEffectId, Duration = durationMs.ToString(CultureInfo.InvariantCulture), Fill = P.TimeNodeFillValues.Hold },
                    new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId }))
            };
            if (isEntrance)
            {
                animateScale.FromPosition = new P.FromPosition { X = 0, Y = 0 };
                animateScale.ToPosition = new P.ToPosition { X = 100000, Y = 100000 };
            }
            else
            {
                animateScale.FromPosition = new P.FromPosition { X = 100000, Y = 100000 };
                animateScale.ToPosition = new P.ToPosition { X = 0, Y = 0 };
            }

            effectChildren.AppendChild(animateScale);
        }
        else if (presetId == 27)
        {
            effectChildren.AppendChild(new P.AnimateRotation
            {
                By = 21600000,
                CommonBehavior = new P.CommonBehavior(
                        new P.CommonTimeNode { Id = animationEffectId, Duration = durationMs.ToString(CultureInfo.InvariantCulture), Fill = P.TimeNodeFillValues.Hold },
                    new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId }))
            });
        }
        else if (presetId == 26)
        {
            effectChildren.AppendChild(new P.AnimateScale
            {
                ZoomContents = true,
                CommonBehavior = new P.CommonBehavior(
                    new P.CommonTimeNode { Id = animationEffectId, Duration = durationMs.ToString(CultureInfo.InvariantCulture), Fill = P.TimeNodeFillValues.Hold },
                    new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId })),
                ByPosition = new P.ByPosition { X = 125000, Y = 125000 },
            });
        }
        else if (filter is not null)
        {
            effectChildren.AppendChild(new P.AnimateEffect
            {
                Transition = transition,
                Filter = filter,
                CommonBehavior = new P.CommonBehavior(
                        new P.CommonTimeNode { Id = animationEffectId, Duration = durationMs.ToString(CultureInfo.InvariantCulture) },
                    new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId }))
            });
        }

        var effectNode = new P.CommonTimeNode
        {
            Id = effectId,
            PresetId = presetId,
            PresetClass = presetClass,
            PresetSubtype = presetSubtype,
            Fill = P.TimeNodeFillValues.Hold,
            GroupId = (uint)groupId,
            NodeType = nodeType,
            StartConditionList = new P.StartConditionList(new P.Condition { Delay = "0" }),
            ChildTimeNodeList = effectChildren,
        };

        var midNode = new P.CommonTimeNode
        {
            Id = nextTimingId++,
            Fill = P.TimeNodeFillValues.Hold,
            StartConditionList = new P.StartConditionList(new P.Condition { Delay = delayMs > 0 ? delayMs.ToString(CultureInfo.InvariantCulture) : "0" }),
            ChildTimeNodeList = new P.ChildTimeNodeList(new P.ParallelTimeNode { CommonTimeNode = effectNode }),
        };

        return new P.ParallelTimeNode
        {
            CommonTimeNode = new P.CommonTimeNode
            {
                Id = nextTimingId++,
                Fill = P.TimeNodeFillValues.Hold,
                StartConditionList = new P.StartConditionList(new P.Condition { Delay = outerDelay }),
                ChildTimeNodeList = new P.ChildTimeNodeList(new P.ParallelTimeNode { CommonTimeNode = midNode }),
            }
        };
    }

    private static void BuildFlyAnimation(P.ChildTimeNodeList effectChildren, string shapeId, int durationMs, int presetSubtype, bool isEntrance, ref uint nextTimingId)
    {
        var (attributeName, offscreenFormula, onscreenFormula) = presetSubtype switch
        {
            8 => ("ppt_x", "0-#ppt_w/2", "#ppt_x"),
            2 => ("ppt_x", "1+#ppt_w/2", "#ppt_x"),
            1 => ("ppt_y", "0-#ppt_h/2", "#ppt_y"),
            _ => ("ppt_y", "1+#ppt_h/2", "#ppt_y"),
        };

        var startValue = isEntrance ? offscreenFormula : onscreenFormula;
        var endValue = isEntrance ? onscreenFormula : offscreenFormula;
        effectChildren.AppendChild(new P.Animate
        {
            CalculationMode = P.AnimateBehaviorCalculateModeValues.Linear,
            ValueType = P.AnimateBehaviorValues.Number,
            CommonBehavior = new P.CommonBehavior(
                new P.CommonTimeNode { Id = nextTimingId++, Duration = durationMs.ToString(CultureInfo.InvariantCulture), Fill = P.TimeNodeFillValues.Hold },
                new P.TargetElement(new P.ShapeTarget { ShapeId = shapeId }),
                new P.AttributeNameList(new P.AttributeName(attributeName)))
            {
                Additive = P.BehaviorAdditiveValues.Base,
            },
            TimeAnimateValueList = new P.TimeAnimateValueList(
                new P.TimeAnimateValue
                {
                    Time = "0",
                    VariantValue = new P.VariantValue(new P.StringVariantValue { Val = startValue }),
                },
                new P.TimeAnimateValue
                {
                    Time = "100000",
                    VariantValue = new P.VariantValue(new P.StringVariantValue { Val = endValue }),
                })
        });
    }

    private static uint GetMaxTimingId(P.Timing timing)
    {
        uint max = 1;
        foreach (var node in timing.Descendants<P.CommonTimeNode>())
        {
            if (node.Id?.Value > max)
            {
                max = node.Id.Value;
            }
        }

        return max;
    }

    private static int GetMaxGroupId(P.Timing timing)
    {
        var max = -1;
        foreach (var node in timing.Descendants<P.CommonTimeNode>())
        {
            var groupId = (int?)node.GroupId?.Value;
            if (groupId.HasValue && groupId.Value > max)
            {
                max = groupId.Value;
            }
        }

        return max + 1;
    }

    private enum AnimationTrigger
    {
        OnClick,
        AfterPrevious,
    }

    private readonly record struct AnimationEffect(int PresetId, P.TimeNodePresetClassValues PresetClass, string? Filter, int PresetSubtype);

    private readonly record struct PlannedAnimation(DeckAnimationDirective Animation, DeckDirectiveArguments Arguments);

    private readonly record struct ResolvedAnimation(DeckAnimationDirective Animation, DeckDirectiveArguments Arguments, AnimationEffect? Effect, uint? ShapeId);

    private static DeckGridSize ResolveGrid(DeckDocDocument document, DeckDocLayout? layout)
    {
        if (layout?.GridOverride is DeckGridSize layoutGrid)
        {
            return layoutGrid;
        }

        if (document.Attributes.TryGetValue("grid", out var gridText) && !string.IsNullOrWhiteSpace(gridText))
        {
            return ParseGridSize(gridText);
        }

        return new DeckGridSize(32, 18);
    }

    private static Dictionary<string, ResolvedTarget> ResolveTargets(DeckDocDocument document, DeckDocLayout? layout, DeckDocSlide slide, DeckGridSize grid)
    {
        var targets = new Dictionary<string, ResolvedTarget>(StringComparer.OrdinalIgnoreCase);

        if (layout is not null)
        {
            foreach (var target in layout.Targets)
            {
                ApplyTargetDefinition(targets, target, grid);
            }

            var resolvedSlots = new List<ResolvedLayoutSlot>();
            foreach (var slot in layout.Slots)
            {
                if (!TryResolvePlacementRect(slot.Placement, grid, targets, out var rect, out var inheritedArguments))
                {
                    if (TryResolveRepeatedSlotTargets(document, targets, slot, out var repeatedTargets))
                    {
                        foreach (var repeatedTarget in repeatedTargets)
                        {
                            targets[repeatedTarget.Name] = repeatedTarget.Target;
                        }
                    }

                    continue;
                }

                resolvedSlots.Add(new ResolvedLayoutSlot(
                    slot,
                    rect,
                    inheritedArguments,
                    GetSharedSlotSourceKey(slot.Placement)));
            }

            foreach (var sharedGroup in resolvedSlots
                .Where(static slot => slot.SharedSourceKey is not null)
                .GroupBy(static slot => slot.SharedSourceKey!, StringComparer.OrdinalIgnoreCase))
            {
                var slots = sharedGroup.ToArray();
                if (slots.Length == 1)
                {
                    continue;
                }

                foreach (var resolvedSlot in ResolveSharedSlotTargets(document, slots))
                {
                    targets[resolvedSlot.Name] = resolvedSlot.Target;
                }
            }

            var multiSlotSourceKeys = resolvedSlots
                .Where(static slot => slot.SharedSourceKey is not null)
                .GroupBy(static slot => slot.SharedSourceKey!, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var resolvedSlot in resolvedSlots)
            {
                if (resolvedSlot.SharedSourceKey is not null && multiSlotSourceKeys.Contains(resolvedSlot.SharedSourceKey))
                {
                    continue;
                }

                targets[resolvedSlot.Slot.Name] = new ResolvedTarget(
                    resolvedSlot.Rect,
                    MergeArguments(document, resolvedSlot.InheritedArguments, resolvedSlot.Slot.Arguments));
            }
        }

        foreach (var target in slide.Targets)
        {
            ApplyTargetDefinition(targets, target, grid);
        }

        return targets;
    }

    private static bool TryResolveRepeatedSlotTargets(DeckDocDocument document, Dictionary<string, ResolvedTarget> targets, DeckSlotDefinition slot, out IReadOnlyList<NamedResolvedTarget> repeatedTargets)
    {
        repeatedTargets = Array.Empty<NamedResolvedTarget>();
        if (slot.Placement.Mode != DeckObjectAddressingMode.Target || string.IsNullOrWhiteSpace(slot.Placement.TargetName))
        {
            return false;
        }

        var prefix = slot.Placement.TargetName + "[";
        var matches = new List<NamedResolvedTarget>();
        foreach (var pair in targets)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = pair.Key[slot.Placement.TargetName.Length..];
            matches.Add(new NamedResolvedTarget(
                slot.Name + suffix,
                new ResolvedTarget(pair.Value.Rect, MergeArguments(document, pair.Value.Arguments, slot.Arguments))));
        }

        repeatedTargets = matches;
        return matches.Count > 0;
    }

    private static void MaterializeIndexedSlotTargets(DeckDocDocument document, DeckDocLayout layout, DeckDocSlide slide, Dictionary<string, ResolvedTarget> targets)
    {
        foreach (var placement in slide.Objects.Select(static item => item.Placement).Concat(slide.ObjectOverrides.Select(static item => item.Placement)))
        {
            if (placement.Mode != DeckObjectAddressingMode.Target || placement.TargetName is null || placement.TargetIndex is null)
            {
                continue;
            }

            var targetKey = $"{placement.TargetName}[{placement.TargetIndex}]";
            if (targets.ContainsKey(targetKey))
            {
                continue;
            }

            var slot = layout.Slots.FirstOrDefault(candidate => string.Equals(candidate.Name, placement.TargetName, StringComparison.OrdinalIgnoreCase));
            if (slot?.Placement.TargetName is null)
            {
                continue;
            }

            var sourceTargetKey = $"{slot.Placement.TargetName}[{placement.TargetIndex}]";
            if (!targets.TryGetValue(sourceTargetKey, out var sourceTarget))
            {
                continue;
            }

            targets[targetKey] = new ResolvedTarget(sourceTarget.Rect, MergeArguments(document, sourceTarget.Arguments, slot.Arguments));
        }
    }

    private static string? GetSharedSlotSourceKey(DeckObjectPlacement placement)
    {
        if (placement.Mode == DeckObjectAddressingMode.Target && !string.IsNullOrWhiteSpace(placement.TargetName))
        {
            return placement.TargetIndex is null
                ? placement.TargetName
                : $"{placement.TargetName}[{placement.TargetIndex}]";
        }

        if (placement.Mode == DeckObjectAddressingMode.Geometry && placement.Anchor is DeckGridAnchor anchor && placement.Span is DeckGridSize span)
        {
            return $"{FormatAnchorReference(anchor)}:{span.Width.ToString(CultureInfo.InvariantCulture)}x{span.Height.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    private static IReadOnlyList<NamedResolvedTarget> ResolveSharedSlotTargets(DeckDocDocument document, IReadOnlyList<ResolvedLayoutSlot> slots)
    {
        if (slots.Count == 0)
        {
            return Array.Empty<NamedResolvedTarget>();
        }

        const double gap = 0.4;
        const double minFlexibleHeight = 1.4;

        var availableHeight = Math.Max(0.5, slots[0].Rect.Height - (gap * Math.Max(0, slots.Count - 1)));
        var plannedHeights = new double[slots.Count];
        var flexibleIndexes = new List<int>();
        double fixedHeightTotal = 0;

        for (var index = 0; index < slots.Count; index++)
        {
            var mergedArguments = MergeArguments(document, slots[index].InheritedArguments, slots[index].Slot.Arguments);
            var preferredHeight = GetPreferredSharedSlotHeight(mergedArguments, slots[index].Slot.Name);
            if (preferredHeight is double value)
            {
                plannedHeights[index] = value;
                fixedHeightTotal += value;
            }
            else
            {
                flexibleIndexes.Add(index);
            }
        }

        if (flexibleIndexes.Count == 0)
        {
            var scale = fixedHeightTotal > 0 ? availableHeight / fixedHeightTotal : 1;
            for (var index = 0; index < plannedHeights.Length; index++)
            {
                plannedHeights[index] *= scale;
            }
        }
        else
        {
            var maximumFixedHeight = Math.Max(0, availableHeight - (flexibleIndexes.Count * minFlexibleHeight));
            if (fixedHeightTotal > maximumFixedHeight && fixedHeightTotal > 0)
            {
                var scale = maximumFixedHeight / fixedHeightTotal;
                for (var index = 0; index < plannedHeights.Length; index++)
                {
                    if (plannedHeights[index] > 0)
                    {
                        plannedHeights[index] *= scale;
                    }
                }

                fixedHeightTotal = plannedHeights.Sum();
            }

            var flexibleHeight = Math.Max(minFlexibleHeight, (availableHeight - fixedHeightTotal) / flexibleIndexes.Count);
            foreach (var flexibleIndex in flexibleIndexes)
            {
                plannedHeights[flexibleIndex] = flexibleHeight;
            }
        }

        var resolvedTargets = new List<NamedResolvedTarget>(slots.Count);
        var cursor = slots[0].Rect.Top;
        for (var index = 0; index < slots.Count; index++)
        {
            var resolvedRect = new DeckGridRect(
                slots[0].Rect.Left,
                cursor,
                slots[0].Rect.Width,
                plannedHeights[index]);
            resolvedTargets.Add(new NamedResolvedTarget(
                slots[index].Slot.Name,
                new ResolvedTarget(
                    resolvedRect,
                    MergeArguments(document, slots[index].InheritedArguments, slots[index].Slot.Arguments))));
            cursor += plannedHeights[index] + gap;
        }

        return resolvedTargets;
    }

    private static double? GetPreferredSharedSlotHeight(DeckDirectiveArguments arguments, string slotName)
    {
        var role = InferRole(arguments, slotName);
        return role switch
        {
            "title" => 3.2,
            "subtitle" => 2.1,
            "caption" => 1.2,
            _ => null,
        };
    }

    private static void ApplyTargetDefinition(Dictionary<string, ResolvedTarget> targets, DeckLayoutTargetDefinition definition, DeckGridSize grid)
    {
        switch (definition)
        {
            case DeckAreaTargetDefinition area:
                targets[area.Name] = new ResolvedTarget(ToRect(area.Range), area.Arguments.Clone());
                break;

            case DeckSplitTargetDefinition split:
                ApplySplit(targets, split, grid);
                break;

            case DeckGridTargetDefinition repeatedGrid:
                ApplyGrid(targets, repeatedGrid, grid);
                break;

            case DeckStackTargetDefinition stack:
                ApplyStack(targets, stack, grid);
                break;
        }
    }

    private static void ApplySplit(Dictionary<string, ResolvedTarget> targets, DeckSplitTargetDefinition split, DeckGridSize grid)
    {
        var source = ResolveSourceRect(split.Source, targets, grid);
        if (source is null)
        {
            return;
        }

        var available = split.IsRows ? source.Value.Height : source.Value.Width;
        var gapBudget = split.Gap * Math.Max(0, split.OutputNames.Count - 1);
        var lengths = ResolveSplitLengths(split.Parts, available, gapBudget);
        var cursor = split.IsRows ? source.Value.Top : source.Value.Left;

        for (var index = 0; index < split.OutputNames.Count && index < lengths.Count; index++)
        {
            DeckGridRect rect;
            if (split.IsRows)
            {
                rect = new DeckGridRect(source.Value.Left, cursor, source.Value.Width, lengths[index]);
                cursor += lengths[index] + split.Gap;
            }
            else
            {
                rect = new DeckGridRect(cursor, source.Value.Top, lengths[index], source.Value.Height);
                cursor += lengths[index] + split.Gap;
            }

            targets[split.OutputNames[index]] = new ResolvedTarget(rect, new DeckDirectiveArguments());
        }
    }

    private static void ApplyGrid(Dictionary<string, ResolvedTarget> targets, DeckGridTargetDefinition definition, DeckGridSize grid)
    {
        var source = ResolveSourceRect(definition.Source, targets, grid);
        if (source is null)
        {
            return;
        }

        var rowCount = definition.Rows.GetValueOrDefault(1);
        var cellWidth = (source.Value.Width - (definition.HorizontalGap * Math.Max(0, definition.Columns - 1))) / Math.Max(1, definition.Columns);
        var cellHeight = (source.Value.Height - (definition.VerticalGap * Math.Max(0, rowCount - 1))) / Math.Max(1, rowCount);
        var ordinal = 1;

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < definition.Columns; column++)
            {
                var rect = new DeckGridRect(
                    source.Value.Left + (column * (cellWidth + definition.HorizontalGap)),
                    source.Value.Top + (row * (cellHeight + definition.VerticalGap)),
                    cellWidth,
                    cellHeight);
                var defaultArgs = definition.Arguments.Clone();
                targets[$"{definition.Name}[{ordinal}]" ] = new ResolvedTarget(rect, defaultArgs);
                targets[$"{definition.Name}[r{row + 1}c{column + 1}]" ] = new ResolvedTarget(rect, defaultArgs.Clone());
                ordinal++;
            }
        }
    }

    private static void ApplyStack(Dictionary<string, ResolvedTarget> targets, DeckStackTargetDefinition definition, DeckGridSize grid)
    {
        var source = ResolveSourceRect(definition.Source, targets, grid);
        if (source is null)
        {
            return;
        }

        var isVertical = string.Equals(definition.Direction, "down", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Direction, "up", StringComparison.OrdinalIgnoreCase);
        var itemSize = isVertical
            ? (source.Value.Height - (definition.Gap * Math.Max(0, definition.Count - 1))) / Math.Max(1, definition.Count)
            : (source.Value.Width - (definition.Gap * Math.Max(0, definition.Count - 1))) / Math.Max(1, definition.Count);

        for (var index = 0; index < definition.Count; index++)
        {
            var rect = isVertical
                ? new DeckGridRect(source.Value.Left, source.Value.Top + (index * (itemSize + definition.Gap)), source.Value.Width, itemSize)
                : new DeckGridRect(source.Value.Left + (index * (itemSize + definition.Gap)), source.Value.Top, itemSize, source.Value.Height);
            targets[$"{definition.Name}[{index + 1}]" ] = new ResolvedTarget(rect, definition.Arguments.Clone());
        }
    }

    private static IReadOnlyList<double> ResolveSplitLengths(IReadOnlyList<string> parts, double available, double gapBudget)
    {
        var normalizedAvailable = Math.Max(0, available - gapBudget);
        var lengths = new List<double>(parts.Count);
        var parsed = parts.Select(ParseSplitPart).ToArray();
        var totalAbsolute = parsed.Where(static part => part.Kind == SplitPartKind.Absolute).Sum(static part => part.Value);
        var totalPercent = parsed.Where(static part => part.Kind == SplitPartKind.Percent).Sum(static part => part.Value);
        var totalFraction = parsed.Where(static part => part.Kind == SplitPartKind.Fraction).Sum(static part => part.Value);

        if (totalPercent == 0 && totalFraction == 0)
        {
            if (Math.Abs(totalAbsolute - available) <= 0.01)
            {
                return parsed.Select(static part => part.Value).ToArray();
            }

            var scale = totalAbsolute > 0 ? normalizedAvailable / totalAbsolute : 1D;
            return parsed.Select(part => part.Value * scale).ToArray();
        }

        var remaining = Math.Max(0, normalizedAvailable - totalAbsolute - (normalizedAvailable * (totalPercent / 100D)));
        foreach (var part in parsed)
        {
            lengths.Add(part.Kind switch
            {
                SplitPartKind.Absolute => part.Value,
                SplitPartKind.Percent => normalizedAvailable * (part.Value / 100D),
                _ => totalFraction > 0 ? remaining * (part.Value / totalFraction) : 0D,
            });
        }

        return lengths;
    }

    private static SplitPart ParseSplitPart(string value)
    {
        if (value.EndsWith('%'))
        {
            return new SplitPart(ParseDouble(value[..^1]), SplitPartKind.Percent);
        }

        if (value.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
        {
            return new SplitPart(ParseDouble(value[..^2]), SplitPartKind.Fraction);
        }

        return new SplitPart(ParseDouble(value), SplitPartKind.Absolute);
    }

    private static double ParseDouble(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0D;

    private static DeckGridRect? ResolveSourceRect(string source, Dictionary<string, ResolvedTarget> targets, DeckGridSize grid)
    {
        if (string.Equals(source, "__grid", StringComparison.Ordinal))
        {
            return new DeckGridRect(0, 0, grid.Width, grid.Height);
        }

        if (source.Contains(':', StringComparison.Ordinal))
        {
            var range = ParseRange(source);
            return ToRect(range);
        }

        return targets.TryGetValue(source, out var target) ? target.Rect : GetFallbackTargetRect(source, grid);
    }

    private static bool TryResolvePlacementRect(
        DeckObjectPlacement placement,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets,
        out DeckGridRect rect,
        out DeckDirectiveArguments inheritedArguments)
    {
        inheritedArguments = new DeckDirectiveArguments();

        if (placement.Mode == DeckObjectAddressingMode.Geometry && placement.Anchor is DeckGridAnchor anchor && placement.Span is DeckGridSize span)
        {
            rect = new DeckGridRect(anchor.ColumnNumber - 1, anchor.RowNumber - 1, span.Width, span.Height);
            return true;
        }

        if (placement.TargetName is null)
        {
            rect = default;
            return false;
        }

        var targetKey = placement.TargetIndex is null ? placement.TargetName : $"{placement.TargetName}[{placement.TargetIndex}]";
        if (targets.TryGetValue(targetKey, out var resolved))
        {
            rect = resolved.Rect;
            inheritedArguments = resolved.Arguments.Clone();
            return true;
        }

        if (placement.TargetIndex is null && GetFallbackTargetRect(placement.TargetName, grid) is DeckGridRect fallbackRect)
        {
            rect = fallbackRect;
            return true;
        }

        rect = default;
        return false;
    }

    private static bool TryCreateObjectElement(
        SlidePart slidePart,
        DeckDocDocument document,
        DeckObjectPlacement placement,
        DeckDirectiveArguments objectArguments,
        IReadOnlyList<string> payloadSegments,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets,
        IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages,
        uint shapeId,
        int lineNumber,
        out OpenXmlElement element,
        out int layer,
        out bool isVisibleTitle)
    {
        if (!TryResolvePlacementRect(placement, grid, targets, out var rect, out var inheritedArguments))
        {
            element = null!;
            layer = 0;
            isVisibleTitle = false;
            return false;
        }

        var arguments = MergeArguments(document, inheritedArguments, objectArguments);
        var kind = ResolveObjectKind(arguments, payloadSegments);
        var name = arguments.GetValue("name") ?? BuildElementName(kind, placement, shapeId, lineNumber);
        layer = ResolveLayer(arguments);
        isVisibleTitle = string.Equals(placement.TargetName, "title", StringComparison.OrdinalIgnoreCase)
            || arguments.Roles.Any(static role => string.Equals(role, "title", StringComparison.OrdinalIgnoreCase));
        var defaultRole = InferRole(arguments, placement.TargetName);
        var textColor = ResolveTextColor(document, arguments, defaultRole);

        switch (kind)
        {
            case "image":
            case "icon":
                if (TryResolveImage(arguments, resolvedImages, out var image))
                {
                    element = CreatePicture(slidePart, shapeId, arguments.GetValue("alt") ?? name, ToEmuRect(rect, grid), image, arguments);
                    return true;
                }

                element = CreateTextShape(shapeId, name, ToEmuRect(rect, grid), [arguments.GetValue("alt") ?? name], ResolveDefaultFontSize("caption"), false, null, null, ResolveColor(document, "#5B6472"), A.ShapeTypeValues.Rectangle);
                return true;

            case "shape":
                element = CreateTextShape(
                    shapeId,
                    name,
                    ToEmuRect(rect, grid),
                    payloadSegments.Count > 0 ? [string.Join('\n', payloadSegments)] : Array.Empty<string>(),
                    ResolveFontSize(arguments, defaultRole),
                    arguments.HasToken("bold"),
                    ResolveColor(document, arguments.GetValue("fill")),
                    ResolveColor(document, arguments.GetValue("stroke")),
                    textColor,
                    ResolveShapeKind(arguments));
                return true;

            case "line":
                element = CreateTextShape(
                    shapeId,
                    name,
                    NormalizeLineRect(ToEmuRect(rect, grid)),
                    Array.Empty<string>(),
                    ResolveDefaultFontSize("body"),
                    false,
                    ResolveColor(document, arguments.GetValue("stroke") ?? arguments.GetValue("fill") ?? "#CBD5E1"),
                    ResolveColor(document, arguments.GetValue("stroke") ?? "#CBD5E1"),
                    null,
                    A.ShapeTypeValues.Line);
                return true;

            case "list":
                element = CreateTextShape(
                    shapeId,
                    name,
                    ToEmuRect(rect, grid),
                    BuildListLines(arguments, payloadSegments),
                    ResolveFontSize(arguments, defaultRole),
                    arguments.HasToken("bold"),
                    ResolveColor(document, arguments.GetValue("fill")),
                    ResolveColor(document, arguments.GetValue("stroke")),
                    textColor,
                    A.ShapeTypeValues.Rectangle);
                return true;

            default:
                element = CreateTextShape(
                    shapeId,
                    name,
                    ToEmuRect(rect, grid),
                    payloadSegments.Count > 0 ? payloadSegments : [placement.TargetName ?? string.Empty],
                    ResolveFontSize(arguments, defaultRole),
                    arguments.HasToken("bold") || isVisibleTitle,
                    ResolveColor(document, arguments.GetValue("fill")),
                    ResolveColor(document, arguments.GetValue("stroke")),
                    textColor,
                    A.ShapeTypeValues.Rectangle);
                return true;
        }
    }

    private static bool TryCreateObjectOverrideElement(
        SlidePart slidePart,
        DeckDocDocument document,
        DeckObjectOverrideDefinition deckObject,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets,
        IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages,
        uint shapeId,
        out OpenXmlElement element,
        out int layer)
    {
        element = null!;
        layer = 0;

        var payloadSegments = ResolveOverridePayload(deckObject);
        if (payloadSegments.Length == 0 && deckObject.Arguments.GetValue("asset") is null && deckObject.Arguments.GetValue("ref") is null)
        {
            return false;
        }

        return TryCreateObjectElement(
            slidePart,
            document,
            deckObject.Placement,
            deckObject.Arguments,
            payloadSegments,
            grid,
            targets,
            resolvedImages,
            shapeId,
            deckObject.LineNumber,
            out element,
            out layer,
            out _);
    }

    private static string[] ResolveOverridePayload(DeckObjectOverrideDefinition deckObject)
    {
        if (!string.IsNullOrWhiteSpace(deckObject.Payload))
        {
            return [deckObject.Payload];
        }

        if (deckObject.Arguments.GetValue("rich") is string richText && !string.IsNullOrWhiteSpace(richText))
        {
            return [StripInlineRichMarkup(richText)];
        }

        if (deckObject.Arguments.GetValue("runs") is string runsText && !string.IsNullOrWhiteSpace(runsText))
        {
            var extractedText = ExtractRunTexts(runsText);
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                return [extractedText];
            }
        }

        return Array.Empty<string>();
    }

    private static string StripInlineRichMarkup(string text)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\[(?:/)?[A-Za-z][^\]]*\]", string.Empty);
        return stripped.Replace("\\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string ExtractRunTexts(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, "(?:'text'|\"text\")\\s*:\\s*(?:'(?<single>(?:\\\\'|[^'])*)'|\"(?<double>(?:\\\\\"|[^\"])*)\")");
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var values = new List<string>(matches.Count);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups["single"].Success)
            {
                values.Add(match.Groups["single"].Value.Replace("\\'", "'", StringComparison.Ordinal));
            }
            else if (match.Groups["double"].Success)
            {
                values.Add(match.Groups["double"].Value.Replace("\\\"", "\"", StringComparison.Ordinal));
            }
        }

        return string.Concat(values).Trim();
    }

    private static bool TryCreateBackground(
        SlidePart slidePart,
        DeckDocDocument document,
        DeckDirectiveArguments background,
        DeckGridSize grid,
        IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages,
        uint shapeId,
        out OpenXmlElement element)
    {
        var fullRect = ToEmuRect(new DeckGridRect(0, 0, grid.Width, grid.Height), grid);
        if (TryResolveImage(background, resolvedImages, out var image))
        {
            element = CreatePicture(slidePart, shapeId, "Background", fullRect, image, background);
            return true;
        }

        var fill = ResolveColor(document, background.GetValue("fill"));
        if (fill is not null)
        {
            element = CreateTextShape(shapeId, "Background", fullRect, Array.Empty<string>(), ResolveDefaultFontSize("body"), false, fill, fill, null, A.ShapeTypeValues.Rectangle);
            return true;
        }

        element = null!;
        return false;
    }

    private static bool TryResolveImage(DeckDirectiveArguments arguments, IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages, out ResolvedDeckImage image)
    {
        if (arguments.GetValue("asset") is string assetName && resolvedImages.TryGetValue(assetName, out image!))
        {
            return true;
        }

        if (arguments.GetValue("ref") is string directReference && resolvedImages.TryGetValue($"ref:{directReference}", out image!))
        {
            return true;
        }

        image = null!;
        return false;
    }

    private static DeckDirectiveArguments MergeArguments(DeckDocDocument document, params DeckDirectiveArguments?[] parts)
    {
        var merged = new DeckDirectiveArguments();
        foreach (var part in parts)
        {
            ApplyArguments(document, merged, part);
        }

        return merged;
    }

    private static void ApplyArguments(DeckDocDocument document, DeckDirectiveArguments destination, DeckDirectiveArguments? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var role in source.Roles)
        {
            if (document.Styles.TryGetValue(role, out var style))
            {
                ApplyArguments(document, destination, style);
            }

            if (!destination.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                destination.Roles.Add(role);
            }
        }

        foreach (var token in source.BareTokens)
        {
            if (!destination.BareTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                destination.BareTokens.Add(token);
            }
        }

        foreach (var pair in source.Values)
        {
            destination.Values[pair.Key] = pair.Value;
        }
    }

    private static string ResolveObjectKind(DeckDirectiveArguments arguments, IReadOnlyList<string> payloadSegments)
    {
        foreach (var candidate in arguments.BareTokens)
        {
            switch (candidate.ToLowerInvariant())
            {
                case "text":
                case "list":
                case "image":
                case "shape":
                case "line":
                case "icon":
                    return candidate.ToLowerInvariant();
            }
        }

        if (arguments.GetValue("asset") is not null || arguments.GetValue("ref") is not null)
        {
            return "image";
        }

        return payloadSegments.Count > 1 ? "list" : "text";
    }

    private static string BuildElementName(string kind, DeckObjectPlacement placement, uint shapeId, int lineNumber)
    {
        if (placement.TargetName is not null)
        {
            return $"{kind}:{placement.TargetName}:{lineNumber}";
        }

        return $"{kind}:{shapeId}";
    }

    private static string? InferRole(DeckDirectiveArguments arguments, string? targetName)
    {
        if (arguments.Roles.Count > 0)
        {
            return arguments.Roles[0];
        }

        return targetName?.ToLowerInvariant() switch
        {
            "title" => "title",
            "subtitle" => "subtitle",
            "caption" => "caption",
            _ => "body",
        };
    }

    private static int ResolveFontSize(DeckDirectiveArguments arguments, string? defaultRole)
    {
        if (arguments.GetValue("size") is string sizeText && int.TryParse(sizeText, out var explicitSize))
        {
            return explicitSize * 100;
        }

        return ResolveDefaultFontSize(defaultRole ?? "body");
    }

    private static string? ResolveTextColor(DeckDocDocument document, DeckDirectiveArguments arguments, string? defaultRole)
    {
        if (ResolveColor(document, arguments.GetValue("fg")) is string explicitColor)
        {
            return explicitColor;
        }

        foreach (var role in arguments.Roles)
        {
            if (TryResolveRoleStyleColor(document, role, out var styledRoleColor))
            {
                return styledRoleColor;
            }

            if (TryResolveRoleHintColor(document, role, out var roleColor))
            {
                return roleColor;
            }
        }

        if (TryResolveRoleStyleColor(document, defaultRole, out var defaultStyledRoleColor))
        {
            return defaultStyledRoleColor;
        }

        if (TryResolveRoleHintColor(document, defaultRole, out var defaultRoleColor))
        {
            return defaultRoleColor;
        }

        return ResolveColor(
            document,
            document.ThemeTokens.GetValueOrDefault("ink")
            ?? document.ThemeTokens.GetValueOrDefault("text-main")
            ?? document.ThemeTokens.GetValueOrDefault("dark")
            ?? document.ThemeTokens.GetValueOrDefault("primary"));
    }

    private static bool TryResolveRoleStyleColor(DeckDocDocument document, string? role, out string? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(role)
            || !document.Styles.TryGetValue(role.Trim(), out var styleArguments))
        {
            return false;
        }

        color = ResolveColor(document, styleArguments.GetValue("fg"));
        return color is not null;
    }

    private static bool TryResolveRoleHintColor(DeckDocDocument document, string? role, out string? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalizedRole = role.Trim().ToLowerInvariant();
        string? candidate = normalizedRole switch
        {
            var value when value.Contains("head", StringComparison.Ordinal) => document.ThemeTokens.GetValueOrDefault("white") ?? document.ThemeTokens.GetValueOrDefault("text-light") ?? document.ThemeTokens.GetValueOrDefault("light"),
            var value when value.Contains("light", StringComparison.Ordinal) => document.ThemeTokens.GetValueOrDefault("text-light") ?? document.ThemeTokens.GetValueOrDefault("light"),
            var value when value.Contains("muted", StringComparison.Ordinal) => document.ThemeTokens.GetValueOrDefault("text-muted") ?? document.ThemeTokens.GetValueOrDefault("muted"),
            var value when value.Contains("dark", StringComparison.Ordinal) => document.ThemeTokens.GetValueOrDefault("dark") ?? document.ThemeTokens.GetValueOrDefault("ink") ?? document.ThemeTokens.GetValueOrDefault("text-main"),
            _ => null,
        };

        color = ResolveColor(document, candidate);
        return color is not null;
    }

    private static int ResolveDefaultFontSize(string role) =>
        role.ToLowerInvariant() switch
        {
            "title" => 2_800,
            "subtitle" => 1_600,
            "caption" => 1_200,
            _ => 1_800,
        };

    private static int ResolveLayer(DeckDirectiveArguments arguments)
    {
        var layer = arguments.GetValue("layer");
        if (string.Equals(layer, "back", StringComparison.OrdinalIgnoreCase))
        {
            return -1_000;
        }

        if (string.Equals(layer, "front", StringComparison.OrdinalIgnoreCase))
        {
            return 1_000;
        }

        return int.TryParse(layer, out var numericLayer) ? numericLayer : 0;
    }

    private static A.ShapeTypeValues ResolveShapeKind(DeckDirectiveArguments arguments)
    {
        if (arguments.BareTokens.Count == 0)
        {
            return A.ShapeTypeValues.Rectangle;
        }

        var shapeIndex = arguments.BareTokens.FindIndex(static token => string.Equals(token, "shape", StringComparison.OrdinalIgnoreCase));
        var token = shapeIndex >= 0 && shapeIndex + 1 < arguments.BareTokens.Count
            ? arguments.BareTokens[shapeIndex + 1]
            : arguments.BareTokens[0];

        return token.ToLowerInvariant() switch
        {
            "roundrect" => A.ShapeTypeValues.RoundRectangle,
            "ellipse" => A.ShapeTypeValues.Ellipse,
            "diamond" => A.ShapeTypeValues.Diamond,
            "triangle" => A.ShapeTypeValues.Triangle,
            "line" or "arrow" => A.ShapeTypeValues.Line,
            _ => A.ShapeTypeValues.Rectangle,
        };
    }

    private static IEnumerable<string> BuildListLines(DeckDirectiveArguments arguments, IReadOnlyList<string> payloadSegments)
    {
        var bullet = arguments.GetValue("bullet")?.ToLowerInvariant() ?? "disc";
        var start = 1;
        if (bullet == "number" && int.TryParse(arguments.GetValue("start"), out var parsedStart))
        {
            start = parsedStart;
        }

        for (var index = 0; index < payloadSegments.Count; index++)
        {
            var prefix = bullet switch
            {
                "dash" => "- ",
                "check" => "[x] ",
                "number" => $"{start + index}. ",
                _ => "• ",
            };
            yield return prefix + payloadSegments[index];
        }
    }

    private static P.Shape CreateTextShape(
        uint id,
        string name,
        EmuRect rect,
        IEnumerable<string> lines,
        int fontSize,
        bool bold,
        string? fillHex,
        string? strokeHex,
        string? textColorHex,
        A.ShapeTypeValues shapeKind)
    {
        var bodyProperties = new A.BodyProperties
        {
            Anchor = A.TextAnchoringTypeValues.Top,
            Wrap = A.TextWrappingValues.Square,
        };
        bodyProperties.Append(new A.NormalAutoFit
        {
            FontScale = 70_000,
            LineSpaceReduction = 20_000,
        });
        var textBody = new P.TextBody(bodyProperties, new A.ListStyle());
        var appendedParagraph = false;
        foreach (var line in lines)
        {
            textBody.Append(CreateParagraph(line, fontSize, bold, textColorHex));
            appendedParagraph = true;
        }

        if (!appendedParagraph)
        {
            textBody.Append(CreateParagraph(string.Empty, fontSize, bold, textColorHex));
        }

        var shapeProperties = new P.ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = rect.X, Y = rect.Y },
                new A.Extents { Cx = rect.Width, Cy = rect.Height }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = shapeKind });

        if (fillHex is null)
        {
            shapeProperties.Append(new A.NoFill());
        }
        else
        {
            shapeProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fillHex }));
        }

        if (strokeHex is null)
        {
            shapeProperties.Append(new A.Outline(new A.NoFill()));
        }
        else
        {
            shapeProperties.Append(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = strokeHex })) { Width = 12_700 });
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            shapeProperties,
            textBody);
    }

    private static A.Paragraph CreateParagraph(string text, int fontSize, bool bold, string? textColorHex)
    {
        var runProperties = new A.RunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Bold = bold,
            Dirty = false,
        };
        if (textColorHex is not null)
        {
            runProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = textColorHex }));
        }

        return new A.Paragraph(
            new A.ParagraphProperties(),
            new A.Run(runProperties, new A.Text(text ?? string.Empty)),
            new A.EndParagraphRunProperties
            {
                Language = "en-US",
                FontSize = fontSize,
                Dirty = false,
            });
    }

    private static P.Picture CreatePicture(SlidePart slidePart, uint id, string name, EmuRect rect, ResolvedDeckImage image, DeckDirectiveArguments? arguments)
    {
        var imagePart = slidePart.AddImagePart(image.ContentType);
        using (var stream = imagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            stream.Write(image.Content, 0, image.Content.Length);
        }

        var relationshipId = slidePart.GetIdOfPart(imagePart);
        var pictureRect = rect;
        var sourceRectangle = CreateSourceRectangle(image, rect, arguments, ref pictureRect);
        var blipFill = new P.BlipFill(
            new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print });
        if (sourceRectangle is not null)
        {
            blipFill.Append(sourceRectangle);
        }

        blipFill.Append(new A.Stretch(new A.FillRectangle()));
        return new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties
                {
                    Id = id,
                    Name = name,
                    Description = name,
                },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            blipFill,
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = pictureRect.X, Y = pictureRect.Y },
                    new A.Extents { Cx = pictureRect.Width, Cy = pictureRect.Height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    private static A.SourceRectangle? CreateSourceRectangle(ResolvedDeckImage image, EmuRect destinationRect, DeckDirectiveArguments? arguments, ref EmuRect pictureRect)
    {
        var fit = arguments?.GetValue("fit")?.Trim().ToLowerInvariant();
        var crop = ParseCrop(arguments?.GetValue("crop"));
        if (!TryGetImageSize(image, out var imageWidth, out var imageHeight))
        {
            return crop;
        }

        switch (fit)
        {
            case "contain":
                pictureRect = FitContain(destinationRect, imageWidth, imageHeight);
                return crop;
            case "cover":
                return MergeCrop(crop, CreateCoverCrop(destinationRect, imageWidth, imageHeight));
            case "stretch":
            case null:
            case "":
                return crop;
            default:
                return crop;
        }
    }

    private static EmuRect FitContain(EmuRect destinationRect, int imageWidth, int imageHeight)
    {
        var widthScale = (double)destinationRect.Width / imageWidth;
        var heightScale = (double)destinationRect.Height / imageHeight;
        var scale = Math.Min(widthScale, heightScale);
        var fittedWidth = (long)Math.Round(imageWidth * scale, MidpointRounding.AwayFromZero);
        var fittedHeight = (long)Math.Round(imageHeight * scale, MidpointRounding.AwayFromZero);
        return new EmuRect(
            destinationRect.X + ((destinationRect.Width - fittedWidth) / 2),
            destinationRect.Y + ((destinationRect.Height - fittedHeight) / 2),
            fittedWidth,
            fittedHeight);
    }

    private static A.SourceRectangle CreateCoverCrop(EmuRect destinationRect, int imageWidth, int imageHeight)
    {
        var imageAspectRatio = (double)imageWidth / imageHeight;
        var destinationAspectRatio = (double)destinationRect.Width / destinationRect.Height;
        if (Math.Abs(imageAspectRatio - destinationAspectRatio) < 0.0001d)
        {
            return new A.SourceRectangle();
        }

        if (imageAspectRatio > destinationAspectRatio)
        {
            var visibleWidthFraction = destinationAspectRatio / imageAspectRatio;
            var totalCropFraction = 1d - visibleWidthFraction;
            var sideCrop = ToSourceRectangleValue(totalCropFraction / 2d);
            return new A.SourceRectangle { Left = sideCrop, Right = sideCrop };
        }

        var visibleHeightFraction = imageAspectRatio / destinationAspectRatio;
        var verticalCropFraction = 1d - visibleHeightFraction;
        var edgeCrop = ToSourceRectangleValue(verticalCropFraction / 2d);
        return new A.SourceRectangle { Top = edgeCrop, Bottom = edgeCrop };
    }

    private static A.SourceRectangle? MergeCrop(A.SourceRectangle? baseCrop, A.SourceRectangle? additionalCrop)
    {
        if (baseCrop is null)
        {
            return additionalCrop;
        }

        if (additionalCrop is null)
        {
            return baseCrop;
        }

        return new A.SourceRectangle
        {
            Left = ClampSourceRectangleValue(baseCrop.Left?.Value ?? 0, additionalCrop.Left?.Value ?? 0),
            Top = ClampSourceRectangleValue(baseCrop.Top?.Value ?? 0, additionalCrop.Top?.Value ?? 0),
            Right = ClampSourceRectangleValue(baseCrop.Right?.Value ?? 0, additionalCrop.Right?.Value ?? 0),
            Bottom = ClampSourceRectangleValue(baseCrop.Bottom?.Value ?? 0, additionalCrop.Bottom?.Value ?? 0),
        };
    }

    private static Int32Value ClampSourceRectangleValue(int primary, int additional) =>
        Math.Clamp(primary + additional, 0, 100_000);

    private static A.SourceRectangle? ParseCrop(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return null;
        }

        return new A.SourceRectangle
        {
            Left = ParseCropComponent(parts[0]),
            Top = ParseCropComponent(parts[1]),
            Right = ParseCropComponent(parts[2]),
            Bottom = ParseCropComponent(parts[3]),
        };
    }

    private static int ParseCropComponent(string value)
    {
        var numeric = ParseDouble(value);
        return numeric <= 1d
            ? ToSourceRectangleValue(numeric)
            : ToSourceRectangleValue(numeric / 100d);
    }

    private static int ToSourceRectangleValue(double fraction) =>
        Math.Clamp((int)Math.Round(fraction * 100_000d, MidpointRounding.AwayFromZero), 0, 100_000);

    private static bool TryGetImageSize(ResolvedDeckImage image, out int width, out int height)
    {
        var content = image.Content;
        if (content.Length >= 24 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            width = ReadBigEndianInt32(content, 16);
            height = ReadBigEndianInt32(content, 20);
            return width > 0 && height > 0;
        }

        if (content.Length >= 10 && content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46)
        {
            width = content[6] | (content[7] << 8);
            height = content[8] | (content[9] << 8);
            return width > 0 && height > 0;
        }

        if (content.Length >= 26 && content[0] == 0x42 && content[1] == 0x4D)
        {
            width = BitConverter.ToInt32(content, 18);
            height = Math.Abs(BitConverter.ToInt32(content, 22));
            return width > 0 && height > 0;
        }

        if (TryGetJpegSize(content, out width, out height))
        {
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryGetJpegSize(byte[] content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 8 < content.Length)
        {
            if (content[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < content.Length && content[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                break;
            }

            var marker = content[offset++];
            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }

            if (offset + 1 >= content.Length)
            {
                break;
            }

            var segmentLength = (content[offset] << 8) | content[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > content.Length)
            {
                break;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                height = (content[offset + 3] << 8) | content[offset + 4];
                width = (content[offset + 5] << 8) | content[offset + 6];
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool TryResolveStandaloneBlockRect(
        DeckGridAnchor? anchor,
        DeckGridSize? size,
        string? targetName,
        string? targetIndex,
        DeckGridSize grid,
        Dictionary<string, ResolvedTarget> targets,
        out DeckGridRect rect)
    {
        if (targetName is not null)
        {
            return TryResolvePlacementRect(
                new DeckObjectPlacement
                {
                    Mode = DeckObjectAddressingMode.Target,
                    TargetName = targetName,
                    TargetIndex = targetIndex,
                },
                grid,
                targets,
                out rect,
                out _);
        }

        if (anchor is DeckGridAnchor resolvedAnchor && size is DeckGridSize resolvedSize)
        {
            rect = new DeckGridRect(resolvedAnchor.ColumnNumber - 1, resolvedAnchor.RowNumber - 1, resolvedSize.Width, resolvedSize.Height);
            return true;
        }

        rect = default;
        return false;
    }

    private static int ReadBigEndianInt32(byte[] buffer, int offset) =>
        (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

    private static P.GraphicFrame CreateTableFrame(DeckDocDocument document, DeckTableBlock table, DeckGridRect tableRect, DeckGridSize grid, uint id)
    {
        var rect = ToEmuRect(tableRect, grid);
        var visibleRows = table.HasHeaderSeparator && table.Rows.Count > 1
            ? table.Rows.Where(static row => !IsMarkdownSeparatorRow(row)).ToArray()
            : table.Rows.ToArray();
        var hasHeaderRow = visibleRows.Length > 1 && !table.Arguments.HasToken("noheader");
        var columnCount = Math.Max(1, visibleRows.DefaultIfEmpty(Array.Empty<string>()).Max(static row => row.Count));
        var rowCount = Math.Max(1, visibleRows.Length);
        var columnWidth = rect.Width / columnCount;
        var rowHeight = rect.Height / rowCount;

        var drawingTable = new A.Table();
        drawingTable.Append(new A.TableProperties
        {
            FirstRow = hasHeaderRow,
            BandRow = table.Arguments.HasToken("banded"),
        });

        var gridColumns = new A.TableGrid();
        for (var index = 0; index < columnCount; index++)
        {
            gridColumns.Append(new A.GridColumn { Width = columnWidth });
        }

        drawingTable.Append(gridColumns);

        for (var rowIndex = 0; rowIndex < visibleRows.Length; rowIndex++)
        {
            var isHeaderRow = hasHeaderRow && rowIndex == 0;
            var row = new A.TableRow { Height = rowHeight };
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var cellText = columnIndex < visibleRows[rowIndex].Count ? visibleRows[rowIndex][columnIndex] : string.Empty;
                row.Append(new A.TableCell(
                    new A.TextBody(
                        new A.BodyProperties(),
                        new A.ListStyle(),
                        CreateParagraph(
                            cellText,
                            ResolveDefaultFontSize("body"),
                            isHeaderRow,
                            ResolveColor(document, isHeaderRow ? document.ThemeTokens.GetValueOrDefault("primary") ?? document.ThemeTokens.GetValueOrDefault("ink") : document.ThemeTokens.GetValueOrDefault("ink")))).CloneNode(true),
                    CreateTableCellProperties(document, isHeaderRow)));
            }

            drawingTable.Append(row);
        }

        var graphicData = new A.GraphicData(drawingTable)
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/table",
        };

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = table.Name },
                new P.NonVisualGraphicFrameDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(
                new A.Offset { X = rect.X, Y = rect.Y },
                new A.Extents { Cx = rect.Width, Cy = rect.Height }),
            new A.Graphic(graphicData));
    }

    private static bool IsMarkdownSeparatorRow(IReadOnlyList<string> row)
    {
        var hasSeparatorCell = false;
        foreach (var cell in row)
        {
            var trimmed = cell.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            hasSeparatorCell = true;
            if (!string.Equals(trimmed, "---", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return hasSeparatorCell;
    }

    private static P.GraphicFrame CreateChartFrame(SlidePart slidePart, DeckDocDocument document, DeckChartBlock chart, DeckGridRect chartRect, DeckGridSize grid, uint id)
    {
        var rect = ToEmuRect(chartRect, grid);
        var chartPart = slidePart.AddNewPart<ChartPart>();
        chartPart.ChartSpace = CreateChartSpace(document, chart);
        chartPart.ChartSpace.Save();

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = chart.Name },
                new P.NonVisualGraphicFrameDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(
                new A.Offset { X = rect.X, Y = rect.Y },
                new A.Extents { Cx = rect.Width, Cy = rect.Height }),
            new A.Graphic(
                new A.GraphicData(
                    new C.ChartReference { Id = slidePart.GetIdOfPart(chartPart) })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                }));
    }

    private static A.TableCellProperties CreateTableCellProperties(DeckDocDocument document, bool isHeaderRow)
    {
        var properties = new A.TableCellProperties();
        if (!isHeaderRow)
        {
            return properties;
        }

        if (ResolveColor(document, document.ThemeTokens.GetValueOrDefault("secondary") ?? "E2E8F0") is string headerFill)
        {
            properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = headerFill }));
        }

        return properties;
    }

    private static C.ChartSpace CreateChartSpace(DeckDocDocument document, DeckChartBlock chart)
    {
        var chartSpace = new C.ChartSpace();
        chartSpace.Append(new C.EditingLanguage { Val = "en-US" });

        var chartElement = new C.Chart();
        chartElement.Append(CreateChartTitle(chart.Name));

        var plotArea = new C.PlotArea();
        plotArea.Append(new C.Layout());

        switch (chart.Type.ToLowerInvariant())
        {
            case "line":
                AppendLineChart(plotArea, chart, document);
                break;
            case "pie":
            case "doughnut":
                AppendPieChart(plotArea, chart, document, string.Equals(chart.Type, "doughnut", StringComparison.OrdinalIgnoreCase));
                break;
            case "combo":
                AppendComboChart(plotArea, chart, document);
                break;
            case "bar":
                AppendBarChart(plotArea, chart, document, horizontal: true);
                break;
            default:
                AppendBarChart(plotArea, chart, document, horizontal: false);
                break;
        }

        chartElement.Append(plotArea);
        if (chart.Series.Count > 1)
        {
            chartElement.Append(new C.Legend(new C.LegendPosition { Val = C.LegendPositionValues.Right }, new C.Layout()));
        }

        chartElement.Append(new C.PlotVisibleOnly { Val = true });
        chartSpace.Append(chartElement);
        chartSpace.Append(new C.PrintSettings(
            new C.HeaderFooter(),
            new C.PageMargins
            {
                Left = 0.7D,
                Right = 0.7D,
                Top = 0.75D,
                Bottom = 0.75D,
                Header = 0.3D,
                Footer = 0.3D,
            },
            new C.PageSetup()));
        return chartSpace;
    }

    private static C.Title CreateChartTitle(string title) =>
        new(
            new C.ChartText(
                new C.RichText(
                    new A.BodyProperties(),
                    new A.ListStyle(),
                    new A.Paragraph(
                        new A.Run(
                            new A.RunProperties { Language = "en-US" },
                            new A.Text(title)),
                        new A.EndParagraphRunProperties { Language = "en-US" }))),
            new C.Layout(),
            new C.Overlay { Val = false });

    private static void AppendBarChart(C.PlotArea plotArea, DeckChartBlock chart, DeckDocDocument document, bool horizontal)
    {
        const uint categoryAxisId = 48650112U;
        const uint valueAxisId = 48672768U;

        var chartElement = new C.BarChart(
            new C.BarDirection { Val = horizontal ? C.BarDirectionValues.Bar : C.BarDirectionValues.Column },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = chart.Series.Count <= 1 });

        for (var index = 0; index < chart.Series.Count; index++)
        {
            chartElement.Append(CreateBarSeries(chart.Series[index], index, document));
        }

        chartElement.Append(CreateCommonDataLabels(chart.Series));
        chartElement.Append(new C.AxisId { Val = categoryAxisId });
        chartElement.Append(new C.AxisId { Val = valueAxisId });

        plotArea.Append(chartElement);
        plotArea.Append(CreateCategoryAxis(categoryAxisId, valueAxisId, horizontal ? C.AxisPositionValues.Left : C.AxisPositionValues.Bottom));
        plotArea.Append(CreateValueAxis(valueAxisId, categoryAxisId, horizontal ? C.AxisPositionValues.Bottom : C.AxisPositionValues.Left));
    }

    private static void AppendLineChart(C.PlotArea plotArea, DeckChartBlock chart, DeckDocDocument document)
    {
        const uint categoryAxisId = 48650112U;
        const uint valueAxisId = 48672768U;

        var chartElement = new C.LineChart(
            new C.Grouping { Val = C.GroupingValues.Standard },
            new C.VaryColors { Val = chart.Series.Count <= 1 });

        for (var index = 0; index < chart.Series.Count; index++)
        {
            chartElement.Append(CreateLineSeries(chart.Series[index], index, document));
        }

        chartElement.Append(CreateCommonDataLabels(chart.Series));
        chartElement.Append(new C.AxisId { Val = categoryAxisId });
        chartElement.Append(new C.AxisId { Val = valueAxisId });

        plotArea.Append(chartElement);
        plotArea.Append(CreateCategoryAxis(categoryAxisId, valueAxisId, C.AxisPositionValues.Bottom));
        plotArea.Append(CreateValueAxis(valueAxisId, categoryAxisId, C.AxisPositionValues.Left));
    }

    private static void AppendPieChart(C.PlotArea plotArea, DeckChartBlock chart, DeckDocDocument document, bool doughnut)
    {
        if (doughnut)
        {
            var doughnutChart = new C.DoughnutChart(new C.VaryColors { Val = true });
            for (var index = 0; index < chart.Series.Count; index++)
            {
                doughnutChart.Append(CreatePieSeries(chart.Series[index], index, document));
            }

            doughnutChart.Append(CreateCommonDataLabels(chart.Series));
            doughnutChart.Append(new C.HoleSize { Val = 50 });
            plotArea.Append(doughnutChart);
            return;
        }

        var pieChart = new C.PieChart(new C.VaryColors { Val = true });
        for (var index = 0; index < chart.Series.Count; index++)
        {
            pieChart.Append(CreatePieSeries(chart.Series[index], index, document));
        }

        pieChart.Append(CreateCommonDataLabels(chart.Series));
        plotArea.Append(pieChart);
    }

    private static void AppendComboChart(C.PlotArea plotArea, DeckChartBlock chart, DeckDocDocument document)
    {
        const uint categoryAxisId = 48650112U;
        const uint primaryValueAxisId = 48672768U;
        const uint secondaryValueAxisId = 48694144U;

        var primarySeries = chart.Series
            .Where(static series => !string.Equals(series.Type, "line", StringComparison.OrdinalIgnoreCase) && !string.Equals(series.Axis, "secondary", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (primarySeries.Length == 0)
        {
            primarySeries = chart.Series.Take(1).ToArray();
        }

        var barChart = new C.BarChart(
            new C.BarDirection { Val = C.BarDirectionValues.Column },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = primarySeries.Length <= 1 });
        for (var index = 0; index < primarySeries.Length; index++)
        {
            barChart.Append(CreateBarSeries(primarySeries[index], index, document));
        }

        barChart.Append(CreateCommonDataLabels(primarySeries));
        barChart.Append(new C.AxisId { Val = categoryAxisId });
        barChart.Append(new C.AxisId { Val = primaryValueAxisId });
        plotArea.Append(barChart);

        var secondarySeries = chart.Series.Except(primarySeries).ToArray();
        if (secondarySeries.Length > 0)
        {
            var lineChart = new C.LineChart(
                new C.Grouping { Val = C.GroupingValues.Standard },
                new C.VaryColors { Val = secondarySeries.Length <= 1 });
            for (var index = 0; index < secondarySeries.Length; index++)
            {
                lineChart.Append(CreateLineSeries(secondarySeries[index], index + primarySeries.Length, document));
            }

            lineChart.Append(CreateCommonDataLabels(secondarySeries));
            lineChart.Append(new C.AxisId { Val = categoryAxisId });
            lineChart.Append(new C.AxisId { Val = secondaryValueAxisId });
            plotArea.Append(lineChart);
        }

        plotArea.Append(CreateCategoryAxis(categoryAxisId, primaryValueAxisId, C.AxisPositionValues.Bottom));
        plotArea.Append(CreateValueAxis(primaryValueAxisId, categoryAxisId, C.AxisPositionValues.Left));
        if (secondarySeries.Length > 0)
        {
            plotArea.Append(CreateValueAxis(secondaryValueAxisId, categoryAxisId, C.AxisPositionValues.Right));
        }
    }

    private static C.DataLabels CreateCommonDataLabels(IEnumerable<DeckChartSeries> series) =>
        new(
            new C.ShowLegendKey { Val = false },
            new C.ShowValue { Val = series.Any(static item => item.Labels) },
            new C.ShowCategoryName { Val = false },
            new C.ShowSeriesName { Val = false },
            new C.ShowPercent { Val = false },
            new C.ShowBubbleSize { Val = false });

    private static C.BarChartSeries CreateBarSeries(DeckChartSeries series, int index, DeckDocDocument document) =>
        new(
            new C.Index { Val = (uint)index },
            new C.Order { Val = (uint)index },
            new C.SeriesText(new C.NumericValue { Text = series.Label }),
            CreateChartShapeProperties(document, series.Color),
            new C.InvertIfNegative { Val = false },
            new C.CategoryAxisData(CreateStringLiteral(series.Categories)),
            new C.Values(CreateNumberLiteral(series.Values)));

    private static C.LineChartSeries CreateLineSeries(DeckChartSeries series, int index, DeckDocDocument document) =>
        new(
            new C.Index { Val = (uint)index },
            new C.Order { Val = (uint)index },
            new C.SeriesText(new C.NumericValue { Text = series.Label }),
            CreateChartShapeProperties(document, series.Color),
            new C.Marker(new C.Symbol { Val = C.MarkerStyleValues.Circle }),
            new C.CategoryAxisData(CreateStringLiteral(series.Categories)),
            new C.Values(CreateNumberLiteral(series.Values)));

    private static C.PieChartSeries CreatePieSeries(DeckChartSeries series, int index, DeckDocDocument document)
    {
        var pieSeries = new C.PieChartSeries(
            new C.Index { Val = (uint)index },
            new C.Order { Val = (uint)index },
            new C.SeriesText(new C.NumericValue { Text = series.Label }),
            new C.CategoryAxisData(CreateStringLiteral(series.Categories)),
            new C.Values(CreateNumberLiteral(series.Values)));
        pieSeries.Append(CreateChartShapeProperties(document, series.Color));
        return pieSeries;
    }

    private static C.ChartShapeProperties CreateChartShapeProperties(DeckDocDocument document, string? color)
    {
        var properties = new C.ChartShapeProperties();
        if (ResolveColor(document, color ?? document.ThemeTokens.GetValueOrDefault("primary")) is string resolvedColor)
        {
            properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = resolvedColor }));
            properties.Append(new A.Outline(new A.NoFill()));
        }

        return properties;
    }

    private static C.StringLiteral CreateStringLiteral(IReadOnlyList<string> values)
    {
        var literal = new C.StringLiteral();
        literal.Append(new C.PointCount { Val = (uint)values.Count });
        for (var index = 0; index < values.Count; index++)
        {
            literal.Append(new C.StringPoint { Index = (uint)index, NumericValue = new C.NumericValue(values[index]) });
        }

        return literal;
    }

    private static C.NumberLiteral CreateNumberLiteral(IReadOnlyList<string> values)
    {
        var literal = new C.NumberLiteral();
        literal.Append(new C.FormatCode("General"));
        literal.Append(new C.PointCount { Val = (uint)values.Count });
        for (var index = 0; index < values.Count; index++)
        {
            literal.Append(new C.NumericPoint { Index = (uint)index, NumericValue = new C.NumericValue(values[index]) });
        }

        return literal;
    }

    private static C.CategoryAxis CreateCategoryAxis(uint axisId, uint crossingAxisId, C.AxisPositionValues position) =>
        new(
            new C.AxisId { Val = axisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = position },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.AutoLabeled { Val = true },
            new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
            new C.LabelOffset { Val = 100 });

    private static C.ValueAxis CreateValueAxis(uint axisId, uint crossingAxisId, C.AxisPositionValues position) =>
        new(
            new C.AxisId { Val = axisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = position },
            new C.MajorGridlines(),
            new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.CrossBetween { Val = C.CrossBetweenValues.Between });

    private static void TryAppendTransition(P.Slide slideDocument, DeckTransitionDirective? transition)
    {
        if (transition is null)
        {
            return;
        }

        var transitionChild = CreateTransitionChild(transition);
        if (transitionChild is null)
        {
            return;
        }

        var durationMs = transition.Arguments.GetValue("dur") is string duration
            ? ParseTimeMilliseconds(duration, defaultValue: 0).ToString(CultureInfo.InvariantCulture)
            : null;

        if (durationMs is not null)
        {
            slideDocument.Append(BuildTransitionAlternateContent(transition, transitionChild, durationMs));
            slideDocument.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            slideDocument.AddNamespaceDeclaration("p14", "http://schemas.microsoft.com/office/powerpoint/2010/main");
            slideDocument.MCAttributes ??= new MarkupCompatibilityAttributes();
            var ignorable = slideDocument.MCAttributes.Ignorable?.Value;
            slideDocument.MCAttributes.Ignorable = string.IsNullOrWhiteSpace(ignorable) || !ignorable.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("p14", StringComparer.Ordinal)
                ? string.IsNullOrWhiteSpace(ignorable) ? "p14" : $"{ignorable} p14"
                : ignorable;
            return;
        }

        var transitionElement = new P.Transition();
        if (transition.Arguments.GetValue("advance") is string advance)
        {
            var normalizedAdvance = advance.Trim();
            if (string.Equals(normalizedAdvance, "click", StringComparison.OrdinalIgnoreCase))
            {
                transitionElement.AdvanceOnClick = true;
            }
            else if (normalizedAdvance.StartsWith("after(", StringComparison.OrdinalIgnoreCase)
                && normalizedAdvance.EndsWith(')'))
            {
                transitionElement.AdvanceOnClick = false;
                transitionElement.AdvanceAfterTime = ParseTimeMilliseconds(normalizedAdvance[6..^1], defaultValue: 0).ToString(CultureInfo.InvariantCulture);
            }
        }

        transitionElement.Append(transitionChild.CloneNode(true));
        slideDocument.Append(transitionElement);
    }

    private static OpenXmlElement? CreateTransitionChild(DeckTransitionDirective transition)
    {
        switch (transition.Type.ToLowerInvariant())
        {
            case "fade":
                return new P.FadeTransition();
            case "push":
                return new P.PushTransition { Direction = ParseTransitionDirection(transition.Arguments.GetValue("dir")) };
            case "wipe":
                return new P.WipeTransition { Direction = ParseTransitionDirection(transition.Arguments.GetValue("dir")) };
            default:
                return null;
        }
    }

    private static OpenXmlUnknownElement BuildTransitionAlternateContent(DeckTransitionDirective transition, OpenXmlElement transitionChild, string durationMs)
    {
        const string markupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const string presentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";
        const string powerPoint2010Namespace = "http://schemas.microsoft.com/office/powerpoint/2010/main";

        var alternateContent = new OpenXmlUnknownElement("mc", "AlternateContent", markupCompatibilityNamespace);
        var choice = new OpenXmlUnknownElement("mc", "Choice", markupCompatibilityNamespace);
        choice.SetAttribute(new OpenXmlAttribute(string.Empty, "Requires", null!, "p14"));

        var choiceTransition = new OpenXmlUnknownElement("p", "transition", presentationNamespace);
        choiceTransition.AddNamespaceDeclaration("p14", powerPoint2010Namespace);
        choiceTransition.SetAttribute(new OpenXmlAttribute("p14", "dur", powerPoint2010Namespace, durationMs));
        ApplyTransitionAdvanceAttributes(choiceTransition, transition.Arguments.GetValue("advance"));
        choiceTransition.AppendChild(CloneUnknownElement(transitionChild, presentationNamespace));
        choice.AppendChild(choiceTransition);

        var fallback = new OpenXmlUnknownElement("mc", "Fallback", markupCompatibilityNamespace);
        var fallbackTransition = new OpenXmlUnknownElement("p", "transition", presentationNamespace);
        ApplyTransitionAdvanceAttributes(fallbackTransition, transition.Arguments.GetValue("advance"));
        fallbackTransition.AppendChild(CloneUnknownElement(transitionChild, presentationNamespace));
        fallback.AppendChild(fallbackTransition);

        alternateContent.AppendChild(choice);
        alternateContent.AppendChild(fallback);
        return alternateContent;
    }

    private static P.TransitionSlideDirectionValues ParseTransitionDirection(string? direction) =>
        direction?.Trim().ToLowerInvariant() switch
        {
            "right" or "r" => P.TransitionSlideDirectionValues.Right,
            "up" or "u" => P.TransitionSlideDirectionValues.Up,
            "down" or "d" => P.TransitionSlideDirectionValues.Down,
            _ => P.TransitionSlideDirectionValues.Left,
        };

    private static void ApplyTransitionAdvanceAttributes(OpenXmlUnknownElement transitionElement, string? advance)
    {
        if (string.IsNullOrWhiteSpace(advance))
        {
            return;
        }

        var normalizedAdvance = advance.Trim();
        if (string.Equals(normalizedAdvance, "click", StringComparison.OrdinalIgnoreCase))
        {
            transitionElement.SetAttribute(new OpenXmlAttribute(string.Empty, "advClick", null!, "1"));
            return;
        }

        if (normalizedAdvance.StartsWith("after(", StringComparison.OrdinalIgnoreCase) && normalizedAdvance.EndsWith(')'))
        {
            transitionElement.SetAttribute(new OpenXmlAttribute(string.Empty, "advClick", null!, "0"));
            transitionElement.SetAttribute(new OpenXmlAttribute(string.Empty, "advTm", null!, ParseTimeMilliseconds(normalizedAdvance[6..^1], defaultValue: 0).ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static OpenXmlUnknownElement CloneUnknownElement(OpenXmlElement element, string namespaceUri)
    {
        var clone = new OpenXmlUnknownElement("p", element.LocalName, namespaceUri)
        {
            InnerXml = element.InnerXml,
        };

        foreach (var attribute in element.GetAttributes())
        {
            clone.SetAttribute(attribute);
        }

        return clone;
    }

    private static void TryWriteNotes(SlidePart slidePart, string notesText)
    {
        var notesPart = slidePart.NotesSlidePart ?? slidePart.AddNewPart<NotesSlidePart>();
        var shapeTree = CreateShapeTree();
        shapeTree.Append(CreateTextShape(
            2U,
            "Speaker Notes",
            new EmuRect(457_200L, 228_600L, 10_820_400L, 5_486_400L),
            [notesText],
            ResolveDefaultFontSize("body"),
            bold: false,
            fillHex: null,
            strokeHex: null,
            textColorHex: "000000",
            shapeKind: A.ShapeTypeValues.Rectangle));
        notesPart.NotesSlide = new P.NotesSlide(
            new P.CommonSlideData(shapeTree) { Name = "Notes" },
            new P.ColorMapOverride(new A.MasterColorMapping()));
        notesPart.NotesSlide.Save();
    }

    private static P.ShapeTree CreateShapeTree() =>
        new(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(
                new A.TransformGroup(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = 0L, Cy = 0L },
                    new A.ChildOffset { X = 0L, Y = 0L },
                    new A.ChildExtents { Cx = 0L, Cy = 0L })));

    private static string? ResolveColor(DeckDocDocument document, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('$'))
        {
            candidate = document.ThemeTokens.GetValueOrDefault(candidate[1..]) ?? candidate;
        }

        if (candidate.StartsWith('#'))
        {
            candidate = candidate[1..];
        }

        return candidate.Length == 6 ? candidate : null;
    }

    private static DeckGridSize ParseGridSize(string value)
    {
        var parts = value.Split('x', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            ? new DeckGridSize(ParseDouble(parts[0]), ParseDouble(parts[1]))
            : new DeckGridSize(32, 18);
    }

    private static DeckGridRange ParseRange(string value)
    {
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        return new DeckGridRange(ParseAnchor(parts[0]), ParseAnchor(parts[1]));
    }

    private static DeckGridAnchor ParseAnchor(string value)
    {
        var splitIndex = 0;
        while (splitIndex < value.Length && char.IsLetter(value[splitIndex]))
        {
            splitIndex++;
        }

        var column = 0;
        foreach (var ch in value[..splitIndex].ToUpperInvariant())
        {
            column = (column * 26) + (ch - 'A' + 1);
        }

        var row = int.Parse(value[splitIndex..], System.Globalization.CultureInfo.InvariantCulture);
        return new DeckGridAnchor(column, row);
    }

    private static DeckGridRect ToRect(DeckGridRange range) =>
        new(range.Start.ColumnNumber - 1, range.Start.RowNumber - 1, range.Width, range.Height);

    private static DeckGridRect? GetFallbackTargetRect(string name, DeckGridSize grid) =>
        name.ToLowerInvariant() switch
        {
            "title" => new DeckGridRect(1, 1, Math.Min(24, grid.Width - 2), 2),
            "subtitle" => new DeckGridRect(1, 3, Math.Min(20, grid.Width - 4), 1.5),
            "body" => new DeckGridRect(1, 4, Math.Min(20, grid.Width - 3), Math.Max(8, grid.Height - 6)),
            "hero" or "visual" => new DeckGridRect(Math.Max(12, grid.Width - 10), 3, Math.Min(9, grid.Width - 2), Math.Max(8, grid.Height - 6)),
            "caption" => new DeckGridRect(1, grid.Height - 2, Math.Min(20, grid.Width - 2), 1),
            _ => null,
        };

    private static EmuRect ToEmuRect(DeckGridRect rect, DeckGridSize grid) =>
        new(
            (long)Math.Round((rect.Left / grid.Width) * SlideWidth),
            (long)Math.Round((rect.Top / grid.Height) * SlideHeight),
            (long)Math.Round((rect.Width / grid.Width) * SlideWidth),
            (long)Math.Round((rect.Height / grid.Height) * SlideHeight));

    private static EmuRect NormalizeLineRect(EmuRect rect)
    {
        if (rect.Width >= rect.Height)
        {
            return new EmuRect(rect.X, rect.Y + (rect.Height / 2), rect.Width, 0L);
        }

        return new EmuRect(rect.X + (rect.Width / 2), rect.Y, 0L, rect.Height);
    }

    /// <summary>
    /// Describes one rendered element and its sort order.
    /// </summary>
    private readonly record struct RenderedElement(int Layer, int Sequence, uint ShapeId, OpenXmlElement Element);

    /// <summary>
    /// Describes one resolved target rectangle and its default arguments.
    /// </summary>
    private sealed record ResolvedTarget(DeckGridRect Rect, DeckDirectiveArguments Arguments);

    /// <summary>
    /// Describes one resolved target name and rectangle pair.
    /// </summary>
    private sealed record NamedResolvedTarget(string Name, ResolvedTarget Target);

    /// <summary>
    /// Describes one layout slot after resolving its source target.
    /// </summary>
    private sealed record ResolvedLayoutSlot(DeckSlotDefinition Slot, DeckGridRect Rect, DeckDirectiveArguments InheritedArguments, string? SharedSourceKey);

    /// <summary>
    /// Represents one logical grid rectangle.
    /// </summary>
    private readonly record struct DeckGridRect(double Left, double Top, double Width, double Height);

    /// <summary>
    /// Represents one slide rectangle expressed in EMUs.
    /// </summary>
    private readonly record struct EmuRect(long X, long Y, long Width, long Height);

    /// <summary>
    /// Represents one split part during target resolution.
    /// </summary>
    private readonly record struct SplitPart(double Value, SplitPartKind Kind);

    /// <summary>
    /// Identifies the split part interpretation mode.
    /// </summary>
    private enum SplitPartKind
    {
        Absolute,
        Percent,
        Fraction,
    }
}