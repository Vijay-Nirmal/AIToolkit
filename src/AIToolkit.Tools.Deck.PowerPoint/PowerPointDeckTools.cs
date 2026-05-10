using System.Reflection;
using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Creates generic <c>deck_*</c> tools backed by Microsoft PowerPoint Open XML presentation formats.
/// </summary>
public static class PowerPointDeckTools
{
    /// <summary>
    /// Gets provider-aware system prompt guidance for the generic deck tools.
    /// </summary>
    public static string GetSystemPromptGuidance(PowerPointDeckToolSetOptions options) =>
        GetSystemPromptGuidance(currentSystemPrompt: null, options);

    /// <summary>
    /// Appends provider-aware system prompt guidance to an existing prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(string? currentSystemPrompt, PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.GetSystemPromptGuidance(currentSystemPrompt, PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Gets provider-aware system prompt guidance from handler and generic deck options.
    /// </summary>
    public static string GetSystemPromptGuidance(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.GetSystemPromptGuidance(currentSystemPrompt: null, PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Appends provider-aware system prompt guidance to an existing prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.GetSystemPromptGuidance(currentSystemPrompt, PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates the default PowerPoint-backed deck tool set.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateFunctions(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        var functions = DeckTools.CreateFunctions(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions)).ToList();
        functions.Add(CreateExportSlideImagesFunctionCore(handlerOptions, deckOptions));
        return functions;
    }

    /// <summary>
    /// Creates the default PowerPoint-backed deck tool set.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null)
    {
        var normalizedHandlerOptions = PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions);
        var deckOptions = options ?? new DeckToolsOptions();
        var functions = DeckTools.CreateFunctions(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, normalizedHandlerOptions)).ToList();
        functions.Add(CreateExportSlideImagesFunctionCore(normalizedHandlerOptions, deckOptions));
        return functions;
    }

    /// <summary>
    /// Creates only the <c>deck_read_file</c> function.
    /// </summary>
    public static AIFunction CreateReadFileFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateReadFileFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_read_file</c> function.
    /// </summary>
    public static AIFunction CreateReadFileFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateReadFileFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_write_file</c> function.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateWriteFileFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_write_file</c> function.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateWriteFileFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_edit_file</c> function.
    /// </summary>
    public static AIFunction CreateEditFileFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateEditFileFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_edit_file</c> function.
    /// </summary>
    public static AIFunction CreateEditFileFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateEditFileFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_grep_search</c> function.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateGrepSearchFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_grep_search</c> function.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateGrepSearchFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_spec_lookup</c> function.
    /// </summary>
    public static AIFunction CreateSpecificationLookupFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateSpecificationLookupFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_spec_lookup</c> function.
    /// </summary>
    public static AIFunction CreateSpecificationLookupFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateSpecificationLookupFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_asset_create</c> function.
    /// </summary>
    public static AIFunction CreateAssetCreateFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateAssetCreateFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_asset_create</c> function.
    /// </summary>
    public static AIFunction CreateAssetCreateFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateAssetCreateFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_asset_search</c> function.
    /// </summary>
    public static AIFunction CreateAssetSearchFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateAssetSearchFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_asset_search</c> function.
    /// </summary>
    public static AIFunction CreateAssetSearchFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateAssetSearchFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_template_list</c> function.
    /// </summary>
    public static AIFunction CreateTemplateListFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateTemplateListFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_template_list</c> function.
    /// </summary>
    public static AIFunction CreateTemplateListFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateTemplateListFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_template_get</c> function.
    /// </summary>
    public static AIFunction CreateTemplateGetFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return DeckTools.CreateTemplateGetFunction(PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions));
    }

    /// <summary>
    /// Creates only the <c>deck_template_get</c> function.
    /// </summary>
    public static AIFunction CreateTemplateGetFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        DeckTools.CreateTemplateGetFunction(PowerPointDeckToolSupport.CloneWithHandler(options, PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>deck_export_slide_images</c> function.
    /// </summary>
    public static AIFunction CreateExportSlideImagesFunction(PowerPointDeckToolSetOptions options)
    {
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(options);
        return CreateExportSlideImagesFunctionCore(handlerOptions, deckOptions);
    }

    /// <summary>
    /// Creates only the <c>deck_export_slide_images</c> function.
    /// </summary>
    public static AIFunction CreateExportSlideImagesFunction(
        PowerPointDeckHandlerOptions? handlerOptions = null,
        DeckToolsOptions? options = null) =>
        CreateExportSlideImagesFunctionCore(PowerPointDeckToolSupport.NormalizeHandlerOptions(handlerOptions), options ?? new DeckToolsOptions());

    /// <summary>
    /// Creates the PowerPoint deck handler.
    /// </summary>
    public static IDeckHandler CreateHandler(PowerPointDeckHandlerOptions? options = null) =>
        new PowerPointDeckHandler(options ?? new PowerPointDeckHandlerOptions());

    /// <summary>
    /// Creates a Microsoft 365 hosted-presentation reference resolver.
    /// </summary>
    public static IDeckReferenceResolver CreateM365ReferenceResolver(PowerPointDeckM365Options options) =>
        new PowerPointM365DeckReferenceResolver(options);

    private static AIFunction CreateExportSlideImagesFunctionCore(PowerPointDeckHandlerOptions handlerOptions, DeckToolsOptions deckOptions)
    {
        var service = new PowerPointSlideImageExportToolService(handlerOptions, deckOptions);
        var method = typeof(PowerPointSlideImageExportToolService).GetMethod(
            nameof(PowerPointSlideImageExportToolService.ExportSlideImagesAsync),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{nameof(PowerPointSlideImageExportToolService.ExportSlideImagesAsync)}' was not found on {nameof(PowerPointSlideImageExportToolService)}.");

        return AIFunctionFactory.Create(
            method,
            service,
            new AIFunctionFactoryOptions
            {
                Name = "deck_export_slide_images",
                Description = GetExportSlideImagesDescription(handlerOptions),
                SerializerOptions = ToolJsonSerializerOptions.CreateWeb(),
            });
    }

    private static string GetExportSlideImagesDescription(PowerPointDeckHandlerOptions handlerOptions)
    {
        var lines = new List<string>
        {
            "Exports one PNG per slide from a PowerPoint presentation or template so you can visually compare rendered output.",
            "Use this after deck_write_file when template generation or visual validation needs exact slide images.",
            "Windows with Microsoft PowerPoint installed is required for slide export.",
        };

        if (handlerOptions.M365 is not null)
        {
            lines.Add("Hosted OneDrive and SharePoint PowerPoint references are resolved first, then exported from a temporary local copy.");
        }

        return string.Join(" ", lines);
    }
}

/// <summary>
/// Tries multiple deck reference resolvers in order.
/// </summary>
internal sealed class ChainedDeckReferenceResolver(IEnumerable<IDeckReferenceResolver> resolvers) : IDeckReferenceResolver
{
    private readonly IDeckReferenceResolver[] _resolvers = resolvers.ToArray();

    /// <inheritdoc />
    public async ValueTask<DeckReferenceResolution?> ResolveAsync(
        string deckReference,
        DeckReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var resolver in _resolvers)
        {
            var resolution = await resolver.ResolveAsync(deckReference, context, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return null;
    }
}

/// <summary>
/// Blocks local PowerPoint file access when the tool set is configured for hosted-only use.
/// </summary>
internal sealed class PowerPointLocalFileAccessResolver : IDeckReferenceResolver
{
    /// <inheritdoc />
    public ValueTask<DeckReferenceResolution?> ResolveAsync(
        string deckReference,
        DeckReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!LooksLikeLocalPowerPointFileReference(deckReference))
        {
            return ValueTask.FromResult<DeckReferenceResolution?>(null);
        }

        throw new InvalidOperationException(
            "Local PowerPoint file support is disabled for this tool set. Enable PowerPointDeckHandlerOptions.EnableLocalFileSupport to read or write local .pptx, .pptm, .potx, or .potm files.");
    }

    private static bool LooksLikeLocalPowerPointFileReference(string deckReference)
    {
        var extension = Path.GetExtension(deckReference);
        if (!PowerPointDeckHandler.SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Uri.TryCreate(deckReference, UriKind.Absolute, out var uri)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }
}
