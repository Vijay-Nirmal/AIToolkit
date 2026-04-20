using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Creates generic <c>document_*</c> tools backed by hosted Google Docs.
/// </summary>
/// <remarks>
/// This builder composes the shared <see cref="DocumentTools"/> surface with a Google Docs handler, prompt provider, and
/// optional hosted-reference resolver. The resulting tools keep the generic <c>document_*</c> names while gaining support
/// for <c>gdocs://</c> references and docs.google.com URLs.
/// </remarks>
public static class GoogleDocsDocumentTools
{
    /// <summary>
    /// Gets Google Docs-aware system prompt guidance without appending it to existing prompt text.
    /// </summary>
    public static string GetSystemPromptGuidance(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Appends Google Docs-aware document guidance to an existing system prompt.
    /// </summary>
    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates the full generic document tool set with Google Docs support enabled.
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_read_file</c> function with Google Docs support enabled.
    /// </summary>
    public static AIFunction CreateReadFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_write_file</c> function with Google Docs support enabled.
    /// </summary>
    public static AIFunction CreateWriteFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_edit_file</c> function with Google Docs support enabled.
    /// </summary>
    public static AIFunction CreateEditFileFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_grep_search</c> function with Google Docs support enabled.
    /// </summary>
    public static AIFunction CreateGrepSearchFunction(
        GoogleDocsDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates a stand-alone Google Docs document handler.
    /// </summary>
    public static IDocumentHandler CreateHandler(GoogleDocsDocumentHandlerOptions? options = null)
    {
        var normalizedOptions = NormalizeHandlerOptions(options);
        var client = GoogleDocsWorkspaceClientFactory.Create(normalizedOptions.Workspace);
        return new GoogleDocsDocumentHandler(normalizedOptions, client);
    }

    /// <summary>
    /// Creates a hosted Google Docs reference resolver from workspace options.
    /// </summary>
    /// <param name="options">The Drive workspace settings used to resolve hosted Google Docs references.</param>
    /// <returns>A resolver that understands docs.google.com URLs and <c>gdocs://</c> references.</returns>
    /// <exception cref="ArgumentException">No supported credential, initializer, API key, or test client was supplied.</exception>
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

/// <summary>
/// Evaluates multiple document reference resolvers in order until one recognizes the reference.
/// </summary>
/// <remarks>
/// Google Docs uses this helper to preserve any caller-supplied resolver while layering hosted Google Docs resolution on
/// top of it.
/// </remarks>
internal sealed class ChainedDocumentReferenceResolver(IEnumerable<IDocumentReferenceResolver> resolvers) : IDocumentReferenceResolver
{
    private readonly IDocumentReferenceResolver[] _resolvers = resolvers.ToArray();

    /// <summary>
    /// Resolves the supplied reference by querying each inner resolver in order.
    /// </summary>
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
