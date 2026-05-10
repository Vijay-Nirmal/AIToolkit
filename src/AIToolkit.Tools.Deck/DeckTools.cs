using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Creates the generic <c>deck_*</c> tools used by Microsoft.Extensions.AI hosts.
/// </summary>
/// <remarks>
/// This entry point wires together shared prompt guidance, stale-read tracking, reference resolution, asset/template
/// storage, and the provider-specific handlers registered through <see cref="DeckToolsOptions"/>.
/// </remarks>
public static class DeckTools
{
    /// <summary>
    /// Gets system-prompt guidance describing how to use the <c>deck_*</c> tools.
    /// </summary>
    public static string GetSystemPromptGuidance() =>
        ToolPromptCatalog.GetDeckSystemPromptGuidance(options: null);

    /// <summary>
    /// Appends deck tool guidance to an existing system prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, GetSystemPromptGuidance());

    /// <summary>
    /// Appends deck tool guidance built from the supplied options to an existing system prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, DeckToolsOptions options) =>
        ToolPromptCatalog.AppendSystemPromptSection(currentSystemPrompt, ToolPromptCatalog.GetDeckSystemPromptGuidance(NormalizeOptions(options)));

    /// <summary>
    /// Creates the default deck tool set.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateFunctions(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateAll();

    /// <summary>
    /// Creates only the <c>deck_read_file</c> function.
    /// </summary>
    public static AIFunction CreateReadFileFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateReadFile();

    /// <summary>
    /// Creates only the <c>deck_write_file</c> function.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateWriteFile();

    /// <summary>
    /// Creates only the <c>deck_edit_file</c> function.
    /// </summary>
    public static AIFunction CreateEditFileFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateEditFile();

    /// <summary>
    /// Creates only the <c>deck_grep_search</c> function.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateGrepSearch();

    /// <summary>
    /// Creates only the <c>deck_spec_lookup</c> function.
    /// </summary>
    public static AIFunction CreateSpecificationLookupFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateSpecificationLookup();

    /// <summary>
    /// Creates only the <c>deck_asset_create</c> function.
    /// </summary>
    public static AIFunction CreateAssetCreateFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateAssetCreate();

    /// <summary>
    /// Creates only the <c>deck_asset_search</c> function.
    /// </summary>
    public static AIFunction CreateAssetSearchFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateAssetSearch();

    /// <summary>
    /// Creates only the <c>deck_template_list</c> function.
    /// </summary>
    public static AIFunction CreateTemplateListFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateTemplateList();

    /// <summary>
    /// Creates only the <c>deck_template_get</c> function.
    /// </summary>
    public static AIFunction CreateTemplateGetFunction(DeckToolsOptions? options = null) =>
        CreateFactory(options).CreateTemplateGet();

    private static DeckAIFunctionFactory CreateFactory(DeckToolsOptions? options)
    {
        var normalizedOptions = NormalizeOptions(options);
        var toolService = new DeckToolService(normalizedOptions);
        return new DeckAIFunctionFactory(toolService, normalizedOptions);
    }

    private static DeckToolsOptions NormalizeOptions(DeckToolsOptions? options)
    {
        var normalizedOptions = options ?? new DeckToolsOptions();
        var workingDirectory = Path.GetFullPath(normalizedOptions.WorkingDirectory ?? Directory.GetCurrentDirectory());

        return new DeckToolsOptions
        {
            WorkingDirectory = workingDirectory,
            ReferenceResolver = normalizedOptions.ReferenceResolver,
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxReadSlides = normalizedOptions.MaxReadSlides,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = normalizedOptions.Handlers,
            PromptProviders = normalizedOptions.PromptProviders,
            AssetInterceptor = normalizedOptions.AssetInterceptor
                ?? new LocalFileDeckAssetInterceptor(new LocalFileDeckAssetInterceptorOptions
                {
                    RootDirectory = Path.Combine(workingDirectory, ".deck-assets"),
                }),
            AssetSessionId = normalizedOptions.AssetSessionId,
            TemplateStore = normalizedOptions.TemplateStore,
            LoggerFactory = normalizedOptions.LoggerFactory,
            LogContentParameters = normalizedOptions.LogContentParameters,
        };
    }
}

