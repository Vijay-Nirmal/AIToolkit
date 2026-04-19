using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace AIToolkit.Tools;

/// <summary>
/// Coordinates built-in and user-provided file handlers for workspace reads.
/// </summary>
/// <remarks>
/// This pipeline is the collaboration point between <see cref="WorkspaceToolService"/>, caller-supplied
/// <see cref="IWorkspaceFileHandler"/> implementations, and the built-in text, notebook, and media readers.
/// Handler ordering is intentional: custom handlers are given first chance to take over a format before the
/// package falls back to default behavior.
/// </remarks>
internal sealed class WorkspaceFileHandlerPipeline(WorkspaceToolsOptions options)
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bmp"] = "image/bmp",
        [".csv"] = "text/csv",
        [".gif"] = "image/gif",
        [".htm"] = "text/html",
        [".html"] = "text/html",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".json"] = "application/json",
        [".md"] = "text/markdown",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain",
        [".wav"] = "audio/wav",
        [".webm"] = "video/webm",
        [".webp"] = "image/webp",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
    };

    private readonly WorkspaceToolsOptions _options = options;
    private readonly IWorkspaceFileHandler[] _builtInHandlers =
    [
        new NotebookWorkspaceFileHandler(),
        new MediaWorkspaceFileHandler(options),
        new TextWorkspaceFileHandler(options),
    ];

    /// <summary>
    /// Creates a file-read context for the resolved file path.
    /// </summary>
    /// <param name="filePath">The resolved absolute file path.</param>
    /// <param name="request">The logical read request supplied by the tool caller.</param>
    /// <param name="isBinary">A value indicating whether the file appears to contain binary data.</param>
    /// <param name="services">The optional service provider for the current tool invocation.</param>
    /// <param name="readAllBytes">A callback that reads the entire file as bytes.</param>
    /// <param name="readAllText">A callback that reads the entire file as text.</param>
    /// <returns>A context object passed to candidate handlers.</returns>
    public WorkspaceFileReadContext CreateContext(
        string filePath,
        WorkspaceFileReadRequest request,
        bool isBinary,
        IServiceProvider? services,
        Func<CancellationToken, ValueTask<byte[]>> readAllBytes,
        Func<CancellationToken, ValueTask<string>> readAllText)
    {
        var extension = Path.GetExtension(filePath);
        var fileInfo = new FileInfo(filePath);
        return new WorkspaceFileReadContext(
            request,
            extension,
            fileInfo.Exists ? fileInfo.Length : 0,
            TryGetMediaType(extension),
            isBinary,
            _options,
            services,
            readAllBytes,
            readAllText);
    }

    /// <summary>
    /// Resolves the first handler that can read the supplied file.
    /// </summary>
    /// <param name="context">The read context to evaluate.</param>
    /// <param name="services">The optional service provider used to locate handler registrations.</param>
    /// <returns>The selected handler, or <see langword="null"/> when no handler can read the file.</returns>
    public IWorkspaceFileHandler? ResolveHandler(WorkspaceFileReadContext context, IServiceProvider? services)
    {
        foreach (var handler in GetHandlers(services))
        {
            if (handler.CanHandle(context))
            {
                return handler;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to map a file extension to a known media type.
    /// </summary>
    /// <param name="extension">The file extension, including the leading period when present.</param>
    /// <returns>The detected media type, or <see langword="null"/> when the extension is unknown.</returns>
    public static string? TryGetMediaType(string? extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? null
            : MediaTypes.TryGetValue(extension, out var mediaType)
                ? mediaType
                : null;

    private IEnumerable<IWorkspaceFileHandler> GetHandlers(IServiceProvider? services)
    {
        // Option-level handlers win first so hosts can override default behavior without using dependency injection.
        if (_options.FileHandlers is not null)
        {
            foreach (var handler in _options.FileHandlers)
            {
                if (handler is not null)
                {
                    yield return handler;
                }
            }
        }

        // Service-resolved handlers come next, which lets hosts compose handler collections dynamically.
        if (services?.GetService(typeof(IEnumerable<IWorkspaceFileHandler>)) is IEnumerable<IWorkspaceFileHandler> serviceHandlers)
        {
            foreach (var handler in serviceHandlers)
            {
                if (handler is not null)
                {
                    yield return handler;
                }
            }
        }

        foreach (var handler in _builtInHandlers)
        {
            yield return handler;
        }
    }

    private sealed class TextWorkspaceFileHandler(WorkspaceToolsOptions options) : IWorkspaceFileHandler
    {
        private readonly WorkspaceToolsOptions _options = options;

        /// <summary>
        /// Determines whether the built-in text handler should read the file.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <returns><see langword="true"/> when the file is non-binary and not a notebook.</returns>
        public bool CanHandle(WorkspaceFileReadContext context) =>
            !context.IsBinary && !string.Equals(context.Extension, ".ipynb", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a text file and returns numbered lines plus truncation guidance when needed.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <param name="cancellationToken">A token that cancels the read.</param>
        /// <returns>The text content parts for the current read window.</returns>
        public async ValueTask<IEnumerable<AIContent>> ReadAsync(
            WorkspaceFileReadContext context,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(context.Request.Pages))
            {
                return
                [
                    new TextContent("The pages parameter is only supported by page-oriented file handlers such as a PDF handler."),
                ];
            }

            var content = NormalizeLineEndings(await context.ReadAllTextAsync(cancellationToken).ConfigureAwait(false));
            if (content.Length == 0)
            {
                return
                [
                    new TextContent("The file exists but is empty."),
                ];
            }

            var lines = content.Split('\n');
            var startLine = Math.Max(1, context.Request.Offset.GetValueOrDefault(1));
            var limit = Math.Clamp(context.Request.Limit ?? _options.MaxReadLines, 1, Math.Max(1, _options.MaxReadLines));

            if (startLine > lines.Length)
            {
                return
                [
                    new TextContent($"The requested offset starts after the end of the file. Total lines: {lines.Length.ToString(CultureInfo.InvariantCulture)}."),
                ];
            }

            var endLine = Math.Min(lines.Length, startLine + limit - 1);
            var selectedLines = lines[(startLine - 1)..endLine];
            var numbered = FormatNumberedLines(selectedLines, startLine);

            if (endLine < lines.Length)
            {
                return
                [
                    new TextContent($"Showing lines {startLine.ToString(CultureInfo.InvariantCulture)}-{endLine.ToString(CultureInfo.InvariantCulture)} of {lines.Length.ToString(CultureInfo.InvariantCulture)}. Use offset and limit to continue reading the file."),
                    new TextContent(numbered),
                ];
            }

            return
            [
                new TextContent(numbered),
            ];
        }
    }

    private sealed class MediaWorkspaceFileHandler(WorkspaceToolsOptions options) : IWorkspaceFileHandler
    {
        private readonly WorkspaceToolsOptions _options = options;

        /// <summary>
        /// Determines whether the built-in media handler should read the file.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <returns><see langword="true"/> when the file is binary or maps to a supported media type.</returns>
        public bool CanHandle(WorkspaceFileReadContext context) =>
            context.IsBinary
            || IsMediaTypeHandled(context.MediaType);

        /// <summary>
        /// Reads media content and returns it as <see cref="DataContent"/> when it fits configured limits.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <param name="cancellationToken">A token that cancels the read.</param>
        /// <returns>The content parts representing the media file.</returns>
        public async ValueTask<IEnumerable<AIContent>> ReadAsync(
            WorkspaceFileReadContext context,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(context.Request.Pages))
            {
                return
                [
                    new TextContent("The built-in media handler does not support the pages parameter. Register a custom IWorkspaceFileHandler for page-oriented formats such as PDF."),
                ];
            }

            if (context.Length > _options.MaxReadBytes)
            {
                return
                [
                    new TextContent($"The file is {context.Length.ToString(CultureInfo.InvariantCulture)} bytes and exceeds the configured read limit of {_options.MaxReadBytes.ToString(CultureInfo.InvariantCulture)} bytes."),
                ];
            }

            var mediaType = context.MediaType ?? "application/octet-stream";
            var bytes = await context.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
            var content = new DataContent(bytes, mediaType)
            {
                Name = Path.GetFileName(context.Request.FilePath),
            };

            return
            [
                new TextContent($"Returning {mediaType} content for {context.Request.FilePath}."),
                content,
            ];
        }

        private static bool IsMediaTypeHandled(string? mediaType) =>
            mediaType is not null
            && (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NotebookWorkspaceFileHandler : IWorkspaceFileHandler
    {
        /// <summary>
        /// Determines whether the built-in notebook handler should read the file.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <returns><see langword="true"/> when the file has an <c>.ipynb</c> extension.</returns>
        public bool CanHandle(WorkspaceFileReadContext context) =>
            string.Equals(context.Extension, ".ipynb", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads notebook cells and outputs as structured AI content.
        /// </summary>
        /// <param name="context">The file metadata for the pending read.</param>
        /// <param name="cancellationToken">A token that cancels the read.</param>
        /// <returns>The text and binary content parts extracted from the notebook.</returns>
        public async ValueTask<IEnumerable<AIContent>> ReadAsync(
            WorkspaceFileReadContext context,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(context.Request.Pages))
            {
                return
                [
                    new TextContent("The pages parameter is not supported for Jupyter notebooks."),
                ];
            }

            var document = JsonNode.Parse(await context.ReadAllTextAsync(cancellationToken).ConfigureAwait(false)) as JsonObject;
            if (document is null || document["cells"] is not JsonArray cells)
            {
                return
                [
                    new TextContent("The notebook file does not contain a valid cells array."),
                ];
            }

            var parts = new List<AIContent>();
            var builder = new StringBuilder();
            for (var index = 0; index < cells.Count; index++)
            {
                if (cells[index] is not JsonObject cell)
                {
                    continue;
                }

                var cellType = cell["cell_type"]?.GetValue<string>() ?? "code";
                var cellId = cell["id"]?.GetValue<string>() ?? index.ToString(CultureInfo.InvariantCulture);
                builder.Append("Cell ");
                builder.Append(index.ToString(CultureInfo.InvariantCulture));
                builder.Append(" [");
                builder.Append(cellType);
                builder.Append(']');
                builder.Append(" id=");
                builder.Append(cellId);
                builder.AppendLine();

                AppendNotebookSource(builder, cell["source"] as JsonArray);
                AppendNotebookOutputs(parts, builder, cell["outputs"] as JsonArray, index);
                builder.AppendLine();
            }

            parts.Insert(0, new TextContent(builder.ToString().TrimEnd()));
            return parts;
        }

        private static void AppendNotebookSource(StringBuilder builder, JsonArray? source)
        {
            builder.AppendLine("Source:");
            if (source is null || source.Count == 0)
            {
                builder.AppendLine("<empty>");
                return;
            }

            foreach (var line in source)
            {
                builder.Append(line?.GetValue<string>() ?? string.Empty);
            }

            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.AppendLine();
            }
        }

        private static void AppendNotebookOutputs(List<AIContent> parts, StringBuilder builder, JsonArray? outputs, int cellIndex)
        {
            if (outputs is null || outputs.Count == 0)
            {
                return;
            }

            builder.AppendLine("Outputs:");
            foreach (var outputNode in outputs)
            {
                if (outputNode is not JsonObject output)
                {
                    continue;
                }

                if (output["text"] is JsonArray textArray)
                {
                    foreach (var line in textArray)
                    {
                        builder.Append(line?.GetValue<string>() ?? string.Empty);
                    }

                    if (builder.Length > 0 && builder[^1] != '\n')
                    {
                        builder.AppendLine();
                    }
                }

                if (output["data"] is JsonObject data)
                {
                    foreach (var property in data)
                    {
                        if (property.Value is JsonArray outputLines)
                        {
                            builder.Append(property.Key);
                            builder.AppendLine(":");
                            foreach (var line in outputLines)
                            {
                                builder.Append(line?.GetValue<string>() ?? string.Empty);
                            }

                            if (builder.Length > 0 && builder[^1] != '\n')
                            {
                                builder.AppendLine();
                            }
                        }
                        else if (IsImageMediaType(property.Key) && property.Value is JsonValue imageValue)
                        {
                            var base64 = imageValue.GetValue<string>();
                            // Binary image payloads are surfaced as separate content parts so callers can inspect or
                            // render them without scraping base64 back out of the textual notebook transcript.
                            parts.Add(new TextContent($"Notebook cell {cellIndex.ToString(CultureInfo.InvariantCulture)} includes {property.Key} output."));
                            parts.Add(new DataContent(Convert.FromBase64String(base64), property.Key)
                            {
                                Name = $"notebook-cell-{cellIndex.ToString(CultureInfo.InvariantCulture)}",
                            });
                        }
                    }
                }
            }
        }

        private static bool IsImageMediaType(string mediaType) =>
            mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    // Read, write, and edit operations compare normalized content so newline differences do not cause false
    // mismatches when callers move between Windows and Unix-style text.
    internal static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static string FormatNumberedLines(string[] lines, int startLine)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            builder.Append((startLine + index).ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(lines[index]);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Stores the latest text-read snapshot for each file path.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceToolService"/> uses this store to enforce read-before-write behavior and to detect when an
/// edit attempt is operating on a stale or partial view of a file.
/// </remarks>
internal sealed class WorkspaceFileReadStateStore
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, WorkspaceFileReadStateEntry> _entries = new(PathComparer);

    /// <summary>
    /// Retrieves the tracked read state for a file path.
    /// </summary>
    /// <param name="path">The normalized file path.</param>
    /// <returns>The stored read state when present; otherwise, <see langword="null"/>.</returns>
    public WorkspaceFileReadStateEntry? Get(string path) =>
        _entries.TryGetValue(path, out var entry) ? entry : null;

    /// <summary>
    /// Records or replaces the tracked read state for a file path.
    /// </summary>
    /// <param name="path">The normalized file path.</param>
    /// <param name="entry">The read state to store.</param>
    public void Set(string path, WorkspaceFileReadStateEntry entry) =>
        _entries[path] = entry;
}

/// <summary>
/// Captures the snapshot information needed to validate write and edit operations after a read.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceToolService"/> stores this record after each successful text read so later writes can
/// detect partial reads or unexpected external modifications.
/// </remarks>
/// <param name="LastWriteUtcTicks">The file's last-write timestamp captured during the read.</param>
/// <param name="NormalizedContent">The normalized file content captured during the read.</param>
/// <param name="IsPartialView"><see langword="true"/> when the read only covered part of the file.</param>
/// <param name="Offset">The optional starting line offset used for the read.</param>
/// <param name="Limit">The optional line limit used for the read.</param>
internal sealed record WorkspaceFileReadStateEntry(
    long LastWriteUtcTicks,
    string NormalizedContent,
    bool IsPartialView,
    int? Offset,
    int? Limit);

/// <summary>
/// Captures decoded text content together with the encoding details needed to preserve file fidelity on write.
/// </summary>
/// <remarks>
/// This record lets edit and write operations compare normalized content while still remembering the original
/// encoding, byte-order mark, and line-ending style for round-tripping the file back to disk.
/// </remarks>
/// <param name="NormalizedContent">The file text with normalized line endings for comparisons.</param>
/// <param name="RawContent">The original decoded text content.</param>
/// <param name="Encoding">The detected text encoding used to decode the file.</param>
/// <param name="HasBom"><see langword="true"/> when the original file included a byte-order mark.</param>
/// <param name="LineEnding">The line-ending style that should be preserved when writing the file.</param>
/// <param name="LastWriteUtcTicks">The file's last-write timestamp captured during the read.</param>
internal sealed record TextFileSnapshot(
    string NormalizedContent,
    string RawContent,
    Encoding Encoding,
    bool HasBom,
    string LineEnding,
    long LastWriteUtcTicks);
