namespace AIToolkit.Tools.PDF;

/// <summary>
/// Configures PDF extraction behavior for <see cref="PdfWorkspaceTools.CreateFileHandler(PdfWorkspaceFileHandlerOptions?)"/>.
/// </summary>
/// <remarks>
/// The PDF handler is read-only and converts page text and embedded images into workspace-read results. These limits keep
/// extraction predictable when PDFs contain many pages, high-resolution images, or large unstructured text blocks.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var handler = PdfWorkspaceTools.CreateFileHandler(
///     new PdfWorkspaceFileHandlerOptions
///     {
///         IncludeImages = false,
///         MaxPages = 10,
///     });
/// ]]></code>
/// </example>
public sealed class PdfWorkspaceFileHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether extracted page text should be returned.
    /// </summary>
    public bool IncludeText { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether extracted page images should be returned as <c>DataContent</c> parts.
    /// </summary>
    /// <remarks>
    /// When enabled, each successfully decoded image is returned together with a descriptive <see cref="TextContent"/> label.
    /// </remarks>
    public bool IncludeImages { get; init; } = true;

    /// <summary>
    /// Gets or sets the maximum number of pages that will be extracted for one read.
    /// </summary>
    public int MaxPages { get; init; } = 50;

    /// <summary>
    /// Gets or sets the maximum number of embedded images that will be returned for one read.
    /// </summary>
    public int MaxImages { get; init; } = 32;

    /// <summary>
    /// Gets or sets the maximum number of characters returned for each extracted page of text.
    /// </summary>
    public int MaxTextCharactersPerPage { get; init; } = 20_000;

    /// <summary>
    /// Gets or sets the maximum number of bytes returned for one extracted image.
    /// </summary>
    public int MaxImageBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether PdfPig should use lenient parsing.
    /// </summary>
    /// <remarks>
    /// Leave this enabled when the handler needs to tolerate PDFs that contain minor structural issues.
    /// </remarks>
    public bool UseLenientParsing { get; init; } = true;
}
