using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Creates generic <c>workbook_*</c> tools backed by Microsoft Excel Open XML workbook formats.
/// </summary>
public static class ExcelWorkbookTools
{
    public static string GetSystemPromptGuidance(ExcelWorkbookToolSetOptions options) =>
        GetSystemPromptGuidance(currentSystemPrompt: null, options);

    public static string GetSystemPromptGuidance(string? currentSystemPrompt, ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static string GetSystemPromptGuidance(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IReadOnlyList<AIFunction> CreateFunctions(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateFunctions(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static IReadOnlyList<AIFunction> CreateFunctions(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateReadFileFunction(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateReadFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateReadFileFunction(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateWriteFileFunction(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateWriteFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateWriteFileFunction(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateEditFileFunction(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateEditFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateEditFileFunction(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateGrepSearchFunction(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateGrepSearchFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateGrepSearchFunction(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateSpecificationLookupFunction(
        ExcelWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateSpecificationLookupFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateSpecificationLookupFunction(
        ExcelWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateSpecificationLookupFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IWorkbookHandler CreateHandler(ExcelWorkbookHandlerOptions? options = null) =>
        new ExcelWorkbookHandler(options ?? new ExcelWorkbookHandlerOptions());

    /// <summary>
    /// Creates a Microsoft 365 hosted-workbook reference resolver.
    /// </summary>
    /// <param name="options">The Microsoft 365 connection settings used to resolve and stream hosted Excel files.</param>
    /// <returns>An <see cref="IWorkbookReferenceResolver"/> for OneDrive and SharePoint Excel references.</returns>
    /// <exception cref="ArgumentException"><paramref name="options"/> does not provide a credential.</exception>
    /// <seealso cref="ExcelWorkbookM365Options"/>
    public static IWorkbookReferenceResolver CreateM365ReferenceResolver(ExcelWorkbookM365Options options) =>
        new ExcelM365WorkbookReferenceResolver(options);

    private static WorkbookToolsOptions CloneWithHandler(WorkbookToolsOptions? options, ExcelWorkbookHandlerOptions handlerOptions)
    {
        var normalizedOptions = options ?? new WorkbookToolsOptions();
        var handler = CreateHandler(handlerOptions);
        IWorkbookHandler[] handlers = normalizedOptions.Handlers is null
            ? [handler]
            : [.. normalizedOptions.Handlers, handler];
        IWorkbookToolPromptProvider[] promptProviders = normalizedOptions.PromptProviders is null
            ? [new ExcelWorkbookPromptProvider(handlerOptions)]
            : [.. normalizedOptions.PromptProviders, new ExcelWorkbookPromptProvider(handlerOptions)];

        return new WorkbookToolsOptions
        {
            WorkingDirectory = normalizedOptions.WorkingDirectory,
            ReferenceResolver = ComposeReferenceResolver(normalizedOptions.ReferenceResolver, handlerOptions),
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = handlers,
            PromptProviders = promptProviders,
            LoggerFactory = normalizedOptions.LoggerFactory,
            LogContentParameters = normalizedOptions.LogContentParameters,
        };
    }

    private static ExcelWorkbookHandlerOptions NormalizeHandlerOptions(ExcelWorkbookHandlerOptions? handlerOptions) =>
        handlerOptions ?? new ExcelWorkbookHandlerOptions();

    private static (ExcelWorkbookHandlerOptions HandlerOptions, WorkbookToolsOptions WorkbookOptions) Split(ExcelWorkbookToolSetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return (
            new ExcelWorkbookHandlerOptions
            {
                EnableLocalFileSupport = options.EnableLocalFileSupport,
                PreferEmbeddedWorkbookDoc = options.PreferEmbeddedWorkbookDoc,
                EnableBestEffortImport = options.EnableBestEffortImport,
                M365 = options.M365,
            },
            new WorkbookToolsOptions
            {
                WorkingDirectory = options.WorkingDirectory,
                ReferenceResolver = options.ReferenceResolver,
                MaxReadLines = options.MaxReadLines,
                MaxEditFileBytes = options.MaxEditFileBytes,
                MaxSearchResults = options.MaxSearchResults,
                Handlers = options.AdditionalHandlers,
                PromptProviders = options.AdditionalPromptProviders,
                LoggerFactory = options.LoggerFactory,
                LogContentParameters = options.LogContentParameters,
            });
    }

    private static IWorkbookReferenceResolver? ComposeReferenceResolver(IWorkbookReferenceResolver? existingResolver, ExcelWorkbookHandlerOptions handlerOptions)
    {
        var resolvers = new List<IWorkbookReferenceResolver>();
        if (existingResolver is not null)
        {
            resolvers.Add(existingResolver);
        }

        if (handlerOptions.M365 is not null)
        {
            resolvers.Add(CreateM365ReferenceResolver(handlerOptions.M365));
        }

        if (!handlerOptions.EnableLocalFileSupport)
        {
            resolvers.Add(new ExcelLocalFileAccessResolver());
        }

        return resolvers.Count switch
        {
            0 => null,
            1 => resolvers[0],
            _ => new ChainedWorkbookReferenceResolver(resolvers),
        };
    }
}

internal sealed class ChainedWorkbookReferenceResolver(IEnumerable<IWorkbookReferenceResolver> resolvers) : IWorkbookReferenceResolver
{
    private readonly IWorkbookReferenceResolver[] _resolvers = resolvers.ToArray();

    public async ValueTask<WorkbookReferenceResolution?> ResolveAsync(
        string workbookReference,
        WorkbookReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var resolver in _resolvers)
        {
            var resolution = await resolver.ResolveAsync(workbookReference, context, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return null;
    }
}

internal sealed class ExcelLocalFileAccessResolver : IWorkbookReferenceResolver
{
    public ValueTask<WorkbookReferenceResolution?> ResolveAsync(
        string workbookReference,
        WorkbookReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!LooksLikeLocalExcelFileReference(workbookReference))
        {
            return ValueTask.FromResult<WorkbookReferenceResolution?>(null);
        }

        throw new InvalidOperationException(
            "Local Excel file support is disabled for this tool set. Enable ExcelWorkbookHandlerOptions.EnableLocalFileSupport to read or write local .xlsx, .xlsm, .xltx, or .xltm files.");
    }

    private static bool LooksLikeLocalExcelFileReference(string workbookReference)
    {
        var extension = Path.GetExtension(workbookReference);
        if (!ExcelWorkbookHandler.SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Uri.TryCreate(workbookReference, UriKind.Absolute, out var uri)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }
}
