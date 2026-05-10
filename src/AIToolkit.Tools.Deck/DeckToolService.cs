using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Implements the behavior behind the public <c>Deck_*</c> AI functions.
/// </summary>
/// <remarks>
/// This service coordinates reference resolution, handler selection, canonical DeckDoc conversion, stale-read tracking,
/// and grep-style search. Provider packages collaborate with it through <see cref="IDeckReferenceResolver"/>,
/// <see cref="IDeckHandler"/>, and <see cref="IDeckToolPromptProvider"/> while the public AI surface stays stable.
/// </remarks>
internal sealed class DeckToolService(DeckToolsOptions options)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "DeckToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private static readonly Action<ILogger, string, string, Exception?> ToolFailureLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, "DeckToolFailure"),
            "AI tool failure {ToolName} returned {Result}");

    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

    private readonly DeckToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly string _defaultWorkingDirectory = NormalizeDirectory(options.WorkingDirectory);
    private readonly DeckHandlerRegistry _handlerRegistry = new(options);
    private readonly DeckReadStateStore _readState = new();

    /// <summary>
    /// Reads a supported deck and returns canonical DeckDoc for a slide range.
    /// </summary>
    /// <param name="deck_reference">The local path or resolver-backed deck reference to read.</param>
    /// <param name="slide_offset">The optional 1-based slide offset to start from.</param>
    /// <param name="slide_limit">The optional maximum number of slides to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The slide-aware read result returned to the AI caller.</returns>
    public async Task<DeckReadFileToolResult> ReadFileAsync(
        string deck_reference,
        int? slide_offset = null,
        int? slide_limit = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_read_file",
            new Dictionary<string, object?>
            {
                ["deck_reference"] = deck_reference,
                ["slide_offset"] = slide_offset,
                ["slide_limit"] = slide_limit,
            });

        try
        {
            var resolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Read, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(false, resolution.ResolvedReference, 0, 0, 0, [], string.Empty, string.Empty, false, false, "The path refers to a directory. This tool can only read files, not directories."));
            }

            if (!await resolution.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(false, resolution.ResolvedReference, 0, 0, 0, [], string.Empty, string.Empty, false, false, "File not found."));
            }

            var context = _handlerRegistry.CreateContext(deck_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(false, resolution.ResolvedReference, 0, 0, 0, [], string.Empty, string.Empty, false, false, BuildNoHandlerMessage(serviceProvider)));
            }

            var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
            var normalized = DeckSupport.NormalizeLineEndings(response.DeckDoc ?? string.Empty);
            if (normalized.Length == 0)
            {
                return new DeckReadFileToolResult(true, context.ResolvedReference, 0, 0, 0, [], string.Empty, handler.ProviderName, false, response.IsLosslessRoundTrip, "The deck converted to an empty DeckDoc payload.");
            }

            var structure = DeckDocStructure.Parse(normalized);
            var effectiveSlideOffset = Math.Max(1, slide_offset.GetValueOrDefault(1));
            var effectiveSlideLimit = Math.Clamp(slide_limit ?? _options.MaxReadSlides, 1, Math.Max(1, _options.MaxReadSlides));
            if (effectiveSlideOffset > structure.Slides.Length)
            {
                return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(
                    false,
                    context.ResolvedReference,
                    structure.Slides.Length,
                    effectiveSlideOffset,
                    0,
                    [],
                    string.Empty,
                    handler.ProviderName,
                    true,
                    response.IsLosslessRoundTrip,
                        $"The requested slide_offset starts after the end of the deck. Total slides: {structure.Slides.Length.ToString(CultureInfo.InvariantCulture)}."));
            }

            var selectedSlides = structure.Slides
                .Skip(effectiveSlideOffset - 1)
                .Take(effectiveSlideLimit)
                .ToArray();
            var isPartialView = selectedSlides.Length != structure.Slides.Length;
            TrackRead(context.ReadStateKey, isPartialView, normalized);

            var messageParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                messageParts.Add(response.Message);
            }

            if (!response.IsLosslessRoundTrip)
            {
                messageParts.Add("Best-effort DeckDoc import. Round-trip fidelity is not guaranteed until this deck is rewritten by deck_write_file or deck_edit_file.");
            }

            if (isPartialView)
            {
                messageParts.Add($"Showing slides {selectedSlides[0].SlideNumber.ToString(CultureInfo.InvariantCulture)}-{selectedSlides[^1].SlideNumber.ToString(CultureInfo.InvariantCulture)} of {structure.Slides.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            return new DeckReadFileToolResult(
                true,
                context.ResolvedReference,
                structure.Slides.Length,
                selectedSlides[0].SlideNumber,
                selectedSlides.Length,
                [.. selectedSlides.Select(static slide => new DeckReadSlideSummary(slide.SlideNumber, slide.Title))],
                DeckSupport.FormatNumberedSelection(structure.ReadSlideRange(selectedSlides[0].SlideNumber, selectedSlides.Length)),
                handler.ProviderName,
                isPartialView,
                response.IsLosslessRoundTrip,
                messageParts.Count == 0 ? null : string.Join(" ", messageParts));
        }
        catch (DeckDocParseException exception)
        {
            return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(false, deck_reference, 0, 0, 0, [], string.Empty, string.Empty, false, false, BuildParseFailureMessage(exception)), exception);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_read_file", new DeckReadFileToolResult(false, deck_reference, 0, 0, 0, [], string.Empty, string.Empty, false, false, exception.Message), exception);
        }
    }

    /// <summary>
    /// Writes a supported Deck from canonical DeckDoc.
    /// </summary>
    /// <param name="Deck_reference">The local path or resolver-backed Deck reference to write.</param>
    /// <param name="content">The canonical DeckDoc payload to persist.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic write result returned to the AI caller.</returns>
    public async Task<DeckWriteFileToolResult> WriteFileAsync(
        string deck_reference,
        string content,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_write_file",
            new Dictionary<string, object?>
            {
                ["deck_reference"] = deck_reference,
                ["content"] = content,
            },
            includeContentParameters: _options.LogContentParameters);

        try
        {
            var resolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, "The target path refers to a directory."));
            }

            var context = _handlerRegistry.CreateContext(deck_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider)));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            DeckDocSnapshot? originalSnapshot = null;
            if (exists)
            {
                var readState = _readState.Get(context.ReadStateKey);
                if (readState is null || readState.IsPartialView)
                {
                    return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it."));
                }

                originalSnapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
                if (HasUnexpectedModification(originalSnapshot, readState))
                {
                    return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, originalSnapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it."));
                }
            }

            var normalizedContent = DeckSupport.NormalizeLineEndings(content ?? string.Empty);
            _ = DeckDocStructure.Parse(normalizedContent);
            var writeResponse = await handler.WriteAsync(context, normalizedContent, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(deck_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new DeckReadStateEntry(updatedSnapshot.NormalizedDeckDoc, false, null, null));

            return new DeckWriteFileToolResult(
                true,
                updatedContext.ResolvedReference,
                exists ? "update" : "create",
                normalizedContent.Length,
                originalSnapshot?.NormalizedDeckDoc,
                updatedSnapshot.NormalizedDeckDoc,
                DeckSupport.CreatePatch(originalSnapshot?.NormalizedDeckDoc, updatedSnapshot.NormalizedDeckDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesDeckDocRoundTrip,
                writeResponse.Message);
        }
        catch (DeckDocParseException exception)
        {
            return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, deck_reference, "update", 0, null, null, null, string.Empty, false, BuildParseFailureMessage(exception)), exception);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_write_file", new DeckWriteFileToolResult(false, deck_reference, "update", 0, null, null, null, string.Empty, false, exception.Message), exception);
        }
    }

    /// <summary>
    /// Applies an exact-string edit against the canonical DeckDoc representation of a supported Deck.
    /// </summary>
    /// <param name="Deck_reference">The local path or resolver-backed Deck reference to edit.</param>
    /// <param name="old_string">The exact canonical DeckDoc text to replace.</param>
    /// <param name="new_string">The replacement canonical DeckDoc text.</param>
    /// <param name="replace_all"><see langword="true"/> to replace every exact match instead of only a unique single match.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic edit result returned to the AI caller.</returns>
    public async Task<DeckEditFileToolResult> EditFileAsync(
        string deck_reference,
        string old_string = "",
        string new_string = "",
        bool replace_all = false,
        DeckSlideEditOperation[]? slide_operations = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSlideOperations = slide_operations ?? [];
        LogToolInvocation(
            serviceProvider,
            "deck_edit_file",
            new Dictionary<string, object?>
            {
                ["deck_reference"] = deck_reference,
                ["replace_all"] = replace_all,
                ["old_string"] = old_string,
                ["new_string"] = new_string,
                ["slide_operations"] = effectiveSlideOperations,
            },
            includeContentParameters: _options.LogContentParameters);

        try
        {
            var resolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (effectiveSlideOperations.Length > 0 && (!string.IsNullOrEmpty(old_string) || !string.IsNullOrEmpty(new_string)))
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, "slide_operations cannot be combined with old_string/new_string exact replacement inputs."));
            }

            if (effectiveSlideOperations.Length == 0 && string.Equals(old_string, new_string, StringComparison.Ordinal))
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, "No changes to make: old_string and new_string are exactly the same."));
            }

            var context = _handlerRegistry.CreateContext(deck_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider)));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                if (effectiveSlideOperations.Length > 0)
                {
                    return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File not found. Use deck_write_file to create a new deck."));
                }

                if (old_string.Length == 0)
                {
                    var normalizedNewDeck = DeckSupport.NormalizeLineEndings(new_string);
                    _ = DeckDocStructure.Parse(normalizedNewDeck);
                    var createResponse = await handler.WriteAsync(context, normalizedNewDeck, cancellationToken).ConfigureAwait(false);
                    var createdResolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                    var createdContext = _handlerRegistry.CreateContext(deck_reference, createdResolution, serviceProvider);
                    var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                    _readState.Set(
                        createdContext.ReadStateKey,
                        new DeckReadStateEntry(createdSnapshot.NormalizedDeckDoc, false, null, null));

                    return new DeckEditFileToolResult(
                        true,
                        createdContext.ResolvedReference,
                        1,
                        createdSnapshot.NormalizedDeckDoc.Length,
                        null,
                        createdSnapshot.NormalizedDeckDoc,
                        DeckSupport.CreatePatch(null, createdSnapshot.NormalizedDeckDoc, createdContext.ResolvedReference),
                        handler.ProviderName,
                        createResponse.PreservesDeckDocRoundTrip,
                        createResponse.Message);
                }

                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File not found."));
            }

            if (context.Length is long knownLength && knownLength > _options.MaxEditFileBytes)
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, $"File is too large to edit ({knownLength.ToString(CultureInfo.InvariantCulture)} bytes). Maximum editable file size is {_options.MaxEditFileBytes.ToString(CultureInfo.InvariantCulture)} bytes."));
            }

            var readState = _readState.Get(context.ReadStateKey);
            if (readState is null || readState.IsPartialView)
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it."));
            }

            var snapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
            if (HasUnexpectedModification(snapshot, readState))
            {
                return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, 0, snapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it."));
            }

            if (effectiveSlideOperations.Length == 0 && old_string.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.NormalizedDeckDoc))
                {
                    return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedDeckDoc.Length, snapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, "Cannot create new file - file already exists."));
                }

                var createdNormalized = DeckSupport.NormalizeLineEndings(new_string);
                _ = DeckDocStructure.Parse(createdNormalized);
                var createResponse = await handler.WriteAsync(context, createdNormalized, cancellationToken).ConfigureAwait(false);
                var createdResolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                var createdContext = _handlerRegistry.CreateContext(deck_reference, createdResolution, serviceProvider);
                var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                _readState.Set(
                    createdContext.ReadStateKey,
                    new DeckReadStateEntry(createdSnapshot.NormalizedDeckDoc, false, null, null));

                return new DeckEditFileToolResult(
                    true,
                    createdContext.ResolvedReference,
                    1,
                    createdSnapshot.NormalizedDeckDoc.Length,
                    snapshot.NormalizedDeckDoc,
                    createdSnapshot.NormalizedDeckDoc,
                    DeckSupport.CreatePatch(snapshot.NormalizedDeckDoc, createdSnapshot.NormalizedDeckDoc, createdContext.ResolvedReference),
                    handler.ProviderName,
                    createResponse.PreservesDeckDocRoundTrip,
                    createResponse.Message);
            }

            string updatedNormalized;
            int changesApplied;
            if (effectiveSlideOperations.Length > 0)
            {
                updatedNormalized = ApplySlideOperations(snapshot.NormalizedDeckDoc, effectiveSlideOperations);
                changesApplied = effectiveSlideOperations.Length;
            }
            else
            {
                var normalizedOld = DeckSupport.NormalizeLineEndings(old_string);
                var normalizedNew = DeckSupport.NormalizeLineEndings(new_string);
                if (string.IsNullOrEmpty(normalizedOld))
                {
                    return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedDeckDoc.Length, snapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, "old_string is required when slide_operations is not supplied."));
                }

                var matches = DeckSupport.CountOccurrences(snapshot.NormalizedDeckDoc, normalizedOld);
                if (matches == 0)
                {
                    return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedDeckDoc.Length, snapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, $"String to replace not found in deck DeckDoc.{Environment.NewLine}String: {old_string}"));
                }

                if (matches > 1 && !replace_all)
                {
                    return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedDeckDoc.Length, snapshot.NormalizedDeckDoc, null, null, handler.ProviderName, false, $"Found {matches.ToString(CultureInfo.InvariantCulture)} matches of the string to replace, but replace_all is false. To replace all occurrences, set replace_all to true. To replace only one occurrence, provide more surrounding context to uniquely identify the instance.{Environment.NewLine}String: {old_string}"));
                }

                updatedNormalized = replace_all
                    ? snapshot.NormalizedDeckDoc.Replace(normalizedOld, normalizedNew, StringComparison.Ordinal)
                    : DeckSupport.ReplaceFirst(snapshot.NormalizedDeckDoc, normalizedOld, normalizedNew);
                changesApplied = replace_all ? matches : 1;
            }

            _ = DeckDocStructure.Parse(updatedNormalized);

            var writeResponse = await handler.WriteAsync(context, updatedNormalized, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveDeckResolutionAsync(deck_reference, DeckToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(deck_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new DeckReadStateEntry(updatedSnapshot.NormalizedDeckDoc, false, null, null));

            return new DeckEditFileToolResult(
                true,
                updatedContext.ResolvedReference,
                changesApplied,
                updatedSnapshot.NormalizedDeckDoc.Length,
                snapshot.NormalizedDeckDoc,
                updatedSnapshot.NormalizedDeckDoc,
                DeckSupport.CreatePatch(snapshot.NormalizedDeckDoc, updatedSnapshot.NormalizedDeckDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesDeckDocRoundTrip,
                writeResponse.Message);
        }
        catch (DeckDocParseException exception)
        {
            return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, deck_reference, 0, 0, null, null, null, string.Empty, false, BuildParseFailureMessage(exception)), exception);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_edit_file", new DeckEditFileToolResult(false, deck_reference, 0, 0, null, null, null, string.Empty, false, exception.Message), exception);
        }
    }

    /// <summary>
    /// Creates or registers a new asset that can later be used from DeckDoc <c>[asset ...]</c> directives.
    /// </summary>
    public async Task<DeckAssetCreateToolResult> CreateAssetAsync(
        string source_reference,
        string asset_path,
        string description,
        string kind = "image",
        string? session_id = null,
        string? scope = null,
        string? display_name = null,
        string? media_type = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_asset_create",
            new Dictionary<string, object?>
            {
                ["source_reference"] = source_reference,
                ["asset_path"] = asset_path,
                ["description"] = description,
                ["kind"] = kind,
                ["session_id"] = session_id,
                ["scope"] = scope,
                ["display_name"] = display_name,
                ["media_type"] = media_type,
            });

        try
        {
            var interceptor = ResolveAssetInterceptor(serviceProvider);
            if (interceptor is null)
            {
                return new DeckAssetCreateToolResult(false, null, BuildNoAssetInterceptorMessage());
            }

            var effectiveSessionId = session_id ?? _options.AssetSessionId;
            var record = await interceptor.CreateAsync(
                new DeckAssetCreateRequest(
                    AssetPath: asset_path,
                    SourceReference: source_reference,
                    Description: description,
                    Kind: kind,
                    Scope: scope ?? (string.IsNullOrWhiteSpace(effectiveSessionId) ? DeckAssetScopes.Global : DeckAssetScopes.Session),
                    SessionId: effectiveSessionId,
                    DisplayName: display_name,
                    MediaType: media_type),
                cancellationToken).ConfigureAwait(false);

            return new DeckAssetCreateToolResult(true, record);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_asset_create", new DeckAssetCreateToolResult(false, null, exception.Message), exception);
        }
    }

    /// <summary>
    /// Searches the configured asset catalog across global and session-scoped assets.
    /// </summary>
    public async Task<DeckAssetSearchToolResult> SearchAssetsAsync(
        string? query = null,
        string? session_id = null,
        int maxResults = 20,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_asset_search",
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["session_id"] = session_id,
                ["maxResults"] = maxResults,
            });

        try
        {
            var interceptor = ResolveAssetInterceptor(serviceProvider);
            if (interceptor is null)
            {
                return LogToolFailure(serviceProvider, "deck_asset_search", new DeckAssetSearchToolResult(false, query ?? string.Empty, session_id ?? _options.AssetSessionId, [], BuildNoAssetInterceptorMessage()));
            }

            var effectiveSessionId = session_id ?? _options.AssetSessionId;
            var assets = await interceptor.SearchAsync(
                new DeckAssetSearchRequest(query, effectiveSessionId, maxResults),
                cancellationToken).ConfigureAwait(false);

            return new DeckAssetSearchToolResult(true, query ?? string.Empty, effectiveSessionId, assets);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_asset_search", new DeckAssetSearchToolResult(false, query ?? string.Empty, session_id ?? _options.AssetSessionId, [], exception.Message), exception);
        }
    }

    /// <summary>
    /// Lists named templates when a template store is configured.
    /// </summary>
    public async Task<DeckTemplateListToolResult> TemplateListAsync(
        string? query = null,
        int maxResults = 20,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_template_list",
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["maxResults"] = maxResults,
            });

        try
        {
            var templateStore = ResolveTemplateStore(serviceProvider);
            if (templateStore is null)
            {
                return LogToolFailure(serviceProvider, "deck_template_list", new DeckTemplateListToolResult(false, query ?? string.Empty, [], BuildNoTemplateStoreMessage()));
            }

            var templates = await templateStore.ListAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
            return new DeckTemplateListToolResult(
                true,
                query ?? string.Empty,
                [.. templates.Select(static templateRecord => new DeckTemplateSummary(templateRecord.Name, templateRecord.Description, templateRecord.Source))]);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_template_list", new DeckTemplateListToolResult(false, query ?? string.Empty, [], exception.Message), exception);
        }
    }

    /// <summary>
    /// Gets one named template when a template store is configured.
    /// </summary>
    public async Task<DeckTemplateGetToolResult> TemplateGetAsync(
        string name,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_template_get",
            new Dictionary<string, object?>
            {
                ["name"] = name,
            });

        try
        {
            var templateStore = ResolveTemplateStore(serviceProvider);
            if (templateStore is null)
            {
                return LogToolFailure(serviceProvider, "deck_template_get", new DeckTemplateGetToolResult(false, name, null, null, null, BuildNoTemplateStoreMessage()));
            }

            var templateRecord = await templateStore.GetAsync(name, cancellationToken).ConfigureAwait(false);
            if (templateRecord is null)
            {
                return LogToolFailure(serviceProvider, "deck_template_get", new DeckTemplateGetToolResult(false, name, null, null, null, "Template not found."));
            }

            return new DeckTemplateGetToolResult(true, templateRecord.Name, templateRecord.Description, templateRecord.Source, templateRecord.DeckDoc);
        }
        catch (Exception exception)
        {
            return LogToolFailure(serviceProvider, "deck_template_get", new DeckTemplateGetToolResult(false, name, null, null, null, exception.Message), exception);
        }
    }

    /// <summary>
    /// Searches canonical DeckDoc across supported local Decks or explicit resolver-backed references.
    /// </summary>
    /// <param name="pattern">The text or regular expression to search for.</param>
    /// <param name="useRegex"><see langword="true"/> to treat <paramref name="pattern"/> as a regular expression.</param>
    /// <param name="includePattern">An optional glob used to limit local workspace files.</param>
    /// <param name="deck_references">Optional explicit resolver-backed references to search instead of scanning the local workspace.</param>
    /// <param name="caseSensitive"><see langword="true"/> to perform a case-sensitive search.</param>
    /// <param name="contextLines">The number of context lines to capture before and after each match.</param>
    /// <param name="workingDirectory">The optional workspace root used for relative path resolution.</param>
    /// <param name="maxResults">The optional maximum number of matches to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The grep-style search result returned to the AI caller.</returns>
    public async Task<DeckGrepSearchToolResult> GrepSearchAsync(
        string pattern,
        bool useRegex = false,
        string? includePattern = null,
        string[]? deck_references = null,
        bool caseSensitive = false,
        int contextLines = 0,
        string? workingDirectory = null,
        int? maxResults = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_grep_search",
            new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["useRegex"] = useRegex,
                ["includePattern"] = includePattern,
                ["deck_references"] = deck_references,
                ["caseSensitive"] = caseSensitive,
                ["contextLines"] = contextLines,
                ["workingDirectory"] = workingDirectory,
                ["maxResults"] = maxResults,
            });

        try
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            var explicitDeckReferences = NormalizeDeckReferences(deck_references);
            if (deck_references is not null && explicitDeckReferences.Length == 0)
            {
                return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, [], "deck_references must contain at least one non-empty deck reference when supplied."));
            }

            if (explicitDeckReferences.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "A search pattern is required."));
                }

                if (includePattern is not null)
                {
                    return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "includePattern cannot be combined with deck_references. Use includePattern for local workspace scans, or use deck_references to search explicit resolver-backed decks."));
                }

                if (!_handlerRegistry.HasHandlers(serviceProvider))
                {
                    return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, [], BuildNoHandlerMessage(serviceProvider)));
                }

                return await GrepExplicitDeckReferencesAsync(
                    explicitDeckReferences,
                    pattern,
                    useRegex,
                    caseSensitive,
                    contextLines,
                    root,
                    maxResults,
                    serviceProvider,
                    cancellationToken).ConfigureAwait(false);
            }

            var supportedExtensions = _handlerRegistry.GetSupportedExtensions(serviceProvider);
            if (supportedExtensions.Length == 0)
            {
                var message = _handlerRegistry.HasHandlers(serviceProvider)
                    ? "Configured deck handlers do not expose local file extensions. deck_grep_search currently searches only local workspace files."
                    : BuildNoHandlerMessage(serviceProvider);
                return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, [], message));
            }

            var supportedExtensionSet = supportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            GlobMatcher? includeMatcher = string.IsNullOrWhiteSpace(includePattern) ? null : new GlobMatcher(includePattern);
            var limit = NormalizeSearchLimit(maxResults);
            var matches = new List<DeckGrepMatch>();
            var truncated = false;
            var effectiveContextLines = Math.Clamp(contextLines, 0, 20);
            Regex? regex = null;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (useRegex)
            {
                regex = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }

            foreach (var file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!supportedExtensionSet.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                var relativePath = ToRelativePath(root, file);
                if (includeMatcher is not null && !includeMatcher.IsMatch(relativePath))
                {
                    continue;
                }

                var context = _handlerRegistry.CreateContext(file, DeckReferenceResolution.CreateFile(file), serviceProvider);
                var handler = _handlerRegistry.ResolveHandler(context);
                if (handler is null)
                {
                    continue;
                }

                DeckReadResponse response;
                try
                {
                    response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var normalizedDeckDoc = DeckSupport.NormalizeLineEndings(response.DeckDoc ?? string.Empty);
                var lines = normalizedDeckDoc.Split('\n');
                var structure = TryParseDeckDocStructure(normalizedDeckDoc);
                for (var index = 0; index < lines.Length; index++)
                {
                    var isMatch = regex is not null
                        ? regex.IsMatch(lines[index])
                        : lines[index].Contains(pattern, comparison);
                    if (!isMatch)
                    {
                        continue;
                    }

                    if (matches.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }

                    var beforeStart = Math.Max(0, index - effectiveContextLines);
                    var afterEnd = Math.Min(lines.Length - 1, index + effectiveContextLines);
                    var slide = structure?.FindSlideForLine(index + 1);
                    matches.Add(new DeckGrepMatch(
                        ToToolDeckReference(file, root),
                        index + 1,
                        slide?.SlideNumber,
                        slide?.Title,
                        lines[index],
                        lines[beforeStart..index],
                        lines[(index + 1)..(afterEnd + 1)]));
                }

                if (truncated)
                {
                    break;
                }
            }

            return new DeckGrepSearchToolResult(true, root, [.. matches], truncated, supportedExtensions);
        }
        catch (Exception exception)
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            return LogToolFailure(serviceProvider, "deck_grep_search", new DeckGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), exception.Message), exception);
        }
    }

    /// <summary>
    /// Looks up advanced DeckDoc guidance by section ID or keyword.
    /// </summary>
    /// <param name="keywords">Optional focused keywords describing the DeckDoc feature to look up.</param>
    /// <param name="section_ids">Optional stable DeckDoc specification section identifiers to return directly.</param>
    /// <param name="maxResults">The optional maximum number of matching guidance sections to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matched DeckDoc guidance sections.</returns>
    public Task<DeckSpecificationLookupToolResult> SpecificationLookupAsync(
        string? keywords = null,
        string[]? section_ids = null,
        int maxResults = 5,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "deck_spec_lookup",
            new Dictionary<string, object?>
            {
                ["keywords"] = keywords,
                ["section_ids"] = section_ids,
                ["maxResults"] = maxResults,
            });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedKeywords = keywords?.Trim() ?? string.Empty;
            var normalizedSectionIds = section_ids?
                .Where(static sectionId => !string.IsNullOrWhiteSpace(sectionId))
                .Select(static sectionId => sectionId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (string.IsNullOrWhiteSpace(normalizedKeywords) && (normalizedSectionIds is null || normalizedSectionIds.Length == 0))
            {
                return Task.FromResult(LogToolFailure(serviceProvider, "deck_spec_lookup", new DeckSpecificationLookupToolResult(
                    Success: false,
                    Query: string.Empty,
                    Matches: [],
                    Message: "A section_ids list or keyword query is required. Prefer section_ids such as 'slide-transition', 'layout-split', or 'table-block' when you know the exact feature.")));
            }

            var effectiveMaxResults = Math.Clamp(maxResults, 1, Math.Max(1, _options.MaxSearchResults));
            var matches = DeckSpecificationCatalog.Search(normalizedKeywords, normalizedSectionIds, effectiveMaxResults);
            var message = matches.Length == 0
                ? "No DeckDoc guidance matched that section_ids or keyword request. Try a stable section ID first, or use shorter, more specific keywords."
                : null;

            return Task.FromResult(new DeckSpecificationLookupToolResult(
                Success: true,
                Query: normalizedKeywords,
                Matches: matches,
                Message: message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(LogToolFailure(serviceProvider, "deck_spec_lookup", new DeckSpecificationLookupToolResult(
                Success: false,
                Query: keywords?.Trim() ?? string.Empty,
                Matches: [],
                Message: exception.Message), exception));
        }
    }

    private void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, object parameters, bool includeContentParameters = false)
    {
        var logger = GetLogger(serviceProvider);
        if (logger is null)
        {
            return;
        }

        var serialized = JsonSerializer.Serialize(FilterLoggedParameters(parameters, includeContentParameters), LogJsonOptions);
        ToolInvocationLog(logger, toolName, serialized, null);
    }

    private T LogToolFailure<T>(IServiceProvider? serviceProvider, string toolName, T result, Exception? exception = null)
        where T : DeckToolResult
    {
        if (result.Success)
        {
            return result;
        }

        var logger = GetLogger(serviceProvider);
        if (logger is null)
        {
            return result;
        }

        var serialized = JsonSerializer.Serialize(result, LogJsonOptions);
        ToolFailureLog(logger, toolName, serialized, exception);
        return result;
    }

    private ILogger? GetLogger(IServiceProvider? serviceProvider)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory ?? _options.LoggerFactory;
        return loggerFactory?.CreateLogger<DeckToolService>();
    }

    private static object FilterLoggedParameters(object parameters, bool includeContentParameters)
    {
        if (includeContentParameters || parameters is not IReadOnlyDictionary<string, object?> parameterMap)
        {
            return parameters;
        }

        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in parameterMap)
        {
            if (string.Equals(pair.Key, "content", StringComparison.Ordinal)
                || string.Equals(pair.Key, "old_string", StringComparison.Ordinal)
                || string.Equals(pair.Key, "new_string", StringComparison.Ordinal))
            {
                continue;
            }

            filtered[pair.Key] = pair.Value;
        }

        return filtered;
    }

    private string BuildNoHandlerMessage(IServiceProvider? serviceProvider)
    {
        var supported = _handlerRegistry.GetSupportedExtensions(serviceProvider);
        if (supported.Length == 0)
        {
            if (_handlerRegistry.HasHandlers(serviceProvider))
            {
                return "Configured Deck handlers accept only resolver-backed references such as URLs or IDs; no local file handler can handle this path/reference.";
            }

            return "No IDeckHandler is configured. Register a handler from a provider package such as AIToolkit.Tools.Deck.PowerPoint.";
        }

        return $"No IDeckHandler can handle this file. Supported extensions: {string.Join(", ", supported)}.";
    }

    private void TrackRead(string readStateKey, bool isPartialView, string normalizedDeckDoc)
    {
        _readState.Set(
            readStateKey,
            new DeckReadStateEntry(normalizedDeckDoc, isPartialView, null, null));
    }

    private static async Task<DeckDocSnapshot> ReadSnapshotAsync(
        IDeckHandler handler,
        DeckHandlerContext context,
        CancellationToken cancellationToken)
    {
        var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        return new DeckDocSnapshot(
            DeckSupport.NormalizeLineEndings(response.DeckDoc ?? string.Empty));
    }

    private static bool HasUnexpectedModification(DeckDocSnapshot snapshot, DeckReadStateEntry readState) =>
        !string.Equals(snapshot.NormalizedDeckDoc, readState.NormalizedDeckDoc, StringComparison.Ordinal);

    private async Task<DeckReferenceResolution> ResolveDeckResolutionAsync(
        string DeckReference,
        DeckToolOperation operation,
        string? workingDirectory,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DeckReference))
        {
            throw new ArgumentException("A Deck reference is required.", nameof(DeckReference));
        }

        var resolver = ResolveReferenceResolver(serviceProvider);
        if (resolver is not null)
        {
            var resolutionContext = new DeckReferenceResolverContext(
                DeckReference,
                ResolveWorkingDirectory(workingDirectory),
                operation,
                _options,
                serviceProvider);
            var resolution = await resolver.ResolveAsync(DeckReference, resolutionContext, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return DeckReferenceResolution.CreateFile(ResolvePath(DeckReference, workingDirectory));
    }

    private async Task<DeckGrepSearchToolResult> GrepExplicitDeckReferencesAsync(
        string[] DeckReferences,
        string pattern,
        bool useRegex,
        bool caseSensitive,
        int contextLines,
        string root,
        int? maxResults,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var limit = NormalizeSearchLimit(maxResults);
        var matches = new List<DeckGrepMatch>();
        var truncated = false;
        var effectiveContextLines = Math.Clamp(contextLines, 0, 20);
        var skippedReferences = 0;
        var searchedKeys = new HashSet<string>(StringComparer.Ordinal);
        Regex? regex = null;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (useRegex)
        {
            regex = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        }

        foreach (var DeckReference in DeckReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DeckReferenceResolution resolution;
            try
            {
                resolution = await ResolveDeckResolutionAsync(DeckReference, DeckToolOperation.Read, root, serviceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                skippedReferences++;
                continue;
            }

            // Skip duplicate resolver targets so aliases that normalize to the same Deck are searched only once.
            if (!searchedKeys.Add(resolution.ReadStateKey))
            {
                continue;
            }

            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                skippedReferences++;
                continue;
            }

            var context = _handlerRegistry.CreateContext(DeckReference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                skippedReferences++;
                continue;
            }

            try
            {
                if (!await context.ExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    skippedReferences++;
                    continue;
                }

                var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
                var normalizedDeckDoc = DeckSupport.NormalizeLineEndings(response.DeckDoc ?? string.Empty);
                if (AddGrepMatches(
                    normalizedDeckDoc,
                    ToToolDeckReference(resolution, root),
                    pattern,
                    regex,
                    comparison,
                    TryParseDeckDocStructure(normalizedDeckDoc),
                    effectiveContextLines,
                    limit,
                    matches,
                    out truncated))
                {
                    break;
                }
            }
            catch
            {
                skippedReferences++;
            }
        }

        var message = skippedReferences > 0
            ? $"Skipped {skippedReferences.ToString(CultureInfo.InvariantCulture)} explicit Deck reference(s) that could not be resolved or read."
            : null;

        return new DeckGrepSearchToolResult(
            true,
            root,
            [.. matches],
            truncated,
            _handlerRegistry.GetSupportedExtensions(serviceProvider),
            message);
    }

    private IDeckReferenceResolver? ResolveReferenceResolver(IServiceProvider? serviceProvider) =>
        _options.ReferenceResolver
        ?? serviceProvider?.GetService(typeof(IDeckReferenceResolver)) as IDeckReferenceResolver;

    private IDeckAssetInterceptor? ResolveAssetInterceptor(IServiceProvider? serviceProvider) =>
        _options.AssetInterceptor
        ?? serviceProvider?.GetService(typeof(IDeckAssetInterceptor)) as IDeckAssetInterceptor;

    private IDeckTemplateStore? ResolveTemplateStore(IServiceProvider? serviceProvider) =>
        _options.TemplateStore
        ?? serviceProvider?.GetService(typeof(IDeckTemplateStore)) as IDeckTemplateStore;

    private static string BuildNoAssetInterceptorMessage() =>
        "No IDeckAssetInterceptor is configured. Provide one through DeckToolsOptions.AssetInterceptor or dependency injection.";

    private static string BuildNoTemplateStoreMessage() =>
        "No IDeckTemplateStore is configured. Provide one through DeckToolsOptions.TemplateStore or dependency injection.";

    private string ResolvePath(string path, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(ResolveWorkingDirectory(workingDirectory), path));
    }

    private string ResolveWorkingDirectory(string? workingDirectory) =>
        string.IsNullOrWhiteSpace(workingDirectory)
            ? _defaultWorkingDirectory
            : NormalizeDirectory(Path.IsPathRooted(workingDirectory)
                ? workingDirectory
                : Path.Combine(_defaultWorkingDirectory, workingDirectory));

    private static string NormalizeDirectory(string? workingDirectory) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory);

    private int NormalizeSearchLimit(int? maxResults) =>
        Math.Clamp(maxResults ?? _options.MaxSearchResults, 1, Math.Max(1, _options.MaxSearchResults));

    private static bool AddGrepMatches(
        string? DeckDoc,
        string DeckReference,
        string pattern,
        Regex? regex,
        StringComparison comparison,
        DeckDocStructure? structure,
        int contextLines,
        int limit,
        List<DeckGrepMatch> matches,
        out bool truncated)
    {
        truncated = false;

        var lines = DeckSupport.NormalizeLineEndings(DeckDoc ?? string.Empty).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var isMatch = regex is not null
                ? regex.IsMatch(lines[index])
                : lines[index].Contains(pattern, comparison);
            if (!isMatch)
            {
                continue;
            }

            if (matches.Count >= limit)
            {
                truncated = true;
                return true;
            }

            var beforeStart = Math.Max(0, index - contextLines);
            var afterEnd = Math.Min(lines.Length - 1, index + contextLines);
            var slide = structure?.FindSlideForLine(index + 1);
            matches.Add(new DeckGrepMatch(
                DeckReference,
                index + 1,
                slide?.SlideNumber,
                slide?.Title,
                lines[index],
                lines[beforeStart..index],
                lines[(index + 1)..(afterEnd + 1)]));
        }

        return false;
    }

    private static DeckDocStructure? TryParseDeckDocStructure(string deckDoc)
    {
        try
        {
            return DeckDocStructure.Parse(deckDoc);
        }
        catch (DeckDocParseException)
        {
            return null;
        }
    }

    private static string BuildParseFailureMessage(DeckDocParseException exception)
    {
        var guidance = DeckSpecificationCatalog.Search(exception.QueryHint ?? exception.Message, 2);
        if (guidance.Length == 0)
        {
            return $"DeckDoc syntax error at line {exception.LineNumber.ToString(CultureInfo.InvariantCulture)}, column {exception.ColumnNumber.ToString(CultureInfo.InvariantCulture)}: {exception.Message}";
        }

        var guidanceText = string.Join(
            Environment.NewLine,
            guidance.Select(match => $"{match.Title}: {string.Join(" ", match.Content)}"));

        return $"DeckDoc syntax error at line {exception.LineNumber.ToString(CultureInfo.InvariantCulture)}, column {exception.ColumnNumber.ToString(CultureInfo.InvariantCulture)}: {exception.Message}{Environment.NewLine}{guidanceText}";
    }

    private static string ApplySlideOperations(string deckDoc, IReadOnlyList<DeckSlideEditOperation> slideOperations)
    {
        var structure = DeckDocStructure.Parse(deckDoc);
        var headerLines = structure.Lines[..(structure.Slides[0].StartLineNumber - 1)].ToList();
        var slideBlocks = structure.Slides
            .Select(static slide => string.Join("\n", slide.Lines).TrimEnd())
            .ToList();

        foreach (var operation in slideOperations)
        {
            var action = operation.Action?.Trim().ToLowerInvariant();
            switch (action)
            {
                case "replace":
                    EnsureSlideIndex(operation.SlideNumber, slideBlocks.Count, action);
                    slideBlocks[operation.SlideNumber - 1] = NormalizeSlideOperationText(operation);
                    break;

                case "delete":
                    EnsureSlideIndex(operation.SlideNumber, slideBlocks.Count, action);
                    slideBlocks.RemoveAt(operation.SlideNumber - 1);
                    break;

                case "insert_before":
                    EnsureInsertBeforeIndex(operation.SlideNumber, slideBlocks.Count);
                    slideBlocks.Insert(operation.SlideNumber - 1, NormalizeSlideOperationText(operation));
                    break;

                case "insert_after":
                    EnsureInsertAfterIndex(operation.SlideNumber, slideBlocks.Count);
                    slideBlocks.Insert(Math.Min(slideBlocks.Count, operation.SlideNumber), NormalizeSlideOperationText(operation));
                    break;

                default:
                    throw new DeckDocParseException(
                        $"Unsupported slide action '{operation.Action}'. Expected replace, insert_before, insert_after, or delete.",
                        1,
                        queryHint: "slides");
            }
        }

        if (slideBlocks.Count == 0)
        {
            throw new DeckDocParseException("A deck must contain at least one slide after applying slide_operations.", 1, queryHint: "slides");
        }

        DeckSupport.CompactBlankLines(headerLines);
        var sections = new List<string>();
        var header = string.Join("\n", headerLines).TrimEnd();
        if (!string.IsNullOrWhiteSpace(header))
        {
            sections.Add(header);
        }

        sections.AddRange(slideBlocks.Select(static slide => slide.Trim()));
        return string.Join("\n\n", sections);
    }

    private static string NormalizeSlideOperationText(DeckSlideEditOperation operation)
    {
        var normalized = DeckSupport.NormalizeLineEndings(operation.SlideText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DeckDocParseException($"Slide text is required for the '{operation.Action}' action.", 1, queryHint: "slides");
        }

        if (!normalized.StartsWith("== ", StringComparison.Ordinal))
        {
            throw new DeckDocParseException("Slide text must start with '== Slide Title'.", 1, queryHint: "slides");
        }

        var candidate = $"= Validation{Environment.NewLine}{Environment.NewLine}{normalized}";
        var structure = DeckDocStructure.Parse(candidate);
        if (structure.Slides.Length != 1)
        {
            throw new DeckDocParseException("Each slide operation must provide exactly one slide block.", 1, queryHint: "slides");
        }

        return normalized;
    }

    private static void EnsureSlideIndex(int slideNumber, int slideCount, string action)
    {
        if (slideNumber < 1 || slideNumber > slideCount)
        {
            throw new DeckDocParseException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} is out of range for '{action}'. Valid slides are 1 through {slideCount.ToString(CultureInfo.InvariantCulture)}.", 1, queryHint: "slides");
        }
    }

    private static void EnsureInsertBeforeIndex(int slideNumber, int slideCount)
    {
        if (slideNumber < 1 || slideNumber > slideCount + 1)
        {
            throw new DeckDocParseException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} is out of range for 'insert_before'. Valid targets are 1 through {(slideCount + 1).ToString(CultureInfo.InvariantCulture)}.", 1, queryHint: "slides");
        }
    }

    private static void EnsureInsertAfterIndex(int slideNumber, int slideCount)
    {
        if (slideCount == 0)
        {
            throw new DeckDocParseException("insert_after requires at least one existing slide. Use deck_write_file to create a new deck.", 1, queryHint: "slides");
        }

        if (slideNumber < 1 || slideNumber > slideCount)
        {
            throw new DeckDocParseException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} is out of range for 'insert_after'. Valid targets are 1 through {slideCount.ToString(CultureInfo.InvariantCulture)}.", 1, queryHint: "slides");
        }
    }

    private static string[] NormalizeDeckReferences(string[]? DeckReferences) =>
        DeckReferences is null
            ? []
            : DeckReferences
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .Select(static reference => reference.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static string ToToolDeckReference(DeckReferenceResolution resolution, string rootDirectory) =>
        resolution.FilePath is not null
            ? ToToolDeckReference(resolution.FilePath, rootDirectory)
            : resolution.ResolvedReference;

    private static string ToToolDeckReference(string filePath, string rootDirectory)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return filePath;
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                pending.Push(child);
            }
        }
    }

    private static string ToRelativePath(string root, string filePath) =>
        Path.GetRelativePath(root, filePath).Replace(Path.DirectorySeparatorChar, '/');
}

