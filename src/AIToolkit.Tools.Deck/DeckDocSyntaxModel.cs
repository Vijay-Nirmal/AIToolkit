namespace AIToolkit.Tools.Deck;

/// <summary>
/// Represents a fully parsed canonical DeckDoc document.
/// </summary>
internal sealed class DeckDocDocument
{
    /// <summary>
    /// Gets the presentation title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the normalized source lines.
    /// </summary>
    public required string[] Lines { get; init; }

    /// <summary>
    /// Gets the document attributes declared before shared directives and slides.
    /// </summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the shared theme tokens.
    /// </summary>
    public Dictionary<string, string> ThemeTokens { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the shared style definitions keyed by style name.
    /// </summary>
    public Dictionary<string, DeckDirectiveArguments> Styles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the shared asset references keyed by asset name.
    /// </summary>
    public Dictionary<string, string> SharedAssets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the shared motion definitions keyed by motion name.
    /// </summary>
    public Dictionary<string, DeckDirectiveArguments> Motions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets shared extension directives that appeared before the first slide.
    /// </summary>
    public List<DeckExtensionDirective> SharedExtensions { get; } = [];

    /// <summary>
    /// Gets the reusable layout blocks.
    /// </summary>
    public List<DeckDocLayout> Layouts { get; } = [];

    /// <summary>
    /// Gets the reusable layout blocks keyed by layout name.
    /// </summary>
    public Dictionary<string, DeckDocLayout> LayoutMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the slides in source order.
    /// </summary>
    public List<DeckDocSlide> Slides { get; } = [];
}

/// <summary>
/// Stores generic directive arguments as roles, bare tokens, and named values.
/// </summary>
internal sealed class DeckDirectiveArguments
{
    /// <summary>
    /// Gets the role tokens declared with a leading dot.
    /// </summary>
    public List<string> Roles { get; } = [];

    /// <summary>
    /// Gets the bare tokens that were neither roles nor key-value pairs.
    /// </summary>
    public List<string> BareTokens { get; } = [];

    /// <summary>
    /// Gets the named values keyed by entry name.
    /// </summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether a specific bare token is present.
    /// </summary>
    public bool HasToken(string value) =>
        BareTokens.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a named value when present.
    /// </summary>
    public string? GetValue(string name) =>
        Values.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Creates a shallow clone.
    /// </summary>
    public DeckDirectiveArguments Clone()
    {
        var clone = new DeckDirectiveArguments();
        clone.Roles.AddRange(Roles);
        clone.BareTokens.AddRange(BareTokens);
        foreach (var pair in Values)
        {
            clone.Values[pair.Key] = pair.Value;
        }

        return clone;
    }
}

/// <summary>
/// Represents one reusable layout block.
/// </summary>
internal sealed class DeckDocLayout
{
    /// <summary>
    /// Gets the layout name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the 1-based start line of the layout block header.
    /// </summary>
    public required int StartLineNumber { get; init; }

    /// <summary>
    /// Gets or sets the 1-based line number of the matching <c>[end]</c>.
    /// </summary>
    public int EndLineNumber { get; set; }

    /// <summary>
    /// Gets or sets the optional layout-local grid override.
    /// </summary>
    public DeckGridSize? GridOverride { get; set; }

    /// <summary>
    /// Gets or sets the optional layout background directive.
    /// </summary>
    public DeckDirectiveArguments? Background { get; set; }

    /// <summary>
    /// Gets or sets the optional default transition directive.
    /// </summary>
    public DeckTransitionDirective? Transition { get; set; }

    /// <summary>
    /// Gets the layout-local target definitions.
    /// </summary>
    public List<DeckLayoutTargetDefinition> Targets { get; } = [];

    /// <summary>
    /// Gets the recurring fixed layout objects.
    /// </summary>
    public List<DeckFixedObjectDefinition> FixedObjects { get; } = [];

    /// <summary>
    /// Gets the named slot definitions.
    /// </summary>
    public List<DeckSlotDefinition> Slots { get; } = [];

    /// <summary>
    /// Gets layout-level extension directives.
    /// </summary>
    public List<DeckExtensionDirective> Extensions { get; } = [];
}

/// <summary>
/// Represents one parsed slide.
/// </summary>
internal sealed class DeckDocSlide
{
    /// <summary>
    /// Gets the 1-based slide number.
    /// </summary>
    public required int SlideNumber { get; init; }

    /// <summary>
    /// Gets the logical slide heading text.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the 1-based start line for the slide heading.
    /// </summary>
    public required int StartLineNumber { get; init; }

    /// <summary>
    /// Gets or sets the inclusive 1-based end line for the slide section.
    /// </summary>
    public int EndLineNumber { get; set; }

    /// <summary>
    /// Gets or sets the applied layout name.
    /// </summary>
    public string? LayoutName { get; set; }

    /// <summary>
    /// Gets or sets the optional logical section name.
    /// </summary>
    public string? SectionName { get; set; }

    /// <summary>
    /// Gets or sets whether the slide is hidden.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the slide-local background directive.
    /// </summary>
    public DeckDirectiveArguments? Background { get; set; }

    /// <summary>
    /// Gets or sets the slide-local speaker notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the slide-local transition directive.
    /// </summary>
    public DeckTransitionDirective? Transition { get; set; }

    /// <summary>
    /// Gets the slide-local target definitions.
    /// </summary>
    public List<DeckLayoutTargetDefinition> Targets { get; } = [];

    /// <summary>
    /// Gets the compact object lines.
    /// </summary>
    public List<DeckObjectDefinition> Objects { get; } = [];

    /// <summary>
    /// Gets the explicit object override directives.
    /// </summary>
    public List<DeckObjectOverrideDefinition> ObjectOverrides { get; } = [];

    /// <summary>
    /// Gets the grouping directives.
    /// </summary>
    public List<DeckGroupDirective> Groups { get; } = [];

    /// <summary>
    /// Gets the table blocks.
    /// </summary>
    public List<DeckTableBlock> Tables { get; } = [];

    /// <summary>
    /// Gets the chart blocks.
    /// </summary>
    public List<DeckChartBlock> Charts { get; } = [];

    /// <summary>
    /// Gets the animation directives.
    /// </summary>
    public List<DeckAnimationDirective> Animations { get; } = [];

    /// <summary>
    /// Gets slide-level extension directives.
    /// </summary>
    public List<DeckExtensionDirective> Extensions { get; } = [];
}

/// <summary>
/// Describes one slide transition directive.
/// </summary>
internal sealed class DeckTransitionDirective
{
    /// <summary>
    /// Gets the transition type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the remaining transition arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Describes one object-level animation directive.
/// </summary>
internal sealed class DeckAnimationDirective
{
    /// <summary>
    /// Gets the referenced target name.
    /// </summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// Gets the optional target index selector.
    /// </summary>
    public string? TargetIndex { get; init; }

    /// <summary>
    /// Gets the animation arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Describes one extension directive.
/// </summary>
internal sealed class DeckExtensionDirective
{
    /// <summary>
    /// Gets the extension provider token when supplied.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets the extension arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Describes one grouping directive.
/// </summary>
internal sealed class DeckGroupDirective
{
    /// <summary>
    /// Gets the group name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the group member references.
    /// </summary>
    public required IReadOnlyList<string> Members { get; init; }
}

/// <summary>
/// Describes one target definition.
/// </summary>
internal abstract class DeckLayoutTargetDefinition
{
    /// <summary>
    /// Gets the 1-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }
}

/// <summary>
/// Defines a named area target.
/// </summary>
internal sealed class DeckAreaTargetDefinition : DeckLayoutTargetDefinition
{
    /// <summary>
    /// Gets the target name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the target range.
    /// </summary>
    public required DeckGridRange Range { get; init; }

    /// <summary>
    /// Gets the default arguments applied to the target.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Defines a named split target set.
/// </summary>
internal sealed class DeckSplitTargetDefinition : DeckLayoutTargetDefinition
{
    /// <summary>
    /// Gets the source reference or source range token.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets whether the split is row-oriented.
    /// </summary>
    public bool IsRows { get; init; }

    /// <summary>
    /// Gets the part sizes in source order.
    /// </summary>
    public required IReadOnlyList<string> Parts { get; init; }

    /// <summary>
    /// Gets the produced target names.
    /// </summary>
    public required IReadOnlyList<string> OutputNames { get; init; }

    /// <summary>
    /// Gets the optional gap value.
    /// </summary>
    public double Gap { get; init; }
}

/// <summary>
/// Defines a repeated grid target set.
/// </summary>
internal sealed class DeckGridTargetDefinition : DeckLayoutTargetDefinition
{
    /// <summary>
    /// Gets the target family name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the source reference or source range token.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the column count.
    /// </summary>
    public int Columns { get; init; }

    /// <summary>
    /// Gets the optional row count.
    /// </summary>
    public int? Rows { get; init; }

    /// <summary>
    /// Gets the horizontal gap.
    /// </summary>
    public double HorizontalGap { get; init; }

    /// <summary>
    /// Gets the vertical gap.
    /// </summary>
    public double VerticalGap { get; init; }

    /// <summary>
    /// Gets the item fill order.
    /// </summary>
    public string Order { get; init; } = "row";

    /// <summary>
    /// Gets the default arguments applied to each target.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Defines a repeated stack target set.
/// </summary>
internal sealed class DeckStackTargetDefinition : DeckLayoutTargetDefinition
{
    /// <summary>
    /// Gets the target family name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the source reference or source range token.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the stack direction.
    /// </summary>
    public required string Direction { get; init; }

    /// <summary>
    /// Gets the item count.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets the gap between items.
    /// </summary>
    public double Gap { get; init; }

    /// <summary>
    /// Gets the default arguments applied to each target.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Defines a named slot inside a layout block.
/// </summary>
internal sealed class DeckSlotDefinition
{
    /// <summary>
    /// Gets the slot name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the 1-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the placement bound to the slot.
    /// </summary>
    public required DeckObjectPlacement Placement { get; init; }

    /// <summary>
    /// Gets the default slot arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Defines one recurring fixed layout object.
/// </summary>
internal sealed class DeckFixedObjectDefinition
{
    /// <summary>
    /// Gets the 1-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the geometry placement.
    /// </summary>
    public required DeckObjectPlacement Placement { get; init; }

    /// <summary>
    /// Gets the object arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }
}

/// <summary>
/// Defines one compact object line.
/// </summary>
internal sealed class DeckObjectDefinition
{
    /// <summary>
    /// Gets the 1-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the object placement.
    /// </summary>
    public required DeckObjectPlacement Placement { get; init; }

    /// <summary>
    /// Gets the object attrlist arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }

    /// <summary>
    /// Gets the payload segments split on top-level pipe separators.
    /// </summary>
    public required IReadOnlyList<string> PayloadSegments { get; init; }

    /// <summary>
    /// Gets the raw source line.
    /// </summary>
    public required string RawLine { get; init; }
}

/// <summary>
/// Defines one explicit object override.
/// </summary>
internal sealed class DeckObjectOverrideDefinition
{
    /// <summary>
    /// Gets the 1-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the target placement reference.
    /// </summary>
    public required DeckObjectPlacement Placement { get; init; }

    /// <summary>
    /// Gets the override arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }

    /// <summary>
    /// Gets the optional override payload text.
    /// </summary>
    public string? Payload { get; init; }
}

/// <summary>
/// Defines one table block.
/// </summary>
internal sealed class DeckTableBlock
{
    /// <summary>
    /// Gets the table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the 1-based start line number for the table header.
    /// </summary>
    public required int StartLineNumber { get; init; }

    /// <summary>
    /// Gets or sets the 1-based end line number for the closing <c>[end]</c>.
    /// </summary>
    public int EndLineNumber { get; set; }

    /// <summary>
    /// Gets the table anchor when the table uses geometry addressing.
    /// </summary>
    public DeckGridAnchor? Anchor { get; init; }

    /// <summary>
    /// Gets the table size when the table uses geometry addressing.
    /// </summary>
    public DeckGridSize? Size { get; init; }

    /// <summary>
    /// Gets the target name when the table binds to a named layout target.
    /// </summary>
    public string? TargetName { get; init; }

    /// <summary>
    /// Gets the optional target index when the table binds to a repeated target.
    /// </summary>
    public string? TargetIndex { get; init; }

    /// <summary>
    /// Gets the table arguments.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }

    /// <summary>
    /// Gets the parsed row cells.
    /// </summary>
    public List<IReadOnlyList<string>> Rows { get; } = [];

    /// <summary>
    /// Gets or sets whether a markdown-style header separator row was present.
    /// </summary>
    public bool HasHeaderSeparator { get; set; }
}

/// <summary>
/// Defines one chart block.
/// </summary>
internal sealed class DeckChartBlock
{
    /// <summary>
    /// Gets the chart name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the 1-based start line number for the chart header.
    /// </summary>
    public required int StartLineNumber { get; init; }

    /// <summary>
    /// Gets or sets the 1-based end line number for the closing <c>[end]</c>.
    /// </summary>
    public int EndLineNumber { get; set; }

    /// <summary>
    /// Gets the chart type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the chart anchor when the chart uses geometry addressing.
    /// </summary>
    public DeckGridAnchor? Anchor { get; init; }

    /// <summary>
    /// Gets the chart size when the chart uses geometry addressing.
    /// </summary>
    public DeckGridSize? Size { get; init; }

    /// <summary>
    /// Gets the target name when the chart binds to a named layout target.
    /// </summary>
    public string? TargetName { get; init; }

    /// <summary>
    /// Gets the optional target index when the chart binds to a repeated target.
    /// </summary>
    public string? TargetIndex { get; init; }

    /// <summary>
    /// Gets any extra chart arguments beyond the required header values.
    /// </summary>
    public required DeckDirectiveArguments Arguments { get; init; }

    /// <summary>
    /// Gets the chart series.
    /// </summary>
    public List<DeckChartSeries> Series { get; } = [];
}

/// <summary>
/// Defines one chart series line.
/// </summary>
internal sealed class DeckChartSeries
{
    /// <summary>
    /// Gets the series type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the raw category items.
    /// </summary>
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>
    /// Gets the raw numeric values.
    /// </summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>
    /// Gets the axis binding.
    /// </summary>
    public string Axis { get; init; } = "primary";

    /// <summary>
    /// Gets the optional series color.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets whether data labels were requested.
    /// </summary>
    public bool Labels { get; init; }
}

/// <summary>
/// Describes a compact object placement.
/// </summary>
internal sealed class DeckObjectPlacement
{
    /// <summary>
    /// Gets the addressing mode.
    /// </summary>
    public required DeckObjectAddressingMode Mode { get; init; }

    /// <summary>
    /// Gets the anchor when the placement uses geometry addressing.
    /// </summary>
    public DeckGridAnchor? Anchor { get; init; }

    /// <summary>
    /// Gets the span when the placement uses geometry addressing.
    /// </summary>
    public DeckGridSize? Span { get; init; }

    /// <summary>
    /// Gets the named target when the placement uses target addressing.
    /// </summary>
    public string? TargetName { get; init; }

    /// <summary>
    /// Gets the optional target index selector.
    /// </summary>
    public string? TargetIndex { get; init; }
}

/// <summary>
/// Identifies how a compact object line addresses its destination.
/// </summary>
internal enum DeckObjectAddressingMode
{
    /// <summary>
    /// The object uses an explicit anchor and span.
    /// </summary>
    Geometry,

    /// <summary>
    /// The object binds to a named target.
    /// </summary>
    Target,
}

/// <summary>
/// Represents one logical grid anchor.
/// </summary>
internal readonly record struct DeckGridAnchor(int ColumnNumber, int RowNumber);

/// <summary>
/// Represents one logical grid size.
/// </summary>
internal readonly record struct DeckGridSize(double Width, double Height);

/// <summary>
/// Represents one logical grid range.
/// </summary>
internal readonly record struct DeckGridRange(DeckGridAnchor Start, DeckGridAnchor End)
{
    /// <summary>
    /// Gets the inclusive width of the range.
    /// </summary>
    public double Width => End.ColumnNumber - Start.ColumnNumber + 1;

    /// <summary>
    /// Gets the inclusive height of the range.
    /// </summary>
    public double Height => End.RowNumber - Start.RowNumber + 1;
}