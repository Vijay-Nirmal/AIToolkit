using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Collects internal helpers shared across the generic workbook tool pipeline.
/// </summary>
/// <remarks>
/// These helpers keep line-ending normalization, diff generation, and simple text operations consistent across handler
/// implementations so stale-read checks remain provider agnostic.
/// </remarks>
internal static class WorkbookSupport
{
    internal static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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

    internal static int CountOccurrences(string value, string find)
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

    internal static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        var index = content.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0
            ? content
            : string.Concat(content.AsSpan(0, index), newValue, content.AsSpan(index + oldValue.Length));
    }

    internal static string CreatePatch(string? originalContent, string updatedContent, string path)
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
}

/// <summary>
/// Resolves workbook handlers from options and dependency injection.
/// </summary>
/// <remarks>
/// The registry merges handlers supplied directly through <see cref="WorkbookToolsOptions.Handlers"/> with handlers
/// registered in the active <see cref="IServiceProvider"/>. <see cref="WorkbookToolService"/> uses it to locate the first
/// handler that can own a resolved workbook reference.
/// </remarks>
internal sealed class WorkbookHandlerRegistry(WorkbookToolsOptions options)
{
    private readonly WorkbookToolsOptions _options = options;

    /// <summary>
    /// Determines whether any handlers are currently available.
    /// </summary>
    public bool HasHandlers(IServiceProvider? services) =>
        GetHandlers(services).Any();

    /// <summary>
    /// Creates the handler context used for a resolved workbook operation.
    /// </summary>
    public WorkbookHandlerContext CreateContext(string workbookReference, WorkbookReferenceResolution resolution, IServiceProvider? services) =>
        new(workbookReference, resolution, _options, services);

    /// <summary>
    /// Resolves the first handler that can process the supplied workbook context.
    /// </summary>
    public IWorkbookHandler? ResolveHandler(WorkbookHandlerContext context)
    {
        foreach (var handler in GetHandlers(context.Services))
        {
            if (handler.CanHandle(context))
            {
                return handler;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the distinct set of locally searchable file extensions exposed by all known handlers.
    /// </summary>
    public string[] GetSupportedExtensions(IServiceProvider? services) =>
        GetHandlers(services)
            .SelectMany(static handler => handler.SupportedExtensions)
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IEnumerable<IWorkbookHandler> GetHandlers(IServiceProvider? services)
    {
        if (_options.Handlers is not null)
        {
            foreach (var handler in _options.Handlers)
            {
                if (handler is not null)
                {
                    yield return handler;
                }
            }
        }

        if (services?.GetService(typeof(IEnumerable<IWorkbookHandler>)) is IEnumerable<IWorkbookHandler> serviceHandlers)
        {
            foreach (var handler in serviceHandlers)
            {
                if (handler is not null)
                {
                    yield return handler;
                }
            }
        }
    }
}

/// <summary>
/// Tracks the last canonical WorkbookDoc snapshot read for each resolved workbook.
/// </summary>
/// <remarks>
/// The generic edit and write flows use this store to reject stale updates and to distinguish full reads from partial
/// reads.
/// </remarks>
internal sealed class WorkbookReadStateStore
{
    private readonly ConcurrentDictionary<string, WorkbookReadStateEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Retrieves the read-state entry for a resolved workbook key.
    /// </summary>
    public WorkbookReadStateEntry? Get(string path) =>
        _entries.TryGetValue(path, out var entry) ? entry : null;

    /// <summary>
    /// Stores or replaces the read-state entry for a resolved workbook key.
    /// </summary>
    public void Set(string path, WorkbookReadStateEntry entry) =>
        _entries[path] = entry;
}

/// <summary>
/// Captures the canonical WorkbookDoc that was last read together with whether the model saw the full workbook.
/// </summary>
internal sealed record WorkbookReadStateEntry(
    string NormalizedWorkbookDoc,
    bool IsPartialView,
    int? Offset,
    int? Limit);

/// <summary>
/// Represents the current canonical WorkbookDoc snapshot re-read from a provider before a write or edit completes.
/// </summary>
internal sealed record WorkbookDocSnapshot(
    string NormalizedWorkbookDoc);

/// <summary>
/// Evaluates a subset of glob patterns needed for workbook file searches.
/// </summary>
/// <remarks>
/// The matcher intentionally supports only the workbook search scenarios used by <see cref="WorkbookToolService"/>:
/// <c>*</c>, <c>**</c>, <c>?</c>, and simple brace groups.
/// </remarks>
internal sealed class GlobMatcher(string pattern)
{
    private readonly Regex _regex = new(GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>
    /// Determines whether a normalized relative path matches the configured glob expression.
    /// </summary>
    public bool IsMatch(string relativePath) =>
        _regex.IsMatch(relativePath.Replace('\\', '/'));

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
