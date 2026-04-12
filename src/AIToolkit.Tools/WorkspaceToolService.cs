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
    private static readonly JsonSerializerOptions LogJsonOptions = ToolJsonSerializerOptions.CreateWeb();

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

    public async Task<WorkspaceReadFileToolResult> ReadFileAsync(
        string path,
        int? startLine = null,
        int? endLine = null,
        string? workingDirectory = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_read_file",
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["startLine"] = startLine,
                ["endLine"] = endLine,
                ["workingDirectory"] = workingDirectory,
            });

        try
        {
            var resolvedPath = ResolvePath(path, workingDirectory);
            if (!File.Exists(resolvedPath))
            {
                return new WorkspaceReadFileToolResult(false, resolvedPath, string.Empty, 0, 0, 0, false, "File not found.");
            }

            if (LooksBinary(resolvedPath))
            {
                return new WorkspaceReadFileToolResult(false, resolvedPath, string.Empty, 0, 0, 0, false, "Binary files are not supported by workspace_read_file.");
            }

            var lines = await File.ReadAllLinesAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            if (lines.Length == 0)
            {
                return new WorkspaceReadFileToolResult(true, resolvedPath, string.Empty, 0, 0, 0, false);
            }

            var actualStartLine = Math.Max(1, startLine ?? 1);
            var actualEndLine = endLine ?? Math.Min(lines.Length, actualStartLine + _options.MaxReadLines - 1);
            actualEndLine = Math.Min(lines.Length, actualEndLine);

            if (actualStartLine > actualEndLine)
            {
                return new WorkspaceReadFileToolResult(false, resolvedPath, string.Empty, 0, 0, lines.Length, false, "The requested line range is invalid.");
            }

            var selectedLines = lines[(actualStartLine - 1)..actualEndLine];
            var truncated = endLine is null && actualEndLine < lines.Length;
            return new WorkspaceReadFileToolResult(
                true,
                resolvedPath,
                string.Join("\n", selectedLines),
                actualStartLine,
                actualEndLine,
                lines.Length,
                truncated);
        }
        catch (Exception exception)
        {
            return new WorkspaceReadFileToolResult(false, path, string.Empty, 0, 0, 0, false, exception.Message);
        }
    }

    public async Task<WorkspaceWriteFileToolResult> WriteFileAsync(
        string path,
        string content,
        bool overwrite = true,
        string? workingDirectory = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_write_file",
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["overwrite"] = overwrite,
                ["workingDirectory"] = workingDirectory,
            });

        try
        {
            var resolvedPath = ResolvePath(path, workingDirectory);
            var exists = File.Exists(resolvedPath);
            if (exists && !overwrite)
            {
                return new WorkspaceWriteFileToolResult(false, resolvedPath, true, 0, "File already exists and overwrite is disabled.");
            }

            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(resolvedPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return new WorkspaceWriteFileToolResult(true, resolvedPath, exists, content.Length);
        }
        catch (Exception exception)
        {
            return new WorkspaceWriteFileToolResult(false, path, false, 0, exception.Message);
        }
    }

    public async Task<WorkspaceEditFileToolResult> EditFileAsync(
        string path,
        WorkspaceFileEditOperation operation,
        string? oldText = null,
        string? newText = null,
        string? anchorText = null,
        string? content = null,
        string? workingDirectory = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(
            serviceProvider,
            "workspace_edit_file",
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["operation"] = operation,
                ["workingDirectory"] = workingDirectory,
            });

        try
        {
            var resolvedPath = ResolvePath(path, workingDirectory);
            if (!File.Exists(resolvedPath))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, 0, "File not found.");
            }

            if (LooksBinary(resolvedPath))
            {
                return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, 0, "Binary files are not supported by workspace_edit_file.");
            }

            var original = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
            var updated = original;
            var changesApplied = 0;

            switch (operation)
            {
                case WorkspaceFileEditOperation.ReplaceOnce:
                    if (string.IsNullOrEmpty(oldText))
                    {
                        return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "oldText is required for ReplaceOnce.");
                    }

                    {
                        var index = updated.IndexOf(oldText, StringComparison.Ordinal);
                        if (index < 0)
                        {
                            return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "oldText was not found.");
                        }

                        updated = updated.Remove(index, oldText.Length).Insert(index, newText ?? string.Empty);
                        changesApplied = 1;
                    }

                    break;

                case WorkspaceFileEditOperation.ReplaceAll:
                    if (string.IsNullOrEmpty(oldText))
                    {
                        return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "oldText is required for ReplaceAll.");
                    }

                    changesApplied = CountOccurrences(updated, oldText);
                    if (changesApplied == 0)
                    {
                        return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "oldText was not found.");
                    }

                    updated = updated.Replace(oldText, newText ?? string.Empty, StringComparison.Ordinal);
                    break;

                case WorkspaceFileEditOperation.InsertBefore:
                    if (string.IsNullOrEmpty(anchorText))
                    {
                        return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "anchorText is required for InsertBefore.");
                    }

                    {
                        var index = updated.IndexOf(anchorText, StringComparison.Ordinal);
                        if (index < 0)
                        {
                            return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "anchorText was not found.");
                        }

                        updated = updated.Insert(index, content ?? string.Empty);
                        changesApplied = 1;
                    }

                    break;

                case WorkspaceFileEditOperation.InsertAfter:
                    if (string.IsNullOrEmpty(anchorText))
                    {
                        return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "anchorText is required for InsertAfter.");
                    }

                    {
                        var index = updated.IndexOf(anchorText, StringComparison.Ordinal);
                        if (index < 0)
                        {
                            return new WorkspaceEditFileToolResult(false, resolvedPath, operation, 0, original.Length, "anchorText was not found.");
                        }

                        updated = updated.Insert(index + anchorText.Length, content ?? string.Empty);
                        changesApplied = 1;
                    }

                    break;

                case WorkspaceFileEditOperation.Prepend:
                    updated = (content ?? string.Empty) + updated;
                    changesApplied = 1;
                    break;

                case WorkspaceFileEditOperation.Append:
                    updated += content ?? string.Empty;
                    changesApplied = 1;
                    break;
            }

            await File.WriteAllTextAsync(resolvedPath, updated, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return new WorkspaceEditFileToolResult(true, resolvedPath, operation, changesApplied, updated.Length);
        }
        catch (Exception exception)
        {
            return new WorkspaceEditFileToolResult(false, path, operation, 0, 0, exception.Message);
        }
    }

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

        public void Append(string value) => _buffer.Append(value);
    }

    private sealed class StreamingOutputSink(Action<string> append) : IOutputSink
    {
        private readonly Action<string> _append = append;

        public void Append(string value) => _append(value);
    }

    /// <summary>
    /// Evaluates a subset of glob patterns needed for workspace file searches.
    /// </summary>
    private sealed class GlobMatcher(string pattern)
    {
        private readonly Regex _regex = new(GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

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