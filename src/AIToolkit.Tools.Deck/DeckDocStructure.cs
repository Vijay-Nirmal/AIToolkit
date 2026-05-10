namespace AIToolkit.Tools.Deck;

/// <summary>
/// Represents a parse error in DeckDoc text with an exact line and column.
/// </summary>
public sealed class DeckDocParseException : Exception
{
    /// <summary>
    /// Initializes a new parse exception.
    /// </summary>
    public DeckDocParseException(string message, int lineNumber, int columnNumber = 1, string? queryHint = null)
        : base(message)
    {
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        QueryHint = queryHint;
    }

    /// <summary>
    /// Gets the 1-based line number where parsing failed.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the 1-based column number where parsing failed.
    /// </summary>
    public int ColumnNumber { get; }

    /// <summary>
    /// Gets an optional focused lookup hint for spec guidance.
    /// </summary>
    public string? QueryHint { get; }
}

/// <summary>
/// Represents the slide boundaries of a canonical DeckDoc document.
/// </summary>
internal sealed class DeckDocStructure
{
    private DeckDocStructure(string title, string[] lines, DeckSlideSection[] slides)
    {
        Title = title;
        Lines = lines;
        Slides = slides;
    }

    /// <summary>
    /// Gets the presentation title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets all canonical DeckDoc lines.
    /// </summary>
    public string[] Lines { get; }

    /// <summary>
    /// Gets the slide sections in source order.
    /// </summary>
    public DeckSlideSection[] Slides { get; }

    /// <summary>
    /// Parses canonical DeckDoc text into slide sections.
    /// </summary>
    public static DeckDocStructure Parse(string deckDoc)
    {
        var parsed = DeckDocSyntaxParser.Parse(deckDoc);
        var slides = parsed.Slides
            .Select(static slide => new DeckSlideSection(
                slide.SlideNumber,
                slide.Title,
                slide.StartLineNumber,
                slide.EndLineNumber,
                slide.EndLineNumber >= slide.StartLineNumber
                    ? slide.StartLineNumber - 1 < slide.EndLineNumber
                        ? Array.Empty<string>()
                        : Array.Empty<string>()
                    : Array.Empty<string>()))
            .ToArray();

        for (var index = 0; index < parsed.Slides.Count; index++)
        {
            slides[index] = new DeckSlideSection(
                parsed.Slides[index].SlideNumber,
                parsed.Slides[index].Title,
                parsed.Slides[index].StartLineNumber,
                parsed.Slides[index].EndLineNumber,
                parsed.Lines[(parsed.Slides[index].StartLineNumber - 1)..parsed.Slides[index].EndLineNumber]);
        }

        return new DeckDocStructure(parsed.Title, parsed.Lines, slides);
    }

    /// <summary>
    /// Returns the slide number containing a specific 1-based line number, when any.
    /// </summary>
    public DeckSlideSection? FindSlideForLine(int lineNumber) =>
        Slides.FirstOrDefault(slide => lineNumber >= slide.StartLineNumber && lineNumber <= slide.EndLineNumber);

    /// <summary>
    /// Gets a line selection that preserves document header lines and returns only the requested slide range.
    /// </summary>
    public IReadOnlyList<(int LineNumber, string Line)> ReadSlideRange(int slideOffset, int slideLimit)
    {
        var normalizedOffset = Math.Clamp(slideOffset, 1, Slides.Length);
        var normalizedLimit = Math.Clamp(slideLimit, 1, Math.Max(1, Slides.Length));
        var firstSlide = Slides[normalizedOffset - 1];
        var lastSlide = Slides[Math.Min(Slides.Length, normalizedOffset + normalizedLimit - 1) - 1];

        var selected = new List<(int LineNumber, string Line)>();
        var headerEnd = firstSlide.StartLineNumber - 1;
        for (var index = 0; index < headerEnd; index++)
        {
            selected.Add((index + 1, Lines[index]));
        }

        for (var index = firstSlide.StartLineNumber - 1; index < lastSlide.EndLineNumber; index++)
        {
            selected.Add((index + 1, Lines[index]));
        }

        return selected;
    }

}

/// <summary>
/// Describes one slide section within canonical DeckDoc.
/// </summary>
internal sealed record DeckSlideSection(
    int SlideNumber,
    string Title,
    int StartLineNumber,
    int EndLineNumber,
    string[] Lines);