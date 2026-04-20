namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Supplies Google Docs-specific prompt guidance for hosted Google Docs references.
/// </summary>
/// <remarks>
/// This provider explains the supported <c>gdocs://</c> reference shapes, the local-versus-hosted search split, and the
/// AsciiDoc syntax conventions that the Google Docs bridge preserves through the Word renderer.
/// </remarks>
internal sealed class GoogleDocsDocumentPromptProvider(GoogleDocsDocumentHandlerOptions options) : IDocumentToolPromptProvider
{
    private readonly GoogleDocsDocumentHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Builds the Google Docs-specific prompt contribution.
    /// </summary>
    public DocumentToolPromptContribution GetPromptContribution()
    {
        var locationLines = CreateLocationLines();
        var syntaxLines = CreateAsciiDocSyntaxLines();
        var writeLines = new List<string>(locationLines);
        writeLines.AddRange(syntaxLines);

        var editLines = new List<string>(locationLines)
        {
            "Preserve existing Google Docs role lines and valid AsciiDoc structure unless you intentionally want to change them.",
        };
        editLines.AddRange(syntaxLines);

        var grepLines = new List<string>
        {
            "Hosted Google Docs can also be searched by passing explicit document_references to document_grep_search. Directory-based grep still scans only the local workspace and does not crawl Drive automatically.",
        };

        var systemLines = new List<string>(locationLines);
        systemLines.AddRange(syntaxLines);
        systemLines.Add("Hosted Google Docs references are addressed directly by reference. To search hosted content, pass explicit document_references to document_grep_search; directory-based grep remains local-workspace search and does not crawl Drive automatically.");

        return new DocumentToolPromptContribution(
            ReadFileDescriptionLines: locationLines,
            WriteFileDescriptionLines: writeLines,
            EditFileDescriptionLines: editLines,
            GrepSearchDescriptionLines: grepLines,
            SystemPromptLines: systemLines);
    }

    private static List<string> CreateLocationLines()
    {
        return
        [
            "Hosted Google Docs are supported through Google Docs URLs such as https://docs.google.com/document/d/{documentId}/edit for existing documents.",
            "Stable hosted references are also supported through gdocs://documents/{documentId} for an existing document and gdocs://folders/root/documents/{title} or gdocs://folders/{folderId}/documents/{title} when you want to create or update by folder and title.",
            "To create a hosted Google Doc in the current user's Drive, use the folder-and-title form. Prefer gdocs://folders/root/documents/Release%20Notes when Drive root is the target.",
        ];
    }

    private static List<string> CreateAsciiDocSyntaxLines() =>
    [
        "Supported Google Docs style hints include [.text-center], [.text-left], [.text-right], [.text-blue], [.text-green], [.text-yellow], [.text-purple], [.text-orange], [.text-red], and [.text-highlight]. Preserve those lines unless you intentionally want to change styling.",
        "Use AsciiDoc list markers such as * item, ** nested item, and . ordered item. Do not use Markdown-style list markers such as - item.",
        "Put block roles such as [.text-center], [.text-red], or [.text-center.bold] on their own line before the block, and use inline role spans such as [.text-green]#Green text# or [.underline]#underlined text#. The Google Docs bridge also understands +underlined text+ and +++bold underlined text+++.",
        "When delimiter characters would terminate role or link syntax, escape them or isolate them with passthrough markup, escape ] inside link labels when needed, and always close role spans with a matching #, for example [.text-blue]#right aligned text.#. Write [.text-red]#Newer C\\# improvements# or [.text-red]#Newer +C#+ improvements# instead of invalid forms like [.text-red]#Newer C# improvements# or [.text-blue]#right aligned text.",
        "Use AsciiDoc links such as link:https://dotnet.microsoft.com[Official .NET site]. For a styled link, wrap the full link in a role span such as [.text-blue]#link:https://github.com/dotnet/runtime[GitHub Repository]#. Do not use Markdown links.",
        "Use |=== to start and end AsciiDoc tables, one leading | per cell, and no trailing | after the last cell value. For table cell alignment or color, style the text inside the cell, for example | [.text-right]#Version# | 7.0.0 or | [.text-center]#Release Version# | Release Highlights. Do not use invalid forms like | :.text-right Version | 7.0.0 or |=.text-center Release Version | Release Highlights, and do not wrap tables in ----.",
        "For a styled label followed by a link, keep them as separate valid segments such as * [.text-purple]#Official .NET Site:# link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]. Do not use malformed forms such as link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#, [.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#, or place role tokens inside the URL target.",
    ];
}
