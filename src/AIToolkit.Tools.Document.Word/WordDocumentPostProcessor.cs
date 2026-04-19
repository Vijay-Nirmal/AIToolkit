using AIToolkit.Tools.Document;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Provides a hook for modifying a generated Word document before the write completes.
/// </summary>
/// <remarks>
/// Use this for provider-specific polish such as injecting custom styles, metadata, or package parts after the AsciiDoc
/// renderer has produced the baseline document.
/// </remarks>
public interface IWordDocumentPostProcessor
{
    /// <summary>
    /// Applies final modifications to the generated Word document.
    /// </summary>
    /// <param name="context">The generated Word package and source AsciiDoc for the current write.</param>
    /// <param name="cancellationToken">A token that cancels post-processing.</param>
    /// <returns>A task that completes when all post-processing changes have been applied.</returns>
    ValueTask ProcessAsync(
        WordDocumentPostProcessorContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the generated Word package and source AsciiDoc to a post-processor.
/// </summary>
/// <remarks>
/// The context links the generic document-tool metadata to the Open XML package being written so post-processors can
/// inspect the target reference, modify the package, and reason about the original canonical AsciiDoc at the same time.
/// </remarks>
public sealed class WordDocumentPostProcessorContext
{
    internal WordDocumentPostProcessorContext(
        DocumentHandlerContext documentContext,
        WordprocessingDocument document,
        string asciiDoc)
    {
        DocumentContext = documentContext;
        Document = document;
        MainDocumentPart = document.MainDocumentPart
            ?? throw new InvalidOperationException("The Word document does not contain a main document part.");
        AsciiDoc = asciiDoc;
    }

    /// <summary>
    /// Gets the document handler context for the current write operation.
    /// </summary>
    public DocumentHandlerContext DocumentContext { get; }

    /// <summary>
    /// Gets the open WordprocessingDocument being written.
    /// </summary>
    /// <remarks>
    /// The document remains owned by the write pipeline. Post-processors should mutate it in place and must not dispose it.
    /// </remarks>
    public WordprocessingDocument Document { get; }

    /// <summary>
    /// Gets the main document part that contains the visible Word body.
    /// </summary>
    public MainDocumentPart MainDocumentPart { get; }

    /// <summary>
    /// Gets the canonical AsciiDoc used to generate the document.
    /// </summary>
    public string AsciiDoc { get; }
}
