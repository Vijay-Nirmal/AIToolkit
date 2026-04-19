namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Captures the parsed AsciiDoc document state needed by the Word renderer.
/// </summary>
internal sealed class WordAsciiDocDocumentModel(
    string? title,
    IReadOnlyDictionary<string, string?> attributes,
    IReadOnlyList<WordAsciiDocBlockModel> blocks)
{
    public string? Title { get; } = title;

    public IReadOnlyDictionary<string, string?> Attributes { get; } = attributes;

    public IReadOnlyList<WordAsciiDocBlockModel> Blocks { get; } = blocks;

    public bool HasAttribute(string name) =>
        Attributes.TryGetValue(name, out var value)
        && (value is null || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));

    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Carries parsed block metadata such as roles, positional attributes, named attributes, and titles.
/// </summary>
internal sealed class WordAsciiDocBlockMetadata(
    string? title,
    IReadOnlyList<string> roles,
    IReadOnlyList<string> positionalAttributes,
    IReadOnlyDictionary<string, string> namedAttributes)
{
    public static WordAsciiDocBlockMetadata Empty { get; } = new(null, [], [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public string? Title { get; } = title;

    public IReadOnlyList<string> Roles { get; } = roles;

    public IReadOnlyList<string> PositionalAttributes { get; } = positionalAttributes;

    public IReadOnlyDictionary<string, string> NamedAttributes { get; } = namedAttributes;

    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool TryGetNamedAttribute(string name, out string value)
    {
        if (NamedAttributes.TryGetValue(name, out var existing))
        {
            value = existing;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

internal abstract record WordAsciiDocBlockModel(WordAsciiDocBlockMetadata Metadata);

internal sealed record WordAsciiDocHeadingBlockModel(
    int Level,
    string Text,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocParagraphBlockModel(
    string Text,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocListItemModel(
    WordAsciiDocListKind Kind,
    int Level,
    string Text,
    string? ContinuationText = null);

internal sealed record WordAsciiDocListBlockModel(
    IReadOnlyList<WordAsciiDocListItemModel> Items,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocTableBlockModel(
    bool HasHeader,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocDelimitedBlockModel(
    WordAsciiDocDelimitedBlockKind Kind,
    string? Label,
    string? Language,
    string Content,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocMacroBlockModel(
    WordAsciiDocMacroKind Kind,
    string Target,
    string? Label,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocPageBreakBlockModel(WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal sealed record WordAsciiDocThematicBreakBlockModel(WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

internal enum WordAsciiDocListKind
{
    Unordered,
    Ordered,
    ChecklistChecked,
    ChecklistUnchecked,
    Callout,
}

internal enum WordAsciiDocDelimitedBlockKind
{
    Admonition,
    Source,
    Quote,
    Verse,
    Example,
    Literal,
    Open,
}

internal enum WordAsciiDocMacroKind
{
    Image,
    Audio,
    Video,
}

internal abstract record WordAsciiDocInlineModel;

internal sealed record WordAsciiDocTextInlineModel(string Text) : WordAsciiDocInlineModel;

internal sealed record WordAsciiDocLineBreakInlineModel() : WordAsciiDocInlineModel;

internal sealed record WordAsciiDocStyledInlineModel(
    IReadOnlyList<WordAsciiDocInlineModel> Children,
    bool Bold = false,
    bool Italic = false,
    bool Code = false,
    bool Highlight = false,
    string? HyperlinkUrl = null,
    IReadOnlyList<string>? Roles = null)
    : WordAsciiDocInlineModel;