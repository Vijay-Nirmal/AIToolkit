using System.Globalization;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Describes which document tool is resolving a document reference.
/// </summary>
public enum DocumentToolOperation
{
    Read,
    Write,
    Edit,
}

/// <summary>
/// Provides contextual information to an <see cref="IDocumentReferenceResolver"/>.
/// </summary>
public sealed class DocumentReferenceResolverContext
{
    internal DocumentReferenceResolverContext(
        string documentReference,
        string workingDirectory,
        DocumentToolOperation operation,
        DocumentToolsOptions options,
        IServiceProvider? services)
    {
        DocumentReference = documentReference;
        WorkingDirectory = workingDirectory;
        Operation = operation;
        Options = options;
        Services = services;
    }

    /// <summary>
    /// Gets the raw document reference supplied to the tool.
    /// </summary>
    public string DocumentReference { get; }

    /// <summary>
    /// Gets the working directory used by the document tools.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the active tool operation.
    /// </summary>
    public DocumentToolOperation Operation { get; }

    /// <summary>
    /// Gets the active document tool options.
    /// </summary>
    public DocumentToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }
}

/// <summary>
/// Represents a resolved document resource that can be read from and written to.
/// </summary>
public sealed class DocumentReferenceResolution
{
    private readonly Func<CancellationToken, ValueTask<bool>> _existsAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openWriteAsync;

    private DocumentReferenceResolution(
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
    /// Gets the resolved document reference surfaced by tool results and diagnostics.
    /// </summary>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the document tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the document extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets an optional version token supplied by the resolver, such as an ETag or revision ID.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the known document length in bytes when the resolver can supply it.
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
    /// Checks whether the resolved document currently exists.
    /// </summary>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _existsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved document.
    /// </summary>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _openReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved document.
    /// </summary>
    /// <remarks>
    /// Custom resolvers may return a stream that persists changes on dispose, for example by uploading to a remote service.
    /// </remarks>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _openWriteAsync(cancellationToken);

    /// <summary>
    /// Creates a resolution backed by a local file.
    /// </summary>
    public static DocumentReferenceResolution CreateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        return new DocumentReferenceResolution(
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
    public static DocumentReferenceResolution CreateStreamBacked(
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
/// Resolves a document reference such as a path, URL, or ID to a document resource.
/// </summary>
public interface IDocumentReferenceResolver
{
    /// <summary>
    /// Resolves the supplied document reference to a document resource.
    /// </summary>
    /// <remarks>
    /// Return <see langword="null"/> to fall back to the built-in path resolution behavior.
    /// </remarks>
    ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default);
}