using AIToolkit.Tools;

namespace AIToolkit.Tools.PDF;

/// <summary>
/// Creates PDF-aware workspace file handlers for <c>workspace_read_file</c>.
/// </summary>
public static class PdfWorkspaceTools
{
    /// <summary>
    /// Creates a PDF file handler that can be added to <see cref="WorkspaceToolsOptions.FileHandlers"/>.
    /// </summary>
    /// <param name="options">Optional PDF extraction settings.</param>
    /// <returns>A workspace file handler for PDF documents.</returns>
    public static IWorkspaceFileHandler CreateFileHandler(PdfWorkspaceFileHandlerOptions? options = null) =>
        new PdfWorkspaceFileHandler(options ?? new PdfWorkspaceFileHandlerOptions());
}