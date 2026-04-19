namespace AIToolkit.Tools.Document;

/// <summary>
/// Provides provider-specific conversion between a document format and canonical AsciiDoc text.
/// </summary>
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
    /// Determines whether this handler can operate on the current file.
    /// </summary>
    bool CanHandle(DocumentHandlerContext context);

    /// <summary>
    /// Reads a provider-specific document and returns canonical AsciiDoc.
    /// </summary>
    ValueTask<DocumentReadResponse> ReadAsync(
        DocumentHandlerContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes canonical AsciiDoc into the provider-specific document format.
    /// </summary>
    ValueTask<DocumentWriteResponse> WriteAsync(
        DocumentHandlerContext context,
        string asciiDoc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides file metadata and tool options to a document handler.
/// </summary>
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
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _resolution.ExistsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved document.
    /// </summary>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved document.
    /// </summary>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenWriteAsync(cancellationToken);
}

/// <summary>
/// Represents canonical AsciiDoc produced by a document handler.
/// </summary>
public sealed record DocumentReadResponse(
    string AsciiDoc,
    bool IsLosslessRoundTrip,
    string? SourceFormat = null,
    string? Message = null);

/// <summary>
/// Represents the provider-specific outcome of writing canonical AsciiDoc.
/// </summary>
public sealed record DocumentWriteResponse(
    bool PreservesAsciiDocRoundTrip,
    string? OutputFormat = null,
    string? Message = null);