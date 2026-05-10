namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Configures the PowerPoint deck handler.
/// </summary>
/// <remarks>
/// The PowerPoint provider can operate on local Open XML presentation packages, hosted Microsoft 365 references, or
/// both. These options control which reference types are allowed and how reads behave when a presentation does or does
/// not contain an embedded canonical DeckDoc payload.
/// </remarks>
public sealed class PowerPointDeckHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether local PowerPoint file paths should be handled by this tool set.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical DeckDoc should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedDeckDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external PowerPoint files that do
    /// not contain embedded canonical DeckDoc.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional Microsoft 365 hosted-presentation settings used when <see cref="PowerPointDeckTools"/>
    /// creates functions.
    /// </summary>
    /// <seealso cref="PowerPointDeckM365Options"/>
    public PowerPointDeckM365Options? M365 { get; init; }
}
