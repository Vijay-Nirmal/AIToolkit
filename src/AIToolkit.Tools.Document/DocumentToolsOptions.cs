namespace AIToolkit.Tools.Document;

/// <summary>
/// Configures the generic <c>document_*</c> tools created by <see cref="DocumentTools"/>.
/// </summary>
/// <remarks>
/// These options define the workspace defaults, handler pipeline, resolver pipeline, and result limits shared by all
/// generic document operations. Provider-specific builders typically clone and extend this type instead of introducing a
/// separate option system.
/// </remarks>
public sealed class DocumentToolsOptions
{
    /// <summary>
    /// Gets or sets the default working directory used to resolve relative document paths.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the optional resolver that can map a document reference such as a path, URL, or ID to a resolved document resource.
    /// </summary>
    public IDocumentReferenceResolver? ReferenceResolver { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of AsciiDoc lines returned by document reads when no explicit range is provided.
    /// </summary>
    /// <remarks>
    /// Partial reads are tracked as partial view state, so callers must perform a full read before exact-string edits or
    /// full rewrites of an existing document.
    /// </remarks>
    public int MaxReadLines { get; init; } = 2_000;

    /// <summary>
    /// Gets or sets the maximum file size allowed for exact AsciiDoc edits.
    /// </summary>
    public long MaxEditFileBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of results returned by document content searches.
    /// </summary>
    public int MaxSearchResults { get; init; } = 200;

    /// <summary>
    /// Gets or sets the document handlers used to convert provider-specific files to and from canonical AsciiDoc.
    /// </summary>
    /// <remarks>
    /// Handlers supplied here are evaluated before handlers resolved from dependency injection.
    /// </remarks>
    public IEnumerable<IDocumentHandler>? Handlers { get; init; }

    /// <summary>
    /// Gets or sets the provider-specific prompt contributors that extend the generic document tool guidance.
    /// </summary>
    public IEnumerable<IDocumentToolPromptProvider>? PromptProviders { get; init; }
}
