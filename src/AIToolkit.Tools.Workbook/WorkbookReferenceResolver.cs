using System.Globalization;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Identifies which workbook tool is resolving a workbook reference.
/// </summary>
public enum WorkbookToolOperation
{
    /// <summary>
    /// Resolves a reference for a read operation.
    /// </summary>
    Read,
    /// <summary>
    /// Resolves a reference for a full workbook write.
    /// </summary>
    Write,
    /// <summary>
    /// Resolves a reference for an exact-string edit flow.
    /// </summary>
    Edit,
}

/// <summary>
/// Provides contextual information to an <see cref="IWorkbookReferenceResolver"/>.
/// </summary>
/// <remarks>
/// Resolvers receive the original user input together with the active working directory, current tool operation, and
/// configured workbook options. This allows provider packages to normalize references, reject unsupported aliases, or
/// carry provider-specific state into the handler pipeline.
/// </remarks>
public sealed class WorkbookReferenceResolverContext
{
    internal WorkbookReferenceResolverContext(
        string workbookReference,
        string workingDirectory,
        WorkbookToolOperation operation,
        WorkbookToolsOptions options,
        IServiceProvider? services)
    {
        WorkbookReference = workbookReference;
        WorkingDirectory = workingDirectory;
        Operation = operation;
        Options = options;
        Services = services;
    }

    /// <summary>
    /// Gets the raw workbook reference supplied to the tool.
    /// </summary>
    public string WorkbookReference { get; }

    /// <summary>
    /// Gets the working directory used by the workbook tools.
    /// </summary>
    /// <remarks>
    /// Relative references that fall back to file-based resolution are interpreted against this directory.
    /// </remarks>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the active tool operation.
    /// </summary>
    public WorkbookToolOperation Operation { get; }

    /// <summary>
    /// Gets the active workbook tool options.
    /// </summary>
    public WorkbookToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }
}

/// <summary>
/// Represents a resolved workbook resource that can be read from and written to.
/// </summary>
/// <remarks>
/// A resolution abstracts over both local files and resolver-backed resources such as hosted workbooks. It carries the
/// normalized reference, read-state identity, optional version metadata, and delegates that open the actual streams used
/// by handlers. Resolver-backed writes may publish changes only when the returned stream is disposed.
/// </remarks>
public sealed class WorkbookReferenceResolution
{
    private readonly Func<CancellationToken, ValueTask<bool>> _existsAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>> _openWriteAsync;

    private WorkbookReferenceResolution(
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
    /// Gets the resolved workbook reference surfaced by tool results and diagnostics.
    /// </summary>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the workbook tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the workbook extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets an optional version token supplied by the resolver, such as an ETag or revision ID.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the known workbook length in bytes when the resolver can supply it.
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
    /// Checks whether the resolved workbook currently exists.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the existence check.</param>
    /// <returns><see langword="true"/> when the workbook exists; otherwise, <see langword="false"/>.</returns>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _existsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved workbook.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>A readable stream over the resolved workbook.</returns>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _openReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved workbook.
    /// </summary>
    /// <remarks>
    /// Custom resolvers may return a stream that persists changes on dispose, for example by uploading to a remote service.
    /// </remarks>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _openWriteAsync(cancellationToken);

    /// <summary>
    /// Creates a resolution backed by a local file.
    /// </summary>
    /// <param name="filePath">The local file path to normalize and expose as a workbook resource.</param>
    /// <returns>A resolution that opens local file streams and derives version metadata from the file system.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is null, empty, or whitespace.</exception>
    public static WorkbookReferenceResolution CreateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        return new WorkbookReferenceResolution(
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
    /// <param name="state">Optional provider-specific state carried through to <see cref="WorkbookHandlerContext.State"/>.</param>
    /// <param name="readStateKey">
    /// An optional stable key for stale-read detection. When omitted, <paramref name="resolvedReference"/> is used.
    /// </param>
    /// <returns>A stream-backed resolution suitable for resolver-managed resources.</returns>
    public static WorkbookReferenceResolution CreateStreamBacked(
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
/// Resolves a workbook reference such as a path, URL, or ID to a workbook resource.
/// </summary>
/// <remarks>
/// Implementations can translate user-facing references into local file paths, hosted workbook handles, or custom
/// stream-backed resources. Returning <see langword="null"/> allows the generic tool flow to fall back to built-in path
/// resolution instead of treating the reference as an error.
/// </remarks>
public interface IWorkbookReferenceResolver
{
    /// <summary>
    /// Resolves the supplied workbook reference to a workbook resource.
    /// </summary>
    /// <remarks>
    /// Return <see langword="null"/> to fall back to the built-in path resolution behavior.
    /// </remarks>
    /// <param name="workbookReference">The user-supplied workbook reference to resolve.</param>
    /// <param name="context">The current resolver context, including the tool operation and working directory.</param>
    /// <param name="cancellationToken">A token that cancels resolution before or during provider I/O.</param>
    /// <returns>
    /// A <see cref="WorkbookReferenceResolution"/> when the resolver recognizes the reference; otherwise, <see langword="null"/>.
    /// </returns>
    ValueTask<WorkbookReferenceResolution?> ResolveAsync(
        string workbookReference,
        WorkbookReferenceResolverContext context,
        CancellationToken cancellationToken = default);
}
