using AIToolkit.Tools.Document;
using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Creates generic <c>document_*</c> tools backed by Microsoft Office Word document formats.
/// </summary>
public static class WordDocumentTools
{
    public static string GetSystemPromptGuidance(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IReadOnlyList<AIFunction> CreateFunctions(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateReadFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateWriteFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateEditFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static AIFunction CreateGrepSearchFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    public static IDocumentHandler CreateHandler(WordDocumentHandlerOptions? options = null) =>
        new WordDocumentHandler(options ?? new WordDocumentHandlerOptions());

    public static IDocumentReferenceResolver CreateM365ReferenceResolver(WordDocumentM365Options options) =>
        new WordM365DocumentReferenceResolver(options);

    private static DocumentToolsOptions CloneWithHandler(DocumentToolsOptions? options, WordDocumentHandlerOptions handlerOptions)
    {
        var normalizedOptions = options ?? new DocumentToolsOptions();
        var handler = CreateHandler(handlerOptions);
        IDocumentHandler[] handlers = normalizedOptions.Handlers is null
            ? [handler]
            : [.. normalizedOptions.Handlers, handler];
        IDocumentToolPromptProvider[] promptProviders = normalizedOptions.PromptProviders is null
            ? [new WordDocumentPromptProvider(handlerOptions)]
            : [.. normalizedOptions.PromptProviders, new WordDocumentPromptProvider(handlerOptions)];

        return new DocumentToolsOptions
        {
            WorkingDirectory = normalizedOptions.WorkingDirectory,
            ReferenceResolver = ComposeReferenceResolver(normalizedOptions.ReferenceResolver, handlerOptions),
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = handlers,
            PromptProviders = promptProviders,
        };
    }

    private static WordDocumentHandlerOptions NormalizeHandlerOptions(WordDocumentHandlerOptions? handlerOptions) =>
        handlerOptions ?? new WordDocumentHandlerOptions();

    private static IDocumentReferenceResolver? ComposeReferenceResolver(IDocumentReferenceResolver? existingResolver, WordDocumentHandlerOptions handlerOptions)
    {
        var resolvers = new List<IDocumentReferenceResolver>();
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
            resolvers.Add(new WordLocalFileAccessResolver());
        }

        return resolvers.Count switch
        {
            0 => null,
            1 => resolvers[0],
            _ => new ChainedDocumentReferenceResolver(resolvers),
        };
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

internal sealed class WordLocalFileAccessResolver : IDocumentReferenceResolver
{
    public ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!LooksLikeLocalWordFileReference(documentReference))
        {
            return ValueTask.FromResult<DocumentReferenceResolution?>(null);
        }

        throw new InvalidOperationException(
            "Local Word file support is disabled for this tool set. Enable WordDocumentHandlerOptions.EnableLocalFileSupport to read or write local .docx, .docm, .dotx, or .dotm files.");
    }

    private static bool LooksLikeLocalWordFileReference(string documentReference)
    {
        var extension = Path.GetExtension(documentReference);
        if (!WordDocumentHandler.SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.IsPathRooted(documentReference))
        {
            return true;
        }

        if (documentReference.StartsWith("./", StringComparison.Ordinal)
            || documentReference.StartsWith(@".\", StringComparison.Ordinal)
            || documentReference.StartsWith("../", StringComparison.Ordinal)
            || documentReference.StartsWith(@"..\", StringComparison.Ordinal)
            || documentReference.Contains('/')
            || documentReference.Contains('\\'))
        {
            return true;
        }

        return !Uri.TryCreate(documentReference, UriKind.Absolute, out _);
    }
}