namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Represents the common success and message fields returned by PowerPoint-specific helper utilities and tools.
/// </summary>
/// <param name="Success"><see langword="true"/> when the operation completed successfully.</param>
/// <param name="Message">Optional guidance or error information.</param>
public abstract record PowerPointDeckOperationResult(bool Success, string? Message = null);

/// <summary>
/// Describes one exported slide image produced from a PowerPoint presentation.
/// </summary>
/// <param name="SlideNumber">The 1-based slide number.</param>
/// <param name="ImagePath">The absolute path to the exported PNG file.</param>
public sealed record PowerPointDeckSlideImage(
    int SlideNumber,
    string ImagePath);

/// <summary>
/// Represents the outcome of converting a PowerPoint presentation into canonical DeckDoc.
/// </summary>
/// <param name="Success"><see langword="true"/> when the conversion completed successfully.</param>
/// <param name="PresentationReference">The resolved presentation reference that was read.</param>
/// <param name="DeckDoc">The canonical DeckDoc recovered from the presentation.</param>
/// <param name="TotalSlideCount">The total number of slides recovered from the presentation.</param>
/// <param name="PreservesDeckDocRoundTrip">Whether future writes and reads are expected to preserve the same DeckDoc exactly.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record PowerPointDeckReadResult(
    bool Success,
    string PresentationReference,
    string? DeckDoc,
    int TotalSlideCount,
    bool PreservesDeckDocRoundTrip,
    string? Message = null)
    : PowerPointDeckOperationResult(Success, Message);

/// <summary>
/// Represents the outcome of converting canonical DeckDoc into a PowerPoint presentation.
/// </summary>
/// <param name="Success"><see langword="true"/> when the conversion completed successfully.</param>
/// <param name="PresentationReference">The resolved presentation reference that was written.</param>
/// <param name="DeckDoc">The canonical DeckDoc recovered after the write completed.</param>
/// <param name="PreservesDeckDocRoundTrip">Whether future reads are expected to recover the same DeckDoc exactly.</param>
/// <param name="Message">Optional provider guidance or error information.</param>
public sealed record PowerPointDeckWriteResult(
    bool Success,
    string PresentationReference,
    string? DeckDoc,
    bool PreservesDeckDocRoundTrip,
    string? Message = null)
    : PowerPointDeckOperationResult(Success, Message);

/// <summary>
/// Represents the outcome of exporting one PNG per slide from a PowerPoint presentation.
/// </summary>
/// <param name="Success"><see langword="true"/> when the export completed successfully.</param>
/// <param name="PresentationReference">The resolved presentation reference that was exported.</param>
/// <param name="OutputDirectory">The absolute directory containing the exported PNG files.</param>
/// <param name="Slides">The exported slide images in slide order.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record PowerPointDeckSlideImageExportResult(
    bool Success,
    string PresentationReference,
    string OutputDirectory,
    PowerPointDeckSlideImage[] Slides,
    string? Message = null)
    : PowerPointDeckOperationResult(Success, Message);

/// <summary>
/// Configures how a PowerPoint presentation is exported to slide images.
/// </summary>
public sealed class PowerPointDeckSlideImageExportOptions
{
    /// <summary>
    /// Gets or sets the PowerPoint tool options used to resolve the source presentation reference.
    /// </summary>
    public PowerPointDeckToolSetOptions? ToolOptions { get; init; }

    /// <summary>
    /// Gets or sets the destination directory for the exported PNG files.
    /// </summary>
    /// <remarks>
    /// When omitted, local presentations export beside the source file and hosted references export under the current
    /// working directory.
    /// </remarks>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// Gets or sets the optional export width in pixels.
    /// </summary>
    /// <remarks>
    /// When either <see cref="Width"/> or <see cref="Height"/> is specified, both values must be greater than zero.
    /// Leave both values at <c>0</c> to let PowerPoint choose its default export size.
    /// </remarks>
    public int Width { get; init; }

    /// <summary>
    /// Gets or sets the optional export height in pixels.
    /// </summary>
    /// <remarks>
    /// When either <see cref="Width"/> or <see cref="Height"/> is specified, both values must be greater than zero.
    /// Leave both values at <c>0</c> to let PowerPoint choose its default export size.
    /// </remarks>
    public int Height { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether an existing export directory can be reused.
    /// </summary>
    public bool Force { get; init; }
}

/// <summary>
/// Configures AI-assisted template generation from an input PowerPoint presentation.
/// </summary>
public sealed class PowerPointDeckTemplateGenerationOptions
{
    /// <summary>
    /// Gets or sets the PowerPoint tool options used for DeckDoc conversion, spec lookup, and slide export.
    /// </summary>
    public PowerPointDeckToolSetOptions? ToolOptions { get; init; }

    /// <summary>
    /// Gets or sets the presentation reference where the generated template preview presentation should be written.
    /// </summary>
    /// <remarks>
    /// When omitted, the utility writes a preview file next to the source presentation using a <c>-template-preview</c>
    /// suffix.
    /// </remarks>
    public string? GeneratedPresentationReference { get; init; }

    /// <summary>
    /// Gets or sets an optional directory for the exported source slide PNG files.
    /// </summary>
    public string? SourceSlideImageOutputDirectory { get; init; }

    /// <summary>
    /// Gets or sets an optional directory for the exported generated-preview slide PNG files.
    /// </summary>
    public string? GeneratedSlideImageOutputDirectory { get; init; }

    /// <summary>
    /// Gets or sets optional additional instructions appended to the template-authoring prompt.
    /// </summary>
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// Gets or sets the DeckDoc specification lookup query used to collect guidance before drafting the template.
    /// </summary>
    public string SpecificationLookupQuery { get; init; } = "layout style split grid stack asset image icon shape line text list table chart group animate notes transition template";

    /// <summary>
    /// Gets or sets the maximum number of correction rounds attempted after the initial template draft.
    /// </summary>
    public int MaxCorrectionRounds { get; init; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether exported slide PNG files should be attached to the review prompts.
    /// </summary>
    /// <remarks>
    /// Leave this enabled for vision-capable chat clients. Set it to <see langword="false"/> when the host uses a
    /// text-only model and relies only on the textual slide summary and exported file paths.
    /// </remarks>
    public bool AttachSlideImagesToPrompts { get; init; } = true;

    /// <summary>
    /// Gets or sets the slide-export width used during visual comparison.
    /// </summary>
    public int ExportWidth { get; init; } = 1600;

    /// <summary>
    /// Gets or sets the slide-export height used during visual comparison.
    /// </summary>
    public int ExportHeight { get; init; } = 900;
}

/// <summary>
/// Represents the outcome of generating a reusable DeckDoc template and rendered preview from an existing PowerPoint deck.
/// </summary>
/// <param name="Success"><see langword="true"/> when a similar-enough template preview was produced.</param>
/// <param name="SourcePresentationReference">The resolved source presentation reference.</param>
/// <param name="GeneratedPresentationReference">The resolved preview presentation reference generated from the template DeckDoc.</param>
/// <param name="TemplateDeckDoc">The final template DeckDoc produced by the utility.</param>
/// <param name="IterationCount">The number of rendered comparison rounds that completed.</param>
/// <param name="SimilarEnough">Whether the final generated preview was judged similar enough to the source presentation.</param>
/// <param name="Summary">A short summary of the template-generation outcome.</param>
/// <param name="Issues">The remaining issues reported by the model for the final iteration.</param>
/// <param name="SourceSlideImages">The exported source slide images used during comparison.</param>
/// <param name="GeneratedSlideImages">The exported generated-preview slide images used during comparison.</param>
/// <param name="Message">Optional guidance or error information.</param>
public sealed record PowerPointDeckTemplateGenerationResult(
    bool Success,
    string SourcePresentationReference,
    string GeneratedPresentationReference,
    string? TemplateDeckDoc,
    int IterationCount,
    bool SimilarEnough,
    string Summary,
    string[] Issues,
    PowerPointDeckSlideImageExportResult? SourceSlideImages,
    PowerPointDeckSlideImageExportResult? GeneratedSlideImages,
    string? Message = null)
    : PowerPointDeckOperationResult(Success, Message);