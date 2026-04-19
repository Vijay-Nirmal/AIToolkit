using AIToolkit.Tools.Document;
using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Creates generic <c>document_*</c> tools backed by hosted Google Docs.
/// </summary>
public static class GoogleDocsDocumentTools
{
    public static string GetSystemPromptGuidance(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IReadOnlyList<AIFunction> CreateFunctions(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateReadFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateWriteFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateEditFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateGrepSearchFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IDocumentHandler CreateHandler(GoogleDocsDocumentHandlerOptions? options = null)
    {
        var normalizedOptions = NormalizeHandlerOptions(options);
        var client = GoogleDocsWorkspaceClientFactory.Create(normalizedOptions.Workspace);
        return new GoogleDocsDocumentHandler(normalizedOptions, client);
    }

    public static IDocumentReferenceResolver CreateReferenceResolver(GoogleDocsWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Credential is null
            && options.HttpClientInitializer is null
            && string.IsNullOrWhiteSpace(options.ApiKey)
            && options.Client is null)
        {
            throw new ArgumentException(
                "GoogleDocsWorkspaceOptions.Credential, GoogleDocsWorkspaceOptions.HttpClientInitializer, or GoogleDocsWorkspaceOptions.ApiKey is required when creating a Google Docs reference resolver.",
                nameof(options));
        }

        return
        new GoogleDocsDocumentReferenceResolver(GoogleDocsWorkspaceClientFactory.Create(options));
    }

    private static DocumentToolsOptions CloneWithHandler(DocumentToolsOptions? options, GoogleDocsDocumentHandlerOptions handlerOptions)
    {
        var normalizedOptions = options ?? new DocumentToolsOptions();
        var client = GoogleDocsWorkspaceClientFactory.Create(handlerOptions.Workspace);
        var handler = new GoogleDocsDocumentHandler(handlerOptions, client);
        IDocumentHandler[] handlers = normalizedOptions.Handlers is null
            ? [handler]
            : [.. normalizedOptions.Handlers, handler];
        IDocumentToolPromptProvider[] promptProviders = normalizedOptions.PromptProviders is null
            ? [new GoogleDocsDocumentPromptProvider(handlerOptions)]
            : [.. normalizedOptions.PromptProviders, new GoogleDocsDocumentPromptProvider(handlerOptions)];

        return new DocumentToolsOptions
        {
            WorkingDirectory = normalizedOptions.WorkingDirectory,
            ReferenceResolver = ComposeReferenceResolver(normalizedOptions.ReferenceResolver, handlerOptions, client),
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = handlers,
            PromptProviders = promptProviders,
        };
    }

    private static GoogleDocsDocumentHandlerOptions NormalizeHandlerOptions(GoogleDocsDocumentHandlerOptions? handlerOptions) =>
        handlerOptions ?? new GoogleDocsDocumentHandlerOptions();

    private static IDocumentReferenceResolver? ComposeReferenceResolver(
        IDocumentReferenceResolver? existingResolver,
        GoogleDocsDocumentHandlerOptions handlerOptions,
        IGoogleDocsWorkspaceClient client)
    {
        var googleResolver = new GoogleDocsDocumentReferenceResolver(client);
        return existingResolver is null
            ? googleResolver
            : new ChainedDocumentReferenceResolver([existingResolver, googleResolver]);
    }
}

internal sealed class ChainedDocumentReferenceResolver(IEnumerable<IDocumentReferenceResolver> resolvers) : IDocumentReferenceResolver
{
    private readonly IDocumentReferenceResolver[] _resolvers = resolvers.ToArray();

    public async ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var resolver in _resolvers)
        {
            var resolution = await resolver.ResolveAsync(documentReference, context, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return null;
    }
}
