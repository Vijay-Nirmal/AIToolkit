namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Converts a provider-specific workbook representation to and from the canonical WorkbookDoc form used by the
/// <c>workbook_*</c> tools.
/// </summary>
/// <remarks>
/// Implementations bridge <see cref="WorkbookToolService"/> to concrete providers such as local Excel packages or
/// resolver-backed hosted workbooks. Reads should return the WorkbookDoc that the model will reason over, while writes
/// should persist an equivalent provider workbook and report whether that round trip remains lossless.
/// </remarks>
public interface IWorkbookHandler
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
    /// Determines whether this handler can operate on the resolved workbook reference.
    /// </summary>
    /// <param name="context">The resolved workbook context, including extension, resolver state, and active options.</param>
    /// <returns><see langword="true"/> when this handler should own the operation; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(WorkbookHandlerContext context);

    /// <summary>
    /// Reads a provider-specific workbook and returns canonical WorkbookDoc.
    /// </summary>
    /// <param name="context">The resolved workbook context describing the source workbook and any resolver-defined state.</param>
    /// <param name="cancellationToken">A token that cancels the read before or during provider I/O.</param>
    /// <returns>A <see cref="WorkbookReadResponse"/> containing the WorkbookDoc payload used by downstream tool flows.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workbook can be opened but not interpreted by the provider.</exception>
    ValueTask<WorkbookReadResponse> ReadAsync(
        WorkbookHandlerContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes canonical WorkbookDoc into the provider-specific workbook format.
    /// </summary>
    /// <param name="context">The resolved workbook context describing the write target and any resolver-defined state.</param>
    /// <param name="workbookDoc">The canonical WorkbookDoc payload to persist.</param>
    /// <param name="cancellationToken">A token that cancels the write before or during provider I/O.</param>
    /// <returns>
    /// A <see cref="WorkbookWriteResponse"/> describing the output format and whether a later read can recover the same
    /// WorkbookDoc exactly.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the target cannot be created or updated in the provider format.</exception>
    ValueTask<WorkbookWriteResponse> WriteAsync(
        WorkbookHandlerContext context,
        string workbookDoc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides resolved metadata, resolver state, and tool configuration to an <see cref="IWorkbookHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="WorkbookToolService"/> creates this context after an <see cref="IWorkbookReferenceResolver"/> maps the
/// user-supplied reference to a concrete resource. Handlers use it to inspect the resolved extension, open streams,
/// discover provider-specific state, and honor the active <see cref="WorkbookToolsOptions"/>.
/// </remarks>
public sealed class WorkbookHandlerContext
{
    private readonly WorkbookReferenceResolution _resolution;

    internal WorkbookHandlerContext(
        string workbookReference,
        WorkbookReferenceResolution resolution,
        WorkbookToolsOptions options,
        IServiceProvider? services)
    {
        _resolution = resolution;
        WorkbookReference = workbookReference;
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
    /// Gets the raw workbook reference supplied to the tool.
    /// </summary>
    public string WorkbookReference { get; }

    /// <summary>
    /// Gets the resolved workbook reference surfaced by tool results and diagnostics.
    /// </summary>
    /// <remarks>
    /// This can differ from <see cref="WorkbookReference"/> when a resolver normalizes the input into a stable URL, ID,
    /// or absolute file path.
    /// </remarks>
    public string ResolvedReference { get; }

    /// <summary>
    /// Gets the stable key used by the workbook tools for stale-read tracking.
    /// </summary>
    public string ReadStateKey { get; }

    /// <summary>
    /// Gets the resolved local file path when the workbook is backed by a local file; otherwise, <see langword="null"/>.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the file extension, including the leading period when one exists.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the known workbook length in bytes when the resolver can supply it.
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
    /// Providers use this to carry rich transport state such as hosted-workbook locations without exposing that detail
    /// through the generic public API.
    /// </remarks>
    public object? State { get; }

    /// <summary>
    /// Gets the active workbook tool options.
    /// </summary>
    public WorkbookToolsOptions Options { get; }

    /// <summary>
    /// Gets the optional tool-call service provider.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// Checks whether the resolved workbook currently exists.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the existence check.</param>
    /// <returns><see langword="true"/> when the resolved workbook currently exists; otherwise, <see langword="false"/>.</returns>
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        _resolution.ExistsAsync(cancellationToken);

    /// <summary>
    /// Opens a readable stream for the resolved workbook.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>A readable stream over the resolved workbook.</returns>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenReadAsync(cancellationToken);

    /// <summary>
    /// Opens a writable stream for the resolved workbook.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels stream acquisition.</param>
    /// <returns>
    /// A writable stream for the resolved workbook. Some resolvers persist changes only when the stream is disposed.
    /// </returns>
    /// <seealso cref="WorkbookReferenceResolution.OpenWriteAsync(CancellationToken)"/>
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _resolution.OpenWriteAsync(cancellationToken);
}

/// <summary>
/// Represents canonical WorkbookDoc produced by a workbook handler.
/// </summary>
/// <param name="WorkbookDoc">The canonical WorkbookDoc text returned to the model.</param>
/// <param name="IsLosslessRoundTrip">
/// <see langword="true"/> when writing the same WorkbookDoc back through the same provider should preserve the content exactly.
/// </param>
/// <param name="SourceFormat">The provider-specific source format, such as <c>xlsx</c> or <c>google-sheets</c>.</param>
/// <param name="Message">Optional provider guidance about payload selection, best-effort import, or read caveats.</param>
public sealed record WorkbookReadResponse(
    string WorkbookDoc,
    bool IsLosslessRoundTrip,
    string? SourceFormat = null,
    string? Message = null);

/// <summary>
/// Represents the provider-specific outcome of writing canonical WorkbookDoc.
/// </summary>
/// <param name="PreservesWorkbookDocRoundTrip">
/// <see langword="true"/> when the provider stored enough metadata to reconstruct the same WorkbookDoc on a later read.
/// </param>
/// <param name="OutputFormat">The provider-specific format produced by the write.</param>
/// <param name="Message">Optional provider guidance describing write-side caveats or follow-up behavior.</param>
public sealed record WorkbookWriteResponse(
    bool PreservesWorkbookDocRoundTrip,
    string? OutputFormat = null,
    string? Message = null);
