namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Configures the Word document handler.
/// </summary>
/// <remarks>
/// The Word provider can operate on local packages, hosted Microsoft 365 references, or both. These options control
/// which reference types are allowed, whether embedded canonical payloads should be preferred, and how generated Word
/// documents are post-processed before the write completes.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var options = new WordDocumentHandlerOptions
/// {
///     PreferEmbeddedAsciiDoc = true,
///     EnableBestEffortImport = true,
/// };
/// ]]></code>
/// </example>
public sealed class WordDocumentHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether local Word file paths should be handled by this tool set.
    /// </summary>
    public bool EnableLocalFileSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether embedded canonical AsciiDoc should be preferred when present.
    /// </summary>
    /// <remarks>
    /// When enabled, reads recover the hidden payload written by <see cref="WordAsciiDocPayload"/> before attempting any
    /// best-effort import from visible document content.
    /// </remarks>
    public bool PreferEmbeddedAsciiDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used for external Word files that do not contain embedded canonical AsciiDoc.
    /// </summary>
    /// <remarks>
    /// Disable this when callers must avoid lossy conversions and should fail instead of importing visible Word content.
    /// </remarks>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional post-processor used to apply final changes to generated Word documents before the write completes.
    /// </summary>
    /// <seealso cref="IWordDocumentPostProcessor"/>
    public IWordDocumentPostProcessor? PostProcessor { get; init; }

    /// <summary>
    /// Gets or sets the optional Microsoft 365 hosted-document settings used when WordDocumentTools creates document functions.
    /// </summary>
    /// <seealso cref="WordDocumentM365Options"/>
    public WordDocumentM365Options? M365 { get; init; }
}
