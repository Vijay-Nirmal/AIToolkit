namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Configures the Word document handler.
/// </summary>
public sealed class WordDocumentHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether local Word file paths should be handled by this tool set.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical AsciiDoc should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedAsciiDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external Word files that do not contain embedded canonical AsciiDoc.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional post-processor used to apply final changes to generated Word documents before the write completes.
    /// </summary>
    public IWordDocumentPostProcessor? PostProcessor { get; init; }

    /// <summary>
    /// Gets or sets the optional Microsoft 365 hosted-document settings used when WordDocumentTools creates document functions.
    /// </summary>
    public WordDocumentM365Options? M365 { get; init; }
}