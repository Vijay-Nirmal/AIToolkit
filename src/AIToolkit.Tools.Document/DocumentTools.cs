using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Creates the generic <c>document_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This entry point wires together shared prompt guidance, stale-read tracking, reference resolution, and the
/// provider-specific handlers registered through <see cref="DocumentToolsOptions"/>.
/// </remarks>
public static class DocumentTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>document_*</c> tools.
    /// </summary>
    /// <returns>The shared system-prompt guidance for the generic document tool family.</returns>
    public static string GetSystemPromptGuidance() =>
        ToolPromptCatalog.GetDocumentSystemPromptGuidance(options: null);

    /// <summary>
    /// Appends document tool guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing system prompt text, if any.</param>
    /// <returns>The existing prompt followed by the shared document guidance section.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance());

    /// <summary>
    /// Appends document tool guidance built from the supplied options to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing system prompt text, if any.</param>
    /// <param name="options">The options whose provider contributions should be merged into the guidance.</param>
    /// <returns>The existing prompt followed by merged shared and provider-specific document guidance.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, DocumentToolsOptions options) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, ToolPromptCatalog.GetDocumentSystemPromptGuidance(options));

    /// <summary>
    /// Creates the default document tool set.
    /// </summary>
    /// <param name="options">Optional document-tool configuration, handlers, and resolvers.</param>
    /// <returns>The read, write, edit, and grep functions for supported document providers.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateAll();

    /// <summary>
    /// Creates only the <c>document_read_file</c> function.
    /// </summary>
    public static AIFunction CreateReadFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateReadFile();

    /// <summary>
    /// Creates only the <c>document_write_file</c> function.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateWriteFile();

    /// <summary>
    /// Creates only the <c>document_edit_file</c> function.
    /// </summary>
    public static AIFunction CreateEditFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateEditFile();

    /// <summary>
    /// Creates only the <c>document_grep_search</c> function.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateGrepSearch();

    private static DocumentAIFunctionFactory CreateFactory(DocumentToolsOptions? options)
    {
        var normalizedOptions = options ?? new DocumentToolsOptions();
        var toolService = new DocumentToolService(normalizedOptions);
        return new DocumentAIFunctionFactory(toolService, normalizedOptions);
    }
}
