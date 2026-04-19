using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Document;

/// <summary>
/// Implements the behavior behind the public <c>document_*</c> AI functions.
/// </summary>
/// <remarks>
/// This service coordinates reference resolution, handler selection, canonical AsciiDoc conversion, stale-read tracking,
/// and grep-style search. Provider packages collaborate with it through <see cref="IDocumentReferenceResolver"/>,
/// <see cref="IDocumentHandler"/>, and <see cref="IDocumentToolPromptProvider"/> while the public AI surface stays stable.
/// </remarks>
internal sealed class DocumentToolService(DocumentToolsOptions options)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "DocumentToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

    private readonly DocumentToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly string _defaultWorkingDirectory = NormalizeDirectory(options.WorkingDirectory);
    private readonly DocumentHandlerRegistry _handlerRegistry = new(options);
    private readonly DocumentReadStateStore _readState = new();

    /// <summary>
    /// Reads a supported document and returns canonical AsciiDoc as numbered text content parts.
    /// </summary>
    /// <param name="document_reference">The local path or resolver-backed document reference to read.</param>
    /// <param name="offset">The optional 1-based AsciiDoc line offset to start from.</param>
    /// <param name="limit">The optional maximum number of AsciiDoc lines to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// One or more <see cref="AIContent"/> parts containing guidance messages plus numbered AsciiDoc lines.
    /// </returns>
    public async Task<IEnumerable<AIContent>> ReadFileAsync(
        string document_reference,
        int? offset = null,
        int? limit = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "document_read_file",
            new Dictionary<string, object?>
            {
                ["document_reference"] = document_reference,
                ["offset"] = offset,
                ["limit"] = limit,
            });

        try
        {
            var resolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Read, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return
                [
                    new TextContent("The path refers to a directory. This tool can only read files, not directories."),
                ];
            }

            if (!await resolution.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return
                [
                    new TextContent("File not found."),
                ];
            }

            var context = _handlerRegistry.CreateContext(document_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return
                [
                    new TextContent(BuildNoHandlerMessage(serviceProvider)),
                ];
            }

            var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
            var normalized = DocumentSupport.NormalizeLineEndings(response.AsciiDoc ?? string.Empty);
            // Track the exact canonical snapshot that was shown to the model so later edits can reject stale or partial
            // reads before mutating a binary-backed provider document.
            TrackRead(context.ReadStateKey, offset, limit, normalized);

            var contents = new List<AIContent>();
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                contents.Add(new TextContent(response.Message));
            }

            if (!response.IsLosslessRoundTrip)
            {
                contents.Add(new TextContent("Best-effort AsciiDoc import. Round-trip fidelity is not guaranteed until this document is rewritten by document_write_file or document_edit_file."));
            }

            if (normalized.Length == 0)
            {
                contents.Add(new TextContent("The document converted to an empty AsciiDoc payload."));
                return contents;
            }

            var lines = normalized.Split('\n');
            var startLine = Math.Max(1, offset.GetValueOrDefault(1));
            var effectiveLimit = Math.Clamp(limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));
            if (startLine > lines.Length)
            {
                contents.Add(new TextContent($"The requested offset starts after the end of the converted AsciiDoc. Total lines: {lines.Length.ToString(CultureInfo.InvariantCulture)}."));
                return contents;
            }

            var endLine = Math.Min(lines.Length, startLine + effectiveLimit - 1);
            if (endLine < lines.Length)
            {
                contents.Add(new TextContent($"Showing lines {startLine.ToString(CultureInfo.InvariantCulture)}-{endLine.ToString(CultureInfo.InvariantCulture)} of {lines.Length.ToString(CultureInfo.InvariantCulture)} from the document's canonical AsciiDoc. Use offset and limit to continue reading."));
            }

            contents.Add(new TextContent(DocumentSupport.FormatNumberedLines(lines[(startLine - 1)..endLine], startLine)));
            return contents;
        }
        catch (Exception exception)
        {
            return
            [
                new TextContent(exception.Message),
            ];
        }
    }

    /// <summary>
    /// Writes a supported document from canonical AsciiDoc.
    /// </summary>
    /// <param name="document_reference">The local path or resolver-backed document reference to write.</param>
    /// <param name="content">The canonical AsciiDoc payload to persist.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic write result returned to the AI caller.</returns>
    public async Task<DocumentWriteFileToolResult> WriteFileAsync(
        string document_reference,
        string content,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "document_write_file",
            new Dictionary<string, object?>
            {
                ["document_reference"] = document_reference,
                ["content"] = content // TODO: Remove it or put it under a flag
            });

        try
        {
            var resolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return new DocumentWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, "The target path refers to a directory.");
            }

            var context = _handlerRegistry.CreateContext(document_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return new DocumentWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            DocumentAsciiDocSnapshot? originalSnapshot = null;
            if (exists)
            {
                var readState = _readState.Get(context.ReadStateKey);
                if (readState is null || readState.IsPartialView)
                {
                    return new DocumentWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it.");
                }

                originalSnapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
                if (HasUnexpectedModification(originalSnapshot, readState))
                {
                    return new DocumentWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, originalSnapshot.NormalizedAsciiDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it.");
                }
            }

            var normalizedContent = DocumentSupport.NormalizeLineEndings(content ?? string.Empty);
            var writeResponse = await handler.WriteAsync(context, normalizedContent, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(document_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new DocumentReadStateEntry(updatedSnapshot.NormalizedAsciiDoc, false, null, null));

            return new DocumentWriteFileToolResult(
                true,
                updatedContext.ResolvedReference,
                exists ? "update" : "create",
                normalizedContent.Length,
                originalSnapshot?.NormalizedAsciiDoc,
                updatedSnapshot.NormalizedAsciiDoc,
                DocumentSupport.CreatePatch(originalSnapshot?.NormalizedAsciiDoc, updatedSnapshot.NormalizedAsciiDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesAsciiDocRoundTrip,
                writeResponse.Message);
        }
        catch (Exception exception)
        {
            return new DocumentWriteFileToolResult(false, document_reference, "update", 0, null, null, null, string.Empty, false, exception.Message);
        }
    }

    /// <summary>
    /// Applies an exact-string edit against the canonical AsciiDoc representation of a supported document.
    /// </summary>
    /// <param name="document_reference">The local path or resolver-backed document reference to edit.</param>
    /// <param name="old_string">The exact canonical AsciiDoc text to replace.</param>
    /// <param name="new_string">The replacement canonical AsciiDoc text.</param>
    /// <param name="replace_all"><see langword="true"/> to replace every exact match instead of only a unique single match.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic edit result returned to the AI caller.</returns>
    public async Task<DocumentEditFileToolResult> EditFileAsync(
        string document_reference,
        string old_string,
        string new_string,
        bool replace_all = false,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "document_edit_file",
            new Dictionary<string, object?>
            {
                ["document_reference"] = document_reference,
                ["replace_all"] = replace_all,
            });

        try
        {
            var resolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (string.Equals(old_string, new_string, StringComparison.Ordinal))
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, "No changes to make: old_string and new_string are exactly the same.");
            }

            var context = _handlerRegistry.CreateContext(document_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                if (old_string.Length == 0)
                {
                    var normalizedNewDocument = DocumentSupport.NormalizeLineEndings(new_string);
                    var createResponse = await handler.WriteAsync(context, normalizedNewDocument, cancellationToken).ConfigureAwait(false);
                    var createdResolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                    var createdContext = _handlerRegistry.CreateContext(document_reference, createdResolution, serviceProvider);
                    var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                    _readState.Set(
                        createdContext.ReadStateKey,
                        new DocumentReadStateEntry(createdSnapshot.NormalizedAsciiDoc, false, null, null));

                    return new DocumentEditFileToolResult(
                        true,
                        createdContext.ResolvedReference,
                        1,
                        createdSnapshot.NormalizedAsciiDoc.Length,
                        null,
                        createdSnapshot.NormalizedAsciiDoc,
                        DocumentSupport.CreatePatch(null, createdSnapshot.NormalizedAsciiDoc, createdContext.ResolvedReference),
                        handler.ProviderName,
                        createResponse.PreservesAsciiDocRoundTrip,
                        createResponse.Message);
                }

                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File not found.");
            }

            if (context.Length is long knownLength && knownLength > _options.MaxEditFileBytes)
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, $"File is too large to edit ({knownLength.ToString(CultureInfo.InvariantCulture)} bytes). Maximum editable file size is {_options.MaxEditFileBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            var readState = _readState.Get(context.ReadStateKey);
            if (readState is null || readState.IsPartialView)
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it.");
            }

            var snapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
            if (HasUnexpectedModification(snapshot, readState))
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, 0, snapshot.NormalizedAsciiDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it.");
            }

            if (old_string.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.NormalizedAsciiDoc))
                {
                    return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedAsciiDoc.Length, snapshot.NormalizedAsciiDoc, null, null, handler.ProviderName, false, "Cannot create new file - file already exists.");
                }

                var createdNormalized = DocumentSupport.NormalizeLineEndings(new_string);
                var createResponse = await handler.WriteAsync(context, createdNormalized, cancellationToken).ConfigureAwait(false);
                var createdResolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                var createdContext = _handlerRegistry.CreateContext(document_reference, createdResolution, serviceProvider);
                var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                _readState.Set(
                    createdContext.ReadStateKey,
                    new DocumentReadStateEntry(createdSnapshot.NormalizedAsciiDoc, false, null, null));

                return new DocumentEditFileToolResult(
                    true,
                    createdContext.ResolvedReference,
                    1,
                    createdSnapshot.NormalizedAsciiDoc.Length,
                    snapshot.NormalizedAsciiDoc,
                    createdSnapshot.NormalizedAsciiDoc,
                    DocumentSupport.CreatePatch(snapshot.NormalizedAsciiDoc, createdSnapshot.NormalizedAsciiDoc, createdContext.ResolvedReference),
                    handler.ProviderName,
                    createResponse.PreservesAsciiDocRoundTrip,
                    createResponse.Message);
            }

            var normalizedOld = DocumentSupport.NormalizeLineEndings(old_string);
            var normalizedNew = DocumentSupport.NormalizeLineEndings(new_string);
            var matches = DocumentSupport.CountOccurrences(snapshot.NormalizedAsciiDoc, normalizedOld);
            if (matches == 0)
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedAsciiDoc.Length, snapshot.NormalizedAsciiDoc, null, null, handler.ProviderName, false, $"String to replace not found in document AsciiDoc.{Environment.NewLine}String: {old_string}");
            }

            if (matches > 1 && !replace_all)
            {
                return new DocumentEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedAsciiDoc.Length, snapshot.NormalizedAsciiDoc, null, null, handler.ProviderName, false, $"Found {matches.ToString(CultureInfo.InvariantCulture)} matches of the string to replace, but replace_all is false. To replace all occurrences, set replace_all to true. To replace only one occurrence, provide more surrounding context to uniquely identify the instance.{Environment.NewLine}String: {old_string}");
            }

            var updatedNormalized = replace_all
                ? snapshot.NormalizedAsciiDoc.Replace(normalizedOld, normalizedNew, StringComparison.Ordinal)
                : DocumentSupport.ReplaceFirst(snapshot.NormalizedAsciiDoc, normalizedOld, normalizedNew);

            var writeResponse = await handler.WriteAsync(context, updatedNormalized, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveDocumentResolutionAsync(document_reference, DocumentToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(document_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new DocumentReadStateEntry(updatedSnapshot.NormalizedAsciiDoc, false, null, null));

            return new DocumentEditFileToolResult(
                true,
                updatedContext.ResolvedReference,
                replace_all ? matches : 1,
                updatedSnapshot.NormalizedAsciiDoc.Length,
                snapshot.NormalizedAsciiDoc,
                updatedSnapshot.NormalizedAsciiDoc,
                DocumentSupport.CreatePatch(snapshot.NormalizedAsciiDoc, updatedSnapshot.NormalizedAsciiDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesAsciiDocRoundTrip,
                writeResponse.Message);
        }
        catch (Exception exception)
        {
            return new DocumentEditFileToolResult(false, document_reference, 0, 0, null, null, null, string.Empty, false, exception.Message);
        }
    }

    /// <summary>
    /// Searches canonical AsciiDoc across supported local documents or explicit resolver-backed references.
    /// </summary>
    /// <param name="pattern">The text or regular expression to search for.</param>
    /// <param name="useRegex"><see langword="true"/> to treat <paramref name="pattern"/> as a regular expression.</param>
    /// <param name="includePattern">An optional glob used to limit local workspace files.</param>
    /// <param name="document_references">Optional explicit resolver-backed references to search instead of scanning the local workspace.</param>
    /// <param name="caseSensitive"><see langword="true"/> to perform a case-sensitive search.</param>
    /// <param name="contextLines">The number of context lines to capture before and after each match.</param>
    /// <param name="workingDirectory">The optional workspace root used for relative path resolution.</param>
    /// <param name="maxResults">The optional maximum number of matches to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The grep-style search result returned to the AI caller.</returns>
    public async Task<DocumentGrepSearchToolResult> GrepSearchAsync(
        string pattern,
        bool useRegex = false,
        string? includePattern = null,
        string[]? document_references = null,
        bool caseSensitive = false,
        int contextLines = 0,
        string? workingDirectory = null,
        int? maxResults = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "document_grep_search",
            new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["useRegex"] = useRegex,
                ["includePattern"] = includePattern,
                ["document_references"] = document_references,
                ["caseSensitive"] = caseSensitive,
                ["contextLines"] = contextLines,
                ["workingDirectory"] = workingDirectory,
                ["maxResults"] = maxResults,
            });

        try
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            var explicitDocumentReferences = NormalizeDocumentReferences(document_references);
            if (document_references is not null && explicitDocumentReferences.Length == 0)
            {
                return new DocumentGrepSearchToolResult(false, root, [], false, [], "document_references must contain at least one non-empty document reference when supplied.");
            }

            if (explicitDocumentReferences.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    return new DocumentGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "A search pattern is required.");
                }

                if (includePattern is not null)
                {
                    return new DocumentGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "includePattern cannot be combined with document_references. Use includePattern for local workspace scans, or use document_references to search explicit resolver-backed documents.");
                }

                if (!_handlerRegistry.HasHandlers(serviceProvider))
                {
                    return new DocumentGrepSearchToolResult(false, root, [], false, [], BuildNoHandlerMessage(serviceProvider));
                }

                return await GrepExplicitDocumentReferencesAsync(
                    explicitDocumentReferences,
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
                    ? "Configured document handlers do not expose local file extensions. document_grep_search currently searches only local workspace files."
                    : BuildNoHandlerMessage(serviceProvider);
                return new DocumentGrepSearchToolResult(false, root, [], false, [], message);
            }

            var supportedExtensionSet = supportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            GlobMatcher? includeMatcher = string.IsNullOrWhiteSpace(includePattern) ? null : new GlobMatcher(includePattern);
            var limit = NormalizeSearchLimit(maxResults);
            var matches = new List<DocumentGrepMatch>();
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

                var context = _handlerRegistry.CreateContext(file, DocumentReferenceResolution.CreateFile(file), serviceProvider);
                var handler = _handlerRegistry.ResolveHandler(context);
                if (handler is null)
                {
                    continue;
                }

                DocumentReadResponse response;
                try
                {
                    response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var lines = DocumentSupport.NormalizeLineEndings(response.AsciiDoc ?? string.Empty).Split('\n');
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
                    matches.Add(new DocumentGrepMatch(
                        ToToolDocumentReference(file, root),
                        index + 1,
                        lines[index],
                        lines[beforeStart..index],
                        lines[(index + 1)..(afterEnd + 1)]));
                }

                if (truncated)
                {
                    break;
                }
            }

            return new DocumentGrepSearchToolResult(true, root, [.. matches], truncated, supportedExtensions);
        }
        catch (Exception exception)
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            return new DocumentGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), exception.Message);
        }
    }

    private static void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, object parameters)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        if (loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger<DocumentToolService>();
        var serialized = JsonSerializer.Serialize(parameters, LogJsonOptions);
        ToolInvocationLog(logger, toolName, serialized, null);
    }

    private string BuildNoHandlerMessage(IServiceProvider? serviceProvider)
    {
        var supported = _handlerRegistry.GetSupportedExtensions(serviceProvider);
        if (supported.Length == 0)
        {
            if (_handlerRegistry.HasHandlers(serviceProvider))
            {
                return "Configured document handlers accept only resolver-backed references such as URLs or IDs; no local file handler can handle this path/reference.";
            }

            return "No IDocumentHandler is configured. Register a handler from a provider package such as AIToolkit.Tools.Document.Word.";
        }

        return $"No IDocumentHandler can handle this file. Supported extensions: {string.Join(", ", supported)}.";
    }

    private void TrackRead(string readStateKey, int? offset, int? limit, string normalizedAsciiDoc)
    {
        var lineCount = normalizedAsciiDoc.Length == 0
            ? 0
            : normalizedAsciiDoc.Split('\n').Length;
        var startLine = Math.Max(1, offset.GetValueOrDefault(1));
        var effectiveLimit = Math.Clamp(limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));
        var endLine = lineCount == 0 ? 0 : Math.Min(lineCount, startLine + effectiveLimit - 1);
        var isFullView = lineCount == 0 || startLine <= 1 && endLine >= lineCount;

        _readState.Set(
            readStateKey,
            new DocumentReadStateEntry(normalizedAsciiDoc, !isFullView, offset, limit));
    }

    private static async Task<DocumentAsciiDocSnapshot> ReadSnapshotAsync(
        IDocumentHandler handler,
        DocumentHandlerContext context,
        CancellationToken cancellationToken)
    {
        var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        return new DocumentAsciiDocSnapshot(
            DocumentSupport.NormalizeLineEndings(response.AsciiDoc ?? string.Empty));
    }

    private static bool HasUnexpectedModification(DocumentAsciiDocSnapshot snapshot, DocumentReadStateEntry readState) =>
        !string.Equals(snapshot.NormalizedAsciiDoc, readState.NormalizedAsciiDoc, StringComparison.Ordinal);

    private async Task<DocumentReferenceResolution> ResolveDocumentResolutionAsync(
        string documentReference,
        DocumentToolOperation operation,
        string? workingDirectory,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentReference))
        {
            throw new ArgumentException("A document reference is required.", nameof(documentReference));
        }

        var resolver = ResolveReferenceResolver(serviceProvider);
        if (resolver is not null)
        {
            var resolutionContext = new DocumentReferenceResolverContext(
                documentReference,
                ResolveWorkingDirectory(workingDirectory),
                operation,
                _options,
                serviceProvider);
            var resolution = await resolver.ResolveAsync(documentReference, resolutionContext, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return DocumentReferenceResolution.CreateFile(ResolvePath(documentReference, workingDirectory));
    }

    private async Task<DocumentGrepSearchToolResult> GrepExplicitDocumentReferencesAsync(
        string[] documentReferences,
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
        var matches = new List<DocumentGrepMatch>();
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

        foreach (var documentReference in documentReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentReferenceResolution resolution;
            try
            {
                resolution = await ResolveDocumentResolutionAsync(documentReference, DocumentToolOperation.Read, root, serviceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                skippedReferences++;
                continue;
            }

            // Skip duplicate resolver targets so aliases that normalize to the same document are searched only once.
            if (!searchedKeys.Add(resolution.ReadStateKey))
            {
                continue;
            }

            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                skippedReferences++;
                continue;
            }

            var context = _handlerRegistry.CreateContext(documentReference, resolution, serviceProvider);
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
                if (AddGrepMatches(
                    response.AsciiDoc,
                    ToToolDocumentReference(resolution, root),
                    pattern,
                    regex,
                    comparison,
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
            ? $"Skipped {skippedReferences.ToString(CultureInfo.InvariantCulture)} explicit document reference(s) that could not be resolved or read."
            : null;

        return new DocumentGrepSearchToolResult(
            true,
            root,
            [.. matches],
            truncated,
            _handlerRegistry.GetSupportedExtensions(serviceProvider),
            message);
    }

    private IDocumentReferenceResolver? ResolveReferenceResolver(IServiceProvider? serviceProvider) =>
        _options.ReferenceResolver
        ?? serviceProvider?.GetService(typeof(IDocumentReferenceResolver)) as IDocumentReferenceResolver;

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
        string? asciiDoc,
        string documentReference,
        string pattern,
        Regex? regex,
        StringComparison comparison,
        int contextLines,
        int limit,
        List<DocumentGrepMatch> matches,
        out bool truncated)
    {
        truncated = false;

        var lines = DocumentSupport.NormalizeLineEndings(asciiDoc ?? string.Empty).Split('\n');
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
            matches.Add(new DocumentGrepMatch(
                documentReference,
                index + 1,
                lines[index],
                lines[beforeStart..index],
                lines[(index + 1)..(afterEnd + 1)]));
        }

        return false;
    }

    private static string[] NormalizeDocumentReferences(string[]? documentReferences) =>
        documentReferences is null
            ? []
            : documentReferences
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .Select(static reference => reference.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static string ToToolDocumentReference(DocumentReferenceResolution resolution, string rootDirectory) =>
        resolution.FilePath is not null
            ? ToToolDocumentReference(resolution.FilePath, rootDirectory)
            : resolution.ResolvedReference;

    private static string ToToolDocumentReference(string filePath, string rootDirectory)
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
