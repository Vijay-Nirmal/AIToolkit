using AIToolkit.Tools.PDF;
using Microsoft.Extensions.AI;

namespace AIToolkit.Tools.PDF.Tests;

[TestClass]
public class PdfWorkspaceFileHandlerTests
{
    private const string SamplePdfText = "This is a sample for AIToolkit Tools to show pdf capabilities";

    [TestMethod]
    public async Task ReadFileExtractsTextFromSamplePdf()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var pdfPath = CopySamplePdfToWorkingDirectory(workingDirectory);

        var functions = WorkspaceTools.CreateFunctions(
            new WorkspaceToolsOptions
            {
                WorkingDirectory = workingDirectory,
                FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
            });

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = "media/document.pdf",
            }));

        Assert.AreEqual(Path.Combine(workingDirectory, "media", "document.pdf"), pdfPath);
        Assert.IsTrue(contents.OfType<TextContent>().Any(static content => content.Text.Contains(SamplePdfText, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReadFileHonorsPdfPagesParameter()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        CopySamplePdfToWorkingDirectory(workingDirectory);

        var functions = WorkspaceTools.CreateFunctions(
            new WorkspaceToolsOptions
            {
                WorkingDirectory = workingDirectory,
                FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
            });

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = "media/document.pdf",
                pages = "1",
            }));

        Assert.IsTrue(contents.OfType<TextContent>().Any(static content => content.Text.Contains("Selected pages: 1", StringComparison.Ordinal)));
        Assert.IsTrue(contents.OfType<TextContent>().Any(static content => content.Text.Contains(SamplePdfText, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReadFileRejectsInvalidPdfPagesParameter()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        CopySamplePdfToWorkingDirectory(workingDirectory);

        var functions = WorkspaceTools.CreateFunctions(
            new WorkspaceToolsOptions
            {
                WorkingDirectory = workingDirectory,
                FileHandlers = [PdfWorkspaceTools.CreateFileHandler()],
            });

        var contents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workspace_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                file_path = "media/document.pdf",
                pages = "3-5",
            }));

        Assert.AreEqual(1, contents.Count);
        Assert.IsTrue(contents[0] is TextContent text && text.Text.Contains("exceeds the PDF page count", StringComparison.Ordinal));
    }

    private static string CopySamplePdfToWorkingDirectory(string workingDirectory)
    {
        var destinationPath = Path.Combine(workingDirectory, "media", "document.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(GetSamplePdfPath(), destinationPath, overwrite: true);
        return destinationPath;
    }

    private static string GetSamplePdfPath()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return Path.Combine(repositoryRoot, "samples", "AIToolkit.Tools.Sample", "media", "document.pdf");
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AIToolkit.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing AIToolkit.slnx.");
    }
}