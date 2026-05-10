using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Provides a flattened, host-friendly configuration surface for PowerPoint-backed <c>deck_*</c> tools.
/// </summary>
public sealed class PowerPointDeckToolSetOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative local presentation and asset paths.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets an optional additional resolver that can recognize URLs, IDs, or other non-path references.
    /// </summary>
    public IDeckReferenceResolver? ReferenceResolver { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of DeckDoc lines returned when no explicit line limit is provided.
    /// </summary>
    public int MaxReadLines { get; init; } = 2_000;

    /// <summary>
    /// Gets or sets the maximum number of slides returned when no explicit slide limit is provided.
    /// </summary>
    public int MaxReadSlides { get; init; } = 20;

    /// <summary>
    /// Gets or sets the maximum file size allowed for DeckDoc edits.
    /// </summary>
    public long MaxEditFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of grep and lookup results.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether direct local PowerPoint paths are enabled.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical DeckDoc should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedDeckDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external PowerPoint files.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the hosted Microsoft 365 PowerPoint settings. Leave <see langword="null"/> to disable hosted M365 support.
    /// </summary>
    public PowerPointDeckM365Options? M365 { get; init; }

    /// <summary>
    /// Gets or sets an optional asset interceptor used for <c>[asset ...]</c> resolution during PowerPoint writes.
    /// </summary>
    public IDeckAssetInterceptor? AssetInterceptor { get; init; }

    /// <summary>
    /// Gets or sets the optional session identifier used when resolving session-scoped assets.
    /// </summary>
    public string? AssetSessionId { get; init; }

    /// <summary>
    /// Gets or sets an optional template store. When omitted, the provider supplies built-in templates.
    /// </summary>
    public IDeckTemplateStore? TemplateStore { get; init; }

    /// <summary>
    /// Gets or sets an optional logger factory used when tool invocations should be logged.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether DeckDoc payload parameters should be included in logs.
    /// </summary>
    public bool LogContentParameters { get; init; }

    /// <summary>
    /// Gets or sets optional additional deck handlers appended after the PowerPoint handler.
    /// </summary>
    public IEnumerable<IDeckHandler>? AdditionalHandlers { get; init; }

    /// <summary>
    /// Gets or sets optional additional prompt providers appended after the PowerPoint prompt provider.
    /// </summary>
    public IEnumerable<IDeckToolPromptProvider>? AdditionalPromptProviders { get; init; }
}
