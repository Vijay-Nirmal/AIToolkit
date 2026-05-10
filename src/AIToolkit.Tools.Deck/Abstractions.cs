namespace AIToolkit.Tools.Deck;

/// <summary>
/// Converts a provider-specific Deck representation to and from the canonical DeckDoc form used by the
/// <c>Deck_*</c> tools.
/// </summary>
/// <remarks>
/// Implementations bridge <see cref="DeckToolService"/> to concrete providers such as local Excel packages or
/// resolver-backed hosted Decks. Reads should return the DeckDoc that the model will reason over, while writes
/// should persist an equivalent provider Deck and report whether that round trip remains lossless.
/// </remarks>
public interface IDeckHandler
{
    /// <summary>
    /// Gets the stable provider name surfaced by tool results and diagnostics.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the file extensions supported by this handler, including the leading period.
    /// </summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Determines whether this handler can operate on the resolved Deck reference.
    /// </summary>
    /// <param name="context">The resolved Deck context, including extension, resolver state, and active options.</param>
    /// <returns><see langword="true"/> when this handler should own the operation; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(DeckHandlerContext context);

    /// <summary>
    /// Reads a provider-specific Deck and returns canonical DeckDoc.
    /// </summary>
    /// <param name="context">The resolved Deck context describing the source Deck and any resolver-defined state.</param>
    /// <param name="cancellationToken">A token that cancels the read before or during provider I/O.</param>
    /// <returns>A <see cref="DeckReadResponse"/> containing the DeckDoc payload used by downstream tool flows.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Deck can be opened but not interpreted by the provider.</exception>
    ValueTask<DeckReadResponse> ReadAsync(
        DeckHandlerContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes canonical DeckDoc into the provider-specific Deck format.
    /// </summary>
    /// <param name="context">The resolved Deck context describing the write target and any resolver-defined state.</param>
    /// <param name="DeckDoc">The canonical DeckDoc payload to persist.</param>
    /// <param name="cancellationToken">A token that cancels the write before or during provider I/O.</param>
    /// <returns>
    /// A <see cref="DeckWriteResponse"/> describing the output format and whether a later read can recover the same
    /// DeckDoc exactly.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the target cannot be created or updated in the provider format.</exception>
    ValueTask<DeckWriteResponse> WriteAsync(
        DeckHandlerContext context,
        string DeckDoc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides resolved metadata, resolver state, and tool configuration to an <see cref="IDeckHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="DeckToolService"/> creates this context after an <see cref="IDeckReferenceResolver"/> maps the
/// user-supplied reference to a concrete resource. Handlers use it to inspect the resolved extension, open streams,
/// discover provider-specific state, and honor the active <see cref="DeckToolsOptions"/>.
/// </remarks>
public sealed class DeckHandlerContext
{
    private readonly DeckReferenceResolution _resolution;

    internal DeckHandlerContext(
        string deckReference,
        DeckReferenceResolution resolution,
        DeckToolsOptions options,
        IServiceProvider? services)
    {
        _resolution = resolution;
        DeckReference = deckReference;
        ResolvedReference = resolution.ResolvedReference;
        ReadStateKey = resolution.ReadStateKey;
        FilePath = resolution.FilePath;
        Extension = resolution.Extension;
        Length = resolution.Length;
        Version = resolution.Version;
        State = resolution.State;
        Options = options;
        Services = services;
    }

    /// <summary>
    /// Gets the raw Deck reference supplied to the tool.
    /// </summary>
    public string DeckReference { get; }

    /// <summary>
    /// Gets the resolved Deck reference surfaced by tool results and diagnostics.
    /// </summary>
    /// <remarks>
    /// This can differ from <see cref="DeckReference"/> when a resolver normalizes the input into a stable URL, ID,
    /// or absolute file path.
    /// </remarks>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the Deck tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the resolved local file path when the Deck is backed by a local file; otherwise, <see langword="null"/>.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the file extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the known Deck length in bytes when the resolver can supply it.
    /// </summary>
    public long? Length { get; }

    /// <summary>
    /// Gets an optional version token supplied by the resolver, such as an ETag or revision ID.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets an optional resolver-defined state object.
    /// </summary>
    /// <remarks>
    /// Providers use this to carry rich transport state such as hosted-Deck locations without exposing that detail
    /// through the generic public API.
    /// </remarks>
    public object? State { get; }

    /// <summary>
    /// Gets the active Deck tool options.
    /// </summary>
    public DeckToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// Checks whether the resolved Deck currently exists.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the existence check.</param>
    /// <returns><see langword="true"/> when the resolved Deck currently exists; otherwise, <see langword="false"/>.</returns>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _resolution.ExistsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved Deck.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>A readable stream over the resolved Deck.</returns>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved Deck.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>
    /// A writable stream for the resolved Deck. Some resolvers persist changes only when the stream is disposed.
    /// </returns>
    /// <seealso cref="DeckReferenceResolution.OpenWriteAsync(CancellationToken)"/>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenWriteAsync(cancellationToken);
}

/// <summary>
/// Represents canonical DeckDoc produced by a Deck handler.
/// </summary>
/// <param name="DeckDoc">The canonical DeckDoc text returned to the model.</param>
/// <param name="IsLosslessRoundTrip">
/// <see langword="true"/> when writing the same DeckDoc back through the same provider should preserve the content exactly.
/// </param>
/// <param name="SourceFormat">The provider-specific source format, such as <c>xlsx</c> or <c>google-sheets</c>.</param>
/// <param name="Message">Optional provider guidance about payload selection, best-effort import, or read caveats.</param>
public sealed record DeckReadResponse(
    string DeckDoc,
    bool IsLosslessRoundTrip,
    string? SourceFormat = null,
    string? Message = null);

/// <summary>
/// Represents the provider-specific outcome of writing canonical DeckDoc.
/// </summary>
/// <param name="PreservesDeckDocRoundTrip">
/// <see langword="true"/> when the provider stored enough metadata to reconstruct the same DeckDoc on a later read.
/// </param>
/// <param name="OutputFormat">The provider-specific format produced by the write.</param>
/// <param name="Message">Optional provider guidance describing write-side caveats or follow-up behavior.</param>
public sealed record DeckWriteResponse(
    bool PreservesDeckDocRoundTrip,
    string? OutputFormat = null,
    string? Message = null);

