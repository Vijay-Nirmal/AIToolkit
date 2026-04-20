using Azure.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.Document.Word.Tests;

[TestClass]
public class WordDocumentToolsTests
{
    private const string StructuredRenderingAsciiDoc = """
        = Release Notes: Modern .NET
        :icons: font
        :sectnums:
        :sectnumlevels: 3
        :toc: left
        :toclevels: 3
        :toc-title: Contents
        :source-highlighter: rouge

        [.text-center.bold.underline#title]
        == Modern .NET Release Highlights

        [.text-green]
        === Introduction

        Modern .NET is the unified platform for building all types of applications. It combines the power of .NET Core with the broad reach of .NET Framework, Xamarin, and Mono.

        [.text-blue]
        === Key Features

        * Cross-platform support: Windows, Linux, macOS
        * High performance and scalability
        * Unified base libraries and runtime
        * Support for cloud, mobile, desktop, and IoT

        [.text-purple]
        === Detailed Features Table

        [cols="1,2,2,2", options="header"]
        |===
        | Feature | Description | Benefits | Notes

        | Cross-platform
        | Runs on Windows, Linux, macOS
        | Write once run anywhere
        | Supports ARM and x64 architectures

        | High performance
        | Compiled with optimizations for speed and memory
        | Improves responsiveness and scalability
        | Builds optimized with tiered compilation

        | Unified libraries
        | Common API across all platforms
        | Easier code sharing and maintenance
        | Supports .NET Standard 2.1 and beyond

        | Cloud integration
        | Built-in frameworks for cloud-native apps
        | Scalable microservices and serverless
        | Supports Azure, AWS, and Google Cloud

        | Mobile and IoT
        | Xamarin support for mobile devices
        | Single codebase for multiple platforms
        | Expanding device ecosystem

        |===

        [.text-orange]
        === Code Sample

        [source,csharp]
        ----
        public class HelloWorld
        {
            public static void Main()
            {
                System.Console.WriteLine("Hello, .NET!");
            }
        }
        ----

        [.text-orange]
        === More Information

        You can learn more at the official link:https://dotnet.microsoft.com[.NET website].

        [.text-red]
        === Summary

        Modern .NET delivers a powerful, versatile, and efficient platform to build next-generation applications across all devices and cloud environments.

        [.text-highlight]
        === Notes

        [NOTE]
        ====
        .NET 6 introduces significant performance improvements and new language features.
        ====

        [IMPORTANT]
        ====
        Always test your applications thoroughly when upgrading to a new .NET version.
        ====

        [WARNING]
        ====
        Preview features may change and are not recommended for production.
        ====

        [.text-center.bold]
        Thank you for choosing .NET!
        """;

    private static readonly string[] ExpectedToolNames =
    [
        "document_edit_file",
        "document_grep_search",
        "document_read_file",
        "document_write_file",
    ];

    private static readonly string[] ExpectedFirstFeatureRow =
    [
        "Cross-platform",
        "Runs on Windows, Linux, macOS",
        "Write once run anywhere",
        "Supports ARM and x64 architectures",
    ];

    private static readonly string[] ExpectedRecoveredMalformedTableRow =
    [
        "Span",
        "String",
        "Represents a sequence of characters.",
    ];

    private static readonly string[] ExpectedImplicitTableHeaderRow =
    [
        "Feature",
        "Description",
    ];

    private static readonly string[] ExpectedImplicitTableFirstDataRow =
    [
        "Unified platform",
        "Modern .NET supports building web, desktop, mobile, gaming, and IoT apps with one SDK.",
    ];

    private static readonly string[] ExpectedTocEntryTexts =
    [
        "1 First Section",
        "1.1 Nested Topic",
        "2 Final Section",
    ];

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void CreateM365ReferenceResolverRequiresCredential()
    {
        try
        {
            _ = WordDocumentTools.CreateM365ReferenceResolver(new WordDocumentM365Options());
            Assert.Fail("Expected missing M365 credentials to throw an ArgumentException.");
        }
        catch (ArgumentException exception)
        {
            StringAssert.Contains(exception.Message, "Credential");
        }
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesEnabledWordCapabilities()
    {
        var prompt = WordDocumentTools.GetSystemPromptGuidance(
            "Base prompt",
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions());

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "Local Word file paths are supported");
        StringAssert.Contains(prompt, "m365://drives/me/root/path/to/file.docx");
        StringAssert.Contains(prompt, "[.text-center]");
        StringAssert.Contains(prompt, "[.text-center.bold]");
        StringAssert.Contains(prompt, "+underlined text+");
        StringAssert.Contains(prompt, "+++bold underlined text+++");
        StringAssert.Contains(prompt, "[.text-blue]#right aligned text.#");
        StringAssert.Contains(prompt, "link:https://dotnet.microsoft.com[Official .NET site]");
        StringAssert.Contains(prompt, "|=== to start and end AsciiDoc tables");
        StringAssert.Contains(prompt, "| :.text-right Version | 7.0.0");
        StringAssert.Contains(prompt, "[.text-red]#Newer C\\# improvements#");
        StringAssert.Contains(prompt, "link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#");
        StringAssert.Contains(prompt, "document_references to document_grep_search");
    }

    [TestMethod]
    public void CreateFunctionsIncludesWordSpecificAsciiDocSyntaxGuidance()
    {
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions());

        var writeDescription = functions.Single(static function => function.Name == "document_write_file").Description;
        var editDescription = functions.Single(static function => function.Name == "document_edit_file").Description;

        Assert.IsNotNull(writeDescription);
        Assert.IsNotNull(editDescription);
        StringAssert.Contains(writeDescription, "[.text-center]");
        StringAssert.Contains(writeDescription, "[.text-center.bold]");
        StringAssert.Contains(writeDescription, "Do not use Markdown-style list markers such as - item");
        StringAssert.Contains(writeDescription, "+underlined text+");
        StringAssert.Contains(writeDescription, "+++bold underlined text+++");
        StringAssert.Contains(writeDescription, "[.text-red]#Newer C\\# improvements#");
        StringAssert.Contains(writeDescription, "[.text-blue]#right aligned text.#");
        StringAssert.Contains(writeDescription, "[.text-blue]#right aligned text.");
        StringAssert.Contains(writeDescription, "link:https://dotnet.microsoft.com[Official .NET site]");
        StringAssert.Contains(writeDescription, "| [.text-right]#Version# | 7.0.0");
        StringAssert.Contains(writeDescription, "| :.text-right Version | 7.0.0");
        StringAssert.Contains(writeDescription, "|=.text-center Release Version | Release Highlights");
        StringAssert.Contains(writeDescription, "do not wrap tables in ----");
        StringAssert.Contains(writeDescription, "link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#");
        StringAssert.Contains(writeDescription, "[.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#");
        StringAssert.Contains(editDescription, "Preserve existing Word role lines");
        StringAssert.Contains(editDescription, "[.underline]#underlined text#");
        StringAssert.Contains(editDescription, "[.text-purple]#Official .NET Site:# link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]");
        StringAssert.Contains(editDescription, "[.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#");
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsStillSupportsLocalFiles()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });
        var filePath = Path.Combine(workingDirectory, "local-with-m365.docx");
        const string asciiDoc = "= Mixed Support\n\nLocal files still work when hosted M365 references are enabled.";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        Assert.AreEqual(asciiDoc, FunctionTestUtilities.ReadAsciiDocText(contents));
    }

    [TestMethod]
    public async Task CreateFunctionsWithLocalSupportDisabledRejectsLocalWordFiles()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "blocked.docx");
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                EnableLocalFileSupport = false,
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });

        var writeDescription = functions.Single(static function => function.Name == "document_write_file").Description;
        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = "= Blocked\n\nLocal Word support is disabled.",
            }));

        StringAssert.Contains(writeDescription, "Local Word file paths are disabled");
        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message, "Local Word file support is disabled");
        StringAssert.Contains(writeResult.Message, "EnableLocalFileSupport");
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsRejectsUnsupportedOneDriveAlias()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "OneDrive://Documents/IndianHistorySummary.docx",
                content = "= Indian History Summary\n\nDraft content.",
            }));

        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message, "unsupported alias scheme");
        StringAssert.Contains(writeResult.Message, "m365://drives/me/root/Documents/IndianHistorySummary.docx");
        StringAssert.Contains(writeResult.Message, "replace me with the target drive ID");
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsStillHonorsExistingReferenceResolver()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var resolvedPath = Path.Combine(workingDirectory, "docs", "alias.docx");
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
                ReferenceResolver = new AliasDocumentReferenceResolver(resolvedPath),
            });
        const string asciiDoc = "= Alias\n\nResolved through a custom reference resolver.";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:alias",
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.AreEqual(asciiDoc, ReadEmbeddedAsciiDoc(resolvedPath));
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsCanGrepExplicitHostedReferences()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        const string hostedUrl = "https://contoso.sharepoint.com/sites/Docs/Shared%20Documents/release-notes.docx";
        const string resolvedReference = "m365://drives/drive-123/items/item-123";
        const string asciiDoc = "= Release Notes\n\nHosted M365 content";
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                M365 = new WordDocumentM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
                ReferenceResolver = new HostedWordReferenceResolver(hostedUrl, resolvedReference, CreateHostedWordDocumentBytes(asciiDoc)),
            });

        var result = await FunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "Hosted M365 content",
                document_references = new[] { hostedUrl },
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual(resolvedReference, result.Matches[0].Path);
        Assert.AreEqual(3, result.Matches[0].Offset);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = result.Matches[0].Path,
                offset = result.Matches[0].Offset,
                limit = 1,
            }));

        Assert.AreEqual("Hosted M365 content", FunctionTestUtilities.ReadAsciiDocText(readContents));
    }

    [TestMethod]
    [DataRow(".docx")]
    [DataRow(".docm")]
    [DataRow(".dotx")]
    [DataRow(".dotm")]
    public async Task WriteAndReadRoundTripAcrossSupportedExtensions(string extension)
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "roundtrip" + extension);
        const string asciiDoc = "= Roundtrip\n\nA paragraph with *bold* text and link:https://example.com[Example].";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.IsTrue(writeResult.PreservesAsciiDocRoundTrip);
        Assert.AreEqual("word", writeResult.ProviderName);
        Assert.AreEqual(asciiDoc, ReadEmbeddedAsciiDoc(filePath));

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        Assert.AreEqual(asciiDoc, FunctionTestUtilities.ReadAsciiDocText(contents));
    }

    [TestMethod]
    public async Task CanWriteAndReadFullAsciiDocOutline()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "AsciiDoc-langulare-spec.docx");
        var outline = FunctionTestUtilities.NormalizeLineEndings(await File.ReadAllTextAsync(FunctionTestUtilities.GetSpecOutlinePath()));

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = outline,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.AreEqual(outline, ReadEmbeddedAsciiDoc(filePath));

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        Assert.AreEqual(outline, FunctionTestUtilities.ReadAsciiDocText(contents));
    }

    [TestMethod]
    public async Task GeneratedWordDocumentRendersStructuredContentInsteadOfRawAsciiDoc()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "rendered.docx");

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = StructuredRenderingAsciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var mainPart = GetRequiredMainDocumentPart(document);
        var body = GetRequiredBody(mainPart);
        var paragraphs = body.Elements<Paragraph>().ToArray();
        var paragraphTexts = paragraphs.Select(static paragraph => paragraph.InnerText).ToArray();

        Assert.IsFalse(paragraphTexts.Any(static text => text.Contains(":doctype:", StringComparison.Ordinal)));
        Assert.IsFalse(paragraphTexts.Any(static text => text.Contains("[.text-center]", StringComparison.Ordinal)));
        Assert.IsFalse(paragraphTexts.Any(static text => text.Contains("|===", StringComparison.Ordinal)));
        Assert.IsFalse(paragraphTexts.Any(static text => text.Contains("----", StringComparison.Ordinal)));

        StringAssert.Contains(paragraphTexts[0], "Release Notes: Modern .NET");
        Assert.IsTrue(paragraphs.Any(static paragraph => paragraph.InnerText == "Contents"));
        Assert.IsTrue(body.Descendants<Hyperlink>().Any(static hyperlink => !string.IsNullOrWhiteSpace(hyperlink.Anchor?.Value)));
        Assert.IsTrue(body.Descendants<BookmarkStart>().Any(static bookmark => !string.IsNullOrWhiteSpace(bookmark.Name?.Value)));

        var releaseHighlightsParagraph = paragraphs.Single(static paragraph =>
            paragraph.InnerText == "1 Modern .NET Release Highlights"
            && paragraph.Descendants<BookmarkStart>().Any());
        Assert.AreEqual(JustificationValues.Center, releaseHighlightsParagraph.ParagraphProperties?.Justification?.Val?.Value);
        Assert.IsTrue(releaseHighlightsParagraph.Descendants<Underline>().Any());

        var keyFeaturesParagraph = paragraphs.Single(static paragraph =>
            paragraph.InnerText == "1.2 Key Features"
            && paragraph.Descendants<BookmarkStart>().Any());
        Assert.AreEqual("1F4E79", keyFeaturesParagraph.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));

        var closingParagraph = paragraphs.Single(static paragraph => paragraph.InnerText == "Thank you for choosing .NET!");
        Assert.AreEqual(JustificationValues.Center, closingParagraph.ParagraphProperties?.Justification?.Val?.Value);
        Assert.IsTrue(closingParagraph.Descendants<Bold>().Any());

        Assert.IsTrue(body.InnerText.Contains("Console.WriteLine", StringComparison.Ordinal));
        Assert.IsTrue(body.Descendants<RunFonts>().Any(static fonts => string.Equals(fonts.Ascii?.Value, "Consolas", StringComparison.OrdinalIgnoreCase)));

        var contentTable = body.Elements<Table>().First(static table => table.Elements<TableRow>().First().InnerText.Contains("Feature", StringComparison.Ordinal));
        Assert.AreEqual(6, contentTable.Elements<TableRow>().Count());
        Assert.IsTrue(contentTable.Elements<TableRow>().All(static row => row.Elements<TableCell>().Count() == 4));
        CollectionAssert.AreEqual(
            ExpectedFirstFeatureRow,
            contentTable.Elements<TableRow>().Skip(1).First().Elements<TableCell>().Select(static cell => cell.InnerText).ToArray());
        Assert.AreEqual(1, mainPart.HyperlinkRelationships.Count());

        var documentText = body.InnerText;
        StringAssert.Contains(documentText, "NOTE");
        StringAssert.Contains(documentText, "IMPORTANT");
        StringAssert.Contains(documentText, "WARNING");
    }

    [TestMethod]
    [DataRow("text-blue", "1F4E79")]
    [DataRow("text-green", "2E8B57")]
    [DataRow("text-yellow", "BF9000")]
    [DataRow("text-purple", "7030A0")]
    [DataRow("text-orange", "C55A11")]
    [DataRow("text-red", "C00000")]
    public async Task SupportedColorHintsRenderExpectedWordColors(string role, string expectedColor)
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, role + ".docx");
        var asciiDoc = $"= Sample\n\n[.{role}]\nColor sample";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Color sample");
        Assert.AreEqual(expectedColor, paragraph.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [TestMethod]
    [DataRow("text-left", "Left")]
    [DataRow("text-center", "Center")]
    [DataRow("text-right", "Right")]
    public async Task SupportedAlignmentHintsRenderExpectedJustification(string role, string expectedJustification)
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, role + ".docx");
        var asciiDoc = $"= Sample\n\n[.{role}]\nAlignment sample";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Alignment sample");
        var expectedValue = expectedJustification switch
        {
            "Left" => JustificationValues.Left,
            "Center" => JustificationValues.Center,
            "Right" => JustificationValues.Right,
            _ => throw new AssertInconclusiveException($"Unsupported justification expectation '{expectedJustification}'."),
        };

        Assert.AreEqual(expectedValue, paragraph.ParagraphProperties?.Justification?.Val?.Value);
    }

    [TestMethod]
    public async Task CombinedBlockRolesRenderBoldCenteredAndUnderlinedText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "combined-roles.docx");
        const string asciiDoc = "= Sample\n\n[.text-center.bold.underline]\nThank you for choosing .NET!";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Thank you for choosing .NET!");
        Assert.AreEqual(JustificationValues.Center, paragraph.ParagraphProperties?.Justification?.Val?.Value);
        Assert.IsTrue(paragraph.Descendants<Bold>().Any());
        Assert.IsTrue(paragraph.Descendants<Underline>().Any());
    }

    [TestMethod]
    public async Task UnderlineShorthandRendersUnderlinedText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "underline-shortcut.docx");
        const string asciiDoc = "= Sample\n\nUse +underlined text+ here.";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var run = GetRequiredBody(document).Descendants<Run>().Single(static value => value.InnerText == "underlined text");
        Assert.IsTrue(run.RunProperties?.Underline is not null);
    }

    [TestMethod]
    public async Task RolePrefixedUnderlineSyntaxRendersColoredUnderlinedListTextWithoutRawMarkers()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "role-prefixed-inline.docx");
        const string asciiDoc = "= Sample\n\n[.text-green]#Highlights:#\n\n* [.text-purple]+Unified platform+ for all application types";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        Assert.IsFalse(body.InnerText.Contains("[.text-purple]", StringComparison.Ordinal));
        Assert.IsFalse(body.InnerText.Contains("+Unified platform+", StringComparison.Ordinal));

        var run = body.Descendants<Run>().Single(static value => value.InnerText == "Unified platform");
        Assert.IsTrue(run.RunProperties?.Underline is not null);
        Assert.AreEqual("7030A0", run.RunProperties?.Color?.Val?.Value);
    }

    [TestMethod]
    public async Task PostProcessorCanApplyFinalBrandingToGeneratedWordDocuments()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = WordDocumentTools.CreateFunctions(
            new WordDocumentHandlerOptions
            {
                PostProcessor = new BrandingPostProcessor("Contoso Confidential"),
            },
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
            });
        var filePath = Path.Combine(workingDirectory, "branded.docx");

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = "= Sample\n\nVisible body text.",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraphs = GetRequiredBody(document).Elements<Paragraph>()
            .Select(static paragraph => paragraph.InnerText)
            .ToArray();

        CollectionAssert.Contains(paragraphs, "Contoso Confidential");
    }

    [TestMethod]
    public async Task MarkdownHyphenBulletsRenderAsStructuredWordList()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "markdown-bullets.docx");
        const string asciiDoc = "= Sample\n\n- **Cross-platform** support for Windows, macOS, and Linux.\n- Performance improvements and reduced memory footprint.";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        var bulletParagraphs = body.Elements<Paragraph>()
            .Where(static paragraph => paragraph.InnerText.StartsWith("• ", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(2, bulletParagraphs.Length);
        Assert.IsFalse(body.InnerText.Contains("- **Cross-platform**", StringComparison.Ordinal));
        Assert.IsTrue(bulletParagraphs[0].Descendants<Bold>().Any());
    }

    [TestMethod]
    public async Task MarkdownResourceLinksRenderAsListWithoutRawRoleTokensOrDanglingHashes()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "markdown-resource-links.docx");
        const string asciiDoc = "= Sample\n\n- [.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#\n- [.text-purple]#GitHub Repository: #link:https://github.com/dotnet/runtime[https://github.com/dotnet/runtime]#\n- [.text-purple]#Microsoft Learn: #link:https://learn.microsoft.com/en-us/dotnet/[https://learn.microsoft.com/en-us/dotnet/]#";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var mainPart = GetRequiredMainDocumentPart(document);
        var body = GetRequiredBody(mainPart);
        var bulletParagraphs = body.Elements<Paragraph>()
            .Where(static paragraph => paragraph.InnerText.StartsWith("• ", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(3, bulletParagraphs.Length);
        Assert.AreEqual(3, mainPart.HyperlinkRelationships.Count());
        Assert.IsFalse(body.InnerText.Contains("[.text-purple]", StringComparison.Ordinal));
        Assert.IsFalse(bulletParagraphs.Any(static paragraph => paragraph.InnerText.EndsWith('#')));
    }

    [TestMethod]
    public async Task BestEffortReadNormalizesMarkdownResourceLinksIntoCanonicalBulletLinks()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var writerFunctions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var readerFunctions = FunctionTestUtilities.CreateFunctions(
            workingDirectory,
            new WordDocumentHandlerOptions
            {
                PreferEmbeddedAsciiDoc = false,
            });
        var filePath = Path.Combine(workingDirectory, "markdown-resource-links-import.docx");
        var asciiDoc = FunctionTestUtilities.NormalizeLineEndings(WordAsciiDocBestEffortRoundTripCases.Get("markdown-resource-links-variant"));

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            writerFunctions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            readerFunctions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        var importedAsciiDoc = FunctionTestUtilities.ReadAsciiDocText(contents);
        StringAssert.Contains(importedAsciiDoc, "* [.text-purple]#Official .NET Site:# link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]");
        StringAssert.Contains(importedAsciiDoc, "* [.text-purple]#GitHub Repository:# link:https://github.com/dotnet/runtime[https://github.com/dotnet/runtime]");
        StringAssert.Contains(importedAsciiDoc, "* [.text-purple]#Microsoft Learn:# link:https://learn.microsoft.com/en-us/dotnet/[https://learn.microsoft.com/en-us/dotnet/]");
    }

    [TestMethod]
    public async Task EscapedHashInsideRoleSpanRendersLiteralHashWithoutBackslashArtifacts()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "escaped-hash.docx");
        const string asciiDoc = "= Sample\n\n[.text-red]#Newer C\\# improvements#\n\n[.text-red]#Newer +C#+ improvements#";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var bodyText = GetRequiredBody(document).InnerText;
        StringAssert.Contains(bodyText, "Newer C# improvements");
        Assert.IsFalse(bodyText.Contains("C\\#", StringComparison.Ordinal));
        Assert.AreEqual(2, GetRequiredBody(document).Elements<Paragraph>().Count(static paragraph => paragraph.InnerText == "Newer C# improvements"));
    }

    [TestMethod]
    public async Task TriplePlusShorthandRendersBoldAndUnderlinedText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "triple-plus.docx");
        const string asciiDoc = "= Sample\n\n* +++Bold/Underline+++";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var run = GetRequiredBody(document).Descendants<Run>().Single(static value => value.InnerText == "Bold/Underline");
        Assert.IsTrue(run.RunProperties?.Bold is not null);
        Assert.IsTrue(run.RunProperties?.Underline is not null);
        Assert.IsFalse(GetRequiredBody(document).InnerText.Contains("+++", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RoleNamedAttributeAppliesAdditionalFormattingRoles()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "role-attribute.docx");
        const string asciiDoc = "= Sample\n\n[.text-red,role=\"text-center.bold\"]\nImportant: Always keep your .NET SDK updated!";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Important: Always keep your .NET SDK updated!");
        Assert.AreEqual(JustificationValues.Center, paragraph.ParagraphProperties?.Justification?.Val?.Value);
        Assert.IsTrue(paragraph.Descendants<Bold>().Any());
        Assert.AreEqual("C00000", paragraph.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [TestMethod]
    public async Task MalformedStyledLinkSyntaxIsRecoveredIntoAColoredHyperlink()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "malformed-link.docx");
        const string asciiDoc = "= Sample\n\n* link:https://github.com/dotnet/runtime[.text-blue]#GitHub Repository#";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var mainPart = GetRequiredMainDocumentPart(document);
        var hyperlink = GetRequiredBody(mainPart).Descendants<Hyperlink>().Single(static value => value.InnerText == "GitHub Repository");
        Assert.AreEqual(1, mainPart.HyperlinkRelationships.Count(static relationship => relationship.Uri.AbsoluteUri == "https://github.com/dotnet/runtime"));
        Assert.AreEqual("1F4E79", hyperlink.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [TestMethod]
    public async Task MalformedTableRoleCellsAreRecoveredWithoutRawAsciiDocMarkers()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "malformed-table.docx");
        const string asciiDoc = """
            = Sample

            [cols="1,1,2", options="header"]
            |===
            | Name | Type | Description

            | [.text-green]#Span|
            | [.text-red]#String|
            | [.text-purple]#Represents a sequence of characters.
            |===
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        Assert.IsFalse(body.InnerText.Contains("[.text-green]", StringComparison.Ordinal));
        Assert.IsFalse(body.InnerText.Contains("#Span", StringComparison.Ordinal));

        var table = body.Elements<Table>().Single();
        CollectionAssert.AreEqual(
            ExpectedRecoveredMalformedTableRow,
            table.Elements<TableRow>().Skip(1).Single().Elements<TableCell>().Select(static cell => cell.InnerText).ToArray());
    }

    [TestMethod]
    public async Task MalformedTableAlignmentShorthandIsRecoveredIntoRightAlignedCellText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "table-alignment.docx");
        const string asciiDoc = """
            = Sample

            [cols="1,3", options="header"]
            |===
            | Attribute | Description
            | :.text-right Version | 7.0.0
            |===
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var table = GetRequiredBody(document).Elements<Table>().Single();
        var rightAlignedCellParagraph = table.Elements<TableRow>().Skip(1).Single().Elements<TableCell>().First().Elements<Paragraph>().Single();
        Assert.AreEqual("Version", rightAlignedCellParagraph.InnerText);
        Assert.AreEqual(JustificationValues.Right, rightAlignedCellParagraph.ParagraphProperties?.Justification?.Val?.Value);
    }

    [TestMethod]
    public async Task MalformedHeaderCellRoleSyntaxIsRecoveredIntoCenteredHeaderCellText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "header-role-cell.docx");
        const string asciiDoc = """
            = Sample

            [cols="1,3",options="header"]
            |===
            |=.text-center Release Version | Release Highlights
            | 7.0 | Introduced Native AOT compilation for faster startup and reduced app size.
            | 7.0 | Improved performance in JSON serialization and HTTP pipeline.
            |===
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        Assert.IsFalse(body.InnerText.Contains("=.text-center", StringComparison.Ordinal));

        var firstHeaderCellParagraph = body.Elements<Table>().Single().Elements<TableRow>().First().Elements<TableCell>().First().Elements<Paragraph>().Single();
        Assert.AreEqual("Release Version", firstHeaderCellParagraph.InnerText);
        Assert.AreEqual(JustificationValues.Center, firstHeaderCellParagraph.ParagraphProperties?.Justification?.Val?.Value);
    }

    [TestMethod]
    public async Task TableRowsWithoutExplicitDelimitersRenderAsStructuredWordTable()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "implicit-table.docx");
        const string asciiDoc = """
            = Sample

            [cols="1,2", options="header"]
            | Feature | Description
            | [.text-blue]#Unified platform# | Modern .NET supports building web, desktop, mobile, gaming, and IoT apps with one SDK.
            | [.text-red]#Performance# | Significant runtime improvements reduce memory usage and improve speed.
            | [.text-orange]#Language Enhancements# | New C# 10 features like global using directives, file-scoped namespaces, and improved pattern matching.
            | [.text-green]#Minimal APIs# | Simplified code for building web APIs with less boilerplate.
            | [.text-purple]#Cloud Integration# | Enhanced support for building cloud-native applications and containers.
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        Assert.IsFalse(body.InnerText.Contains("| Feature | Description", StringComparison.Ordinal));

        var table = body.Elements<Table>().Single();
        Assert.AreEqual(6, table.Elements<TableRow>().Count());
        CollectionAssert.AreEqual(
            ExpectedImplicitTableHeaderRow,
            table.Elements<TableRow>().First().Elements<TableCell>().Select(static cell => cell.InnerText).ToArray());
        CollectionAssert.AreEqual(
            ExpectedImplicitTableFirstDataRow,
            table.Elements<TableRow>().Skip(1).First().Elements<TableCell>().Select(static cell => cell.InnerText).ToArray());
        Assert.AreEqual(
            "1F4E79",
            table.Elements<TableRow>().Skip(1).First().Elements<TableCell>().First().Descendants<RunProperties>()
                .Select(static properties => properties.Color?.Val?.Value)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [TestMethod]
    public async Task UnclosedRoleSpanAtEndOfParagraphRendersStyledTextWithoutRawSyntax()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "unclosed-role-span.docx");
        const string asciiDoc = "= Sample\n\nThis text is [.text-red]#red#, this is [.underline]#underlined#, this is +++bold underlined+++, and this is [.text-blue]#right aligned text.";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        Assert.IsFalse(body.InnerText.Contains("[.text-blue]", StringComparison.Ordinal));
        var run = body.Descendants<Run>().Single(static value => value.InnerText == "right aligned text.");
        Assert.AreEqual("1F4E79", run.RunProperties?.Color?.Val?.Value);
    }

    [TestMethod]
    public async Task TocCreatesHyperlinkedEntriesThatPointToHeadingBookmarks()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "toc-links.docx");
        const string asciiDoc = """
            = Sample
            :toc:
            :sectnums:
            :toclevels: 3

            == First Section

            === Nested Topic

            == Final Section
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = GetRequiredBody(document);
        var internalHyperlinks = body.Descendants<Hyperlink>()
            .Where(static hyperlink => !string.IsNullOrWhiteSpace(hyperlink.Anchor?.Value))
            .ToArray();
        var bookmarks = body.Descendants<BookmarkStart>()
            .Where(static bookmark => !string.IsNullOrWhiteSpace(bookmark.Name?.Value))
            .Select(static bookmark => bookmark.Name!.Value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.AreEqual(3, internalHyperlinks.Length);
        CollectionAssert.AreEquivalent(
            ExpectedTocEntryTexts,
            internalHyperlinks.Select(static hyperlink => hyperlink.InnerText).ToArray());
        Assert.IsTrue(internalHyperlinks.All(hyperlink => bookmarks.Contains(hyperlink.Anchor!.Value!)));
    }

    [TestMethod]
    public async Task TocDocumentsDoNotContainFieldUpdateSettingsOrFieldCodes()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "toc-no-fields.docx");
        const string asciiDoc = "= Sample\n:toc:\n\n== First\n\n== Second";

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var document = WordprocessingDocument.Open(filePath, false);
        Assert.IsFalse(GetRequiredBody(document).Descendants<FieldCode>().Any());
        Assert.IsFalse(GetRequiredMainDocumentPart(document).DocumentSettingsPart?.Settings?.Descendants<UpdateFieldsOnOpen>().Any() == true);
    }

    [TestMethod]
    public async Task BestEffortReadImportsNewInlineAndTableSyntaxFromGeneratedWordBody()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var writerFunctions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var readerFunctions = FunctionTestUtilities.CreateFunctions(
            workingDirectory,
            new WordDocumentHandlerOptions
            {
                PreferEmbeddedAsciiDoc = false,
            });
        var filePath = Path.Combine(workingDirectory, "import-visible-body.docx");
        const string asciiDoc = """
            = Sample

            [.text-center.bold]
            == Overview

            [.text-red]#Newer C\# improvements#

            [.underline]#Modern C\# Features:#

            * +++Bold/Underline+++

            For more information, visit the official [.text-blue]#link:https://dotnet.microsoft.com[.NET official website]#.

            [cols="1,2",options="header"]
            |===
            | Aspect | Benefit
            | [.text-green]#Performance# | [.text-green]#Faster runtime and optimizations#
            | [.text-right]#Version# | 7.0.0
            |===
            """;

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            writerFunctions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            readerFunctions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        var importedAsciiDoc = FunctionTestUtilities.ReadAsciiDocText(contents);
        StringAssert.Contains(importedAsciiDoc, "[.text-center]");
        StringAssert.Contains(importedAsciiDoc, "Overview");
        StringAssert.Contains(importedAsciiDoc, "[.text-red]#Newer C\\# improvements#");
        StringAssert.Contains(importedAsciiDoc, "+Modern C# Features:+");
        StringAssert.Contains(importedAsciiDoc, "* +++Bold/Underline+++");
        StringAssert.Contains(importedAsciiDoc, "[.text-blue]#link:https://dotnet.microsoft.com[.NET official website]#");
        StringAssert.Contains(importedAsciiDoc, "| [.text-green]#Performance# | [.text-green]#Faster runtime and optimizations#");
        StringAssert.Contains(importedAsciiDoc, "| [.text-right]#Version# | 7.0.0");
    }

    [TestMethod]
    public async Task FocusedCoverageCasesRoundTripWithDocumentTools()
    {
        foreach (var testCase in WordAsciiDocCoverageCases.All)
        {
            var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
            var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
            var filePath = Path.Combine(workingDirectory, testCase.Name + ".docx");
            var normalized = FunctionTestUtilities.NormalizeLineEndings(testCase.AsciiDoc);

            var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
                functions,
                "document_write_file",
                FunctionTestUtilities.CreateArguments(new
                {
                    document_reference = filePath,
                    content = normalized,
                }));

            Assert.IsTrue(writeResult.Success, $"{testCase.Name}: {writeResult.Message}");

            var contents = await FunctionTestUtilities.InvokeContentAsync(
                functions,
                "document_read_file",
                FunctionTestUtilities.CreateArguments(new
                {
                    document_reference = filePath,
                }));

            Assert.AreEqual(normalized, FunctionTestUtilities.ReadAsciiDocText(contents), testCase.Name);
        }
    }

    [TestMethod]
    public async Task EditFileUpdatesCanonicalAsciiDocAndEmbeddedPayload()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "edit.docx");
        const string original = "= Editable Sample\n\nThis paragraph is the editable anchor.";

        _ = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = original,
            }));

        _ = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        var editResult = await FunctionTestUtilities.InvokeAsync<DocumentEditFileToolResult>(
            functions,
            "document_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                old_string = "editable anchor",
                new_string = "updated anchor",
            }));

        Assert.IsTrue(editResult.Success, editResult.Message);
        StringAssert.Contains(editResult.UpdatedAsciiDoc!, "updated anchor");
        Assert.IsFalse(editResult.UpdatedAsciiDoc!.Contains("editable anchor", StringComparison.Ordinal));
        StringAssert.Contains(ReadEmbeddedAsciiDoc(filePath), "updated anchor");
    }

    [TestMethod]
    public async Task ReadImportsExternalWordDocumentWithoutEmbeddedPayload()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "external.docx");
        CreateExternalWordDocument(filePath);
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(textParts.Any(static text => text.Contains("Best-effort AsciiDoc import", StringComparison.Ordinal)));

        var importedAsciiDoc = FunctionTestUtilities.ReadAsciiDocText(contents);
        StringAssert.Contains(importedAsciiDoc, "= Imported Title");
        StringAssert.Contains(importedAsciiDoc, "Imported paragraph");
        StringAssert.Contains(importedAsciiDoc, "|===");
        StringAssert.Contains(importedAsciiDoc, "| Key | Value");
    }

    [TestMethod]
    public async Task GrepSearchFindsTextInsideWordDocuments()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);

        _ = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = Path.Combine(workingDirectory, "docs", "guide.docx"),
                content = "= Guide\n\nContent",
            }));
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "docs", "notes.txt"), "Guide");

        var result = await FunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "Guide",
                includePattern = "**/*.docx",
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual("docs/guide.docx", result.Matches[0].Path);
        Assert.AreEqual(1, result.Matches[0].Offset);
        CollectionAssert.Contains(result.SupportedExtensions, ".docx");
    }

    private sealed class BrandingPostProcessor(string brandingText) : IWordDocumentPostProcessor
    {
        public ValueTask ProcessAsync(WordDocumentPostProcessorContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var renderedDocument = GetRequiredDocument(context.MainDocumentPart);
            var body = renderedDocument.Body;
            if (body is null)
            {
                body = renderedDocument.AppendChild(new Body());
            }

            body.AppendChild(new Paragraph(new Run(new Text(brandingText))));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AliasDocumentReferenceResolver(string resolvedPath) : IDocumentReferenceResolver
    {
        public ValueTask<DocumentReferenceResolution?> ResolveAsync(
            string documentReference,
            DocumentReferenceResolverContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DocumentReferenceResolution?>(
                string.Equals(documentReference, "doc:alias", StringComparison.Ordinal)
                    ? DocumentReferenceResolution.CreateFile(resolvedPath)
                    : null);
    }

    private sealed class HostedWordReferenceResolver(string originalReference, string resolvedReference, byte[] content) : IDocumentReferenceResolver
    {
        public ValueTask<DocumentReferenceResolution?> ResolveAsync(
            string documentReference,
            DocumentReferenceResolverContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DocumentReferenceResolution?>(
                string.Equals(documentReference, originalReference, StringComparison.Ordinal)
                || string.Equals(documentReference, resolvedReference, StringComparison.Ordinal)
                    ? DocumentReferenceResolution.CreateStreamBacked(
                        resolvedReference: resolvedReference,
                        extension: ".docx",
                        existsAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(true);
                        },
                        openReadAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
                        },
                        openWriteAsync: _ => throw new NotSupportedException("Hosted grep tests do not support writes through the fake resolver."),
                        version: "1",
                        length: content.LongLength,
                        readStateKey: resolvedReference)
                    : null);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    [TestMethod]
    public void EveryChecklistItemHasAtLeastOneMappedTestCase()
    {
        var coverageMap = WordAsciiDocCoverageCases.BuildChecklistCoverageMap();
        var checklistItems = FunctionTestUtilities.LoadChecklistItems();
        var knownCaseNames = new HashSet<string>(
            [
                WordAsciiDocCoverageCases.FullSpecOutlineCaseName,
                .. WordAsciiDocCoverageCases.All.Select(static testCase => testCase.Name),
            ],
            StringComparer.Ordinal);

        foreach (var checklistItem in checklistItems)
        {
            Assert.IsTrue(coverageMap.TryGetValue(checklistItem, out var mappedCases), $"Missing coverage mapping for checklist item: {checklistItem}");
            Assert.IsTrue(mappedCases.Length > 0, $"Checklist item '{checklistItem}' has no mapped test cases.");
            foreach (var mappedCase in mappedCases)
            {
                Assert.IsTrue(knownCaseNames.Contains(mappedCase), $"Checklist item '{checklistItem}' references unknown test case '{mappedCase}'.");
            }
        }
    }

    private static string ReadEmbeddedAsciiDoc(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return WordAsciiDocPayload.TryRead(document.MainDocumentPart)
            ?? throw new InvalidOperationException("Expected embedded canonical AsciiDoc payload.");
    }

    private static byte[] CreateHostedWordDocumentBytes(string asciiDoc)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            WordAsciiDocRenderer.Write(mainPart, asciiDoc);
            WordAsciiDocPayload.Write(mainPart, asciiDoc);
            SaveRequiredDocument(mainPart);
        }

        return stream.ToArray();
    }

    private static void CreateExternalWordDocument(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
            new Body(
                new Paragraph(
                    new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                    new Run(new Text("Imported Title"))),
                new Paragraph(new Run(new Text("Imported paragraph with ")), new Run(new RunProperties(new Bold()), new Text("bold"))),
                new Table(
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Key")))),
                        new TableCell(new Paragraph(new Run(new Text("Value"))))),
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Alpha")))),
                        new TableCell(new Paragraph(new Run(new Text("One"))))))));
        SaveRequiredDocument(mainPart);
    }

    private static MainDocumentPart GetRequiredMainDocumentPart(WordprocessingDocument document) =>
        document.MainDocumentPart ?? throw new InvalidOperationException("Expected the Word package to contain a main document part.");

    private static DocumentFormat.OpenXml.Wordprocessing.Document GetRequiredDocument(MainDocumentPart mainPart) =>
        mainPart.Document ?? throw new InvalidOperationException("Expected the main document part to contain a Word document.");

    private static Body GetRequiredBody(WordprocessingDocument document) =>
        GetRequiredBody(GetRequiredMainDocumentPart(document));

    private static Body GetRequiredBody(MainDocumentPart mainPart) =>
        GetRequiredDocument(mainPart).Body ?? throw new InvalidOperationException("Expected the Word document to contain a body.");

    private static void SaveRequiredDocument(MainDocumentPart mainPart)
    {
        GetRequiredDocument(mainPart).Save();
    }
}
