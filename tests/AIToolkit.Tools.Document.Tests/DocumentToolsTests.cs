using Microsoft.Extensions.AI;
using System.Text;

namespace AIToolkit.Tools.Document.Tests;

[TestClass]
public class DocumentToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "document_edit_file",
        "document_grep_search",
        "document_read_file",
        "document_write_file",
    ];

    private static readonly string[] GuideDocumentReferences = ["doc:guide"];

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubDocumentHandler());

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void GetSystemPromptGuidanceMentionsAsciiDocAndTools()
    {
        var prompt = DocumentTools.GetSystemPromptGuidance("Base prompt");

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using document tools");
        StringAssert.Contains(prompt, "AsciiDoc");
        StringAssert.Contains(prompt, "document_read_file");
        StringAssert.Contains(prompt, "document_write_file");
        StringAssert.Contains(prompt, "document_edit_file");
        StringAssert.Contains(prompt, "document_grep_search");
        StringAssert.Contains(prompt, "IDocumentReferenceResolver");
        StringAssert.Contains(prompt, "Do not invent alias schemes or pseudo-URLs");
        StringAssert.Contains(prompt, "direct path or, when the host configures an IDocumentReferenceResolver, a document reference such as a URL or ID");
        StringAssert.Contains(prompt, "stale changes are detected");
        StringAssert.Contains(prompt, "specific keywords or the document set is large");
        StringAssert.Contains(prompt, "document_references");
        StringAssert.Contains(prompt, "paths or resolved references and offsets that can be passed directly to document_read_file");
        Assert.IsFalse(prompt.Contains("m365://drives/me/root", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("[.text-center]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ConfiguredPromptProvidersAreMergedIntoSystemPromptGuidance()
    {
        var prompt = DocumentTools.GetSystemPromptGuidance(
            "Base prompt",
            new DocumentToolsOptions
            {
                PromptProviders = [new StubPromptProvider()],
            });

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "Stub system guidance.");
    }

    [TestMethod]
    public void ReadAndGrepDescriptionsStayFocusedOnReadingAndSearching()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubDocumentHandler());

        var readDescription = functions.Single(static function => function.Name == "document_read_file").Description;
        var grepDescription = functions.Single(static function => function.Name == "document_grep_search").Description;

        Assert.IsNotNull(readDescription);
        Assert.IsNotNull(grepDescription);
        Assert.IsFalse(readDescription.Contains("[.text-center]", StringComparison.Ordinal));
        Assert.IsFalse(readDescription.Contains("m365://drives/me/root", StringComparison.Ordinal));
        Assert.IsFalse(grepDescription.Contains("[.text-center]", StringComparison.Ordinal));
        StringAssert.Contains(readDescription, "Do not invent alias schemes or pseudo-URLs");
        StringAssert.Contains(readDescription, "line number + tab");
        StringAssert.Contains(readDescription, "document_grep_search first");
        StringAssert.Contains(grepDescription, "includePattern");
        StringAssert.Contains(grepDescription, "document_references");
        StringAssert.Contains(grepDescription, "path or resolved reference and offset");
    }

    [TestMethod]
    public void ProviderPromptContributionsAreMergedIntoWriteAndEditDescriptions()
    {
        var functions = FunctionTestUtilities.CreateFunctions(
            handler: new StubDocumentHandler(),
            promptProviders: [new StubPromptProvider()]);

        var writeDescription = functions.Single(static function => function.Name == "document_write_file").Description;
        var editDescription = functions.Single(static function => function.Name == "document_edit_file").Description;

        Assert.IsNotNull(writeDescription);
        Assert.IsNotNull(editDescription);

        StringAssert.Contains(writeDescription, "IDocumentReferenceResolver");
        StringAssert.Contains(writeDescription, "Do not invent alias schemes or pseudo-URLs");
        StringAssert.Contains(writeDescription, "Read existing documents first before overwriting them.");
        StringAssert.Contains(writeDescription, "Stub write guidance.");
        StringAssert.Contains(editDescription, "Preserve the exact AsciiDoc you read unless you intentionally want to change structure or styling.");
        StringAssert.Contains(editDescription, "Stub edit guidance.");
    }

    [TestMethod]
    public async Task ReadFileUsesConfiguredHandlerAndNumbersAsciiDocOutput()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "notes.stubdoc");
        await File.WriteAllTextAsync(filePath, "alpha\nbeta\ngamma");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory, new StubDocumentHandler());

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                offset = 2,
                limit = 2,
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(textParts.Any(static text => text.Contains("2\tbeta", StringComparison.Ordinal) && text.Contains("3\tgamma", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task WriteAndEditRequireFreshFullReadState()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "fresh.stubdoc");
        await File.WriteAllTextAsync(filePath, "before");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory, new StubDocumentHandler());

        var writeWithoutRead = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = "after",
            }));

        Assert.IsFalse(writeWithoutRead.Success);
        StringAssert.Contains(writeWithoutRead.Message!, "Read it first");

        _ = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        await File.WriteAllTextAsync(filePath, "changed-outside");

        var editWithStaleRead = await FunctionTestUtilities.InvokeAsync<DocumentEditFileToolResult>(
            functions,
            "document_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                old_string = "changed-outside",
                new_string = "patched",
            }));

        Assert.IsFalse(editWithStaleRead.Success);
        StringAssert.Contains(editWithStaleRead.Message!, "modified since read");
    }

    [TestMethod]
    public async Task GrepSearchFindsTextOnlyInSupportedDocumentExtensions()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workingDirectory, "docs"));
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "docs", "guide.stubdoc"), "alpha\nbeta\ngamma");
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "docs", "notes.txt"), "beta");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory, new StubDocumentHandler());

        var result = await FunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "beta",
                includePattern = "**/*.stubdoc",
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual("docs/guide.stubdoc", result.Matches[0].Path);
        Assert.AreEqual(2, result.Matches[0].Offset);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = result.Matches[0].Path,
                offset = result.Matches[0].Offset,
                limit = 1,
            }));

        Assert.AreEqual("beta", FunctionTestUtilities.ReadAsciiDocText(readContents));
    }

    [TestMethod]
    public async Task GrepSearchCanSearchExplicitResolverBackedDocumentReferences()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var store = new InMemoryDocumentStore();
        var functions = DocumentTools.CreateFunctions(
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 200,
                MaxSearchResults = 100,
                ReferenceResolver = new StubDocumentReferenceResolver(store),
                Handlers = [new StubDocumentHandler()],
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:guide",
                content = "alpha\nbeta\ngamma",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var result = await FunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "beta",
                document_references = GuideDocumentReferences,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual("doc:guide", result.Matches[0].Path);
        Assert.AreEqual(2, result.Matches[0].Offset);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = result.Matches[0].Path,
                offset = result.Matches[0].Offset,
                limit = 1,
            }));

        Assert.AreEqual("beta", FunctionTestUtilities.ReadAsciiDocText(readContents));
    }

    [TestMethod]
    public async Task GrepSearchRejectsIncludePatternWhenExplicitDocumentReferencesAreProvided()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var store = new InMemoryDocumentStore();
        var functions = DocumentTools.CreateFunctions(
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 200,
                MaxSearchResults = 100,
                ReferenceResolver = new StubDocumentReferenceResolver(store),
                Handlers = [new StubDocumentHandler()],
            });

        var result = await FunctionTestUtilities.InvokeAsync<DocumentGrepSearchToolResult>(
            functions,
            "document_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "beta",
                includePattern = "**/*.stubdoc",
                document_references = GuideDocumentReferences,
            }));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message!, "includePattern cannot be combined with document_references");
    }

    [TestMethod]
    public async Task ReferenceResolverCanMapDocumentIdsToStreamBackedDocuments()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var store = new InMemoryDocumentStore();
        var functions = DocumentTools.CreateFunctions(
            new DocumentToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 200,
                MaxSearchResults = 100,
                ReferenceResolver = new StubDocumentReferenceResolver(store),
                Handlers = [new StubDocumentHandler()],
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            functions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:guide",
                content = "alpha\nbeta",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.AreEqual("doc:guide", writeResult.Path);
        Assert.AreEqual("alpha\nbeta", store.ReadText());

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:guide",
                offset = 2,
                limit = 1,
            }));

        Assert.AreEqual("beta", FunctionTestUtilities.ReadAsciiDocText(contents));

        _ = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:guide",
            }));

        var editResult = await FunctionTestUtilities.InvokeAsync<DocumentEditFileToolResult>(
            functions,
            "document_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = "doc:guide",
                old_string = "beta",
                new_string = "gamma",
            }));

        Assert.IsTrue(editResult.Success, editResult.Message);
        StringAssert.Contains(store.ReadText(), "gamma");
    }

    private sealed class StubDocumentReferenceResolver(InMemoryDocumentStore store) : IDocumentReferenceResolver
    {
        public ValueTask<DocumentReferenceResolution?> ResolveAsync(string documentReference, DocumentReferenceResolverContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DocumentReferenceResolution?>(
                string.Equals(documentReference, "doc:guide", StringComparison.OrdinalIgnoreCase)
                    ? DocumentReferenceResolution.CreateStreamBacked(
                        resolvedReference: "doc:guide",
                        extension: ".stubdoc",
                        existsAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(store.Exists);
                        },
                        openReadAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult<Stream>(new MemoryStream(store.Content, writable: false));
                        },
                        openWriteAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult<Stream>(new CommitOnDisposeMemoryStream(store));
                        },
                        version: store.Version,
                        length: store.Exists ? store.Content.LongLength : null,
                        state: store)
                    : null);
    }

    private sealed class StubPromptProvider : IDocumentToolPromptProvider
    {
        public DocumentToolPromptContribution GetPromptContribution() =>
            new(
                ReadFileDescriptionLines: ["Stub read guidance."],
                WriteFileDescriptionLines: ["Stub write guidance."],
                EditFileDescriptionLines: ["Stub edit guidance."],
                GrepSearchDescriptionLines: ["Stub grep guidance."],
                SystemPromptLines: ["Stub system guidance."]);
    }

    private sealed class StubDocumentHandler : IDocumentHandler
    {
        public string ProviderName => "stub";

        public IReadOnlyCollection<string> SupportedExtensions => [".stubdoc"];

        public bool CanHandle(DocumentHandlerContext context) =>
            string.Equals(context.Extension, ".stubdoc", StringComparison.OrdinalIgnoreCase);

        public async ValueTask<DocumentReadResponse> ReadAsync(DocumentHandlerContext context, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenReadAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return new DocumentReadResponse(content, true, "stub");
        }

        public async ValueTask<DocumentWriteResponse> WriteAsync(DocumentHandlerContext context, string asciiDoc, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenWriteAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.SetLength(0);
            }

            using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: false);
            await writer.WriteAsync(asciiDoc.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return new DocumentWriteResponse(true, "stub");
        }
    }

    private sealed class InMemoryDocumentStore
    {
        public byte[] Content { get; private set; } = [];

        public bool Exists { get; private set; }

        public string Version { get; private set; } = Guid.NewGuid().ToString("N");

        public string ReadText() => Encoding.UTF8.GetString(Content);

        public void Save(byte[] content)
        {
            Content = content;
            Exists = true;
            Version = Guid.NewGuid().ToString("N");
        }
    }

    private sealed class CommitOnDisposeMemoryStream(InMemoryDocumentStore store) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                store.Save(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}