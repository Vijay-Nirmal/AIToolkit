using System.Text;

namespace AIToolkit.Tools.Workbook;

/// <summary>
/// Builds the shared and provider-specific prompt text for the generic workbook tools.
/// </summary>
/// <remarks>
/// The catalog centralizes wording so AI function descriptions, system prompt guidance, and provider contributions stay
/// aligned.
/// </remarks>
internal static class ToolPromptCatalog
{
    /// <summary>
    /// Appends a workbook-guidance section to an existing system prompt when one is already present.
    /// </summary>
    public static string AppendSystemPromptSection(string? currentSystemPrompt, string guidance)
    {
        if (string.IsNullOrWhiteSpace(currentSystemPrompt))
        {
            return guidance;
        }

        return string.Join("\n\n", currentSystemPrompt, guidance);
    }

    /// <summary>
    /// Builds the description for <c>workbook_read_file</c>.
    /// </summary>
    public static string GetWorkbookReadFileDescription(WorkbookToolsOptions? options = null) =>
        BuildToolDescription(
            "Reads a supported workbook file and returns its canonical WorkbookDoc representation.",
            [
                "Use this when you need workbook content as text for inspection, summarization, quoting, or planning edits.",
                "The workbook_reference input can be a direct path or a workbook reference such as a URL or ID when the host configures an IWorkbookReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Results use line number + tab format with line numbers starting at 1.",
                "Use workbook_grep_search first when you only need specific keywords, and use offset and limit for partial reads.",
                "Use workbook_edit_file for precise changes and workbook_write_file for full rewrites.",
                "WorkbookDoc basics: start the workbook with '= Workbook Name', start each sheet with '== Sheet Name', and use anchored rows such as '@B5 | a | b | c' for normal sheet data.",
            ],
            GetProviderLines(options, static contribution => contribution.ReadFileDescriptionLines));

    /// <summary>
    /// Builds the description for <c>workbook_write_file</c>.
    /// </summary>
    public static string GetWorkbookWriteFileDescription(WorkbookToolsOptions? options = null) =>
        BuildToolDescription(
            "Writes a supported workbook file from canonical WorkbookDoc.",
            [
                "The workbook_reference input can be a direct path or a workbook reference such as a URL or ID when the host configures an IWorkbookReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Read existing workbooks first before overwriting them.",
                "Use workbook_edit_file for targeted changes and workbook_write_file for new workbooks or full rewrites.",
                "WorkbookDoc basics: use inline formulas such as '=SUM(B2:B10)', optional cached results such as '[result=1250] =SUM(B2:B10)', and compact cell attrs such as '[.hdr bold bg=#D9E2F3] Revenue'.",
            ],
            GetProviderLines(options, static contribution => contribution.WriteFileDescriptionLines));

    /// <summary>
    /// Builds the description for <c>workbook_edit_file</c>.
    /// </summary>
    public static string GetWorkbookEditFileDescription(WorkbookToolsOptions? options = null) =>
        BuildToolDescription(
            "Performs exact string replacements against the canonical WorkbookDoc representation of a supported workbook file.",
            [
                "The workbook_reference input can be a direct path or a workbook reference such as a URL or ID when the host configures an IWorkbookReferenceResolver.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "You must use workbook_read_file first.",
                "Preserve the exact WorkbookDoc you read unless you intentionally want to change structure or styling.",
                "The edit fails when old_string is missing or ambiguous unless replace_all is true.",
            ],
            GetProviderLines(options, static contribution => contribution.EditFileDescriptionLines));

    /// <summary>
    /// Builds the description for <c>workbook_grep_search</c>.
    /// </summary>
    public static string GetWorkbookGrepSearchDescription(WorkbookToolsOptions? options = null) =>
        BuildToolDescription(
            "Searches the canonical text content of supported workbook files.",
            [
                "Use it to find text inside registered workbook formats without opening every file manually.",
                "Prefer it before workbook_read_file when you only need specific keywords or the workbook set is large.",
                "Use includePattern to limit which local workbook files are scanned, useRegex for regular expressions, and contextLines for surrounding text.",
                "Use workbook_references when you want to search explicit resolver-backed workbooks such as URLs or IDs. When workbook_references is supplied, only those references are searched.",
                "Each match returns a path or resolved reference and offset that can be passed directly to workbook_read_file.",
                "Results come from the workbook's canonical WorkbookDoc representation, not the raw provider-specific package format.",
            ],
            GetProviderLines(options, static contribution => contribution.GrepSearchDescriptionLines));

    /// <summary>
    /// Builds the description for <c>workbook_spec_lookup</c>.
    /// </summary>
    public static string GetWorkbookSpecificationLookupDescription(WorkbookToolsOptions? options = null) =>
        BuildToolDescription(
            "Looks up advanced WorkbookDoc language guidance by keyword.",
            [
                "Use this when you need details for advanced WorkbookDoc features such as charts, conditional formatting, ranges, merges, or sparkline syntax.",
                "Pass focused keywords rather than whole paragraphs, for example 'chart combo secondary axis', 'conditional formatting data bar', or 'merge align center'.",
                "The result returns small exact guidance snippets chosen from a curated WorkbookDoc feature index rather than the full markdown specification.",
                "For the common case, prefer the built-in prompt guidance: '= Workbook Name', '== Sheet Name', anchored rows like '@B5 | a | b | c', '[used ...]', '[merge ...]', '[fmt ...]', and '[type ...]'.",
            ],
            GetProviderLines(options, static contribution => contribution.SpecificationLookupDescriptionLines));

    /// <summary>
    /// Builds the merged system-prompt guidance for the generic workbook tools.
    /// </summary>
    public static string GetWorkbookSystemPromptGuidance(WorkbookToolsOptions? options)
    {
        var lines = MergeUniqueLines(
            [
                "Use workbook_read_file when you need a supported workbook as canonical WorkbookDoc instead of raw binary bytes.",
                "The workbook_reference input can be a direct path or, when the host configures an IWorkbookReferenceResolver, a workbook reference such as a URL or ID.",
                "Use documented service-specific reference formats exactly. Do not invent alias schemes or pseudo-URLs.",
                "Read existing workbooks before overwriting or editing them so stale changes are detected.",
                "Use workbook_write_file for new workbooks or full rewrites and workbook_edit_file for targeted exact-string updates.",
                "Use workbook_grep_search before workbook_read_file when you only need specific keywords or the workbook set is large.",
                "Use workbook_references when workbook_grep_search should search explicit resolver-backed workbooks such as URLs or IDs; otherwise it scans the local workspace.",
                "workbook_grep_search returns paths or resolved references and offsets that can be passed directly to workbook_read_file.",
                "Treat the returned WorkbookDoc as the source of truth for reasoning, summarization, and edits.",
                "WorkbookDoc basics: start with '= Workbook Name', use '== Sheet Name' for sheets, and write normal grid data as anchored rows such as '@B5 | a | b | c'.",
                "Use inline attrs such as '[.hdr bold bg=#D9E2F3]' only for non-default formatting, and omit default styling such as black text, white fill, and align=left/middle.",
                "Use 'fmt=' for number formats, not 'numfmt='. Use border syntax such as 'border=all:thin:#D9D9D9' or 'border=left:thin:#D9D9D9,right:thin:#D9D9D9,top:thin:#D9D9D9,bottom:thin:#D9D9D9'.",
                "Use '[used B3:H50]' to preserve intended sheet bounds, '[merge B3:H3 align=center/middle]' for merges, '[fmt <range> ...]' for repeated formatting, and '[type <range> date|time|datetime]' for repeated temporal types.",
                "Formula cells keep the formula as content, for example '=SUM(B2:B10)', and may include an optional cached result such as '[result=1250] =SUM(B2:B10)'.",
                "Use 'workbook_spec_lookup' with focused keywords whenever you need advanced WorkbookDoc syntax for charts, sparklines, conditional formatting, or pivots.",
            ],
            GetProviderLines(options, static contribution => contribution.SystemPromptLines));

        var builder = new StringBuilder("# Using workbook tools");
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
        WorkbookToolsOptions? options,
        Func<WorkbookToolPromptContribution, IEnumerable<string>?> selector)
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
