namespace AIToolkit.Tools.Document.Word.Tests;

/// <summary>
/// Provides AsciiDoc fixtures for best-effort Word round-trip verification, including equivalent syntax variants.
/// </summary>
internal static class WordAsciiDocBestEffortRoundTripCases
{
    private static readonly Dictionary<string, string> Cases = new(StringComparer.Ordinal)
    {
        ["title-and-headings"] = """
            = Product Guide

            == Overview

            === Details
            """,
        ["centered-heading"] = """
            = Sample

            [.text-center]
            == Centered Overview
            """,
        ["inline-emphasis-and-code"] = """
            = Sample

            Paragraph with *bold*, _italic_, `code`, and *_combined_* text.
            """,
        ["underline-highlight-and-hard-break"] = """
            = Sample

            Use +underlined+ text and [.text-highlight]#important note# here. +
            Continue on the next line.
            """,
        ["underline-role-span-variant"] = """
            = Sample

            Use [.underline]#underlined# text and [.text-highlight]#important note# here. +
            Continue on the next line.
            """,
        ["role-prefixed-underline-inline-variant"] = """
            = Sample

            * [.text-purple]+Unified platform+ for all application types
            * [.text-red]+Performance improvements+ in runtime and libraries
            """,
        ["markdown-hyphen-list-variant"] = """
            = Sample

            - **Cross-platform** support for Windows, macOS, and Linux.
            - Performance improvements and reduced memory footprint.
            - Unified platform for desktop, web, mobile, cloud, gaming, IoT, and AI.
            """,
        ["escaped-role-span-and-styled-link"] = """
            = Sample

            [.text-red]#Newer C\# improvements# and [.text-blue]#link:https://dotnet.microsoft.com[Official .NET site]#.
            """,
        ["malformed-styled-link-variant"] = """
            = Sample

            * link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#
            """,
        ["markdown-resource-links-variant"] = """
            = Sample

            - [.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#
            - [.text-purple]#GitHub Repository: #link:https://github.com/dotnet/runtime[https://github.com/dotnet/runtime]#
            - [.text-purple]#Microsoft Learn: #link:https://learn.microsoft.com/en-us/dotnet/[https://learn.microsoft.com/en-us/dotnet/]#
            """,
        ["list-variants"] = """
            = Sample

            * unordered item
            ** nested unordered item
            * [x] checked item
            * [ ] unchecked item
            . ordered item
            .. nested ordered item
            <1> callout description
            """,
        ["header-table-with-formatting"] = """
            = Sample

            [cols="1,2",options="header"]
            |===
            | Name | Value
            | [.text-green]#Performance# | [.text-green]#Faster runtime#
            | [.text-right]#Version# | 7.0.0
            |===
            """,
        ["header-table-expanded-layout-variant"] = """
            = Sample

            [cols="1,1,2", options="header"]
            |===
            | Name | Type | Description

            | [.text-green]#Span#
            | [.text-red]#String#
            | [.text-purple]#Represents a sequence of characters.#

            | [.text-green]#List#
            | [.text-orange]#Collection#
            | [.text-blue]#A strongly typed list of objects.#

            | [.text-green]#Dict#
            | [.text-yellow]#Dictionary#
            | [.text-red]#Key-value pairs collection.#
            |===
            """,
        ["implicit-table-without-delimiters-variant"] = """
            = Sample

            [cols="1,2", options="header"]
            | Feature | Description
            | [.text-blue]#Unified platform# | Modern .NET supports building web, desktop, mobile, gaming, and IoT apps with one SDK.
            | [.text-red]#Performance# | Significant runtime improvements reduce memory usage and improve speed.
            | [.text-orange]#Language Enhancements# | New C# 10 features like global using directives, file-scoped namespaces, and improved pattern matching.
            | [.text-green]#Minimal APIs# | Simplified code for building web APIs with less boilerplate.
            | [.text-purple]#Cloud Integration# | Enhanced support for building cloud-native applications and containers.
            """,
        ["malformed-header-cell-role-variant"] = """
            = Sample

            [cols="1,3",options="header"]
            |===
            |=.text-center Release Version | Release Highlights
            | 7.0 | Introduced Native AOT compilation for faster startup and reduced app size.
            | 7.0 | Improved performance in JSON serialization and HTTP pipeline.
            |===
            """,
        ["table-alignment-shorthand-variant"] = """
            = Sample

            [cols="1,3", options="header"]
            |===
            | Attribute | Description
            | :.text-right Version | 7.0.0
            |===
            """,
        ["block-role-attribute-variant"] = """
            = Sample

            [.text-red,role="text-center.bold"]
            Important: Always keep your .NET SDK updated!
            """,
        ["bold-underline-nested-variant"] = """
            = Sample

            * *[.underline]#Bold/Underline#*
            """,
        ["note-admonition"] = """
            = Sample

            [NOTE]
            ====
            Review the generated output before publishing.
            ====
            """,
        ["warning-admonition"] = """
            = Sample

            [WARNING]
            ====
            Preview features may change before release.
            ====
            """,
        ["source-block"] = """
            = Sample

            [source,csharp]
            ----
            Console.WriteLine("hello");
            ----
            """,
        ["bold-underline-shorthand"] = """
            = Sample

            * +++Bold/Underline+++
            """,
        ["unclosed-role-span-at-end-variant"] = """
            = Sample

            This text is [.text-red]#red#, this is [.underline]#underlined#, this is +++bold underlined+++, and this is [.text-blue]#right aligned text.
            """,
        ["literal-block"] = """
            = Sample

            ....
            Literal <value> stays as-is.
            ....
            """,
        ["page-break-and-thematic-break"] = """
            = Sample

            Before break.

            <<<

            After page break.

            '''

            After thematic break.
            """,
    };

    public static string Get(string name) =>
        Cases.TryGetValue(name, out var asciiDoc)
            ? asciiDoc
            : throw new KeyNotFoundException($"Unknown best-effort round-trip case '{name}'.");
}