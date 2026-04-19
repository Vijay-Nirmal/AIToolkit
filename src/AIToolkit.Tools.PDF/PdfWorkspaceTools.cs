using AIToolkit.Tools;

namespace AIToolkit.Tools.PDF;

/// <summary>
/// Creates PDF-aware workspace file handlers for <c>workspace_read_file</c>.
/// </summary>
/// <remarks>
/// The resulting handler plugs into the generic workspace tools without changing tool names. It adds PDF text and image
/// extraction while preserving the normal workspace-read request shape and delegating the actual page parsing to
/// <see cref="PdfWorkspaceFileHandler"/>.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var options = new WorkspaceToolsOptions
/// {
///     FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
/// };
/// ]]></code>
/// </example>
public static class PdfWorkspaceTools
{
    /// <summary>
    /// Creates a PDF file handler that can be added to <see cref="WorkspaceToolsOptions.FileHandlers"/>.
    /// </summary>
    /// <param name="options">Optional PDF extraction settings.</param>
    /// <returns>A workspace file handler for PDF documents.</returns>
    /// <seealso cref="PdfWorkspaceFileHandlerOptions"/>
    public static IWorkspaceFileHandler CreateFileHandler(PdfWorkspaceFileHandlerOptions? options = null) =>
        new PdfWorkspaceFileHandler(options ?? new PdfWorkspaceFileHandlerOptions());
}
