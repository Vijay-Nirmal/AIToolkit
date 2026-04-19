using AIToolkit.Tools.Document;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Provides a hook for modifying a generated Word document before the write completes.
/// </summary>
public interface IWordDocumentPostProcessor
{
    /// <summary>
    /// Applies final modifications to the generated Word document.
    /// </summary>
    ValueTask ProcessAsync(
        WordDocumentPostProcessorContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the generated Word package and source AsciiDoc to a post-processor.
/// </summary>
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