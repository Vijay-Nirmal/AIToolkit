using AIToolkit.Tools.Document.Word;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Configures the Google Docs document handler.
/// </summary>
public sealed class GoogleDocsDocumentHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the managed canonical AsciiDoc payload stored alongside the Google Doc should be preferred when present.
    /// </summary>
    public bool PreferManagedAsciiDocPayload { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an embedded canonical AsciiDoc payload found in an exported DOCX should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedAsciiDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used when a Google Doc does not have a managed canonical payload.
    /// </summary>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional Word-level post-processor used to apply final changes before the generated DOCX is uploaded back into Google Docs.
    /// </summary>
    public IWordDocumentPostProcessor? PostProcessor { get; init; }

    /// <summary>
    /// Gets or sets the Google Drive workspace settings used for hosted Google Docs support.
    /// </summary>
    public GoogleDocsWorkspaceOptions? Workspace { get; init; }
}