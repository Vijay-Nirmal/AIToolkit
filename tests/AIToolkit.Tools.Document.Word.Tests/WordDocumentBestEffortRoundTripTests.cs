using AIToolkit.Tools.Document;
using AIToolkit.Tools.Document.Word;
using Microsoft.Extensions.AI;
using System.Text;

namespace AIToolkit.Tools.Document.Word.Tests;

[TestClass]
public class WordDocumentBestEffortRoundTripTests
{
    [TestMethod]
    [DataRow("title-and-headings")]
    [DataRow("centered-heading")]
    [DataRow("inline-emphasis-and-code")]
    [DataRow("underline-highlight-and-hard-break")]
    [DataRow("underline-role-span-variant")]
    [DataRow("role-prefixed-underline-inline-variant")]
    [DataRow("markdown-hyphen-list-variant")]
    [DataRow("escaped-role-span-and-styled-link")]
    [DataRow("malformed-styled-link-variant")]
    [DataRow("list-variants")]
    [DataRow("header-table-with-formatting")]
    [DataRow("header-table-expanded-layout-variant")]
    [DataRow("implicit-table-without-delimiters-variant")]
    [DataRow("malformed-header-cell-role-variant")]
    [DataRow("table-alignment-shorthand-variant")]
    [DataRow("block-role-attribute-variant")]
    [DataRow("note-admonition")]
    [DataRow("warning-admonition")]
    [DataRow("source-block")]
    [DataRow("bold-underline-shorthand")]
    [DataRow("bold-underline-nested-variant")]
    [DataRow("unclosed-role-span-at-end-variant")]
    [DataRow("literal-block")]
    [DataRow("page-break-and-thematic-break")]
    public async Task GeneratedWordBodyBestEffortRoundTripsEquivalentAsciiDoc(string caseName)
    {
        var asciiDoc = FunctionTestUtilities.NormalizeLineEndings(WordAsciiDocBestEffortRoundTripCases.Get(caseName));
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var writerFunctions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var readerFunctions = FunctionTestUtilities.CreateFunctions(
            workingDirectory,
            new WordDocumentHandlerOptions
            {
                PreferEmbeddedAsciiDoc = false,
            });
        var filePath = Path.Combine(workingDirectory, caseName + ".docx");

        var writeResult = await FunctionTestUtilities.InvokeAsync<DocumentWriteFileToolResult>(
            writerFunctions,
            "document_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
                content = asciiDoc,
            }));

        Assert.IsTrue(writeResult.Success, $"{caseName}: {writeResult.Message}");

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            readerFunctions,
            "document_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                document_reference = filePath,
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(
            textParts.Any(static text => text.Contains("Best-effort AsciiDoc import", StringComparison.Ordinal)),
            $"{caseName}: expected the read result to come from best-effort import rather than the embedded payload.");

        var importedAsciiDoc = FunctionTestUtilities.ReadAsciiDocText(contents);
        WordAsciiDocRenderedEquivalence.AssertEquivalent(asciiDoc, importedAsciiDoc, caseName);
    }
}