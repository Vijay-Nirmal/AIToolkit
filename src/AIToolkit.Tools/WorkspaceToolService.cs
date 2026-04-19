using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools;

/// <summary>
/// Implements the behavior behind the public <c>workspace_*</c> AI functions.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceAIFunctionFactory"/> reflects over this service to create host-facing AI functions. The
/// service coordinates shell execution, read-before-write safety checks, file handler selection, notebook editing,
/// and file-search behavior while sharing background task state through <see cref="ITaskToolStore"/>.
/// </remarks>
/// <seealso cref="WorkspaceTools"/>
/// <seealso cref="WorkspaceFileHandlerPipeline"/>
internal sealed class WorkspaceToolService(WorkspaceToolsOptions options, ITaskToolStore taskStore)
{
    private static readonly Action<ILogger, string, string, Exception?> ToolInvocationLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "WorkspaceToolInvocation"),
            "AI tool call {ToolName} with parameters {Parameters}");

    private readonly WorkspaceToolsOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ITaskToolStore _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
    private readonly string _defaultWorkingDirectory = NormalizeDirectory(options.WorkingDirectory);
    private readonly WorkspaceFileHandlerPipeline _fileHandlerPipeline = new(options);
    private readonly WorkspaceFileReadStateStore _readState = new();
    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

    /// <summary>
    /// Executes a command using Bash-compatible shells.
    /// </summary>
    /// <param name="command">The command text to execute.</param>
    /// <param name="workingDirectory">The optional working directory, relative to the configured workspace when not rooted.</param>
    /// <param name="timeoutSeconds">The optional timeout in seconds for foreground execution.</param>
    /// <param name="runInBackground"><see langword="true"/> to track the command as a background task instead of waiting for completion.</param>
    /// <param name="taskSubject">The optional task subject to use when <paramref name="runInBackground"/> is <see langword="true"/>.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the tool call.</param>
    /// <returns>A structured result containing command output or the created background task identifier.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceCommandToolResult> RunBashAsync(
        string command,
        string? workingDirectory = null,
        int? timeoutSeconds = null,
        bool runInBackground = false,
        string? taskSubject = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_run_bash",
            new Dictionary<string, object?>
            {
                ["command"] = command,
                ["workingDirectory"] = workingDirectory,
                ["timeoutSeconds"] = timeoutSeconds,
                ["runInBackground"] = runInBackground,
                ["taskSubject"] = taskSubject,
            });

        return await RunShellAsync(
            command,
            ShellKind.Bash,
            workingDirectory,
            timeoutSeconds,
            runInBackground,
            taskSubject,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a command using PowerShell.
    /// </summary>
    /// <param name="command">The command text to execute.</param>
    /// <param name="workingDirectory">The optional working directory, relative to the configured workspace when not rooted.</param>
    /// <param name="timeoutSeconds">The optional timeout in seconds for foreground execution.</param>
    /// <param name="runInBackground"><see langword="true"/> to track the command as a background task instead of waiting for completion.</param>
    /// <param name="taskSubject">The optional task subject to use when <paramref name="runInBackground"/> is <see langword="true"/>.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the tool call.</param>
    /// <returns>A structured result containing command output or the created background task identifier.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceCommandToolResult> RunPowerShellAsync(
        string command,
        string? workingDirectory = null,
        int? timeoutSeconds = null,
        bool runInBackground = false,
        string? taskSubject = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_run_powershell",
            new Dictionary<string, object?>
            {
                ["command"] = command,
                ["workingDirectory"] = workingDirectory,
                ["timeoutSeconds"] = timeoutSeconds,
                ["runInBackground"] = runInBackground,
                ["taskSubject"] = taskSubject,
            });

        return await RunShellAsync(
            command,
            ShellKind.PowerShell,
            workingDirectory,
            timeoutSeconds,
            runInBackground,
            taskSubject,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a file using the first matching workspace file handler.
    /// </summary>
    /// <param name="file_path">The file path to read.</param>
    /// <param name="offset">The optional 1-based starting line for text-style reads.</param>
    /// <param name="limit">The optional maximum number of lines for text-style reads.</param>
    /// <param name="pages">The optional page-range selector for page-oriented formats.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The AI content parts returned by the selected file handler.</returns>
    /// <remarks>
    /// Text-file reads are tracked in <see cref="WorkspaceFileReadStateStore"/> so later write and edit calls can
    /// verify the agent read the file recently and did not only inspect a partial view.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<IEnumerable<AIContent>> ReadFileAsync(
        string file_path,
        int? offset = null,
        int? limit = null,
        string? pages = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_read_file",
            new Dictionary<string, object?>
            {
                ["file_path"] = file_path,
                ["offset"] = offset,
                ["limit"] = limit,
                ["pages"] = pages,
            });

        try
        {
            var resolvedPath = ResolvePath(file_path, null);
            if (Directory.Exists(resolvedPath))
            {
                return
                [
                    new TextContent("The path refers to a directory. This tool can only read files, not directories."),
                ];
            }

            if (!File.Exists(resolvedPath))
            {
                return
                [
                    new TextContent("File not found."),
                ];
            }

            var isBinary = LooksBinary(resolvedPath);
            byte[]? fileBytes = null;
            TextFileSnapshot? textSnapshot = null;
            var request = new WorkspaceFileReadRequest(resolvedPath, offset, limit, pages);
            var context = _fileHandlerPipeline.CreateContext(
                resolvedPath,
                request,
                isBinary,
                serviceProvider,
                async cancellation =>
                {
                    fileBytes ??= await File.ReadAllBytesAsync(resolvedPath, cancellation).ConfigureAwait(false);
                    return fileBytes;
                },
                async cancellation =>
                {
                    textSnapshot ??= await ReadTextSnapshotAsync(resolvedPath, cancellation).ConfigureAwait(false);
                    return textSnapshot.RawContent;
                });

            var handler = _fileHandlerPipeline.ResolveHandler(context, serviceProvider);
            if (handler is null)
            {
                return
                [
                    new TextContent("The file type is not supported by any registered IWorkspaceFileHandler."),
                ];
            }

            var result = await handler.ReadAsync(context, cancellationToken).ConfigureAwait(false);
            // Persist the read snapshot only after the handler succeeds so later writes are validated against the
            // same file state the caller actually saw.
            await TrackTextReadAsync(resolvedPath, isBinary, offset, limit, textSnapshot, cancellationToken).ConfigureAwait(false);
            return result;
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
    /// Writes an entire text file after validating the caller has a fresh full-file read.
    /// </summary>
    /// <param name="file_path">The target file path.</param>
    /// <param name="content">The full replacement file content.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A structured result describing the write outcome.</returns>
    /// <remarks>
    /// Existing files must have been read completely first. This optimistic-concurrency check prevents accidental
    /// overwrites when the user or a formatter changed the file after the tool last read it.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceWriteFileToolResult> WriteFileAsync(
        string file_path,
        string content,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_write_file",
            new Dictionary<string, object?>
            {
                ["file_path"] = file_path,
            });

        try
        {
            var resolvedPath = ResolvePath(file_path, null);
            if (Directory.Exists(resolvedPath))
            {
                return new WorkspaceWriteFileToolResult(false, resolvedPath, "update", 0, null, null, null, "The target path refers to a directory.");
            }

            var exists = File.Exists(resolvedPath);
            if (exists && LooksBinary(resolvedPath))
            {
                return new WorkspaceWriteFileToolResult(false, resolvedPath, "update", 0, null, null, null, "Binary files are not supported by workspace_write_file.");
            }

            TextFileSnapshot? originalSnapshot = null;
            if (exists)
            {
                // Existing files can only be rewritten from a fresh full-file read so the agent does not overwrite
                // unseen content or changes made outside the tool call.
                var readState = _readState.Get(resolvedPath);
                if (readState is null || readState.IsPartialView)
                {
                    return new WorkspaceWriteFileToolResult(false, resolvedPath, "update", 0, null, null, null, "File has not been read yet. Read it first before writing to it.");
                }

                originalSnapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
                if (HasUnexpectedModification(originalSnapshot, readState))
                {
                    return new WorkspaceWriteFileToolResult(false, resolvedPath, "update", 0, originalSnapshot.NormalizedContent, null, null, "File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.");
                }
            }

            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await WriteTextFileAsync(
                resolvedPath,
                content,
                originalSnapshot?.Encoding,
                originalSnapshot?.HasBom ?? false,
                cancellationToken).ConfigureAwait(false);

            var updatedSnapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                resolvedPath,
                new WorkspaceFileReadStateEntry(updatedSnapshot.LastWriteUtcTicks, updatedSnapshot.NormalizedContent, false, null, null));

            return new WorkspaceWriteFileToolResult(
                true,
                resolvedPath,
                exists ? "update" : "create",
                content.Length,
                originalSnapshot?.NormalizedContent,
                content,
                CreatePatch(originalSnapshot?.NormalizedContent, updatedSnapshot.NormalizedContent, resolvedPath));
        }
        catch (Exception exception)
        {
            return new WorkspaceWriteFileToolResult(false, file_path, "update", 0, null, null, null, exception.Message);
        }
    }

    /// <summary>
    /// Applies a deterministic string replacement to a text file.
    /// </summary>
    /// <param name="file_path">The target file path.</param>
    /// <param name="old_string">The exact text to replace.</param>
    /// <param name="new_string">The replacement text.</param>
    /// <param name="replace_all"><see langword="true"/> to replace every match; otherwise, exactly one match must exist.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A structured result describing the edit outcome.</returns>
    /// <remarks>
    /// The tool operates on normalized line endings so the match logic is stable across platforms, then restores
    /// the original line-ending style when writing the updated file back to disk.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceEditFileToolResult> EditFileAsync(
        string file_path,
        string old_string,
        string new_string,
        bool replace_all = false,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_edit_file",
            new Dictionary<string, object?>
            {
                ["file_path"] = file_path,
                ["replace_all"] = replace_all,
            });

        try
        {
            var resolvedPath = ResolvePath(file_path, null);
            if (string.Equals(Path.GetExtension(resolvedPath), ".ipynb", StringComparison.OrdinalIgnoreCase))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, "File is a Jupyter notebook. Use the workspace_edit_notebook tool to edit this file.");
            }

            if (string.Equals(old_string, new_string, StringComparison.Ordinal))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, "No changes to make: old_string and new_string are exactly the same.");
            }

            if (!File.Exists(resolvedPath))
            {
                if (old_string.Length == 0)
                {
                    var directory = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await WriteTextFileAsync(resolvedPath, new_string, null, false, cancellationToken).ConfigureAwait(false);
                    var createdSnapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
                    _readState.Set(
                        resolvedPath,
                        new WorkspaceFileReadStateEntry(createdSnapshot.LastWriteUtcTicks, createdSnapshot.NormalizedContent, false, null, null));

                    return new WorkspaceEditFileToolResult(
                        true,
                        resolvedPath,
                        1,
                        new_string.Length,
                        null,
                        createdSnapshot.NormalizedContent,
                        CreatePatch(null, createdSnapshot.NormalizedContent, resolvedPath));
                }

                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, "File not found.");
            }

            if (LooksBinary(resolvedPath))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, "Binary files are not supported by workspace_edit_file.");
            }

            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > _options.MaxEditFileBytes)
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, $"File is too large to edit ({fileInfo.Length.ToString(CultureInfo.InvariantCulture)} bytes). Maximum editable file size is {_options.MaxEditFileBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            var readState = _readState.Get(resolvedPath);
            if (readState is null || readState.IsPartialView)
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, null, null, null, "File has not been read yet. Read it first before writing to it.");
            }

            var snapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            if (HasUnexpectedModification(snapshot, readState))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, 0, snapshot.NormalizedContent, null, null, "File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.");
            }

            if (old_string.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.NormalizedContent))
                {
                    return new WorkspaceEditFileToolResult(false, resolvedPath, 0, snapshot.NormalizedContent.Length, snapshot.NormalizedContent, null, null, "Cannot create new file - file already exists.");
                }

                var createdNormalized = WorkspaceFileHandlerPipeline.NormalizeLineEndings(new_string);
                await WriteTextFileAsync(
                    resolvedPath,
                    ConvertLineEndings(createdNormalized, snapshot.LineEnding),
                    snapshot.Encoding,
                    snapshot.HasBom,
                    cancellationToken).ConfigureAwait(false);

                var createdSnapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
                _readState.Set(
                    resolvedPath,
                    new WorkspaceFileReadStateEntry(createdSnapshot.LastWriteUtcTicks, createdSnapshot.NormalizedContent, false, null, null));

                return new WorkspaceEditFileToolResult(
                    true,
                    resolvedPath,
                    1,
                    createdSnapshot.NormalizedContent.Length,
                    snapshot.NormalizedContent,
                    createdSnapshot.NormalizedContent,
                    CreatePatch(snapshot.NormalizedContent, createdSnapshot.NormalizedContent, resolvedPath));
            }

            var normalizedOld = WorkspaceFileHandlerPipeline.NormalizeLineEndings(old_string);
            var normalizedNew = WorkspaceFileHandlerPipeline.NormalizeLineEndings(new_string);
            var matches = CountOccurrences(snapshot.NormalizedContent, normalizedOld);
            if (matches == 0)
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, snapshot.NormalizedContent.Length, snapshot.NormalizedContent, null, null, $"String to replace not found in file.{Environment.NewLine}String: {old_string}");
            }

            if (matches > 1 && !replace_all)
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, 0, snapshot.NormalizedContent.Length, snapshot.NormalizedContent, null, null, $"Found {matches.ToString(CultureInfo.InvariantCulture)} matches of the string to replace, but replace_all is false. To replace all occurrences, set replace_all to true. To replace only one occurrence, provide more surrounding context to uniquely identify the instance.{Environment.NewLine}String: {old_string}");
            }

            // Matching is performed against normalized content so edits remain predictable even when the file uses
            // Windows line endings and the incoming replacement text does not.
            var updatedNormalized = replace_all
                ? snapshot.NormalizedContent.Replace(normalizedOld, normalizedNew, StringComparison.Ordinal)
                : ReplaceFirst(snapshot.NormalizedContent, normalizedOld, normalizedNew);

            await WriteTextFileAsync(
                resolvedPath,
                ConvertLineEndings(updatedNormalized, snapshot.LineEnding),
                snapshot.Encoding,
                snapshot.HasBom,
                cancellationToken).ConfigureAwait(false);

            var updatedSnapshot = await ReadTextSnapshotAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            _readState.Set(
                resolvedPath,
                new WorkspaceFileReadStateEntry(updatedSnapshot.LastWriteUtcTicks, updatedSnapshot.NormalizedContent, false, null, null));

            return new WorkspaceEditFileToolResult(
                true,
                resolvedPath,
                replace_all ? matches : 1,
                updatedSnapshot.NormalizedContent.Length,
                snapshot.NormalizedContent,
                updatedSnapshot.NormalizedContent,
                CreatePatch(snapshot.NormalizedContent, updatedSnapshot.NormalizedContent, resolvedPath));
        }
        catch (Exception exception)
        {
            return new WorkspaceEditFileToolResult(false, file_path, 0, 0, null, null, null, exception.Message);
        }
    }

    /// <summary>
    /// Searches for files whose relative paths match a glob pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern to match.</param>
    /// <param name="workingDirectory">The optional working directory to search from.</param>
    /// <param name="maxResults">The optional maximum number of matches to return.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>A structured result containing the matching paths.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public Task<WorkspaceGlobSearchToolResult> GlobSearchAsync(
        string pattern,
        string? workingDirectory = null,
        int? maxResults = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_glob_search",
            new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["workingDirectory"] = workingDirectory,
                ["maxResults"] = maxResults,
            });

        try
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            var matcher = new GlobMatcher(pattern);
            var limit = NormalizeSearchLimit(maxResults);
            var results = new List<string>();
            var truncated = false;

            foreach (var file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = ToRelativePath(root, file);
                if (!matcher.IsMatch(relativePath))
                {
                    continue;
                }

                if (results.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                results.Add(relativePath);
            }

            return Task.FromResult(new WorkspaceGlobSearchToolResult(true, root, [.. results], truncated));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new WorkspaceGlobSearchToolResult(false, ResolveWorkingDirectory(workingDirectory), Array.Empty<string>(), false, exception.Message));
        }
    }

    /// <summary>
    /// Searches text files for a matching line or regular expression.
    /// </summary>
    /// <param name="pattern">The text or regular-expression pattern to search for.</param>
    /// <param name="useRegex"><see langword="true"/> to treat <paramref name="pattern"/> as a regular expression.</param>
    /// <param name="includePattern">An optional glob filter applied to relative file paths before reading file contents.</param>
    /// <param name="caseSensitive"><see langword="true"/> to perform a case-sensitive search.</param>
    /// <param name="contextLines">The number of context lines to include before and after each match.</param>
    /// <param name="maxResults">The optional maximum number of matches to return.</param>
    /// <param name="workingDirectory">The optional working directory to search from.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>A structured result containing the matching lines.</returns>
    /// <exception cref="ArgumentException">Thrown when an invalid regular expression is supplied.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceGrepSearchToolResult> GrepSearchAsync(
        string pattern,
        bool useRegex = false,
        string? includePattern = null,
        bool caseSensitive = false,
        int contextLines = 0,
        int? maxResults = null,
        string? workingDirectory = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_grep_search",
            new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["useRegex"] = useRegex,
                ["includePattern"] = includePattern,
                ["caseSensitive"] = caseSensitive,
                ["contextLines"] = contextLines,
                ["maxResults"] = maxResults,
                ["workingDirectory"] = workingDirectory,
            });

        try
        {
            var root = ResolveWorkingDirectory(workingDirectory);
            var limit = NormalizeSearchLimit(maxResults);
            var effectiveContextLines = Math.Clamp(contextLines, 0, _options.MaxSearchContextLines);
            GlobMatcher? includeMatcher = string.IsNullOrWhiteSpace(includePattern) ? null : new GlobMatcher(includePattern);
            var matches = new List<WorkspaceGrepMatch>();
            var truncated = false;
            Regex? regex = null;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (useRegex)
            {
                regex = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }

            foreach (var file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = ToRelativePath(root, file);
                if (includeMatcher is not null && !includeMatcher.IsMatch(relativePath))
                {
                    continue;
                }

                if (LooksBinary(file))
                {
                    continue;
                }

                var lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
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
                    matches.Add(new WorkspaceGrepMatch(
                        relativePath,
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

            return new WorkspaceGrepSearchToolResult(true, root, [.. matches], truncated);
        }
        catch (Exception exception)
        {
            return new WorkspaceGrepSearchToolResult(false, ResolveWorkingDirectory(workingDirectory), Array.Empty<WorkspaceGrepMatch>(), false, exception.Message);
        }
    }

    /// <summary>
    /// Edits a Jupyter notebook using cell-aware operations.
    /// </summary>
    /// <param name="path">The notebook path.</param>
    /// <param name="operation">The notebook edit operation to perform.</param>
    /// <param name="cellId">The optional target cell identifier.</param>
    /// <param name="cellIndex">The optional zero-based target cell index.</param>
    /// <param name="afterCellId">The optional cell identifier used by <see cref="WorkspaceNotebookEditOperation.InsertAfter"/>.</param>
    /// <param name="afterCellIndex">The optional cell index used by <see cref="WorkspaceNotebookEditOperation.InsertAfter"/>.</param>
    /// <param name="cellType">The optional cell type for inserted or replaced cells.</param>
    /// <param name="content">The optional replacement or inserted cell content.</param>
    /// <param name="workingDirectory">The optional working directory used to resolve relative notebook paths.</param>
    /// <param name="serviceProvider">The optional service provider for the current tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A structured result describing the notebook edit outcome.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<WorkspaceNotebookEditToolResult> EditNotebookAsync(
        string path,
        WorkspaceNotebookEditOperation operation,
        string? cellId = null,
        int? cellIndex = null,
        string? afterCellId = null,
        int? afterCellIndex = null,
        WorkspaceNotebookCellType? cellType = null,
        string? content = null,
        string? workingDirectory = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_edit_notebook",
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["operation"] = operation,
                ["cellId"] = cellId,
                ["cellIndex"] = cellIndex,
                ["afterCellId"] = afterCellId,
                ["afterCellIndex"] = afterCellIndex,
                ["cellType"] = cellType,
                ["workingDirectory"] = workingDirectory,
            });

        try
        {
            var resolvedPath = ResolvePath(path, workingDirectory);
            if (!File.Exists(resolvedPath))
            {
                return new WorkspaceNotebookEditToolResult(false, resolvedPath, operation, 0, null, "Notebook file not found.");
            }

            var document = JsonNode.Parse(await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false)) as JsonObject;
            if (document is null || document["cells"] is not JsonArray cells)
            {
                return new WorkspaceNotebookEditToolResult(false, resolvedPath, operation, 0, null, "The notebook file does not contain a valid cells array.");
            }

            WorkspaceNotebookCellSummary? affectedCell = null;

            switch (operation)
            {
                case WorkspaceNotebookEditOperation.InsertTop:
                    {
                        var newCell = CreateNotebookCell(cellType ?? WorkspaceNotebookCellType.Code, content ?? string.Empty);
                        cells.Insert(0, newCell);
                        affectedCell = CreateCellSummary(newCell.AsObject(), 0);
                    }

                    break;

                case WorkspaceNotebookEditOperation.InsertBottom:
                    {
                        var newCell = CreateNotebookCell(cellType ?? WorkspaceNotebookCellType.Code, content ?? string.Empty);
                        cells.Add(newCell);
                        affectedCell = CreateCellSummary(newCell.AsObject(), cells.Count - 1);
                    }

                    break;

                case WorkspaceNotebookEditOperation.InsertAfter:
                    {
                        var index = ResolveNotebookCellIndex(cells, afterCellId, afterCellIndex);
                        if (index < 0)
                        {
                            return new WorkspaceNotebookEditToolResult(false, resolvedPath, operation, cells.Count, null, "The target cell for InsertAfter was not found.");
                        }

                        var newCell = CreateNotebookCell(cellType ?? WorkspaceNotebookCellType.Code, content ?? string.Empty);
                        cells.Insert(index + 1, newCell);
                        affectedCell = CreateCellSummary(newCell.AsObject(), index + 1);
                    }

                    break;

                case WorkspaceNotebookEditOperation.Replace:
                    {
                        var index = ResolveNotebookCellIndex(cells, cellId, cellIndex);
                        if (index < 0 || cells[index] is not JsonObject existingCell)
                        {
                            return new WorkspaceNotebookEditToolResult(false, resolvedPath, operation, cells.Count, null, "The target cell for Replace was not found.");
                        }

                        if (cellType is not null)
                        {
                            existingCell["cell_type"] = ToNotebookCellType(cellType.Value);
                        }

                        if (content is not null)
                        {
                            existingCell["source"] = CreateNotebookSource(content);
                        }

                        affectedCell = CreateCellSummary(existingCell, index);
                    }

                    break;

                case WorkspaceNotebookEditOperation.Delete:
                    {
                        var index = ResolveNotebookCellIndex(cells, cellId, cellIndex);
                        if (index < 0)
                        {
                            return new WorkspaceNotebookEditToolResult(false, resolvedPath, operation, cells.Count, null, "The target cell for Delete was not found.");
                        }

                        if (cells[index] is JsonObject deletedCell)
                        {
                            affectedCell = CreateCellSummary(deletedCell, index);
                        }

                        cells.RemoveAt(index);
                    }

                    break;
            }

            var json = document.ToJsonString(ToolJsonSerializerOptions.CreateIndented());
            await File.WriteAllTextAsync(resolvedPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return new WorkspaceNotebookEditToolResult(true, resolvedPath, operation, cells.Count, affectedCell);
        }
        catch (Exception exception)
        {
            return new WorkspaceNotebookEditToolResult(false, path, operation, 0, null, exception.Message);
        }
    }

    private async Task TrackTextReadAsync(
        string path,
        bool isBinary,
        int? offset,
        int? limit,
        TextFileSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (isBinary || string.Equals(Path.GetExtension(path), ".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        snapshot ??= await ReadTextSnapshotAsync(path, cancellationToken).ConfigureAwait(false);
        var lineCount = snapshot.NormalizedContent.Length == 0
            ? 0
            : snapshot.NormalizedContent.Split('\n').Length;
        var startLine = Math.Max(1, offset.GetValueOrDefault(1));
        var effectiveLimit = Math.Clamp(limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));
        var endLine = lineCount == 0 ? 0 : Math.Min(lineCount, startLine + effectiveLimit - 1);
        var isFullView = lineCount == 0 || startLine <= 1 && endLine >= lineCount;

        _readState.Set(
            path,
            new WorkspaceFileReadStateEntry(snapshot.LastWriteUtcTicks, snapshot.NormalizedContent, !isFullView, offset, limit));
    }

    private static async Task<TextFileSnapshot> ReadTextSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var lastWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;

        Encoding encoding;
        var preambleLength = 0;
        var hasBom = false;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            preambleLength = 3;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            preambleLength = 2;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 2;
            hasBom = true;
        }
        else
        {
            encoding = new UTF8Encoding(false);
        }

        var rawContent = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var normalized = WorkspaceFileHandlerPipeline.NormalizeLineEndings(rawContent);
        var lineEnding = rawContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new TextFileSnapshot(normalized, rawContent, encoding, hasBom, lineEnding, lastWriteUtcTicks);
    }

    private static async Task WriteTextFileAsync(
        string path,
        string content,
        Encoding? encoding,
        bool hasBom,
        CancellationToken cancellationToken)
    {
        var effectiveEncoding = encoding switch
        {
            UTF8Encoding => new UTF8Encoding(hasBom),
            null => new UTF8Encoding(false),
            _ => encoding,
        };

        await using var stream = File.Create(path);
        var preamble = hasBom ? effectiveEncoding.GetPreamble() : [];
        if (preamble.Length > 0)
        {
            await stream.WriteAsync(preamble, cancellationToken).ConfigureAwait(false);
        }

        var bytes = effectiveEncoding.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasUnexpectedModification(TextFileSnapshot snapshot, WorkspaceFileReadStateEntry readState) =>
        snapshot.LastWriteUtcTicks > readState.LastWriteUtcTicks
        && !string.Equals(snapshot.NormalizedContent, readState.NormalizedContent, StringComparison.Ordinal);

    private static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        var index = content.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0
            ? content
            : string.Concat(content.AsSpan(0, index), newValue, content.AsSpan(index + oldValue.Length));
    }

    private static string ConvertLineEndings(string content, string lineEnding) =>
        string.Equals(lineEnding, "\r\n", StringComparison.Ordinal)
            ? content.Replace("\n", "\r\n", StringComparison.Ordinal)
            : content;

    private static string CreatePatch(string? originalContent, string updatedContent, string path)
    {
        var normalizedOriginal = originalContent ?? string.Empty;
        if (string.Equals(normalizedOriginal, updatedContent, StringComparison.Ordinal))
        {
            return $"--- {path}{Environment.NewLine}+++ {path}{Environment.NewLine}";
        }

        var oldLines = normalizedOriginal.Split('\n');
        var newLines = updatedContent.Split('\n');

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var oldSuffix = oldLines.Length - 1;
        var newSuffix = newLines.Length - 1;
        while (oldSuffix >= prefix && newSuffix >= prefix && string.Equals(oldLines[oldSuffix], newLines[newSuffix], StringComparison.Ordinal))
        {
            oldSuffix--;
            newSuffix--;
        }

        var builder = new StringBuilder();
    builder.Append("--- ");
    builder.Append(path);
    builder.AppendLine();
    builder.Append("+++ ");
    builder.Append(path);
    builder.AppendLine();
        builder.Append("@@ -");
        builder.Append((prefix + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        builder.Append(Math.Max(0, oldSuffix - prefix + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(" +");
        builder.Append((prefix + 1).ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        builder.Append(Math.Max(0, newSuffix - prefix + 1).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(" @@");

        for (var index = prefix; index <= oldSuffix; index++)
        {
            builder.Append('-');
            builder.AppendLine(oldLines[index]);
        }

        for (var index = prefix; index <= newSuffix; index++)
        {
            builder.Append('+');
            builder.AppendLine(newLines[index]);
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<WorkspaceCommandToolResult> RunShellAsync(
        string command,
        ShellKind shellKind,
        string? workingDirectory,
        int? timeoutSeconds,
        bool runInBackground,
        string? taskSubject,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new WorkspaceCommandToolResult(false, string.Empty, ResolveWorkingDirectory(workingDirectory), string.Empty, string.Empty, null, false, false, false, Message: "command is required.");
        }

        var resolvedWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            return new WorkspaceCommandToolResult(false, command, resolvedWorkingDirectory, string.Empty, string.Empty, null, false, false, false, Message: "The working directory does not exist.");
        }

        var effectiveTimeoutSeconds = NormalizeTimeout(timeoutSeconds);
        var startResult = TryStartProcess(shellKind, command, resolvedWorkingDirectory);
        if (!startResult.Success)
        {
            return new WorkspaceCommandToolResult(false, command, resolvedWorkingDirectory, string.Empty, string.Empty, null, false, false, false, Message: startResult.ErrorMessage);
        }

        var process = startResult.Process!;
        if (runInBackground)
        {
            var subject = string.IsNullOrWhiteSpace(taskSubject)
                ? $"{GetShellDisplayName(shellKind)}: {Truncate(command, 80)}"
                : taskSubject;
            var task = _taskStore.CreateProcessTask(subject, command, GetShellDisplayName(shellKind), command, resolvedWorkingDirectory);
            _taskStore.AttachProcess(task.Id, process);
            _ = MonitorBackgroundProcessAsync(_taskStore, task.Id, process, cancellationToken: CancellationToken.None);

            return new WorkspaceCommandToolResult(true, command, resolvedWorkingDirectory, string.Empty, string.Empty, null, false, true, false, task.Id);
        }

        return await RunForegroundProcessAsync(process, command, resolvedWorkingDirectory, effectiveTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceCommandToolResult> RunForegroundProcessAsync(
        Process process,
        string command,
        string workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var ownedProcess = process;
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stdout = new ToolOutputBuffer(_options.MaxTaskOutputCharacters);
        var stderr = new ToolOutputBuffer(_options.MaxTaskOutputCharacters);
        var stdoutTask = CaptureStreamAsync(process.StandardOutput, stdout, CancellationToken.None);
        var stderrTask = CaptureStreamAsync(process.StandardError, stderr, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(linkedTokenSource.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            return new WorkspaceCommandToolResult(
                process.ExitCode == 0,
                command,
                workingDirectory,
                stdout.ToString(),
                stderr.ToString(),
                process.ExitCode,
                false,
                false,
                stdout.Truncated || stderr.Truncated,
                Message: process.ExitCode == 0 ? null : $"Process exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            return new WorkspaceCommandToolResult(
                false,
                command,
                workingDirectory,
                stdout.ToString(),
                stderr.ToString(),
                null,
                true,
                false,
                stdout.Truncated || stderr.Truncated,
                Message: $"Process timed out after {timeoutSeconds} seconds.");
        }
    }

    private static async Task MonitorBackgroundProcessAsync(
        ITaskToolStore taskStore,
        string taskId,
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            var stdoutTask = CaptureStreamAsync(
                process.StandardOutput,
                new StreamingOutputSink(value => taskStore.AppendStandardOutput(taskId, value)),
                cancellationToken);
            var stderrTask = CaptureStreamAsync(
                process.StandardError,
                new StreamingOutputSink(value => taskStore.AppendStandardError(taskId, value)),
                cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            taskStore.CompleteProcess(taskId, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            taskStore.CompleteProcess(taskId, process.HasExited ? process.ExitCode : null);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static Task CaptureStreamAsync(StreamReader reader, ToolOutputBuffer buffer, CancellationToken cancellationToken) =>
        CaptureStreamAsync(reader, new BufferOutputSink(buffer), cancellationToken);

    private static async Task CaptureStreamAsync(StreamReader reader, IOutputSink sink, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            sink.Append(line + Environment.NewLine);
        }
    }

    private static ProcessStartAttempt TryStartProcess(ShellKind shellKind, string command, string workingDirectory)
    {
        foreach (var candidate in GetExecutableCandidates(shellKind))
        {
            try
            {
                var startInfo = CreateStartInfo(shellKind, candidate, command, workingDirectory);
                var process = Process.Start(startInfo);
                if (process is not null)
                {
                    return new ProcessStartAttempt(true, process, null);
                }
            }
            catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
            {
                continue;
            }
        }

        return new ProcessStartAttempt(false, null, $"Unable to start {GetShellDisplayName(shellKind)}. Ensure the shell is installed and available on PATH.");
    }

    private static ProcessStartInfo CreateStartInfo(ShellKind shellKind, string executable, string command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        switch (shellKind)
        {
            case ShellKind.Bash:
                startInfo.ArgumentList.Add("-lc");
                startInfo.ArgumentList.Add(command);
                break;

            case ShellKind.PowerShell:
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(command);
                break;
        }

        return startInfo;
    }

    private static IEnumerable<string> GetExecutableCandidates(ShellKind shellKind)
    {
        if (shellKind == ShellKind.PowerShell)
        {
            if (OperatingSystem.IsWindows())
            {
                yield return "pwsh";
                yield return "powershell";
            }
            else
            {
                yield return "pwsh";
            }

            yield break;
        }

        yield return "bash";
        yield return "sh";
    }

    private static void LogToolInvocation(IServiceProvider? serviceProvider, string toolName, Dictionary<string, object?> parameters)
    {
        if (serviceProvider?.GetService(typeof(ILoggerFactory)) is not ILoggerFactory loggerFactory)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger("AIToolkit.Tools");
        ToolInvocationLog(logger, toolName, JsonSerializer.Serialize(parameters, LogJsonOptions), null);
    }

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

    private int NormalizeTimeout(int? timeoutSeconds)
    {
        var effectiveTimeout = timeoutSeconds ?? _options.DefaultCommandTimeoutSeconds;
        return Math.Clamp(effectiveTimeout, 1, Math.Max(1, _options.MaxCommandTimeoutSeconds));
    }

    private int NormalizeSearchLimit(int? maxResults) =>
        Math.Clamp(maxResults ?? _options.MaxSearchResults, 1, Math.Max(1, _options.MaxSearchResults));

    private static bool LooksBinary(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[512];
        var read = stream.Read(buffer);
        for (var index = 0; index < read; index++)
        {
            if (buffer[index] == 0)
            {
                return true;
            }
        }

        return false;
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

    private static int CountOccurrences(string value, string find)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(find, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += find.Length;
        }

        return count;
    }

    private static int ResolveNotebookCellIndex(JsonArray cells, string? cellId, int? cellIndex)
    {
        if (cellIndex is not null)
        {
            return cellIndex.Value >= 0 && cellIndex.Value < cells.Count ? cellIndex.Value : -1;
        }

        if (!string.IsNullOrWhiteSpace(cellId))
        {
            for (var index = 0; index < cells.Count; index++)
            {
                if (cells[index] is JsonObject cell && string.Equals(cell["id"]?.GetValue<string>(), cellId, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static JsonObject CreateNotebookCell(WorkspaceNotebookCellType cellType, string content)
    {
        var result = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["cell_type"] = ToNotebookCellType(cellType),
            ["metadata"] = new JsonObject(),
            ["source"] = CreateNotebookSource(content),
        };

        if (cellType == WorkspaceNotebookCellType.Code)
        {
            result["execution_count"] = null;
            result["outputs"] = new JsonArray();
        }

        return result;
    }

    private static JsonArray CreateNotebookSource(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var segments = normalized.Split('\n');
        var source = new JsonArray();
        for (var index = 0; index < segments.Length; index++)
        {
            if (index == segments.Length - 1)
            {
                source.Add(segments[index]);
            }
            else
            {
                source.Add(segments[index] + "\n");
            }
        }

        return source;
    }

    private static WorkspaceNotebookCellSummary CreateCellSummary(JsonObject cell, int index)
    {
        var source = cell["source"] as JsonArray;
        return new WorkspaceNotebookCellSummary(
            cell["id"]?.GetValue<string>(),
            index,
            ParseNotebookCellType(cell["cell_type"]?.GetValue<string>()),
            source?.Count ?? 0);
    }

    private static WorkspaceNotebookCellType ParseNotebookCellType(string? value) => value?.ToLowerInvariant() switch
    {
        "markdown" => WorkspaceNotebookCellType.Markdown,
        "raw" => WorkspaceNotebookCellType.Raw,
        _ => WorkspaceNotebookCellType.Code,
    };

    private static string ToNotebookCellType(WorkspaceNotebookCellType value) => value switch
    {
        WorkspaceNotebookCellType.Markdown => "markdown",
        WorkspaceNotebookCellType.Raw => "raw",
        _ => "code",
    };

    private static string GetShellDisplayName(ShellKind shellKind) => shellKind switch
    {
        ShellKind.PowerShell => "PowerShell",
        _ => "Shell",
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    private enum ShellKind
    {
        Bash,
        PowerShell,
    }

    private readonly record struct ProcessStartAttempt(bool Success, Process? Process, string? ErrorMessage);

    private interface IOutputSink
    {
        void Append(string value);
    }

    private sealed class BufferOutputSink(ToolOutputBuffer buffer) : IOutputSink
    {
        private readonly ToolOutputBuffer _buffer = buffer;

        /// <summary>
        /// Appends buffered output text.
        /// </summary>
        /// <param name="value">The text to append.</param>
        public void Append(string value) => _buffer.Append(value);
    }

    private sealed class StreamingOutputSink(Action<string> append) : IOutputSink
    {
        private readonly Action<string> _append = append;

        /// <summary>
        /// Streams output text directly to the callback supplied by the task store monitor.
        /// </summary>
        /// <param name="value">The text to append.</param>
        public void Append(string value) => _append(value);
    }

    /// <summary>
    /// Evaluates a subset of glob patterns needed for workspace file searches.
    /// </summary>
    private sealed class GlobMatcher(string pattern)
    {
        private readonly Regex _regex = new(GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        /// <summary>
        /// Determines whether the supplied relative path matches the configured glob.
        /// </summary>
        /// <param name="relativePath">The relative path to evaluate.</param>
        /// <returns><see langword="true"/> when the path matches the glob; otherwise, <see langword="false"/>.</returns>
        public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath.Replace('\\', '/'));

        private static string GlobToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            var normalized = pattern.Replace('\\', '/');
            for (var index = 0; index < normalized.Length; index++)
            {
                var current = normalized[index];
                if (current == '*')
                {
                    var isDoubleStar = index + 1 < normalized.Length && normalized[index + 1] == '*';
                    if (isDoubleStar)
                    {
                        builder.Append(".*");
                        index++;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    continue;
                }

                if (current == '?')
                {
                    builder.Append("[^/]");
                    continue;
                }

                if (current == '{')
                {
                    var end = normalized.IndexOf('}', index + 1);
                    if (end > index)
                    {
                        var values = normalized[(index + 1)..end].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        builder.Append("(?:");
                        builder.Append(string.Join('|', values.Select(Regex.Escape)));
                        builder.Append(')');
                        index = end;
                        continue;
                    }
                }

                builder.Append(Regex.Escape(current.ToString()));
            }

            builder.Append('$');
            return builder.ToString();
        }
    }
}
