using Microsoft.Extensions.AI;
using System.Reflection;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Maps <see cref="DeckToolService"/> methods to the stable public <c>deck_*</c> AI function names.
/// </summary>
internal sealed class DeckAIFunctionFactory(DeckToolService toolService, DeckToolsOptions options)
{
    private readonly DeckToolService _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
    private readonly DeckToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Creates the complete generic deck tool set.
    /// </summary>
    public IReadOnlyList<AIFunction> CreateAll()
    {
        var functions = new List<AIFunction>
        {
            CreateReadFile(),
            CreateWriteFile(),
            CreateEditFile(),
            CreateGrepSearch(),
            CreateSpecificationLookup(),
            CreateAssetCreate(),
            CreateAssetSearch(),
        };

        if (_options.TemplateStore is not null)
        {
            functions.Add(CreateTemplateList());
            functions.Add(CreateTemplateGet());
        }

        return functions;
    }

    /// <summary>
    /// Creates the <c>deck_read_file</c> function.
    /// </summary>
    public AIFunction CreateReadFile() =>
        Create(nameof(DeckToolService.ReadFileAsync), "deck_read_file", ToolPromptCatalog.GetDeckReadFileDescription(_options));

    /// <summary>
    /// Creates the <c>deck_write_file</c> function.
    /// </summary>
    public AIFunction CreateWriteFile() =>
        Create(nameof(DeckToolService.WriteFileAsync), "deck_write_file", ToolPromptCatalog.GetDeckWriteFileDescription(_options));

    /// <summary>
    /// Creates the <c>deck_edit_file</c> function.
    /// </summary>
    public AIFunction CreateEditFile() =>
        Create(nameof(DeckToolService.EditFileAsync), "deck_edit_file", ToolPromptCatalog.GetDeckEditFileDescription(_options));

    /// <summary>
    /// Creates the <c>deck_grep_search</c> function.
    /// </summary>
    public AIFunction CreateGrepSearch() =>
        Create(nameof(DeckToolService.GrepSearchAsync), "deck_grep_search", ToolPromptCatalog.GetDeckGrepSearchDescription(_options));

    /// <summary>
    /// Creates the <c>deck_spec_lookup</c> function.
    /// </summary>
    public AIFunction CreateSpecificationLookup() =>
        Create(nameof(DeckToolService.SpecificationLookupAsync), "deck_spec_lookup", ToolPromptCatalog.GetDeckSpecificationLookupDescription(_options));

    /// <summary>
    /// Creates the <c>deck_asset_create</c> function.
    /// </summary>
    public AIFunction CreateAssetCreate() =>
        Create(nameof(DeckToolService.CreateAssetAsync), "deck_asset_create", ToolPromptCatalog.GetDeckAssetCreateDescription(_options));

    /// <summary>
    /// Creates the <c>deck_asset_search</c> function.
    /// </summary>
    public AIFunction CreateAssetSearch() =>
        Create(nameof(DeckToolService.SearchAssetsAsync), "deck_asset_search", ToolPromptCatalog.GetDeckAssetSearchDescription(_options));

    /// <summary>
    /// Creates the <c>deck_template_list</c> function.
    /// </summary>
    public AIFunction CreateTemplateList() =>
        Create(nameof(DeckToolService.TemplateListAsync), "deck_template_list", ToolPromptCatalog.GetDeckTemplateListDescription(_options));

    /// <summary>
    /// Creates the <c>deck_template_get</c> function.
    /// </summary>
    public AIFunction CreateTemplateGet() =>
        Create(nameof(DeckToolService.TemplateGetAsync), "deck_template_get", ToolPromptCatalog.GetDeckTemplateGetDescription(_options));

    private AIFunction Create(string methodName, string name, string description)
    {
        var method = typeof(DeckToolService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {nameof(DeckToolService)}.");

        return AIFunctionFactory.Create(
            method,
            _toolService,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = ToolJsonSerializerOptions.CreateWeb(),
            });
    }
}

