namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Supplies Excel-specific prompt guidance for the generic workbook tools.
/// </summary>
internal sealed class ExcelWorkbookPromptProvider(ExcelWorkbookHandlerOptions options) : IWorkbookToolPromptProvider
{
    private readonly ExcelWorkbookHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public WorkbookToolPromptContribution GetPromptContribution()
    {
        var locationLines = CreateLocationLines();
        var syntaxLines = CreateWorkbookDocSyntaxLines();

        var writeLines = new List<string>(locationLines);
        writeLines.AddRange(syntaxLines);

        var editLines = new List<string>(locationLines)
        {
            "Preserve existing WorkbookDoc structure unless you intentionally want to change workbook layout, formulas, or formatting.",
        };
        editLines.AddRange(syntaxLines);

        var grepLines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            grepLines.Add("Local Excel files can be searched by their .xlsx, .xlsm, .xltx, and .xltm extensions.");
        }

        if (_options.M365 is not null)
        {
            grepLines.Add("Hosted OneDrive and SharePoint Excel workbooks can also be searched by passing explicit workbook_references to workbook_grep_search. Directory-based grep still scans only the local workspace.");
        }

        var specLines = new List<string>
        {
            "Use workbook_spec_lookup for advanced WorkbookDoc features such as chart series syntax, conditional-formatting rule forms, and sparkline types.",
        };

        var systemLines = new List<string>(locationLines);
        systemLines.AddRange(syntaxLines);

        if (_options.M365 is not null)
        {
            systemLines.Add("Hosted OneDrive and SharePoint Excel references are addressed directly by reference. To search hosted content, pass explicit workbook_references to workbook_grep_search; directory-based grep remains local-workspace search.");
        }

        return new WorkbookToolPromptContribution(
            ReadFileDescriptionLines: locationLines,
            WriteFileDescriptionLines: writeLines,
            EditFileDescriptionLines: editLines,
            GrepSearchDescriptionLines: grepLines,
            SpecificationLookupDescriptionLines: specLines,
            SystemPromptLines: systemLines);
    }

    private List<string> CreateLocationLines()
    {
        var lines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            lines.Add($"Local Excel file paths are supported for {string.Join(", ", ExcelWorkbookHandler.SupportedFileExtensions)}.");
        }
        else
        {
            lines.Add("Local Excel file paths are disabled in this tool set. Use hosted references or another resolver-backed reference instead.");
        }

        if (_options.M365 is not null)
        {
            lines.Add("Hosted Excel workbooks are supported through SharePoint or OneDrive HTTPS URLs, m365://drives/me/root/path/to/file.xlsx for the current user's OneDrive when a drive ID is not given, and m365://drives/{driveId}/root/path/to/file.xlsx or m365://drives/{driveId}/items/{itemId} when the drive ID is known.");
            lines.Add("To create a hosted Excel file, use the drive-path form. Prefer m365://drives/me/root/path/to/file.xlsx when the current user's OneDrive is the target and no drive ID is available.");
        }

        return lines;
    }

    private static List<string> CreateWorkbookDocSyntaxLines() =>
    [
        "Keep normal sheet data compact with anchored rows such as '@B5 | Region | Revenue | Margin'.",
        "Use 'fmt=' for number formats, not 'numfmt='. For borders, use 'border=all:thin:#D9D9D9' or explicit side entries.",
        "Use '[merge B3:H3 align=center/middle]' for merge-and-center style presentation and '[fmt <range> ...]' for repeated adjacent non-default formatting.",
        "Use '[type <range> date|time|datetime]' for repeated temporal data kinds and keep formulas inline as '=SUM(B2:B10)' with optional '[result=1250]'.",
        "Use canonical conditional-format forms such as '[cf I2:I15 when cell > 0.15 fill=#C6EFCE fg=#006100]', '[cf D2:G15 data-bar(color=#5B9BD5)]', and '[cf J2:J15 scale(min:#F8696B,50%:#FFEB84,max:#63BE7B)]'.",
        "For pivots, prefer 'source=<table-name>' or a source range whose first row is the header row named by '- row', '- col', '- val', and '- filter' lines.",
        "When a pivot value needs a custom label, use '- val Population 2025 sum as \"Sum of Population 2025\"'.",
        "Emit literal WorkbookDoc characters, not JSON unicode escapes such as '\\u0022', '\\u0027', '\\u003E', or '\\u003C'.",
        "Use '[chart \"Name\" type=column at=K8 size=576x320px]' blocks and '[spark D4 source=B4:C4 type=line]' for built-in non-grid features.",
    ];
}
