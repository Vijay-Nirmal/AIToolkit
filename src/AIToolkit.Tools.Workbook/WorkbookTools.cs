using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Creates the generic <c>workbook_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This entry point wires together shared prompt guidance, stale-read tracking, reference resolution, and the
/// provider-specific handlers registered through <see cref="WorkbookToolsOptions"/>.
/// </remarks>
public static class WorkbookTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>workbook_*</c> tools.
    /// </summary>
    /// <returns>The shared system-prompt guidance for the generic workbook tool family.</returns>
    public static string GetSystemPromptGuidance() =>
        ToolPromptCatalog.GetWorkbookSystemPromptGuidance(options: null);

    /// <summary>
    /// Appends workbook tool guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing system prompt text, if any.</param>
    /// <returns>The existing prompt followed by the shared workbook guidance section.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance());

    /// <summary>
    /// Appends workbook tool guidance built from the supplied options to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing system prompt text, if any.</param>
    /// <param name="options">The options whose provider contributions should be merged into the guidance.</param>
    /// <returns>The existing prompt followed by merged shared and provider-specific workbook guidance.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, WorkbookToolsOptions options) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, ToolPromptCatalog.GetWorkbookSystemPromptGuidance(options));

    /// <summary>
    /// Creates the default workbook tool set.
    /// </summary>
    /// <param name="options">Optional workbook-tool configuration, handlers, and resolvers.</param>
    /// <returns>The read, write, edit, grep, and spec-lookup functions for supported workbook providers.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateAll();

    /// <summary>
    /// Creates only the <c>workbook_read_file</c> function.
    /// </summary>
    public static AIFunction CreateReadFileFunction(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateReadFile();

    /// <summary>
    /// Creates only the <c>workbook_write_file</c> function.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateWriteFile();

    /// <summary>
    /// Creates only the <c>workbook_edit_file</c> function.
    /// </summary>
    public static AIFunction CreateEditFileFunction(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateEditFile();

    /// <summary>
    /// Creates only the <c>workbook_grep_search</c> function.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateGrepSearch();

    /// <summary>
    /// Creates only the <c>workbook_spec_lookup</c> function.
    /// </summary>
    public static AIFunction CreateSpecificationLookupFunction(WorkbookToolsOptions? options = null) =>
        CreateFactory(options).CreateSpecificationLookup();

    private static WorkbookAIFunctionFactory CreateFactory(WorkbookToolsOptions? options)
    {
        var normalizedOptions = options ?? new WorkbookToolsOptions();
        var toolService = new WorkbookToolService(normalizedOptions);
        return new WorkbookAIFunctionFactory(toolService, normalizedOptions);
    }
}
