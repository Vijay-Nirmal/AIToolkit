using AIToolkit.Tools.Document;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Supplies Word-specific prompt guidance based on the enabled local and hosted document features.
/// </summary>
internal sealed class WordDocumentPromptProvider(WordDocumentHandlerOptions options) : IDocumentToolPromptProvider
{
    private readonly WordDocumentHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public DocumentToolPromptContribution GetPromptContribution()
    {
        var locationLines = CreateLocationLines();
        var syntaxLines = CreateAsciiDocSyntaxLines();
        var writeLines = new List<string>(locationLines);
        writeLines.AddRange(syntaxLines);

        var editLines = new List<string>(locationLines)
        {
            "Preserve existing Word role lines and valid AsciiDoc structure unless you intentionally want to change them.",
        };
        editLines.AddRange(syntaxLines);

        var grepLines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            grepLines.Add("Local Word files can be searched by their .docx, .docm, .dotx, and .dotm extensions.");
        }

        if (_options.M365 is not null)
        {
            grepLines.Add("Hosted OneDrive and SharePoint Word documents can also be searched by passing explicit document_references to document_grep_search. Directory-based grep still scans only the local workspace.");
        }

        var systemLines = new List<string>(locationLines);
        systemLines.AddRange(syntaxLines);

        if (_options.M365 is not null)
        {
            systemLines.Add("Hosted OneDrive and SharePoint Word references are addressed directly by reference. To search hosted content, pass explicit document_references to document_grep_search; directory-based grep remains local-workspace search.");
        }

        return new DocumentToolPromptContribution(
            ReadFileDescriptionLines: locationLines,
            WriteFileDescriptionLines: writeLines,
            EditFileDescriptionLines: editLines,
            GrepSearchDescriptionLines: grepLines,
            SystemPromptLines: systemLines);
    }

    private List<string> CreateLocationLines()
    {
        var lines = new List<string>();
        if (_options.EnableLocalFileSupport)
        {
            lines.Add($"Local Word file paths are supported for {string.Join(", ", WordDocumentHandler.SupportedFileExtensions)}.");
        }
        else
        {
            lines.Add("Local Word file paths are disabled in this tool set. Use hosted references or another resolver-backed reference instead.");
        }

        if (_options.M365 is not null)
        {
            lines.Add("Hosted Word documents are supported through SharePoint or OneDrive HTTPS URLs, m365://drives/me/root/path/to/file.docx for the current user's OneDrive when a drive ID is not given, and m365://drives/{driveId}/root/path/to/file.docx or m365://drives/{driveId}/items/{itemId} when the drive ID is known.");
            lines.Add("To create a hosted Word file, use the drive-path form. Prefer m365://drives/me/root/path/to/file.docx when the current user's OneDrive is the target and no drive ID is available.");
        }

        return lines;
    }

    private static List<string> CreateAsciiDocSyntaxLines() =>
    [
        "Supported Word style hints include [.text-center], [.text-left], [.text-right], [.text-blue], [.text-green], [.text-yellow], [.text-purple], [.text-orange], [.text-red], and [.text-highlight]. Preserve those lines unless you intentionally want to change styling.",
        "Use AsciiDoc list markers such as * item, ** nested item, and . ordered item. Do not use Markdown-style list markers such as - item.",
        "Put block roles such as [.text-center], [.text-red], or [.text-center.bold] on their own line before the block, and use inline role spans such as [.text-green]#Green text# or [.underline]#underlined text#. The Word provider also understands +underlined text+ and +++bold underlined text+++.",
        "When delimiter characters would terminate role or link syntax, escape them or isolate them with passthrough markup, escape ] inside link labels when needed, and always close role spans with a matching #, for example [.text-blue]#right aligned text.#. Write [.text-red]#Newer C\\# improvements# or [.text-red]#Newer +C#+ improvements# instead of invalid forms like [.text-red]#Newer C# improvements# or [.text-blue]#right aligned text.",
        "Use AsciiDoc links such as link:https://dotnet.microsoft.com[Official .NET site]. For a styled link, wrap the full link in a role span such as [.text-blue]#link:https://github.com/dotnet/runtime[GitHub Repository]#. Do not use Markdown links.",
        "Use |=== to start and end AsciiDoc tables, one leading | per cell, and no trailing | after the last cell value. For table cell alignment or color, style the text inside the cell, for example | [.text-right]#Version# | 7.0.0 or | [.text-center]#Release Version# | Release Highlights. Do not use invalid forms like | :.text-right Version | 7.0.0 or |=.text-center Release Version | Release Highlights, and do not wrap tables in ----.",
        "For a styled label followed by a link, keep them as separate valid segments such as * [.text-purple]#Official .NET Site:# link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]. Do not use malformed forms such as link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#, [.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#, or place role tokens inside the URL target.",
    ];
}