using AIToolkit.Tools.Document.Word;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Configures the Google Docs document handler.
/// </summary>
/// <remarks>
/// The Google Docs provider layers hosted-document resolution on top of the Word AsciiDoc engine. These options control
/// how canonical payloads are discovered, when best-effort DOCX import is allowed, and how generated Word packages are
/// post-processed before Drive conversion uploads them back to Google Docs.
/// </remarks>
public sealed class GoogleDocsDocumentHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the managed canonical AsciiDoc payload stored alongside the Google Doc should be preferred when present.
    /// </summary>
    /// <remarks>
    /// When enabled, reads check the appData sidecar payload before exporting the Google Doc body as DOCX.
    /// </remarks>
    public bool PreferManagedAsciiDocPayload { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an embedded canonical AsciiDoc payload found in an exported DOCX should be preferred when present.
    /// </summary>
    public bool PreferEmbeddedAsciiDoc { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether best-effort import should be used when a Google Doc does not have a managed canonical payload.
    /// </summary>
    /// <remarks>
    /// Disable this when callers must avoid lossy conversions and should fail instead of importing from the Google-exported DOCX.
    /// </remarks>
    public bool EnableBestEffortImport { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional Word-level post-processor used to apply final changes before the generated DOCX is uploaded back into Google Docs.
    /// </summary>
    public IWordDocumentPostProcessor? PostProcessor { get; init; }

    /// <summary>
    /// Gets or sets the Google Drive workspace settings used for hosted Google Docs support.
    /// </summary>
    /// <seealso cref="GoogleDocsWorkspaceOptions"/>
    public GoogleDocsWorkspaceOptions? Workspace { get; init; }
}
