using Microsoft.Extensions.Logging;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Configures the generic <c>Deck_*</c> tools created by <see cref="DeckTools"/>.
/// </summary>
/// <remarks>
/// These options define the workspace defaults, handler pipeline, resolver pipeline, and result limits shared by all
/// generic Deck operations. Provider-specific builders typically clone and extend this type instead of introducing a
/// separate option system.
/// </remarks>
public sealed class DeckToolsOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative Deck paths.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the optional resolver that can map a Deck reference such as a path, URL, or ID to a resolved Deck resource.
    /// </summary>
    public IDeckReferenceResolver? ReferenceResolver { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of DeckDoc lines returned by Deck reads when no explicit range is provided.
    /// </summary>
    /// <remarks>
    /// Partial reads are tracked as partial view state, so callers must perform a full read before exact-string edits or
    /// full rewrites of an existing Deck.
    /// </remarks>
    public int MaxReadLines { get; init; } = 2_000;

    /// <summary>
    /// Gets or sets the maximum number of slides returned by deck reads when no explicit slide range is provided.
    /// </summary>
    public int MaxReadSlides { get; init; } = 8;

    /// <summary>
    /// Gets or sets the maximum file size allowed for exact DeckDoc edits.
    /// </summary>
    public long MaxEditFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of results returned by Deck content searches.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets the Deck handlers used to convert provider-specific files to and from canonical DeckDoc.
    /// </summary>
    /// <remarks>
    /// Handlers supplied here are evaluated before handlers resolved from dependency injection.
    /// </remarks>
    public IEnumerable<IDeckHandler>? Handlers { get; init; }

    /// <summary>
    /// Gets or sets the provider-specific prompt contributors that extend the generic Deck tool guidance.
    /// </summary>
    public IEnumerable<IDeckToolPromptProvider>? PromptProviders { get; init; }

    /// <summary>
    /// Gets or sets the asset interceptor used by deck asset tools and by providers resolving <c>[asset ...]</c> references.
    /// </summary>
    public IDeckAssetInterceptor? AssetInterceptor { get; init; }

    /// <summary>
    /// Gets or sets the default asset session identifier used for session-scoped asset search and resolution.
    /// </summary>
    public string? AssetSessionId { get; init; }

    /// <summary>
    /// Gets or sets the optional template store used by template-list and template-get tools.
    /// </summary>
    public IDeckTemplateStore? TemplateStore { get; init; }

    /// <summary>
    /// Gets or sets an optional logger factory used when tool invocations should be logged without relying on per-call services.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether large content parameters such as write/edit DeckDoc payloads should be included in tool invocation logs.
    /// </summary>
    /// <remarks>
    /// This is disabled by default so logs do not capture Deck content unless the host explicitly opts in.
    /// </remarks>
    public bool LogContentParameters { get; init; }
}

