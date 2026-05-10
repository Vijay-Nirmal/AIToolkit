using System.Globalization;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Identifies which Deck tool is resolving a Deck reference.
/// </summary>
public enum DeckToolOperation
{
    /// <summary>
    /// Resolves a reference for a read operation.
    /// </summary>
    Read,
    /// <summary>
    /// Resolves a reference for a full Deck write.
    /// </summary>
    Write,
    /// <summary>
    /// Resolves a reference for an exact-string edit flow.
    /// </summary>
    Edit,
}

/// <summary>
/// Provides contextual information to an <see cref="IDeckReferenceResolver"/>.
/// </summary>
/// <remarks>
/// Resolvers receive the original user input together with the active working directory, current tool operation, and
/// configured Deck options. This allows provider packages to normalize references, reject unsupported aliases, or
/// carry provider-specific state into the handler pipeline.
/// </remarks>
public sealed class DeckReferenceResolverContext
{
    internal DeckReferenceResolverContext(
        string deckReference,
        string workingDirectory,
        DeckToolOperation operation,
        DeckToolsOptions options,
        IServiceProvider? services)
    {
        DeckReference = deckReference;
        WorkingDirectory = workingDirectory;
        Operation = operation;
        Options = options;
        Services = services;
    }

    /// <summary>
    /// Gets the raw Deck reference supplied to the tool.
    /// </summary>
    public string DeckReference { get; }

    /// <summary>
    /// Gets the working directory used by the Deck tools.
    /// </summary>
    /// <remarks>
    /// Relative references that fall back to file-based resolution are interpreted against this directory.
    /// </remarks>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the active tool operation.
    /// </summary>
    public DeckToolOperation Operation { get; }

    /// <summary>
    /// Gets the active Deck tool options.
    /// </summary>
    public DeckToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }
}

/// <summary>
/// Represents a resolved Deck resource that can be read from and written to.
/// </summary>
/// <remarks>
/// A resolution abstracts over both local files and resolver-backed resources such as hosted Decks. It carries the
/// normalized reference, read-state identity, optional version metadata, and delegates that open the actual streams used
/// by handlers. Resolver-backed writes may publish changes only when the returned stream is disposed.
/// </remarks>
public sealed class DeckReferenceResolution
{
    private readonly Func<CancellationToken, ValueTask<bool>> _existsAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openWriteAsync;

    private DeckReferenceResolution(
        string resolvedReference,
        string readStateKey,
        string extension,
        Func<CancellationToken, ValueTask<bool>> existsAsync,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync,
        Func<CancellationToken, ValueTask<Stream>> openWriteAsync,
        string? version,
        long? length,
        object? state,
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(resolvedReference))
        {
            throw new ArgumentException("A resolved reference is required.", nameof(resolvedReference));
        }

        if (string.IsNullOrWhiteSpace(readStateKey))
        {
            throw new ArgumentException("A read-state key is required.", nameof(readStateKey));
        }

        ResolvedReference = resolvedReference;
        ReadStateKey = readStateKey;
        Extension = NormalizeExtension(extension);
        Version = version;
        Length = length;
        State = state;
        FilePath = filePath;
        _existsAsync = existsAsync ?? throw new ArgumentNullException(nameof(existsAsync));
        _openReadAsync = openReadAsync ?? throw new ArgumentNullException(nameof(openReadAsync));
        _openWriteAsync = openWriteAsync ?? throw new ArgumentNullException(nameof(openWriteAsync));
    }

    /// <summary>
    /// Gets the resolved Deck reference surfaced by tool results and diagnostics.
    /// </summary>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the Deck tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the Deck extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets an optional version token supplied by the resolver, such as an ETag or revision ID.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the known Deck length in bytes when the resolver can supply it.
    /// </summary>
    public long? Length { get; }

    /// <summary>
    /// Gets an optional resolver-defined state object, such as an M365 or Google Docs handle.
    /// </summary>
    public object? State { get; }

    /// <summary>
    /// Gets the local file path when the resolution is backed by a local file; otherwise, <see langword="null"/>.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Checks whether the resolved Deck currently exists.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the existence check.</param>
    /// <returns><see langword="true"/> when the Deck exists; otherwise, <see langword="false"/>.</returns>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _existsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved Deck.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>A readable stream over the resolved Deck.</returns>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _openReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved Deck.
    /// </summary>
    /// <remarks>
    /// Custom resolvers may return a stream that persists changes on dispose, for example by uploading to a remote service.
    /// </remarks>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _openWriteAsync(cancellationToken);

    /// <summary>
    /// Creates a resolution backed by a local file.
    /// </summary>
    /// <param name="filePath">The local file path to normalize and expose as a Deck resource.</param>
    /// <returns>A resolution that opens local file streams and derives version metadata from the file system.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null, empty, or whitespace.</exception>
    public static DeckReferenceResolution CreateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        return new DeckReferenceResolution(
            resolvedReference: fullPath,
            readStateKey: OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath,
            extension: Path.GetExtension(fullPath),
            existsAsync: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(File.Exists(fullPath));
            },
            openReadAsync: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            },
            openWriteAsync: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return ValueTask.FromResult<Stream>(new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None));
            },
            version: fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : null,
            length: fileInfo.Exists ? fileInfo.Length : null,
            state: null,
            filePath: fullPath);
    }

    /// <summary>
    /// Creates a resolution backed by custom streams or other resolver-provided state.
    /// </summary>
    /// <param name="resolvedReference">The normalized reference surfaced back to tool callers.</param>
    /// <param name="extension">The logical extension used for handler selection.</param>
    /// <param name="existsAsync">A delegate that checks whether the resource currently exists.</param>
    /// <param name="openReadAsync">A delegate that opens a readable stream for the resource.</param>
    /// <param name="openWriteAsync">A delegate that opens a writable stream for the resource.</param>
    /// <param name="version">An optional version token such as an ETag or revision ID.</param>
    /// <param name="length">An optional resource length in bytes.</param>
    /// <param name="state">Optional provider-specific state carried through to <see cref="DeckHandlerContext.State"/>.</param>
    /// <param name="readStateKey">
    /// An optional stable key for stale-read detection. When omitted, <paramref name="resolvedReference"/> is used.
    /// </param>
    /// <returns>A stream-backed resolution suitable for resolver-managed resources.</returns>
    public static DeckReferenceResolution CreateStreamBacked(
        string resolvedReference,
        string extension,
        Func<CancellationToken, ValueTask<bool>> existsAsync,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync,
        Func<CancellationToken, ValueTask<Stream>> openWriteAsync,
        string? version = null,
        long? length = null,
        object? state = null,
        string? readStateKey = null) =>
        new(
            resolvedReference,
            string.IsNullOrWhiteSpace(readStateKey) ? resolvedReference : readStateKey,
            extension,
            existsAsync,
            openReadAsync,
            openWriteAsync,
            version,
            length,
            state,
            filePath: null);

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension[0] == '.' ? extension : "." + extension;
    }
}

/// <summary>
/// Resolves a Deck reference such as a path, URL, or ID to a Deck resource.
/// </summary>
/// <remarks>
/// Implementations can translate user-facing references into local file paths, hosted Deck handles, or custom
/// stream-backed resources. Returning <see langword="null"/> allows the generic tool flow to fall back to built-in path
/// resolution instead of treating the reference as an error.
/// </remarks>
public interface IDeckReferenceResolver
{
    /// <summary>
    /// Resolves the supplied Deck reference to a Deck resource.
    /// </summary>
    /// <remarks>
    /// Return <see langword="null"/> to fall back to the built-in path resolution behavior.
    /// </remarks>
    /// <param name="DeckReference">The user-supplied Deck reference to resolve.</param>
    /// <param name="context">The current resolver context, including the tool operation and working directory.</param>
    /// <param name="cancellationToken">A token that cancels resolution before or during provider I/O.</param>
    /// <returns>
    /// A <see cref="DeckReferenceResolution"/> when the resolver recognizes the reference; otherwise, <see langword="null"/>.
    /// </returns>
    ValueTask<DeckReferenceResolution?> ResolveAsync(
        string DeckReference,
        DeckReferenceResolverContext context,
        CancellationToken cancellationToken = default);
}

