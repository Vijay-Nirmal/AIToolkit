using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace AIToolkit.Tools;

/// <summary>
/// Coordinates built-in and user-provided file handlers for workspace reads.
/// </summary>
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

    public static string? TryGetMediaType(string? extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? null
            : MediaTypes.TryGetValue(extension, out var mediaType)
                ? mediaType
                : null;

    private IEnumerable<IWorkspaceFileHandler> GetHandlers(IServiceProvider? services)
    {
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

        public bool CanHandle(WorkspaceFileReadContext context) =>
            !context.IsBinary && !string.Equals(context.Extension, ".ipynb", StringComparison.OrdinalIgnoreCase);

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

        public bool CanHandle(WorkspaceFileReadContext context) =>
            context.IsBinary
            || IsMediaTypeHandled(context.MediaType);

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
        public bool CanHandle(WorkspaceFileReadContext context) =>
            string.Equals(context.Extension, ".ipynb", StringComparison.OrdinalIgnoreCase);

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

internal sealed class WorkspaceFileReadStateStore
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, WorkspaceFileReadStateEntry> _entries = new(PathComparer);

    public WorkspaceFileReadStateEntry? Get(string path) =>
        _entries.TryGetValue(path, out var entry) ? entry : null;

    public void Set(string path, WorkspaceFileReadStateEntry entry) =>
        _entries[path] = entry;
}

internal sealed record WorkspaceFileReadStateEntry(
    long LastWriteUtcTicks,
    string NormalizedContent,
    bool IsPartialView,
    int? Offset,
    int? Limit);

internal sealed record TextFileSnapshot(
    string NormalizedContent,
    string RawContent,
    Encoding Encoding,
    bool HasBom,
    string LineEnding,
    long LastWriteUtcTicks);