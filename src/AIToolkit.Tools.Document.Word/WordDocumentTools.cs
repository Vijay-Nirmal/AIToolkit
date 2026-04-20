using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Creates generic <c>document_*</c> tools backed by Microsoft Office Word document formats.
/// </summary>
/// <remarks>
/// This factory keeps the shared <see cref="DocumentTools"/> surface intact while injecting the Word-specific parsing and
/// rendering pipeline. The created tools collaborate with <see cref="WordDocumentHandler"/> for local or hosted document
/// I/O, <see cref="WordDocumentPromptProvider"/> for prompt guidance, and
/// <see cref="WordM365DocumentReferenceResolver"/> when Microsoft 365 references are enabled.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// var functions = WordDocumentTools.CreateFunctions(
///     new WordDocumentHandlerOptions
///     {
///         EnableLocalFileSupport = true,
///     },
///     new DocumentToolsOptions
///     {
///         WorkingDirectory = @"C:\docs",
///     });
/// ]]></code>
/// </example>
public static class WordDocumentTools
{
    /// <summary>
    /// Gets Word-aware system prompt guidance without appending it to existing prompt text.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls supported references and round-trip behavior.</param>
    /// <param name="options">Optional shared document-tool options to merge with the Word handler.</param>
    /// <returns>A complete system-prompt fragment that describes the enabled Word document capabilities.</returns>
    /// <seealso cref="DocumentTools.GetSystemPromptGuidance(string?, DocumentToolsOptions?)"/>
    public static string GetSystemPromptGuidance(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt: null, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Appends Word-aware document guidance to an existing system prompt.
    /// </summary>
    /// <param name="currentSystemPrompt">The existing prompt text to append to.</param>
    /// <param name="handlerOptions">Optional Word handler configuration that controls supported references and round-trip behavior.</param>
    /// <param name="options">Optional shared document-tool options to merge with the Word handler.</param>
    /// <returns>The combined prompt text with Word-specific guidance appended.</returns>
    /// <seealso cref="GetSystemPromptGuidance(WordDocumentHandlerOptions?, DocumentToolsOptions?)"/>
    public static string GetSystemPromptGuidance(
        string? currentSystemPrompt,
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.GetSystemPromptGuidance(currentSystemPrompt, CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates the full generic document tool set with Word support enabled.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls document parsing, rendering, and reference resolution.</param>
    /// <param name="options">Optional shared document-tool options to extend with Word support.</param>
    /// <returns>The generic document tool set with the Word handler and prompt provider appended.</returns>
    /// <seealso cref="CreateHandler"/>
    public static IReadOnlyList<AIFunction> CreateFunctions(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateFunctions(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_read_file</c> function with Word support enabled.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls how Word files are read.</param>
    /// <param name="options">Optional shared document-tool options to extend with Word support.</param>
    /// <returns>An <see cref="AIFunction"/> that reads Word documents through the generic document tool contract.</returns>
    public static AIFunction CreateReadFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateReadFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_write_file</c> function with Word support enabled.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls how Word files are written.</param>
    /// <param name="options">Optional shared document-tool options to extend with Word support.</param>
    /// <returns>An <see cref="AIFunction"/> that writes canonical AsciiDoc into Word packages.</returns>
    public static AIFunction CreateWriteFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateWriteFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_edit_file</c> function with Word support enabled.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls how Word files are read back, edited, and rewritten.</param>
    /// <param name="options">Optional shared document-tool options to extend with Word support.</param>
    /// <returns>An <see cref="AIFunction"/> that performs exact-string edits against canonical AsciiDoc stored in Word documents.</returns>
    public static AIFunction CreateEditFileFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateEditFileFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates only the <c>document_grep_search</c> function with Word support enabled.
    /// </summary>
    /// <param name="handlerOptions">Optional Word handler configuration that controls which Word references can be searched.</param>
    /// <param name="options">Optional shared document-tool options to extend with Word support.</param>
    /// <returns>An <see cref="AIFunction"/> that searches supported Word documents through the generic grep contract.</returns>
    public static AIFunction CreateGrepSearchFunction(
        WordDocumentHandlerOptions? handlerOptions = null,
        DocumentToolsOptions? options = null) =>
        DocumentTools.CreateGrepSearchFunction(CloneWithHandler(options, NormalizeHandlerOptions(handlerOptions)));

    /// <summary>
    /// Creates a stand-alone Word document handler.
    /// </summary>
    /// <param name="options">Optional handler settings that control round-tripping, local-file support, and post-processing.</param>
    /// <returns>An <see cref="IDocumentHandler"/> that can be supplied directly to <see cref="DocumentToolsOptions.Handlers"/>.</returns>
    /// <seealso cref="WordDocumentHandlerOptions"/>
    public static IDocumentHandler CreateHandler(WordDocumentHandlerOptions? options = null) =>
        new WordDocumentHandler(options ?? new WordDocumentHandlerOptions());

    /// <summary>
    /// Creates a Microsoft 365 hosted-document reference resolver.
    /// </summary>
    /// <param name="options">The Microsoft 365 connection settings used to resolve and stream hosted Word files.</param>
    /// <returns>An <see cref="IDocumentReferenceResolver"/> for OneDrive and SharePoint Word references.</returns>
    /// <exception cref="ArgumentException"><paramref name="options"/> does not provide a credential.</exception>
    /// <seealso cref="WordDocumentM365Options"/>
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

/// <summary>
/// Evaluates multiple document reference resolvers in order until one recognizes the reference.
/// </summary>
/// <remarks>
/// <see cref="WordDocumentTools"/> uses this adapter when callers combine an existing resolver with Word-specific local or
/// Microsoft 365 reference support. The first resolver that recognizes a reference wins.
/// </remarks>
internal sealed class ChainedDocumentReferenceResolver(IEnumerable<IDocumentReferenceResolver> resolvers) : IDocumentReferenceResolver
{
    private readonly IDocumentReferenceResolver[] _resolvers = resolvers.ToArray();

    /// <summary>
    /// Resolves the supplied reference by querying each inner resolver in order.
    /// </summary>
    /// <param name="documentReference">The document reference to resolve.</param>
    /// <param name="context">The shared resolver context for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels resolver evaluation.</param>
    /// <returns>
    /// The first non-<see langword="null"/> resolution returned by an inner resolver, or <see langword="null"/> when none
    /// of them recognize the reference.
    /// </returns>
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

/// <summary>
/// Rejects local Word file paths when a tool set has explicitly disabled local-file support.
/// </summary>
/// <remarks>
/// This resolver does not resolve anything itself. Instead, it fails fast when a caller passes a local Word path into a
/// hosted-only configuration so the agent receives a direct, actionable error.
/// </remarks>
internal sealed class WordLocalFileAccessResolver : IDocumentReferenceResolver
{
    /// <summary>
    /// Throws for local Word file references when local support has been disabled.
    /// </summary>
    /// <param name="documentReference">The document reference supplied to the tool.</param>
    /// <param name="context">The shared resolver context for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the check.</param>
    /// <returns><see langword="null"/> when the reference is not a local Word path; otherwise this method throws.</returns>
    /// <exception cref="InvalidOperationException">The reference points to a local Word file while local support is disabled.</exception>
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
