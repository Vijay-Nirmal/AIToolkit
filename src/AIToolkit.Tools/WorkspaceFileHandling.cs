using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Represents a workspace file read request passed to an <see cref="IWorkspaceFileHandler"/>.
/// </summary>
/// <param name="FilePath">The resolved absolute path to the file.</param>
/// <param name="Offset">The 1-based starting line offset for text-style reads.</param>
/// <param name="Limit">The maximum number of lines to return for text-style reads.</param>
/// <param name="Pages">An optional page range string for page-oriented formats such as PDF.</param>
public sealed record WorkspaceFileReadRequest(
    string FilePath,
    int? Offset = null,
    int? Limit = null,
    string? Pages = null);

/// <summary>
/// Provides file metadata and content access helpers to workspace file handlers.
/// </summary>
public sealed class WorkspaceFileReadContext
{
    private readonly Func<CancellationToken, ValueTask<byte[]>> _readAllBytes;
    private readonly Func<CancellationToken, ValueTask<string>> _readAllText;

    internal WorkspaceFileReadContext(
        WorkspaceFileReadRequest request,
        string extension,
        long length,
        string? mediaType,
        bool isBinary,
        WorkspaceToolsOptions options,
        IServiceProvider? services,
        Func<CancellationToken, ValueTask<byte[]>> readAllBytes,
        Func<CancellationToken, ValueTask<string>> readAllText)
    {
        Request = request;
        Extension = extension;
        Length = length;
        MediaType = mediaType;
        IsBinary = isBinary;
        Options = options;
        Services = services;
        _readAllBytes = readAllBytes;
        _readAllText = readAllText;
    }

    /// <summary>
    /// Gets the original read request.
    /// </summary>
    public WorkspaceFileReadRequest Request { get; }

    /// <summary>
    /// Gets the file extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the file length in bytes.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Gets the detected media type when one is known.
    /// </summary>
    public string? MediaType { get; }

    /// <summary>
    /// Gets a value indicating whether the file looks binary.
    /// </summary>
    public bool IsBinary { get; }

    /// <summary>
    /// Gets the workspace tool options active for the current tool call.
    /// </summary>
    public WorkspaceToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional service provider for the current tool invocation.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// Reads the full file as bytes.
    /// </summary>
    public ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default) =>
        _readAllBytes(cancellationToken);

    /// <summary>
    /// Reads the full file as text using the built-in text decoding behavior.
    /// </summary>
    public ValueTask<string> ReadAllTextAsync(CancellationToken cancellationToken = default) =>
        _readAllText(cancellationToken);
}

/// <summary>
/// Extends <c>workspace_read_file</c> with support for additional file formats.
/// </summary>
public interface IWorkspaceFileHandler
{
    /// <summary>
    /// Determines whether this handler can read the current file.
    /// </summary>
    bool CanHandle(WorkspaceFileReadContext context);

    /// <summary>
    /// Reads the file and returns one or more AI content parts.
    /// </summary>
    ValueTask<IEnumerable<AIContent>> ReadAsync(
        WorkspaceFileReadContext context,
        CancellationToken cancellationToken = default);
}