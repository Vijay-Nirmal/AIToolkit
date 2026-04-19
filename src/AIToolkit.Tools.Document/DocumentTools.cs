using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Creates the generic <c>document_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
public static class DocumentTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>document_*</c> tools.
    /// </summary>
    public static string GetSystemPromptGuidance() =>
        ToolPromptCatalog.GetDocumentSystemPromptGuidance(options: null);

    /// <summary>
    /// Appends document tool guidance to an existing system prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance());

    /// <summary>
    /// Appends document tool guidance built from the supplied options to an existing system prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, DocumentToolsOptions options) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, ToolPromptCatalog.GetDocumentSystemPromptGuidance(options));

    /// <summary>
    /// Creates the default document tool set.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateFunctions(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateAll();

    public static AIFunction CreateReadFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateReadFile();

    public static AIFunction CreateWriteFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateWriteFile();

    public static AIFunction CreateEditFileFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateEditFile();

    public static AIFunction CreateGrepSearchFunction(DocumentToolsOptions? options = null) =>
        CreateFactory(options).CreateGrepSearch();

    private static DocumentAIFunctionFactory CreateFactory(DocumentToolsOptions? options)
    {
        var normalizedOptions = options ?? new DocumentToolsOptions();
        var toolService = new DocumentToolService(normalizedOptions);
        return new DocumentAIFunctionFactory(toolService, normalizedOptions);
    }
}