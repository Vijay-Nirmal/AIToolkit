using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Implements the behavior behind the public <c>workbook_*</c> AI functions.
/// </summary>
/// <remarks>
/// This service coordinates reference resolution, handler selection, canonical WorkbookDoc conversion, stale-read tracking,
/// and grep-style search. Provider packages collaborate with it through <see cref="IWorkbookReferenceResolver"/>,
/// <see cref="IWorkbookHandler"/>, and <see cref="IWorkbookToolPromptProvider"/> while the public AI surface stays stable.
/// </remarks>
internal sealed class WorkbookToolService(WorkbookToolsOptions options)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "WorkbookToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

    private readonly WorkbookToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly string _defaultWorkingDirectory = NormalizeDirectory(options.WorkingDirectory);
    private readonly WorkbookHandlerRegistry _handlerRegistry = new(options);
    private readonly WorkbookReadStateStore _readState = new();

    /// <summary>
    /// Reads a supported workbook and returns canonical WorkbookDoc as numbered text content parts.
    /// </summary>
    /// <param name="workbook_reference">The local path or resolver-backed workbook reference to read.</param>
    /// <param name="offset">The optional 1-based WorkbookDoc line offset to start from.</param>
    /// <param name="limit">The optional maximum number of WorkbookDoc lines to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// One or more <see cref="AIContent"/> parts containing guidance messages plus numbered WorkbookDoc lines.
    /// </returns>
    public async Task<IEnumerable<AIContent>> ReadFileAsync(
        string workbook_reference,
        int? offset = null,
        int? limit = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workbook_read_file",
            new Dictionary<string, object?>
            {
                ["workbook_reference"] = workbook_reference,
                ["offset"] = offset,
                ["limit"] = limit,
            });

        try
        {
            var resolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Read, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
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

            var context = _handlerRegistry.CreateContext(workbook_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return
                [
                    new TextContent(BuildNoHandlerMessage(serviceProvider)),
                ];
            }

            var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
            var normalized = WorkbookSupport.NormalizeLineEndings(response.WorkbookDoc ?? string.Empty);
            // Track the exact canonical snapshot that was shown to the model so later edits can reject stale or partial
            // reads before mutating a binary-backed provider workbook.
            TrackRead(context.ReadStateKey, offset, limit, normalized);

            var contents = new List<AIContent>();
            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                contents.Add(new TextContent(response.Message));
            }

            if (!response.IsLosslessRoundTrip)
            {
                contents.Add(new TextContent("Best-effort WorkbookDoc import. Round-trip fidelity is not guaranteed until this workbook is rewritten by workbook_write_file or workbook_edit_file."));
            }

            if (normalized.Length == 0)
            {
                contents.Add(new TextContent("The workbook converted to an empty WorkbookDoc payload."));
                return contents;
            }

            var lines = normalized.Split('\n');
            var startLine = Math.Max(1, offset.GetValueOrDefault(1));
            var effectiveLimit = Math.Clamp(limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));
            if (startLine > lines.Length)
            {
                contents.Add(new TextContent($"The requested offset starts after the end of the converted WorkbookDoc. Total lines: {lines.Length.ToString(CultureInfo.InvariantCulture)}."));
                return contents;
            }

            var endLine = Math.Min(lines.Length, startLine + effectiveLimit - 1);
            if (endLine < lines.Length)
            {
                contents.Add(new TextContent($"Showing lines {startLine.ToString(CultureInfo.InvariantCulture)}-{endLine.ToString(CultureInfo.InvariantCulture)} of {lines.Length.ToString(CultureInfo.InvariantCulture)} from the workbook's canonical WorkbookDoc. Use offset and limit to continue reading."));
            }

            contents.Add(new TextContent(WorkbookSupport.FormatNumberedLines(lines[(startLine - 1)..endLine], startLine)));
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
    /// Writes a supported workbook from canonical WorkbookDoc.
    /// </summary>
    /// <param name="workbook_reference">The local path or resolver-backed workbook reference to write.</param>
    /// <param name="content">The canonical WorkbookDoc payload to persist.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic write result returned to the AI caller.</returns>
    public async Task<WorkbookWriteFileToolResult> WriteFileAsync(
        string workbook_reference,
        string content,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workbook_write_file",
            new Dictionary<string, object?>
            {
                ["workbook_reference"] = workbook_reference,
                ["content"] = content,
            },
            includeContentParameters: _options.LogContentParameters);

        try
        {
            var resolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return new WorkbookWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, "The target path refers to a directory.");
            }

            var context = _handlerRegistry.CreateContext(workbook_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return new WorkbookWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            WorkbookDocSnapshot? originalSnapshot = null;
            if (exists)
            {
                var readState = _readState.Get(context.ReadStateKey);
                if (readState is null || readState.IsPartialView)
                {
                    return new WorkbookWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it.");
                }

                originalSnapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
                if (HasUnexpectedModification(originalSnapshot, readState))
                {
                    return new WorkbookWriteFileToolResult(false, resolution.ResolvedReference, "update", 0, originalSnapshot.NormalizedWorkbookDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it.");
                }
            }

            var normalizedContent = WorkbookSupport.NormalizeLineEndings(content ?? string.Empty);
            var writeResponse = await handler.WriteAsync(context, normalizedContent, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Write, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(workbook_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new WorkbookReadStateEntry(updatedSnapshot.NormalizedWorkbookDoc, false, null, null));

            return new WorkbookWriteFileToolResult(
                true,
                updatedContext.ResolvedReference,
                exists ? "update" : "create",
                normalizedContent.Length,
                originalSnapshot?.NormalizedWorkbookDoc,
                updatedSnapshot.NormalizedWorkbookDoc,
                WorkbookSupport.CreatePatch(originalSnapshot?.NormalizedWorkbookDoc, updatedSnapshot.NormalizedWorkbookDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesWorkbookDocRoundTrip,
                writeResponse.Message);
        }
        catch (Exception exception)
        {
            return new WorkbookWriteFileToolResult(false, workbook_reference, "update", 0, null, null, null, string.Empty, false, exception.Message);
        }
    }

    /// <summary>
    /// Applies an exact-string edit against the canonical WorkbookDoc representation of a supported workbook.
    /// </summary>
    /// <param name="workbook_reference">The local path or resolver-backed workbook reference to edit.</param>
    /// <param name="old_string">The exact canonical WorkbookDoc text to replace.</param>
    /// <param name="new_string">The replacement canonical WorkbookDoc text.</param>
    /// <param name="replace_all"><see langword="true"/> to replace every exact match instead of only a unique single match.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The provider-agnostic edit result returned to the AI caller.</returns>
    public async Task<WorkbookEditFileToolResult> EditFileAsync(
        string workbook_reference,
        string old_string,
        string new_string,
        bool replace_all = false,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workbook_edit_file",
            new Dictionary<string, object?>
            {
                ["workbook_reference"] = workbook_reference,
                ["replace_all"] = replace_all,
                ["old_string"] = old_string,
                ["new_string"] = new_string,
            },
            includeContentParameters: _options.LogContentParameters);

        try
        {
            var resolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (string.Equals(old_string, new_string, StringComparison.Ordinal))
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, "No changes to make: old_string and new_string are exactly the same.");
            }

            var context = _handlerRegistry.CreateContext(workbook_reference, resolution, serviceProvider);
            var handler = _handlerRegistry.ResolveHandler(context);
            if (handler is null)
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, string.Empty, false, BuildNoHandlerMessage(serviceProvider));
            }

            var exists = await context.ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                if (old_string.Length == 0)
                {
                    var normalizedNewWorkbook = WorkbookSupport.NormalizeLineEndings(new_string);
                    var createResponse = await handler.WriteAsync(context, normalizedNewWorkbook, cancellationToken).ConfigureAwait(false);
                    var createdResolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                    var createdContext = _handlerRegistry.CreateContext(workbook_reference, createdResolution, serviceProvider);
                    var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                    _readState.Set(
                        createdContext.ReadStateKey,
                        new WorkbookReadStateEntry(createdSnapshot.NormalizedWorkbookDoc, false, null, null));

                    return new WorkbookEditFileToolResult(
                        true,
                        createdContext.ResolvedReference,
                        1,
                        createdSnapshot.NormalizedWorkbookDoc.Length,
                        null,
                        createdSnapshot.NormalizedWorkbookDoc,
                        WorkbookSupport.CreatePatch(null, createdSnapshot.NormalizedWorkbookDoc, createdContext.ResolvedReference),
                        handler.ProviderName,
                        createResponse.PreservesWorkbookDocRoundTrip,
                        createResponse.Message);
                }

                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File not found.");
            }

            if (context.Length is long knownLength && knownLength > _options.MaxEditFileBytes)
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, $"File is too large to edit ({knownLength.ToString(CultureInfo.InvariantCulture)} bytes). Maximum editable file size is {_options.MaxEditFileBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            var readState = _readState.Get(context.ReadStateKey);
            if (readState is null || readState.IsPartialView)
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, null, null, null, handler.ProviderName, false, "File has not been read yet. Read it first before writing to it.");
            }

            var snapshot = await ReadSnapshotAsync(handler, context, cancellationToken).ConfigureAwait(false);
            if (HasUnexpectedModification(snapshot, readState))
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, 0, snapshot.NormalizedWorkbookDoc, null, null, handler.ProviderName, false, "File has been modified since read. Read it again before attempting to write it.");
            }

            if (old_string.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.NormalizedWorkbookDoc))
                {
                    return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedWorkbookDoc.Length, snapshot.NormalizedWorkbookDoc, null, null, handler.ProviderName, false, "Cannot create new file - file already exists.");
                }

                var createdNormalized = WorkbookSupport.NormalizeLineEndings(new_string);
                var createResponse = await handler.WriteAsync(context, createdNormalized, cancellationToken).ConfigureAwait(false);
                var createdResolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
                var createdContext = _handlerRegistry.CreateContext(workbook_reference, createdResolution, serviceProvider);
                var createdSnapshot = await ReadSnapshotAsync(handler, createdContext, cancellationToken).ConfigureAwait(false);
                _readState.Set(
                    createdContext.ReadStateKey,
                    new WorkbookReadStateEntry(createdSnapshot.NormalizedWorkbookDoc, false, null, null));

                return new WorkbookEditFileToolResult(
                    true,
                    createdContext.ResolvedReference,
                    1,
                    createdSnapshot.NormalizedWorkbookDoc.Length,
                    snapshot.NormalizedWorkbookDoc,
                    createdSnapshot.NormalizedWorkbookDoc,
                    WorkbookSupport.CreatePatch(snapshot.NormalizedWorkbookDoc, createdSnapshot.NormalizedWorkbookDoc, createdContext.ResolvedReference),
                    handler.ProviderName,
                    createResponse.PreservesWorkbookDocRoundTrip,
                    createResponse.Message);
            }

            var normalizedOld = WorkbookSupport.NormalizeLineEndings(old_string);
            var normalizedNew = WorkbookSupport.NormalizeLineEndings(new_string);
            var matches = WorkbookSupport.CountOccurrences(snapshot.NormalizedWorkbookDoc, normalizedOld);
            if (matches == 0)
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedWorkbookDoc.Length, snapshot.NormalizedWorkbookDoc, null, null, handler.ProviderName, false, $"String to replace not found in workbook WorkbookDoc.{Environment.NewLine}String: {old_string}");
            }

            if (matches > 1 && !replace_all)
            {
                return new WorkbookEditFileToolResult(false, resolution.ResolvedReference, 0, snapshot.NormalizedWorkbookDoc.Length, snapshot.NormalizedWorkbookDoc, null, null, handler.ProviderName, false, $"Found {matches.ToString(CultureInfo.InvariantCulture)} matches of the string to replace, but replace_all is false. To replace all occurrences, set replace_all to true. To replace only one occurrence, provide more surrounding context to uniquely identify the instance.{Environment.NewLine}String: {old_string}");
            }

            var updatedNormalized = replace_all
                ? snapshot.NormalizedWorkbookDoc.Replace(normalizedOld, normalizedNew, StringComparison.Ordinal)
                : WorkbookSupport.ReplaceFirst(snapshot.NormalizedWorkbookDoc, normalizedOld, normalizedNew);

            var writeResponse = await handler.WriteAsync(context, updatedNormalized, cancellationToken).ConfigureAwait(false);
            var updatedResolution = await ResolveWorkbookResolutionAsync(workbook_reference, WorkbookToolOperation.Edit, workingDirectory: null, serviceProvider, cancellationToken).ConfigureAwait(false);
            var updatedContext = _handlerRegistry.CreateContext(workbook_reference, updatedResolution, serviceProvider);
            var updatedSnapshot = await ReadSnapshotAsync(handler, updatedContext, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                updatedContext.ReadStateKey,
                new WorkbookReadStateEntry(updatedSnapshot.NormalizedWorkbookDoc, false, null, null));

            return new WorkbookEditFileToolResult(
                true,
                updatedContext.ResolvedReference,
                replace_all ? matches : 1,
                updatedSnapshot.NormalizedWorkbookDoc.Length,
                snapshot.NormalizedWorkbookDoc,
                updatedSnapshot.NormalizedWorkbookDoc,
                WorkbookSupport.CreatePatch(snapshot.NormalizedWorkbookDoc, updatedSnapshot.NormalizedWorkbookDoc, updatedContext.ResolvedReference),
                handler.ProviderName,
                writeResponse.PreservesWorkbookDocRoundTrip,
                writeResponse.Message);
        }
        catch (Exception exception)
        {
            return new WorkbookEditFileToolResult(false, workbook_reference, 0, 0, null, null, null, string.Empty, false, exception.Message);
        }
    }

    /// <summary>
    /// Searches canonical WorkbookDoc across supported local workbooks or explicit resolver-backed references.
    /// </summary>
    /// <param name="pattern">The text or regular expression to search for.</param>
    /// <param name="useRegex"><see langword="true"/> to treat <paramref name="pattern"/> as a regular expression.</param>
    /// <param name="includePattern">An optional glob used to limit local workspace files.</param>
    /// <param name="workbook_references">Optional explicit resolver-backed references to search instead of scanning the local workspace.</param>
    /// <param name="caseSensitive"><see langword="true"/> to perform a case-sensitive search.</param>
    /// <param name="contextLines">The number of context lines to capture before and after each match.</param>
    /// <param name="workingDirectory">The optional workspace root used for relative path resolution.</param>
    /// <param name="maxResults">The optional maximum number of matches to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging, handlers, and resolvers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The grep-style search result returned to the AI caller.</returns>
    public async Task<WorkbookGrepSearchToolResult> GrepSearchAsync(
        string pattern,
        bool useRegex = false,
        string? includePattern = null,
        string[]? workbook_references = null,
        bool caseSensitive = false,
        int contextLines = 0,
        string? workingDirectory = null,
        int? maxResults = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workbook_grep_search",
            new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["useRegex"] = useRegex,
                ["includePattern"] = includePattern,
                ["workbook_references"] = workbook_references,
                ["caseSensitive"] = caseSensitive,
                ["contextLines"] = contextLines,
                ["workingDirectory"] = workingDirectory,
                ["maxResults"] = maxResults,
            });

        try
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            var explicitWorkbookReferences = NormalizeWorkbookReferences(workbook_references);
            if (workbook_references is not null && explicitWorkbookReferences.Length == 0)
            {
                return new WorkbookGrepSearchToolResult(false, root, [], false, [], "workbook_references must contain at least one non-empty workbook reference when supplied.");
            }

            if (explicitWorkbookReferences.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    return new WorkbookGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "A search pattern is required.");
                }

                if (includePattern is not null)
                {
                    return new WorkbookGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), "includePattern cannot be combined with workbook_references. Use includePattern for local workspace scans, or use workbook_references to search explicit resolver-backed workbooks.");
                }

                if (!_handlerRegistry.HasHandlers(serviceProvider))
                {
                    return new WorkbookGrepSearchToolResult(false, root, [], false, [], BuildNoHandlerMessage(serviceProvider));
                }

                return await GrepExplicitWorkbookReferencesAsync(
                    explicitWorkbookReferences,
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
                    ? "Configured workbook handlers do not expose local file extensions. workbook_grep_search currently searches only local workspace files."
                    : BuildNoHandlerMessage(serviceProvider);
                return new WorkbookGrepSearchToolResult(false, root, [], false, [], message);
            }

            var supportedExtensionSet = supportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            GlobMatcher? includeMatcher = string.IsNullOrWhiteSpace(includePattern) ? null : new GlobMatcher(includePattern);
            var limit = NormalizeSearchLimit(maxResults);
            var matches = new List<WorkbookGrepMatch>();
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

                var context = _handlerRegistry.CreateContext(file, WorkbookReferenceResolution.CreateFile(file), serviceProvider);
                var handler = _handlerRegistry.ResolveHandler(context);
                if (handler is null)
                {
                    continue;
                }

                WorkbookReadResponse response;
                try
                {
                    response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                var lines = WorkbookSupport.NormalizeLineEndings(response.WorkbookDoc ?? string.Empty).Split('\n');
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
                    matches.Add(new WorkbookGrepMatch(
                        ToToolWorkbookReference(file, root),
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

            return new WorkbookGrepSearchToolResult(true, root, [.. matches], truncated, supportedExtensions);
        }
        catch (Exception exception)
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            return new WorkbookGrepSearchToolResult(false, root, [], false, _handlerRegistry.GetSupportedExtensions(serviceProvider), exception.Message);
        }
    }

    /// <summary>
    /// Looks up advanced WorkbookDoc guidance by keyword.
    /// </summary>
    /// <param name="keywords">Focused keywords describing the WorkbookDoc feature to look up.</param>
    /// <param name="maxResults">The optional maximum number of matching guidance sections to return.</param>
    /// <param name="serviceProvider">The optional service provider used for logging.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matched WorkbookDoc guidance sections.</returns>
    public Task<WorkbookSpecificationLookupToolResult> SpecificationLookupAsync(
        string keywords,
        int maxResults = 5,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workbook_spec_lookup",
            new Dictionary<string, object?>
            {
                ["keywords"] = keywords,
                ["maxResults"] = maxResults,
            });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(keywords))
            {
                return Task.FromResult(new WorkbookSpecificationLookupToolResult(
                    Success: false,
                    Query: string.Empty,
                    Matches: [],
                    Message: "A keyword query is required. Use focused terms such as 'chart combo secondary axis', 'merge align center', or 'conditional formatting data bar'."));
            }

            var effectiveMaxResults = Math.Clamp(maxResults, 1, Math.Max(1, _options.MaxSearchResults));
            var matches = WorkbookSpecificationCatalog.Search(keywords, effectiveMaxResults);
            var message = matches.Length == 0
                ? "No WorkbookDoc guidance matched those keywords. Try shorter or more specific terms."
                : null;

            return Task.FromResult(new WorkbookSpecificationLookupToolResult(
                Success: true,
                Query: keywords.Trim(),
                Matches: matches,
                Message: message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new WorkbookSpecificationLookupToolResult(
                Success: false,
                Query: keywords,
                Matches: [],
                Message: exception.Message));
        }
    }

    private void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, object parameters, bool includeContentParameters = false)
    {
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory ?? _options.LoggerFactory;
        if (loggerFactory is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger<WorkbookToolService>();
        var serialized = JsonSerializer.Serialize(FilterLoggedParameters(parameters, includeContentParameters), LogJsonOptions);
        ToolInvocationLog(logger, toolName, serialized, null);
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
                return "Configured workbook handlers accept only resolver-backed references such as URLs or IDs; no local file handler can handle this path/reference.";
            }

            return "No IWorkbookHandler is configured. Register a handler from a provider package such as AIToolkit.Tools.Workbook.Excel.";
        }

        return $"No IWorkbookHandler can handle this file. Supported extensions: {string.Join(", ", supported)}.";
    }

    private void TrackRead(string readStateKey, int? offset, int? limit, string normalizedWorkbookDoc)
    {
        var lineCount = normalizedWorkbookDoc.Length == 0
            ? 0
            : normalizedWorkbookDoc.Split('\n').Length;
        var startLine = Math.Max(1, offset.GetValueOrDefault(1));
        var effectiveLimit = Math.Clamp(limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));
        var endLine = lineCount == 0 ? 0 : Math.Min(lineCount, startLine + effectiveLimit - 1);
        var isFullView = lineCount == 0 || startLine <= 1 && endLine >= lineCount;

        _readState.Set(
            readStateKey,
            new WorkbookReadStateEntry(normalizedWorkbookDoc, !isFullView, offset, limit));
    }

    private static async Task<WorkbookDocSnapshot> ReadSnapshotAsync(
        IWorkbookHandler handler,
        WorkbookHandlerContext context,
        CancellationToken cancellationToken)
    {
        var response = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        return new WorkbookDocSnapshot(
            WorkbookSupport.NormalizeLineEndings(response.WorkbookDoc ?? string.Empty));
    }

    private static bool HasUnexpectedModification(WorkbookDocSnapshot snapshot, WorkbookReadStateEntry readState) =>
        !string.Equals(snapshot.NormalizedWorkbookDoc, readState.NormalizedWorkbookDoc, StringComparison.Ordinal);

    private async Task<WorkbookReferenceResolution> ResolveWorkbookResolutionAsync(
        string workbookReference,
        WorkbookToolOperation operation,
        string? workingDirectory,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workbookReference))
        {
            throw new ArgumentException("A workbook reference is required.", nameof(workbookReference));
        }

        var resolver = ResolveReferenceResolver(serviceProvider);
        if (resolver is not null)
        {
            var resolutionContext = new WorkbookReferenceResolverContext(
                workbookReference,
                ResolveWorkingDirectory(workingDirectory),
                operation,
                _options,
                serviceProvider);
            var resolution = await resolver.ResolveAsync(workbookReference, resolutionContext, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return WorkbookReferenceResolution.CreateFile(ResolvePath(workbookReference, workingDirectory));
    }

    private async Task<WorkbookGrepSearchToolResult> GrepExplicitWorkbookReferencesAsync(
        string[] workbookReferences,
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
        var matches = new List<WorkbookGrepMatch>();
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

        foreach (var workbookReference in workbookReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkbookReferenceResolution resolution;
            try
            {
                resolution = await ResolveWorkbookResolutionAsync(workbookReference, WorkbookToolOperation.Read, root, serviceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                skippedReferences++;
                continue;
            }

            // Skip duplicate resolver targets so aliases that normalize to the same workbook are searched only once.
            if (!searchedKeys.Add(resolution.ReadStateKey))
            {
                continue;
            }

            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                skippedReferences++;
                continue;
            }

            var context = _handlerRegistry.CreateContext(workbookReference, resolution, serviceProvider);
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
                    response.WorkbookDoc,
                    ToToolWorkbookReference(resolution, root),
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
            ? $"Skipped {skippedReferences.ToString(CultureInfo.InvariantCulture)} explicit workbook reference(s) that could not be resolved or read."
            : null;

        return new WorkbookGrepSearchToolResult(
            true,
            root,
            [.. matches],
            truncated,
            _handlerRegistry.GetSupportedExtensions(serviceProvider),
            message);
    }

    private IWorkbookReferenceResolver? ResolveReferenceResolver(IServiceProvider? serviceProvider) =>
        _options.ReferenceResolver
        ?? serviceProvider?.GetService(typeof(IWorkbookReferenceResolver)) as IWorkbookReferenceResolver;

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
        string? workbookDoc,
        string workbookReference,
        string pattern,
        Regex? regex,
        StringComparison comparison,
        int contextLines,
        int limit,
        List<WorkbookGrepMatch> matches,
        out bool truncated)
    {
        truncated = false;

        var lines = WorkbookSupport.NormalizeLineEndings(workbookDoc ?? string.Empty).Split('\n');
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
            matches.Add(new WorkbookGrepMatch(
                workbookReference,
                index + 1,
                lines[index],
                lines[beforeStart..index],
                lines[(index + 1)..(afterEnd + 1)]));
        }

        return false;
    }

    private static string[] NormalizeWorkbookReferences(string[]? workbookReferences) =>
        workbookReferences is null
            ? []
            : workbookReferences
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .Select(static reference => reference.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static string ToToolWorkbookReference(WorkbookReferenceResolution resolution, string rootDirectory) =>
        resolution.FilePath is not null
            ? ToToolWorkbookReference(resolution.FilePath, rootDirectory)
            : resolution.ResolvedReference;

    private static string ToToolWorkbookReference(string filePath, string rootDirectory)
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
