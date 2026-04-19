using Microsoft.Extensions.AI;

namespace AIToolkit.Tools;

/// <summary>
/// Represents a workspace file read request passed to an <see cref="IWorkspaceFileHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceToolService.ReadFileAsync"/> resolves the incoming path before constructing this record, so
/// handlers can assume <see cref="FilePath"/> is absolute and normalized for the current host.
/// </remarks>
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
/// <remarks>
/// Custom handlers receive this context from the workspace file pipeline so they can inspect file metadata,
/// access host services, and lazily load bytes or text only when needed. This keeps handler implementations
/// focused on format-specific parsing rather than filesystem orchestration.
/// </remarks>
/// <seealso cref="IWorkspaceFileHandler"/>
/// <seealso cref="WorkspaceToolsOptions.FileHandlers"/>
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
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The complete file contents as bytes.</returns>
    public ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default) =>
        _readAllBytes(cancellationToken);

    /// <summary>
    /// Reads the full file as text using the built-in text decoding behavior.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The complete file contents decoded as text.</returns>
    public ValueTask<string> ReadAllTextAsync(CancellationToken cancellationToken = default) =>
        _readAllText(cancellationToken);
}

/// <summary>
/// Extends <c>workspace_read_file</c> with support for additional file formats.
/// </summary>
/// <remarks>
/// Register implementations through <see cref="WorkspaceToolsOptions.FileHandlers"/> or dependency injection when
/// you want <c>workspace_read_file</c> to understand new formats. The resolution pipeline checks custom handlers
/// before the built-in text, notebook, and media handlers so package-specific logic can override defaults.
/// </remarks>
/// <example>
/// <code>
/// public sealed class CustomReadHandler : IWorkspaceFileHandler
/// {
///     public bool CanHandle(WorkspaceFileReadContext context) =>
///         string.Equals(context.Extension, ".custom", StringComparison.OrdinalIgnoreCase);
///
///     public ValueTask&lt;IEnumerable&lt;AIContent&gt;&gt; ReadAsync(
///         WorkspaceFileReadContext context,
///         CancellationToken cancellationToken = default) =>
///         ValueTask.FromResult&lt;IEnumerable&lt;AIContent&gt;&gt;([new TextContent("custom content")]);
/// }
/// </code>
/// </example>
public interface IWorkspaceFileHandler
{
    /// <summary>
    /// Determines whether this handler can read the current file.
    /// </summary>
    /// <param name="context">The file metadata and access helpers for the pending read request.</param>
    /// <returns><see langword="true"/> when this handler should read the file; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(WorkspaceFileReadContext context);

    /// <summary>
    /// Reads the file and returns one or more AI content parts.
    /// </summary>
    /// <param name="context">The file metadata and access helpers for the pending read request.</param>
    /// <param name="cancellationToken">A token that cancels the read operation.</param>
    /// <returns>The AI content parts that should be returned from the tool call.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask<IEnumerable<AIContent>> ReadAsync(
        WorkspaceFileReadContext context,
        CancellationToken cancellationToken = default);
}
