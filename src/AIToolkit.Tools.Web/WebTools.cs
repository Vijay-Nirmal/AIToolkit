using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Web;

/// <summary>
/// Creates the generic <c>web_fetch</c> and <c>web_search</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This type is the provider-agnostic entry point for the web tool family. Provider-specific packages supply an
/// <see cref="IWebSearchProvider"/> while the shared package contributes the default <see cref="DefaultWebContentFetcher"/>
/// and prompt guidance used by the public tool contracts.
/// </remarks>
/// <seealso cref="WebToolsOptions"/>
/// <seealso cref="IWebSearchProvider"/>
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
    /// Creates the default shared web tool set.
    /// </summary>
    /// <param name="options">The options that control web fetch and search behavior.</param>
    /// <param name="searchProvider">The optional search provider used by <c>web_search</c>.</param>
    /// <param name="contentFetcher">The optional content fetcher used by <c>web_fetch</c>.</param>
    /// <returns>The <c>web_*</c> AI functions ready to register with an AI host.</returns>
    /// <remarks>
    /// When <paramref name="searchProvider"/> is <see langword="null"/>, the returned <c>web_search</c> function still
    /// exists but reports a structured configuration error at invocation time. This is useful for hosts that always
    /// register the full tool family and enable search only in selected environments.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var functions = WebTools.CreateFunctions(
    ///     new WebToolsOptions { MaxSearchResults = 5 },
    ///     searchProvider: new DuckDuckGoWebSearchProvider());
    /// ]]></code>
    /// </example>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        WebToolsOptions? options = null,
        IWebSearchProvider? searchProvider = null,
        IWebContentFetcher? contentFetcher = null) =>
        CreateFactory(options, searchProvider, contentFetcher).CreateAll();

    /// <summary>
    /// Creates only the shared <c>web_fetch</c> function.
    /// </summary>
    /// <param name="options">The options that control fetch behavior.</param>
    /// <param name="contentFetcher">The optional content fetcher override.</param>
    /// <returns>An AI function that exposes the shared fetch behavior.</returns>
    /// <seealso cref="CreateFunctions(WebToolsOptions?, IWebSearchProvider?, IWebContentFetcher?)"/>
    public static AIFunction CreateFetchFunction(
        WebToolsOptions? options = null,
        IWebContentFetcher? contentFetcher = null) =>
        CreateFactory(options, searchProvider: null, contentFetcher).CreateFetch();

    /// <summary>
    /// Creates only the shared <c>web_search</c> function.
    /// </summary>
    /// <param name="options">The options that control search result limits.</param>
    /// <param name="searchProvider">The optional search provider used to fulfill searches.</param>
    /// <returns>An AI function that exposes the shared search behavior.</returns>
    /// <seealso cref="CreateFunctions(WebToolsOptions?, IWebSearchProvider?, IWebContentFetcher?)"/>
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
