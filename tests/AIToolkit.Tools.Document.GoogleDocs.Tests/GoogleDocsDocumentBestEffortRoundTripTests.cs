using AIToolkit.Tools.Document.Word;
using Microsoft.Extensions.AI;
using WordTestCases = AIToolkit.Tools.Document.Word.Tests;

namespace AIToolkit.Tools.Document.GoogleDocs.Tests;

[TestClass]
public class GoogleDocsDocumentBestEffortRoundTripTests
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
    public async Task GeneratedGoogleDocBodyBestEffortRoundTripsEquivalentAsciiDoc(string caseName)
    {
        var asciiDoc = GoogleDocsFunctionTestUtilities.NormalizeLineEndings(WordTestCases.WordAsciiDocBestEffortRoundTripCases.Get(caseName));
        var workspace = new FakeGoogleDocsWorkspaceClient();
        var functions = GoogleDocsFunctionTestUtilities.CreateFunctions(
            workspace,
            handlerOptions: new GoogleDocsDocumentHandlerOptions
            {
                PreferManagedAsciiDocPayload = false,
                PreferEmbeddedAsciiDoc = false,
            });
        var documentId = workspace.SeedExternalDocument(caseName, CreateRenderedWordDocumentWithoutPayload(asciiDoc), managedAsciiDoc: null);

        var contents = await GoogleDocsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "document_read_file",
            GoogleDocsFunctionTestUtilities.CreateArguments(new
            {
                document_reference = GoogleDocsSupport.CreateDocumentReference(documentId),
            }));

        var textParts = contents.OfType<TextContent>().Select(static content => content.Text).ToArray();
        Assert.IsTrue(
            textParts.Any(static text => text.Contains("Best-effort AsciiDoc import", StringComparison.Ordinal)),
            $"{caseName}: expected the read result to come from best-effort import rather than the managed payload.");

        var importedAsciiDoc = GoogleDocsFunctionTestUtilities.ReadAsciiDocText(contents);
        WordTestCases.WordAsciiDocRenderedEquivalence.AssertEquivalent(asciiDoc, importedAsciiDoc, caseName);
    }

    private static byte[] CreateRenderedWordDocumentWithoutPayload(string asciiDoc)
    {
        using var stream = new MemoryStream();
        using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            WordAsciiDocRenderer.Write(mainPart, asciiDoc);
            (mainPart.Document ?? throw new InvalidOperationException("Expected the rendered Word document to exist.")).Save();
        }

        return stream.ToArray();
    }
}
