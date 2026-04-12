using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Creates the generic <c>web_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
public static class WebTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>web_*</c> tools.
    /// </summary>
    /// <returns>A prompt section that can be appended to a host system prompt.</returns>
    public static string GetSystemPromptGuidance() =>
        ToolPromptCatalog.GetWebSystemPromptGuidance();

    /// <summary>
    /// Appends web tool guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The current system prompt text.</param>
    /// <returns>The combined system prompt text.</returns>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance());

    /// <summary>
    /// Creates the default web tool set.
    /// </summary>
    /// <param name="options">The options that control web fetch and search behavior.</param>
    /// <param name="searchProvider">The optional search provider used by <c>web_search</c>.</param>
    /// <param name="contentFetcher">The optional content fetcher used by <c>web_fetch</c>.</param>
    /// <returns>The <c>web_*</c> AI functions ready to register with an AI host.</returns>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        WebToolsOptions? options = null,
        IWebSearchProvider? searchProvider = null,
        IWebContentFetcher? contentFetcher = null) =>
        CreateFactory(options, searchProvider, contentFetcher).CreateAll();

    public static AIFunction CreateFetchFunction(
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null) =>
        CreateFactory(options, searchProvider: null, contentFetcher).CreateFetch();

    public static AIFunction CreateSearchFunction(
        WebToolsOptions? options = null,
        IWebSearchProvider? searchProvider = null) =>
        CreateFactory(options, searchProvider, contentFetcher: null).CreateSearch();

    private static WebAIFunctionFactory CreateFactory(
        WebToolsOptions? options,
        IWebSearchProvider? searchProvider,
        IWebContentFetcher? contentFetcher)
    {
        var normalizedOptions = options ?? new WebToolsOptions();
        var toolService = new WebToolService(normalizedOptions, searchProvider, contentFetcher);
        return new WebAIFunctionFactory(toolService);
    }
}