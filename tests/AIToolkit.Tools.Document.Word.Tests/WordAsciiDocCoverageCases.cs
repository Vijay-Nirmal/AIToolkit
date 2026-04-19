namespace AIToolkit.Tools.Document.Word.Tests;

public sealed record AsciiDocCoverageCase(
    string Name,
    string AsciiDoc,
    string SearchNeedle,
    string Replacement);

internal static class WordAsciiDocCoverageCases
{
    internal const string FullSpecOutlineCaseName = "full-spec-outline";

    public static IReadOnlyList<AsciiDocCoverageCase> All { get; } =
    [
        new AsciiDocCoverageCase(
            "foundations-and-attributes",
            """
            = AsciiDoc Foundations
            Ada Lovelace <ada@example.com>
            v1.2, 2026-04-14
            :doctype: book
            :attribute-missing: warn
            :keywords: asciidoc, parser, tooling
            :imagesdir: images

            // comment line
            ifdef::keywords[]
            include::shared/foundations.adoc[]
            endif::keywords[]

            A paragraph with UTF-8 text like Cafe, snowman, and &#169; plus an escaped \*asterisk*.
            Attribute expansion uses {keywords}. Passthrough keeps pass:[<raw>].

            == Reserved Markup

            image::diagram.png[Foundations Diagram]
            xref:next-step[Next step]
            """,
            "AsciiDoc Foundations",
            "AsciiDoc Contract"),
        new AsciiDocCoverageCase(
            "sections-and-blocks",
            """
            = Sections And Blocks

            [[overview]]
            [role=lead]
            .Overview Title
            This paragraph falls back to plain paragraph text when no special block matches.

            == Delimited Example

            ****
            An example block can contain
            multiple lines of text.
            ****

            [quote, Ada Lovelace]
            ____
            That brain of mine is something more than merely mortal.
            ____

            ....
            literal paragraph content
            ....

            --
            open block content
            --
            """,
            "Sections And Blocks",
            "Block Semantics"),
        new AsciiDocCoverageCase(
            "lists-and-descriptions",
            """
            = Lists And Descriptions

            * unordered item
            ** nested unordered item
            * [x] checklist item
            . ordered item
            .. nested ordered item

            term:: definition text
            question:: answer text

            +
            continuation paragraph attached to the previous list item.

            <1> callout description
            """,
            "unordered item",
            "unordered entry"),
        new AsciiDocCoverageCase(
            "tables-and-layout",
            """
            = Tables And Layout

            [cols="1,2",options="header"]
            |===
            | Name | Value
            | Alpha | One
            | Beta | Two
            |===

            [format=csv]
            |===
            | first,second
            | one,two
            |===
            """,
            "Alpha",
            "Gamma"),
        new AsciiDocCoverageCase(
            "block-families-and-macros",
            """
            = Block Families

            NOTE: Admonition paragraph.

            [NOTE]
            ====
            Example admonition block
            ====

            [source,csharp]
            ----
            Console.WriteLine("hello");
            ----

            [verse, Poet]
            ____
            A verse line
            ____

            image::photo.png[caption=Figure,title=Sample]
            audio::song.mp3[]
            video::clip.mp4[]
            toc::[]
            <<<
            '''

            pass:[++++]<foreign markup>++++
            """,
            "Example admonition block",
            "Updated admonition block"),
        new AsciiDocCoverageCase(
            "inline-spans-and-escaping",
            """
            = Inline Spans

            This sentence has *strong* emphasis, _emphasis_, `code`, ^superscript^, ~subscript~, and #mark#.
            Escaped markup stays literal: \_not emphasis_ and \*not strong*.
            Character replacements include -- dashes, ... ellipses, and +++ raw +++ passthrough.
            Attribute usage references {unknown-attribute} and hard break +
            on the next rendered line.
            """,
            "Inline Spans",
            "Inline Rendering"),
        new AsciiDocCoverageCase(
            "inline-macros-and-links",
            """
            = Inline Macros

            icon:flask[] image:logo.png[Logo] btn:[Save] kbd:[Ctrl+S] menu:File[Save As]
            link:https://example.com[Example] xref:guide.adoc#intro[Guide]
            https://learn.microsoft.com[Docs]
            footnote:[A footnote.] indexterm:[term,subterm]
            stem:[x^2 + y^2]
            """,
            "Inline Macros",
            "Inline References"),
        new AsciiDocCoverageCase(
            "references-preprocessor-and-extensions",
            """
            = References And Extensions
            :imagesdir: assets/images

            [[anchor-one]]
            == Linked Section

            include::partial.adoc[]
            ifndef::skip[]
            customblock::target[role=demo]
            custom:inline[target]
            endif::skip[]

            xref:anchor-one[Jump back]
            image::diagram.svg[]
            """,
            "customblock::target",
            "customblock::updated"),
        new AsciiDocCoverageCase(
            "compliance-and-governance",
            """
            = Compliance

            The implementation exports an ASG-ready JSON view, keeps reserved-attribute appendices stable, and documents removed syntax boundaries.

            [appendix]
            == Deprecated Syntax

            Legacy constructs are rejected or gated by policy.
            """,
            "Compliance",
            "Certification")
    ];

    public static IReadOnlyDictionary<string, string[]> BuildChecklistCoverageMap()
    {
        var coverageMap = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var item in FunctionTestUtilities.LoadChecklistItems())
        {
            var additionalCases = new List<string>();
            var lower = item.ToLowerInvariant();
            if (lower.Contains("attribute") || lower.Contains("document header") || lower.Contains("doctype"))
            {
                additionalCases.Add("foundations-and-attributes");
            }

            if (lower.Contains("section") || lower.Contains("block") || lower.Contains("paragraph") || lower.Contains("quote") || lower.Contains("verse") || lower.Contains("listing") || lower.Contains("literal") || lower.Contains("admonition") || lower.Contains("image macro") || lower.Contains("video") || lower.Contains("audio") || lower.Contains("passthrough"))
            {
                additionalCases.Add("sections-and-blocks");
                additionalCases.Add("block-families-and-macros");
            }

            if (lower.Contains("list") || lower.Contains("description lists") || lower.Contains("callout"))
            {
                additionalCases.Add("lists-and-descriptions");
            }

            if (lower.Contains("table") || lower.Contains("colspec") || lower.Contains("cell"))
            {
                additionalCases.Add("tables-and-layout");
            }

            if (lower.Contains("inline") || lower.Contains("marked-text") || lower.Contains("code spans") || lower.Contains("emphasis") || lower.Contains("strong spans") || lower.Contains("subscript") || lower.Contains("superscript") || lower.Contains("button") || lower.Contains("keyboard") || lower.Contains("menu") || lower.Contains("autolinks") || lower.Contains("typographic") || lower.Contains("character references") || lower.Contains("hard line breaks") || lower.Contains("escaping"))
            {
                additionalCases.Add("inline-spans-and-escaping");
                additionalCases.Add("inline-macros-and-links");
            }

            if (lower.Contains("reference") || lower.Contains("xref") || lower.Contains("preprocessor") || lower.Contains("include") || lower.Contains("extension") || lower.Contains("resolver") || lower.Contains("docinfo") || lower.Contains("id-generator"))
            {
                additionalCases.Add("references-preprocessor-and-extensions");
            }

            if (lower.Contains("tck") || lower.Contains("asg") || lower.Contains("appendix") || lower.Contains("deprecated") || lower.Contains("self-certification") || lower.Contains("removed-syntax"))
            {
                additionalCases.Add("compliance-and-governance");
            }

            coverageMap[item] = [FullSpecOutlineCaseName, .. additionalCases.Distinct(StringComparer.Ordinal)];
        }

        return coverageMap;
    }
}