using AIToolkit.Tools.Document.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Google.Apis.Http;
using Microsoft.Extensions.AI;
using WordCoverage = AIToolkit.Tools.Document.Word.Tests;

namespace AIToolkit.Tools.Document.GoogleDocs.Tests;

[TestClass]
public class GoogleDocsDocumentToolsTests
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
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void CreateReferenceResolverRequiresAuthConfiguration()
    {
        try
        {
            _ = GoogleDocsDocumentTools.CreateReferenceResolver(new GoogleDocsWorkspaceOptions());
            Assert.Fail("Expected missing Google Docs auth configuration to throw an ArgumentException.");
        }
        catch (ArgumentException exception)
        {
            StringAssert.Contains(exception.Message, "Credential");
            StringAssert.Contains(exception.Message, "HttpClientInitializer");
            StringAssert.Contains(exception.Message, "ApiKey");
        }
    }

    [TestMethod]
    public void CreateReferenceResolverAllowsHttpClientInitializerConfiguration()
    {
        var resolver = GoogleDocsDocumentTools.CreateReferenceResolver(
            new GoogleDocsWorkspaceOptions
            {
                HttpClientInitializer = new FakeHttpClientInitializer(),
            });

        Assert.IsNotNull(resolver);
    }

    [TestMethod]
    public void CreateReferenceResolverAllowsApiKeyConfiguration()
    {
        var resolver = GoogleDocsDocumentTools.CreateReferenceResolver(
            new GoogleDocsWorkspaceOptions
            {
                ApiKey = "sample-api-key",
            });

        Assert.IsNotNull(resolver);
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesEnabledGoogleDocsCapabilities()
    {
        var prompt = GoogleDocsDocumentTools.GetSystemPromptGuidance(
            "Base prompt",
            new GoogleDocsDocumentHandlerOptions
            {
                Workspace = new GoogleDocsWorkspaceOptions
                {
                    Client = new FakeGoogleDocsWorkspaceClient(),
                },
            },
            new DocumentToolsOptions());

        StringAssert.Contains(prompt, "Base prompt");
        Assert.IsFalse(prompt.Contains(".gdoc", StringComparison.Ordinal));
        StringAssert.Contains(prompt, "gdocs://documents/{documentId}");
        StringAssert.Contains(prompt, "gdocs://folders/root/documents/{title}");
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
        StringAssert.Contains(prompt, "does not crawl Drive automatically");
    }

    [TestMethod]
    public void CreateFunctionsIncludesGoogleDocsSpecificAsciiDocSyntaxGuidance()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);

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
        StringAssert.Contains(editDescription, "Preserve existing Google Docs role lines");
        StringAssert.Contains(editDescription, "[.underline]#underlined text#");
        StringAssert.Contains(editDescription, "[.text-purple]#Official .NET Site:# link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]");
        StringAssert.Contains(editDescription, "[.text-purple]#Official .NET Site: #link:https://dotnet.microsoft.com[https://dotnet.microsoft.com]#");
    }

    [TestMethod]
    public async Task CreateFunctionsWithWorkspaceOptionsRejectsUnsupportedAlias()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = "GoogleDrive://Documents/IndianHistorySummary",
                content = "= Indian History Summary\n\nDraft content.",
            }));

        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message!, "unsupported alias scheme");
        StringAssert.Contains(writeResult.Message!, "gdocs://folders/root/documents/Release%20Notes");
    }

    [TestMethod]
    [DataRow("document")]
    [DataRow("url")]
    public async Task WriteAndReadRoundTripAcrossSupportedReferenceForms(string readForm)
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleDocsSupport.CreateFolderReference("root", "roundtrip");
        const string asciiDoc = "= Roundtrip\n\nA paragraph with *bold* text and link:https://example.com[Example].";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = createReference,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.IsTrue(writeResult.PreservesAsciiDocRoundTrip);
        Assert.AreEqual("google-docs", writeResult.ProviderName);
        Assert.AreEqual(asciiDoc, workspace.GetManagedAsciiDoc(writeResult.Path));

        var readReference = readForm switch
        {
            "document" => writeResult.Path,
            "url" => FakeGoogleDocsWorkspaceClient.CreateDocumentUrl(ExtractDocumentId(writeResult.Path)),
            _ => throw new AssertInconclusiveException($"Unsupported read form '{readForm}'."),
        };

        var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = readReference,
            }));

        Assert.AreEqual(asciiDoc, GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents));
    }

    [TestMethod]
    public async Task CanWriteAndReadFullAsciiDocOutline()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleDocsSupport.CreateFolderReference("root", "AsciiDoc-langulare-spec");
        var outline = GoogleDocsFunctionTestUtilities.NormalizeLineEndings(await File.ReadAllTextAsync(GoogleDocsFunctionTestUtilities.GetSpecOutlinePath()).ConfigureAwait(false));

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = createReference,
                content = outline,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.AreEqual(outline, workspace.GetManagedAsciiDoc(writeResult.Path));

        var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = writeResult.Path,
            }));

        Assert.AreEqual(outline, GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents));
    }

    [TestMethod]
    public async Task GeneratedGoogleDocsBridgeRendersStructuredContentInsteadOfRawAsciiDoc()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleDocsSupport.CreateFolderReference("root", "rendered");

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = createReference,
                content = StructuredRenderingAsciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
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
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var asciiDoc = $"= Sample\n\n[.{role}]\nColor sample";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", role),
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Color sample");
        Assert.AreEqual(expectedColor, paragraph.Descendants<RunProperties>().Select(static properties => properties.Color?.Val?.Value).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)));
    }

    [TestMethod]
    [DataRow("text-left", "Left")]
    [DataRow("text-center", "Center")]
    [DataRow("text-right", "Right")]
    public async Task SupportedAlignmentHintsRenderExpectedJustification(string role, string expectedJustification)
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var asciiDoc = $"= Sample\n\n[.{role}]\nAlignment sample";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", role + "-alignment"),
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
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
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        const string asciiDoc = "= Sample\n\n[.text-center.bold.underline]\nThank you for choosing .NET!";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "combined-roles"),
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var paragraph = GetRequiredBody(document).Elements<Paragraph>().Single(static value => value.InnerText == "Thank you for choosing .NET!");
        Assert.AreEqual(JustificationValues.Center, paragraph.ParagraphProperties?.Justification?.Val?.Value);
        Assert.IsTrue(paragraph.Descendants<Bold>().Any());
        Assert.IsTrue(paragraph.Descendants<Underline>().Any());
    }

    [TestMethod]
    public async Task UnderlineShorthandRendersUnderlinedText()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        const string asciiDoc = "= Sample\n\nUse +underlined text+ here.";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "underline-shortcut"),
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var run = GetRequiredBody(document).Descendants<Run>().Single(static value => value.InnerText == "underlined text");
        Assert.IsTrue(run.RunProperties?.Underline is not null);
    }

    [TestMethod]
    public async Task PostProcessorCanApplyFinalBrandingToGeneratedGoogleDocs()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(
            workspace,
            handlerOptions: new GoogleDocsDocumentHandlerOptions
            {
                PostProcessor = new BrandingPostProcessor("Contoso Confidential"),
            });

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "branded"),
                content = "= Sample\n\nVisible body text.",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        using var stream = new MemoryStream(workspace.GetDocumentBytes(writeResult.Path), writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var paragraphs = GetRequiredBody(document).Elements<Paragraph>()
            .Select(static paragraph => paragraph.InnerText)
            .ToArray();

        CollectionAssert.Contains(paragraphs, "Contoso Confidential");
    }

    [TestMethod]
    public async Task BestEffortReadImportsNewInlineAndTableSyntaxFromGeneratedGoogleDocBody()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var writerFunctions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        var readerFunctions = GoogleDocsFunctionTestUtilities.CreateFunctions(
            workspace,
            handlerOptions: new GoogleDocsDocumentHandlerOptions
            {
                PreferManagedAsciiDocPayload = false,
                PreferEmbeddedAsciiDoc = false,
            });
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

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            writerFunctions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "import-visible-body"),
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            readerFunctions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = writeResult.Path,
            }));

        var importedAsciiDoc = GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents);
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
        foreach (var testCase in WordCoverage.WordAsciiDocCoverageCases.All)
        {
            var workspace = new FakeGoogleDocsWorkspaceClient();
            var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
            var normalized = GoogleDocsFunctionTestUtilities.NormalizeLineEndings(testCase.AsciiDoc);

            var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
                functions,
                "document_write_file",
                GoogleDocsFunctionTestUtilities.CreateArguments(new
                {
                    document_reference = GoogleDocsSupport.CreateFolderReference("root", testCase.Name),
                    content = normalized,
                }));

            Assert.IsTrue(writeResult.Success, $"{testCase.Name}: {writeResult.Message}");

            var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
                functions,
                "document_read_file",
                GoogleDocsFunctionTestUtilities.CreateArguments(new
                {
                    document_reference = writeResult.Path,
                }));

            Assert.AreEqual(normalized, GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents), testCase.Name);
        }
    }

    [TestMethod]
    public async Task EditFileUpdatesCanonicalAsciiDocAndManagedPayload()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);
        const string original = "= Editable Sample\n\nThis paragraph is the editable anchor.";

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "edit"),
                content = original,
            }));

        _ = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = writeResult.Path,
            }));

        var editResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentEditFileToolResult>(
            functions,
            "document_edit_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = writeResult.Path,
                old_string = "editable anchor",
                new_string = "updated anchor",
            }));

        Assert.IsTrue(editResult.Success, editResult.Message);
        StringAssert.Contains(editResult.UpdatedAsciiDoc!, "updated anchor");
        Assert.IsFalse(editResult.UpdatedAsciiDoc!.Contains("editable anchor", StringComparison.Ordinal));
        StringAssert.Contains(workspace.GetManagedAsciiDoc(editResult.Path)!, "updated anchor");
    }

    [TestMethod]
    public async Task ReadImportsExternalGoogleDocWithoutManagedPayload()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var documentId = workspace.SeedExternalDocument("external", CreateExternalWordDocumentBytes(), managedAsciiDoc: null);
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace);

        var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateDocumentReference(documentId),
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(textParts.Any(static text => text.Contains("Best-effort AsciiDoc import", StringComparison.Ordinal)));

        var importedAsciiDoc = GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents);
        StringAssert.Contains(importedAsciiDoc, "= Imported Title");
        StringAssert.Contains(importedAsciiDoc, "Imported paragraph");
        StringAssert.Contains(importedAsciiDoc, "|===");
        StringAssert.Contains(importedAsciiDoc, "| Key | Value");
    }

    [TestMethod]
    public async Task GrepSearchReportsHostedOnlyGoogleDocsAreNotLocalWorkspaceFiles()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var workingDirectory = GoogleDocsFunctionTestUtilities.CreateTemporaryDirectory();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace, workingDirectory);

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "Guide"),
                content = "= Guide\n\nContent",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var result = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                pattern = "Guide",
            }));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Matches.Length);
        Assert.AreEqual(0, result.SupportedExtensions.Length);
        StringAssert.Contains(result.Message!, "do not expose local file extensions");
    }

    [TestMethod]
    public async Task GrepSearchFindsTextInExplicitHostedGoogleDocsReferences()
    {
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(workspace, GoogleDocsFunctionTestUtilities.CreateTemporaryDirectory());

        var writeResult = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateFolderReference("root", "Guide"),
                content = "= Guide\n\nHosted Google Docs content",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var hostedUrl = FakeGoogleDocsWorkspaceClient.CreateDocumentUrl(ExtractDocumentId(writeResult.Path));
        var result = await GoogleDocsFunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                pattern = "Hosted Google Docs content",
                document_references = new[] { hostedUrl },
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual(writeResult.Path, result.Matches[0].Path);
        Assert.AreEqual(3, result.Matches[0].Offset);

        var readContents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = result.Matches[0].Path,
                offset = result.Matches[0].Offset,
                limit = 1,
            }));

        Assert.AreEqual("Hosted Google Docs content", GoogleDocsFunctionTestUtilities.ReadAsciiDocText(readContents));
    }

    [TestMethod]
    public void EveryChecklistItemHasAtLeastOneMappedTestCase()
    {
        var coverageMap = WordCoverage.WordAsciiDocCoverageCases.BuildChecklistCoverageMap();
        var checklistItems = GoogleDocsFunctionTestUtilities.LoadChecklistItems();
        var knownCaseNames = new HashSet<string>(
            [
                WordCoverage.WordAsciiDocCoverageCases.FullSpecOutlineCaseName,
                .. WordCoverage.WordAsciiDocCoverageCases.All.Select(static testCase => testCase.Name),
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

    private sealed class FakeHttpClientInitializer : IConfigurableHttpClientInitializer
    {
        private int _initializeCallCount;

        public void Initialize(ConfigurableHttpClient httpClient)
        {
            _initializeCallCount++;
        }

        public void Initialize(ConfigurableMessageHandler messageHandler)
        {
            _initializeCallCount++;
        }
    }

    private static string ExtractDocumentId(string resolvedReference)
    {
        if (Uri.TryCreate(resolvedReference, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "gdocs", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "documents", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        }

        throw new InvalidOperationException($"Expected a gdocs://documents reference but received '{resolvedReference}'.");
    }

    private static byte[] CreateExternalWordDocumentBytes()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
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

        return stream.ToArray();
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
