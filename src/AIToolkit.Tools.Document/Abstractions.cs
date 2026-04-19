namespace AIToolkit.Tools.Document;

/// <summary>
/// Converts a provider-specific document representation to and from the canonical AsciiDoc form used by the
/// <c>document_*</c> tools.
/// </summary>
/// <remarks>
/// Implementations bridge <see cref="DocumentToolService"/> to concrete providers such as local Word packages or
/// resolver-backed hosted documents. Reads should return the AsciiDoc that the model will reason over, while writes
/// should persist an equivalent provider document and report whether that round trip remains lossless.
/// </remarks>
public interface IDocumentHandler
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
    /// Determines whether this handler can operate on the resolved document reference.
    /// </summary>
    /// <param name="context">The resolved document context, including extension, resolver state, and active options.</param>
    /// <returns><see langword="true"/> when this handler should own the operation; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(DocumentHandlerContext context);

    /// <summary>
    /// Reads a provider-specific document and returns canonical AsciiDoc.
    /// </summary>
    /// <param name="context">The resolved document context describing the source document and any resolver-defined state.</param>
    /// <param name="cancellationToken">A token that cancels the read before or during provider I/O.</param>
    /// <returns>A <see cref="DocumentReadResponse"/> containing the AsciiDoc payload used by downstream tool flows.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the document can be opened but not interpreted by the provider.</exception>
    ValueTask<DocumentReadResponse> ReadAsync(
        DocumentHandlerContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes canonical AsciiDoc into the provider-specific document format.
    /// </summary>
    /// <param name="context">The resolved document context describing the write target and any resolver-defined state.</param>
    /// <param name="asciiDoc">The canonical AsciiDoc payload to persist.</param>
    /// <param name="cancellationToken">A token that cancels the write before or during provider I/O.</param>
    /// <returns>
    /// A <see cref="DocumentWriteResponse"/> describing the output format and whether a later read can recover the same
    /// AsciiDoc exactly.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the target cannot be created or updated in the provider format.</exception>
    ValueTask<DocumentWriteResponse> WriteAsync(
        DocumentHandlerContext context,
        string asciiDoc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides resolved metadata, resolver state, and tool configuration to an <see cref="IDocumentHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="DocumentToolService"/> creates this context after an <see cref="IDocumentReferenceResolver"/> maps the
/// user-supplied reference to a concrete resource. Handlers use it to inspect the resolved extension, open streams,
/// discover provider-specific state, and honor the active <see cref="DocumentToolsOptions"/>.
/// </remarks>
public sealed class DocumentHandlerContext
{
    private readonly DocumentReferenceResolution _resolution;

    internal DocumentHandlerContext(
        string documentReference,
        DocumentReferenceResolution resolution,
        DocumentToolsOptions options,
        IServiceProvider? services)
    {
        _resolution = resolution;
        DocumentReference = documentReference;
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
    /// Gets the raw document reference supplied to the tool.
    /// </summary>
    public string DocumentReference { get; }

    /// <summary>
    /// Gets the resolved document reference surfaced by tool results and diagnostics.
    /// </summary>
    /// <remarks>
    /// This can differ from <see cref="DocumentReference"/> when a resolver normalizes the input into a stable URL, ID,
    /// or absolute file path.
    /// </remarks>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the document tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the resolved local file path when the document is backed by a local file; otherwise, <see langword="null"/>.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the file extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the known document length in bytes when the resolver can supply it.
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
    /// Providers use this to carry rich transport state such as hosted-document locations without exposing that detail
    /// through the generic public API.
    /// </remarks>
    public object? State { get; }

    /// <summary>
    /// Gets the active document tool options.
    /// </summary>
    public DocumentToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// Checks whether the resolved document currently exists.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the existence check.</param>
    /// <returns><see langword="true"/> when the resolved document currently exists; otherwise, <see langword="false"/>.</returns>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _resolution.ExistsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved document.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>A readable stream over the resolved document.</returns>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved document.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>
    /// A writable stream for the resolved document. Some resolvers persist changes only when the stream is disposed.
    /// </returns>
    /// <seealso cref="DocumentReferenceResolution.OpenWriteAsync(CancellationToken)"/>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenWriteAsync(cancellationToken);
}

/// <summary>
/// Represents canonical AsciiDoc produced by a document handler.
/// </summary>
/// <param name="AsciiDoc">The canonical AsciiDoc text returned to the model.</param>
/// <param name="IsLosslessRoundTrip">
/// <see langword="true"/> when writing the same AsciiDoc back through the same provider should preserve the content exactly.
/// </param>
/// <param name="SourceFormat">The provider-specific source format, such as <c>docx</c> or <c>google-docs</c>.</param>
/// <param name="Message">Optional provider guidance about payload selection, best-effort import, or read caveats.</param>
public sealed record DocumentReadResponse(
    string AsciiDoc,
    bool IsLosslessRoundTrip,
    string? SourceFormat = null,
    string? Message = null);

/// <summary>
/// Represents the provider-specific outcome of writing canonical AsciiDoc.
/// </summary>
/// <param name="PreservesAsciiDocRoundTrip">
/// <see langword="true"/> when the provider stored enough metadata to reconstruct the same AsciiDoc on a later read.
/// </param>
/// <param name="OutputFormat">The provider-specific format produced by the write.</param>
/// <param name="Message">Optional provider guidance describing write-side caveats or follow-up behavior.</param>
public sealed record DocumentWriteResponse(
    bool PreservesAsciiDocRoundTrip,
    string? OutputFormat = null,
    string? Message = null);
