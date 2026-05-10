namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Normalizes PowerPoint deck options so tool factories and helper utilities share the same handler, resolver, and
/// working-directory behavior.
/// </summary>
internal static class PowerPointDeckToolSupport
{
    /// <summary>
    /// Normalizes nullable handler options to a usable instance.
    /// </summary>
    public static PowerPointDeckHandlerOptions NormalizeHandlerOptions(PowerPointDeckHandlerOptions? handlerOptions) =>
        handlerOptions ?? new PowerPointDeckHandlerOptions();

    /// <summary>
    /// Splits the flattened PowerPoint tool options into handler and generic deck options.
    /// </summary>
    public static (PowerPointDeckHandlerOptions HandlerOptions, DeckToolsOptions DeckOptions) Split(PowerPointDeckToolSetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return (
            new PowerPointDeckHandlerOptions
            {
                EnableLocalFileSupport = options.EnableLocalFileSupport,
                PreferEmbeddedDeckDoc = options.PreferEmbeddedDeckDoc,
                EnableBestEffortImport = options.EnableBestEffortImport,
                M365 = options.M365,
            },
            new DeckToolsOptions
            {
                WorkingDirectory = options.WorkingDirectory,
                ReferenceResolver = options.ReferenceResolver,
                MaxReadLines = options.MaxReadLines,
                MaxReadSlides = options.MaxReadSlides,
                MaxEditFileBytes = options.MaxEditFileBytes,
                MaxSearchResults = options.MaxSearchResults,
                Handlers = options.AdditionalHandlers,
                PromptProviders = options.AdditionalPromptProviders,
                AssetInterceptor = options.AssetInterceptor,
                AssetSessionId = options.AssetSessionId,
                TemplateStore = options.TemplateStore,
                LoggerFactory = options.LoggerFactory,
                LogContentParameters = options.LogContentParameters,
            });
    }

    /// <summary>
    /// Clones generic deck options and appends the PowerPoint handler, prompt provider, and reference resolver chain.
    /// </summary>
    public static DeckToolsOptions CloneWithHandler(DeckToolsOptions? options, PowerPointDeckHandlerOptions handlerOptions)
    {
        var normalizedOptions = options ?? new DeckToolsOptions();
        var normalizedHandlerOptions = NormalizeHandlerOptions(handlerOptions);
        var handler = new PowerPointDeckHandler(normalizedHandlerOptions);
        IDeckHandler[] handlers = normalizedOptions.Handlers is null
            ? [handler]
            : [.. normalizedOptions.Handlers, handler];
        IDeckToolPromptProvider[] promptProviders = normalizedOptions.PromptProviders is null
            ? [new PowerPointDeckPromptProvider(normalizedHandlerOptions)]
            : [.. normalizedOptions.PromptProviders, new PowerPointDeckPromptProvider(normalizedHandlerOptions)];

        return new DeckToolsOptions
        {
            WorkingDirectory = normalizedOptions.WorkingDirectory,
            ReferenceResolver = ComposeReferenceResolver(normalizedOptions.ReferenceResolver, normalizedHandlerOptions),
            MaxReadLines = normalizedOptions.MaxReadLines,
            MaxReadSlides = normalizedOptions.MaxReadSlides,
            MaxEditFileBytes = normalizedOptions.MaxEditFileBytes,
            MaxSearchResults = normalizedOptions.MaxSearchResults,
            Handlers = handlers,
            PromptProviders = promptProviders,
            AssetInterceptor = normalizedOptions.AssetInterceptor,
            AssetSessionId = normalizedOptions.AssetSessionId,
            TemplateStore = normalizedOptions.TemplateStore ?? PowerPointDeckTemplates.CreateDefaultStore(),
            LoggerFactory = normalizedOptions.LoggerFactory,
            LogContentParameters = normalizedOptions.LogContentParameters,
        };
    }

    /// <summary>
    /// Resolves the effective working directory using the same rules as the generic deck tools.
    /// </summary>
    public static string ResolveWorkingDirectory(string defaultWorkingDirectory, string? workingDirectory) =>
        string.IsNullOrWhiteSpace(workingDirectory)
            ? defaultWorkingDirectory
            : NormalizeDirectory(Path.IsPathRooted(workingDirectory)
                ? workingDirectory
                : Path.Combine(defaultWorkingDirectory, workingDirectory));

    /// <summary>
    /// Resolves a local path against the effective working directory.
    /// </summary>
    public static string ResolvePath(string path, string defaultWorkingDirectory, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(ResolveWorkingDirectory(defaultWorkingDirectory, workingDirectory), path));
    }

    /// <summary>
    /// Normalizes a directory path, defaulting to the current directory when none was provided.
    /// </summary>
    public static string NormalizeDirectory(string? workingDirectory) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory);

    private static IDeckReferenceResolver? ComposeReferenceResolver(IDeckReferenceResolver? existingResolver, PowerPointDeckHandlerOptions handlerOptions)
    {
        var resolvers = new List<IDeckReferenceResolver>();
        if (existingResolver is not null)
        {
            resolvers.Add(existingResolver);
        }

        if (handlerOptions.M365 is not null)
        {
            resolvers.Add(new PowerPointM365DeckReferenceResolver(handlerOptions.M365));
        }

        if (!handlerOptions.EnableLocalFileSupport)
        {
            resolvers.Add(new PowerPointLocalFileAccessResolver());
        }

        return resolvers.Count switch
        {
            0 => null,
            1 => resolvers[0],
            _ => new ChainedDeckReferenceResolver(resolvers),
        };
    }
}