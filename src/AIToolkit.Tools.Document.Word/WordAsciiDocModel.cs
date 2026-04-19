namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Captures the parsed AsciiDoc document state needed by the Word renderer.
/// </summary>
/// <remarks>
/// The parser produces this model as a simplified, Word-oriented projection of AsciiDoc. The renderer then consumes it to
/// generate WordprocessingML without reparsing raw text. The model deliberately keeps only the constructs that the Word
/// toolchain can render or re-import consistently.
/// </remarks>
internal sealed class WordAsciiDocDocumentModel(
    string? title,
    IReadOnlyDictionary<string, string?> attributes,
    IReadOnlyList<WordAsciiDocBlockModel> blocks)
{
    /// <summary>
    /// Gets the document title parsed from the AsciiDoc header, when present.
    /// </summary>
    public string? Title { get; } = title;

    /// <summary>
    /// Gets the document-level AsciiDoc attributes visible to the parser and renderer.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Attributes { get; } = attributes;

    /// <summary>
    /// Gets the parsed block sequence in document order.
    /// </summary>
    public IReadOnlyList<WordAsciiDocBlockModel> Blocks { get; } = blocks;

    /// <summary>
    /// Determines whether a document attribute is logically enabled.
    /// </summary>
    /// <param name="name">The attribute name to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the attribute exists and is not explicitly set to <c>false</c>; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public bool HasAttribute(string name) =>
        Attributes.TryGetValue(name, out var value)
        && (value is null || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the raw value for a document attribute, if present.
    /// </summary>
    /// <param name="name">The attribute name to resolve.</param>
    /// <returns>The raw attribute value, or <see langword="null"/> when the attribute is not present.</returns>
    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Carries parsed block metadata such as roles, positional attributes, named attributes, and titles.
/// </summary>
/// <remarks>
/// Metadata is accumulated before the next block is parsed so adjacent attribute lines, titles, and role declarations can
/// be applied to headings, paragraphs, tables, and delimited blocks consistently.
/// </remarks>
internal sealed class WordAsciiDocBlockMetadata(
    string? title,
    IReadOnlyList<string> roles,
    IReadOnlyList<string> positionalAttributes,
    IReadOnlyDictionary<string, string> namedAttributes)
{
    /// <summary>
    /// Gets a reusable empty metadata instance.
    /// </summary>
    public static WordAsciiDocBlockMetadata Empty { get; } = new(null, [], [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the optional block title parsed from a leading <c>.Title</c> line.
    /// </summary>
    public string? Title { get; } = title;

    /// <summary>
    /// Gets the role names applied to the block.
    /// </summary>
    public IReadOnlyList<string> Roles { get; } = roles;

    /// <summary>
    /// Gets positional block attributes preserved for renderer-specific interpretation.
    /// </summary>
    public IReadOnlyList<string> PositionalAttributes { get; } = positionalAttributes;

    /// <summary>
    /// Gets named block attributes such as <c>id</c> or renderer-specific options.
    /// </summary>
    public IReadOnlyDictionary<string, string> NamedAttributes { get; } = namedAttributes;

    /// <summary>
    /// Determines whether the metadata contains the specified role.
    /// </summary>
    /// <param name="role">The role name to look for.</param>
    /// <returns><see langword="true"/> when the block metadata contains the role; otherwise, <see langword="false"/>.</returns>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tries to get a named attribute from the metadata.
    /// </summary>
    /// <param name="name">The attribute name to resolve.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the resolved value.</param>
    /// <returns><see langword="true"/> when the attribute is present; otherwise, <see langword="false"/>.</returns>
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

/// <summary>
/// Represents a parsed block-level AsciiDoc construct.
/// </summary>
/// <remarks>
/// Concrete block records capture just enough information for <see cref="WordAsciiDocRenderer"/> to recreate the visible
/// Word body and for <see cref="WordAsciiDocImporter"/> to round-trip equivalent shapes when possible.
/// </remarks>
internal abstract record WordAsciiDocBlockModel(WordAsciiDocBlockMetadata Metadata);

/// <summary>
/// Represents a parsed heading block.
/// </summary>
internal sealed record WordAsciiDocHeadingBlockModel(
    int Level,
    string Text,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed paragraph block.
/// </summary>
internal sealed record WordAsciiDocParagraphBlockModel(
    string Text,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a single parsed list item, including optional continuation text.
/// </summary>
internal sealed record WordAsciiDocListItemModel(
    WordAsciiDocListKind Kind,
    int Level,
    string Text,
    string? ContinuationText = null);

/// <summary>
/// Represents a parsed list block.
/// </summary>
internal sealed record WordAsciiDocListBlockModel(
    IReadOnlyList<WordAsciiDocListItemModel> Items,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed table block.
/// </summary>
internal sealed record WordAsciiDocTableBlockModel(
    bool HasHeader,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed delimited block such as source, quote, literal, or admonition content.
/// </summary>
internal sealed record WordAsciiDocDelimitedBlockModel(
    WordAsciiDocDelimitedBlockKind Kind,
    string? Label,
    string? Language,
    string Content,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed block macro such as image, audio, or video.
/// </summary>
internal sealed record WordAsciiDocMacroBlockModel(
    WordAsciiDocMacroKind Kind,
    string Target,
    string? Label,
    WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed page-break marker.
/// </summary>
internal sealed record WordAsciiDocPageBreakBlockModel(WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Represents a parsed thematic break marker.
/// </summary>
internal sealed record WordAsciiDocThematicBreakBlockModel(WordAsciiDocBlockMetadata Metadata)
    : WordAsciiDocBlockModel(Metadata);

/// <summary>
/// Identifies the list style used by a parsed list item.
/// </summary>
internal enum WordAsciiDocListKind
{
    Unordered,
    Ordered,
    ChecklistChecked,
    ChecklistUnchecked,
    Callout,
}

/// <summary>
/// Identifies the delimited-block flavor parsed from AsciiDoc.
/// </summary>
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

/// <summary>
/// Identifies the supported block macro category.
/// </summary>
internal enum WordAsciiDocMacroKind
{
    Image,
    Audio,
    Video,
}

/// <summary>
/// Represents a parsed inline AsciiDoc fragment inside a block.
/// </summary>
internal abstract record WordAsciiDocInlineModel;

/// <summary>
/// Represents a plain text inline fragment.
/// </summary>
internal sealed record WordAsciiDocTextInlineModel(string Text) : WordAsciiDocInlineModel;

/// <summary>
/// Represents a hard line break embedded inside inline content.
/// </summary>
internal sealed record WordAsciiDocLineBreakInlineModel() : WordAsciiDocInlineModel;

/// <summary>
/// Represents styled or linked inline content composed from nested inline fragments.
/// </summary>
/// <remarks>
/// Inline styling is flattened into this record so the renderer can merge block roles, inline roles, and hyperlink state
/// while creating WordprocessingML runs.
/// </remarks>
internal sealed record WordAsciiDocStyledInlineModel(
    IReadOnlyList<WordAsciiDocInlineModel> Children,
    bool Bold = false,
    bool Italic = false,
    bool Code = false,
    bool Highlight = false,
    string? HyperlinkUrl = null,
    IReadOnlyList<string>? Roles = null)
    : WordAsciiDocInlineModel;
