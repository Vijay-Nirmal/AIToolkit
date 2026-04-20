using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AIToolkit.Tools.Workbook.Tests;

[TestClass]
public class WorkbookToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "workbook_edit_file",
        "workbook_grep_search",
        "workbook_read_file",
        "workbook_spec_lookup",
        "workbook_write_file",
    ];

    private static readonly string[] GuideWorkbookReferences = ["wb:guide"];

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubWorkbookHandler());

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void GetSystemPromptGuidanceMentionsWorkbookDocAndSpecLookup()
    {
        var prompt = WorkbookTools.GetSystemPromptGuidance("Base prompt");

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "# Using workbook tools");
        StringAssert.Contains(prompt, "WorkbookDoc");
        StringAssert.Contains(prompt, "workbook_read_file");
        StringAssert.Contains(prompt, "workbook_write_file");
        StringAssert.Contains(prompt, "workbook_edit_file");
        StringAssert.Contains(prompt, "workbook_grep_search");
        StringAssert.Contains(prompt, "workbook_spec_lookup");
        StringAssert.Contains(prompt, "@B5 | a | b | c");
        StringAssert.Contains(prompt, "[merge B3:H3 align=center/middle]");
    }

    [TestMethod]
    public void ReadAndGrepDescriptionsStayFocusedOnReadingAndSearching()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubWorkbookHandler());

        var readDescription = functions.Single(static function => function.Name == "workbook_read_file").Description;
        var grepDescription = functions.Single(static function => function.Name == "workbook_grep_search").Description;
        var lookupDescription = functions.Single(static function => function.Name == "workbook_spec_lookup").Description;

        Assert.IsNotNull(readDescription);
        Assert.IsNotNull(grepDescription);
        Assert.IsNotNull(lookupDescription);
        StringAssert.Contains(readDescription, "line number + tab");
        StringAssert.Contains(readDescription, "= Workbook Name");
        StringAssert.Contains(grepDescription, "includePattern");
        StringAssert.Contains(grepDescription, "workbook_references");
        StringAssert.Contains(lookupDescription, "advanced WorkbookDoc features");
        StringAssert.Contains(lookupDescription, "chart combo secondary axis");
    }

    [TestMethod]
    public async Task ReadFileUsesConfiguredHandlerAndNumbersWorkbookDocOutput()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "notes.stubwb");
        await File.WriteAllTextAsync(filePath, "= Sample\n== Sheet1\n@A1 | alpha\n@A2 | beta\n@A3 | gamma");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory, new StubWorkbookHandler());

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = filePath,
                offset = 3,
                limit = 2,
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(textParts.Any(static text => text.Contains("3\t@A1 | alpha", StringComparison.Ordinal) && text.Contains("4\t@A2 | beta", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task WriteAndEditRequireFreshFullReadState()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "fresh.stubwb");
        await File.WriteAllTextAsync(filePath, "= Before\n== Sheet1\n@A1 | before");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory, new StubWorkbookHandler());

        var writeWithoutRead = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = filePath,
                content = "= After\n== Sheet1\n@A1 | after",
            }));

        Assert.IsFalse(writeWithoutRead.Success);
        StringAssert.Contains(writeWithoutRead.Message!, "Read it first");

        _ = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = filePath,
            }));

        await File.WriteAllTextAsync(filePath, "= Changed\n== Sheet1\n@A1 | changed-outside");

        var editWithStaleRead = await FunctionTestUtilities.InvokeAsync<WorkbookEditFileToolResult>(
            functions,
            "workbook_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = filePath,
                old_string = "changed-outside",
                new_string = "patched",
            }));

        Assert.IsFalse(editWithStaleRead.Success);
        StringAssert.Contains(editWithStaleRead.Message!, "modified since read");
    }

    [TestMethod]
    public async Task GrepSearchCanSearchExplicitResolverBackedWorkbookReferences()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var store = new InMemoryWorkbookStore();
        var functions = WorkbookTools.CreateFunctions(
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 200,
                MaxSearchResults = 100,
                ReferenceResolver = new StubWorkbookReferenceResolver(store),
                Handlers = [new StubWorkbookHandler()],
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = "wb:guide",
                content = "= Sample\n== Sheet1\n@A1 | alpha\n@A2 | beta\n@A3 | gamma",
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var result = await FunctionTestUtilities.InvokeAsync<WorkbookGrepSearchToolResult>(
            functions,
            "workbook_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "beta",
                workbook_references = GuideWorkbookReferences,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual("wb:guide", result.Matches[0].Path);
    }

    [TestMethod]
    public async Task SpecificationLookupFindsChartGuidance()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubWorkbookHandler());

        var result = await FunctionTestUtilities.InvokeAsync<WorkbookSpecificationLookupToolResult>(
            functions,
            "workbook_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                keywords = "chart combo secondary axis",
                maxResults = 3,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Matches.Length >= 1);
        Assert.IsTrue(result.Matches.Any(static match => match.SectionId.StartsWith("chart-", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SpecificationLookupNoLongerReturnsValidationGuidance()
    {
        var functions = FunctionTestUtilities.CreateFunctions(handler: new StubWorkbookHandler());

        var result = await FunctionTestUtilities.InvokeAsync<WorkbookSpecificationLookupToolResult>(
            functions,
            "workbook_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                keywords = "validation dropdown reject",
                maxResults = 3,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(0, result.Matches.Length);
        StringAssert.Contains(result.Message, "No WorkbookDoc guidance matched");
    }

    [TestMethod]
    public async Task WriteFileLogsContentOnlyWhenEnabled()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "logged.stubwb");

        var hiddenLogs = new List<string>();
        using var hiddenLoggerFactory = new ListLoggerFactory(hiddenLogs);
        var hiddenFunctions = WorkbookTools.CreateFunctions(
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubWorkbookHandler()],
                LoggerFactory = hiddenLoggerFactory,
                LogContentParameters = false,
            });

        var hiddenWrite = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            hiddenFunctions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = filePath,
                content = "= Hidden\n== Sheet1\n@A1 | hidden",
            }));

        Assert.IsTrue(hiddenWrite.Success, hiddenWrite.Message);
        Assert.IsTrue(hiddenLogs.Any(static entry => entry.Contains("workbook_write_file", StringComparison.Ordinal)));
        Assert.IsFalse(hiddenLogs.Any(static entry => entry.Contains("\"content\"", StringComparison.Ordinal)));

        var visibleLogs = new List<string>();
        using var visibleLoggerFactory = new ListLoggerFactory(visibleLogs);
        var visibleFunctions = WorkbookTools.CreateFunctions(
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubWorkbookHandler()],
                LoggerFactory = visibleLoggerFactory,
                LogContentParameters = true,
            });

        var visibleWrite = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            visibleFunctions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = Path.Combine(workingDirectory, "visible.stubwb"),
                content = "= Visible\n== Sheet1\n@A1 | visible",
            }));

        Assert.IsTrue(visibleWrite.Success, visibleWrite.Message);
        Assert.IsTrue(visibleLogs.Any(static entry => entry.Contains("\"content\":\"= Visible\\n== Sheet1\\n@A1 | visible\"", StringComparison.Ordinal)));
    }

    private sealed class StubWorkbookReferenceResolver(InMemoryWorkbookStore store) : IWorkbookReferenceResolver
    {
        public ValueTask<WorkbookReferenceResolution?> ResolveAsync(string workbookReference, WorkbookReferenceResolverContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkbookReferenceResolution?>(
                string.Equals(workbookReference, "wb:guide", StringComparison.OrdinalIgnoreCase)
                    ? WorkbookReferenceResolution.CreateStreamBacked(
                        resolvedReference: "wb:guide",
                        extension: ".stubwb",
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

    private sealed class StubWorkbookHandler : IWorkbookHandler
    {
        public string ProviderName => "stub";

        public IReadOnlyCollection<string> SupportedExtensions => [".stubwb"];

        public bool CanHandle(WorkbookHandlerContext context) =>
            string.Equals(context.Extension, ".stubwb", StringComparison.OrdinalIgnoreCase);

        public async ValueTask<WorkbookReadResponse> ReadAsync(WorkbookHandlerContext context, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenReadAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return new WorkbookReadResponse(content, true, "stub");
        }

        public async ValueTask<WorkbookWriteResponse> WriteAsync(WorkbookHandlerContext context, string workbookDoc, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenWriteAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.SetLength(0);
            }

            using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: false);
            await writer.WriteAsync(workbookDoc.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return new WorkbookWriteResponse(true, "stub");
        }
    }

    private sealed class InMemoryWorkbookStore
    {
        public byte[] Content { get; private set; } = [];

        public bool Exists { get; private set; }

        public string Version { get; private set; } = Guid.NewGuid().ToString("N");

        public void Save(byte[] content)
        {
            Content = content;
            Exists = true;
            Version = Guid.NewGuid().ToString("N");
        }
    }

    private sealed class CommitOnDisposeMemoryStream(InMemoryWorkbookStore store) : MemoryStream
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

    private sealed class ListLoggerProvider(List<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(entries);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger(List<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add(formatter(state, exception));
    }

    private sealed class ListLoggerFactory(List<string> entries) : ILoggerFactory
    {
        private readonly ListLoggerProvider _provider = new(entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => _provider.CreateLogger(categoryName);

        public void Dispose() => _provider.Dispose();
    }
}
