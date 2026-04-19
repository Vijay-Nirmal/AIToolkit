using System.Text;

namespace AIToolkit.Tools.Document;

internal static class ToolPromptCatalog
{
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

    public static string GetDocumentReadFileDescription(DocumentToolsOptions? options = null) =>
        BuildToolDescription(
            "Reads a supported document file and returns its canonical AsciiDoc representation.",
            [
                "Use this when you need document content as text for inspection, summarization, quoting, or planning edits.",
                "The document_reference input can be a direct path or a document reference such as a URL or ID when the host configures an IDocumentReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Results use line number + tab format with line numbers starting at 1.",
                "Use document_grep_search first when you only need specific keywords, and use offset and limit for partial reads.",
                "Use document_edit_file for precise changes and document_write_file for full rewrites.",
            ],
            GetProviderLines(options, static contribution => contribution.ReadFileDescriptionLines));

    public static string GetDocumentWriteFileDescription(DocumentToolsOptions? options = null) =>
        BuildToolDescription(
            "Writes a supported document file from canonical AsciiDoc.",
            [
                "The document_reference input can be a direct path or a document reference such as a URL or ID when the host configures an IDocumentReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Read existing documents first before overwriting them.",
                "Use document_edit_file for targeted changes and document_write_file for new documents or full rewrites.",
            ],
            GetProviderLines(options, static contribution => contribution.WriteFileDescriptionLines));

    public static string GetDocumentEditFileDescription(DocumentToolsOptions? options = null) =>
        BuildToolDescription(
            "Performs exact string replacements against the canonical AsciiDoc representation of a supported document file.",
            [
                "The document_reference input can be a direct path or a document reference such as a URL or ID when the host configures an IDocumentReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "You must use document_read_file first.",
                "Preserve the exact AsciiDoc you read unless you intentionally want to change structure or styling.",
                "The edit fails when old_string is missing or ambiguous unless replace_all is true.",
            ],
            GetProviderLines(options, static contribution => contribution.EditFileDescriptionLines));

    public static string GetDocumentGrepSearchDescription(DocumentToolsOptions? options = null) =>
        BuildToolDescription(
            "Searches the canonical text content of supported document files.",
            [
                "Use it to find text inside registered document formats without opening every file manually.",
                "Prefer it before document_read_file when you only need specific keywords or the document set is large.",
                "Use includePattern to limit which local document files are scanned, useRegex for regular expressions, and contextLines for surrounding text.",
                "Use document_references when you want to search explicit resolver-backed documents such as URLs or IDs. When document_references is supplied, only those references are searched.",
                "Each match returns a path or resolved reference and offset that can be passed directly to document_read_file.",
                "Results come from the document's canonical AsciiDoc representation, not the raw provider-specific package format.",
            ],
            GetProviderLines(options, static contribution => contribution.GrepSearchDescriptionLines));

    public static string GetDocumentSystemPromptGuidance(DocumentToolsOptions? options)
    {
        var lines = MergeUniqueLines(
            [
                "Use document_read_file when you need a supported document as canonical AsciiDoc instead of raw binary bytes.",
                "The document_reference input can be a direct path or, when the host configures an IDocumentReferenceResolver, a document reference such as a URL or ID.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Read existing documents before overwriting or editing them so stale changes are detected.",
                "Use document_write_file for new documents or full rewrites and document_edit_file for targeted exact-string updates.",
                "Use document_grep_search before document_read_file when you only need specific keywords or the document set is large.",
                "Use document_references when document_grep_search should search explicit resolver-backed documents such as URLs or IDs; otherwise it scans the local workspace.",
                "document_grep_search returns paths or resolved references and offsets that can be passed directly to document_read_file.",
                "Treat the returned AsciiDoc as the source of truth for reasoning, summarization, and edits.",
            ],
            GetProviderLines(options, static contribution => contribution.SystemPromptLines));

        var builder = new StringBuilder("# Using document tools");
        foreach (var line in lines)
        {
            builder.Append('\n');
            builder.Append("- ");
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string BuildToolDescription(
        string summary,
        IEnumerable<string> baseLines,
        IEnumerable<string> providerLines)
    {
        var lines = MergeUniqueLines(baseLines, providerLines);
        var builder = new StringBuilder();
        builder.AppendLine(summary);
        builder.AppendLine();
        builder.AppendLine("Usage:");
        foreach (var line in lines)
        {
            builder.Append("- ");
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static List<string> GetProviderLines(
        DocumentToolsOptions? options,
        Func<DocumentToolPromptContribution, IEnumerable<string>?> selector)
    {
        if (options?.PromptProviders is null)
        {
            return [];
        }

        return MergeUniqueLines(
            options.PromptProviders
                .Where(static provider => provider is not null)
                .SelectMany(provider => selector(provider.GetPromptContribution()) ?? []));
    }

    private static List<string> MergeUniqueLines(
        IEnumerable<string> first,
        IEnumerable<string>? second = null)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in first)
        {
            AddLine(line, merged, seen);
        }

        if (second is not null)
        {
            foreach (var line in second)
            {
                AddLine(line, merged, seen);
            }
        }

        return merged;
    }

    private static void AddLine(string? line, List<string> merged, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var normalized = line.Trim();
        if (seen.Add(normalized))
        {
            merged.Add(normalized);
        }
    }
}