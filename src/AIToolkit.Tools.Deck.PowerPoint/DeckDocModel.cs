namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Holds the parsed DeckDoc needed by the PowerPoint writer.
/// </summary>
internal sealed class DeckDocModel
{
    /// <summary>
    /// Gets the presentation title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the shared header lines that appeared before the first slide.
    /// </summary>
    public List<string> HeaderLines { get; } = [];

    /// <summary>
    /// Gets the named shared assets declared through <c>[asset ...]</c> directives.
    /// </summary>
    public Dictionary<string, string> SharedAssets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the slides in source order.
    /// </summary>
    public List<DeckDocSlideModel> Slides { get; } = [];
}

/// <summary>
/// Represents the renderable parts of one DeckDoc slide.
/// </summary>
internal sealed class DeckDocSlideModel
{
    /// <summary>
    /// Gets the 1-based slide number.
    /// </summary>
    public required int SlideNumber { get; init; }

    /// <summary>
    /// Gets the logical slide title from the <c>==</c> heading.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets or sets the visible title text when a title object is present.
    /// </summary>
    public string? VisibleTitle { get; set; }

    /// <summary>
    /// Gets the raw slide lines excluding the slide heading.
    /// </summary>
    public List<string> RawLines { get; } = [];

    /// <summary>
    /// Gets the plain-text content lines that can be rendered into a body placeholder.
    /// </summary>
    public List<string> TextLines { get; } = [];

    /// <summary>
    /// Gets the image references declared on the slide.
    /// </summary>
    public List<DeckDocImageModel> Images { get; } = [];
}

/// <summary>
/// Represents one image object that the writer may try to render into a slide.
/// </summary>
internal sealed class DeckDocImageModel
{
    /// <summary>
    /// Gets the shared asset name referenced by the image object.
    /// </summary>
    public required string AssetName { get; init; }

    /// <summary>
    /// Gets the optional alternative text supplied in DeckDoc.
    /// </summary>
    public string? AltText { get; init; }
}
