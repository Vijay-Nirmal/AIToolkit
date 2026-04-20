using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Creates generic <c>workbook_*</c> tools backed by hosted Google Sheets.
/// </summary>
public static class GoogleSheetsWorkbookTools
{
    public static string GetSystemPromptGuidance(GoogleSheetsWorkbookToolSetOptions options) =>
        GetSystemPromptGuidance(currentSystemPrompt: null, options);

    public static string GetSystemPromptGuidance(string? currentSystemPrompt, GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static string GetSystemPromptGuidance(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IReadOnlyList<AIFunction> CreateFunctions(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateFunctions(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static IReadOnlyList<AIFunction> CreateFunctions(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateReadFileFunction(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateReadFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateReadFileFunction(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateWriteFileFunction(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateWriteFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateWriteFileFunction(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateEditFileFunction(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateEditFileFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateEditFileFunction(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateGrepSearchFunction(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateGrepSearchFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateGrepSearchFunction(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateSpecificationLookupFunction(GoogleSheetsWorkbookToolSetOptions options)
    {
        var (handlerOptions, workbookOptions) = Split(options);
        return WorkbookTools.CreateSpecificationLookupFunction(CloneWithHandler(workbookOptions, handlerOptions));
    }

    public static AIFunction CreateSpecificationLookupFunction(
        GoogleSheetsWorkbookHandlerOptions? handlerOptions = null,
        WorkbookToolsOptions? options = null) =>
        WorkbookTools.CreateSpecificationLookupFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IWorkbookHandler CreateHandler(GoogleSheetsWorkbookHandlerOptions? options = null)
    {
        var normalizedOptions = NormalizeHandlerOptions(options);
        var client = GoogleSheetsWorkspaceClientFactory.Create(normalizedOptions.Workspace);
        return new GoogleSheetsWorkbookHandler(normalizedOptions, client);
    }

    public static IWorkbookReferenceResolver CreateReferenceResolver(GoogleSheetsWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Credential is null
            && options.HttpClientInitializer is null
            && string.IsNullOrWhiteSpace(options.ApiKey)
            && options.Client is null)
        {
            throw new ArgumentException(
                "GoogleSheetsWorkspaceOptions.Credential, GoogleSheetsWorkspaceOptions.HttpClientInitializer, or GoogleSheetsWorkspaceOptions.ApiKey is required when creating a Google Sheets reference resolver.",
                nameof(options));
        }

        return new GoogleSheetsWorkbookReferenceResolver(GoogleSheetsWorkspaceClientFactory.Create(options));
    }

    private static WorkbookToolsOptions CloneWithHandler(WorkbookToolsOptions? options, GoogleSheetsWorkbookHandlerOptions handlerOptions)
    {
        var normalizedOptions = options ?? new WorkbookToolsOptions();
        var client = GoogleSheetsWorkspaceClientFactory.Create(handlerOptions.Workspace);
        var handler = new GoogleSheetsWorkbookHandler(handlerOptions, client);
        IWorkbookHandler[] handlers = normalizedOptions.Handlers is null
            ? [handler]
            : [.. normalizedOptions.Handlers, handler];
        IWorkbookToolPromptProvider[] promptProviders = normalizedOptions.PromptProviders is null
            ? [new GoogleSheetsWorkbookPromptProvider()]
            : [.. normalizedOptions.PromptProviders, new GoogleSheetsWorkbookPromptProvider()];

        return new WorkbookToolsOptions
        {
            WorkingDirectory = normalizedOptions.WorkingDirectory,
            ReferenceResolver = ComposeReferenceResolver(normalizedOptions.ReferenceResolver, client),
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = handlers,
            PromptProviders = promptProviders,
            LoggerFactory = normalizedOptions.LoggerFactory,
            LogContentParameters = normalizedOptions.LogContentParameters,
        };
    }

    private static GoogleSheetsWorkbookHandlerOptions NormalizeHandlerOptions(GoogleSheetsWorkbookHandlerOptions? handlerOptions) =>
        handlerOptions ?? new GoogleSheetsWorkbookHandlerOptions();

    private static (GoogleSheetsWorkbookHandlerOptions HandlerOptions, WorkbookToolsOptions WorkbookOptions) Split(GoogleSheetsWorkbookToolSetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return (
            new GoogleSheetsWorkbookHandlerOptions
            {
                PreferManagedWorkbookDocPayload = options.PreferManagedWorkbookDocPayload,
                PreferEmbeddedWorkbookDoc = options.PreferEmbeddedWorkbookDoc,
                EnableBestEffortImport = options.EnableBestEffortImport,
                Workspace = options.Workspace,
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

    private static IWorkbookReferenceResolver? ComposeReferenceResolver(IWorkbookReferenceResolver? existingResolver, IGoogleSheetsWorkspaceClient client)
    {
        var googleResolver = new GoogleSheetsWorkbookReferenceResolver(client);
        return existingResolver is null
            ? googleResolver
            : new ChainedWorkbookReferenceResolver([existingResolver, googleResolver]);
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
