using AIToolkit.Tools.Deck;
using AIToolkit.Tools.Deck.PowerPoint;
using Microsoft.Extensions.AI;

internal static class TemplateCreationSample
{
    private static readonly string[] SupportedExtensions = [".pptx", ".pptm", ".potx", ".potm"];

    public static async Task RunAsync(
        IChatClient chatClient,
        string workspaceDirectory,
        PowerPointDeckToolSetOptions toolOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(toolOptions);

        var sourcePresentationPath = PromptForPresentationPath();
        if (sourcePresentationPath is null)
        {
            Console.WriteLine("Template creation cancelled.");
            return;
        }

        var sourceStem = SanitizePathSegment(Path.GetFileNameWithoutExtension(sourcePresentationPath));
        var artifactDirectory = Path.Combine(workspaceDirectory, "template-artifacts", sourceStem);
        var sourceSlideDirectory = Path.Combine(artifactDirectory, "source-slides");
        var generatedSlideDirectory = Path.Combine(artifactDirectory, "preview-slides");
        var generatedPresentationPath = Path.Combine(workspaceDirectory, "presentations", sourceStem + "-template-preview.pptx");
        var generatedDeckDocPath = Path.Combine(artifactDirectory, sourceStem + "-template.deckdoc");

        Directory.CreateDirectory(artifactDirectory);

        Console.WriteLine("AIToolkit.Tools.Deck.PowerPoint template creation sample");
        Console.WriteLine($"Workspace ready: {workspaceDirectory}");
        Console.WriteLine($"Source presentation: {sourcePresentationPath}");
        Console.WriteLine($"Generated preview target: {generatedPresentationPath}");
        Console.WriteLine();

        var result = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
            chatClient,
            sourcePresentationPath,
            new PowerPointDeckTemplateGenerationOptions
            {
                ToolOptions = toolOptions,
                GeneratedPresentationReference = generatedPresentationPath,
                SourceSlideImageOutputDirectory = sourceSlideDirectory,
                GeneratedSlideImageOutputDirectory = generatedSlideDirectory,
                AdditionalInstructions = "Preserve the uploaded deck's reusable visual structure, theme, layout system, and fixed design elements while replacing one-off business copy with template-ready placeholder content.",
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.TemplateDeckDoc))
        {
            await File.WriteAllTextAsync(generatedDeckDocPath, result.TemplateDeckDoc, cancellationToken);
        }

        if (result.Success
            && toolOptions.TemplateStore is not null
            && !string.IsNullOrWhiteSpace(result.TemplateDeckDoc))
        {
            await toolOptions.TemplateStore.StoreAsync(
                new DeckTemplateRecord(
                    sourceStem + "-generated",
                    "Generated from a user-supplied PowerPoint presentation.",
                    result.TemplateDeckDoc,
                    "sample"),
                cancellationToken);
        }

        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Similar enough: {result.SimilarEnough}");
        Console.WriteLine($"Iterations: {result.IterationCount}");
        Console.WriteLine($"Template DeckDoc file: {generatedDeckDocPath}");
        Console.WriteLine($"Preview presentation: {result.GeneratedPresentationReference}");
        Console.WriteLine($"Source slide images: {result.SourceSlideImages?.OutputDirectory ?? sourceSlideDirectory}");
        Console.WriteLine($"Preview slide images: {result.GeneratedSlideImages?.OutputDirectory ?? generatedSlideDirectory}");
        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            Console.WriteLine($"Summary: {result.Summary}");
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            Console.WriteLine($"Message: {result.Message}");
        }

        if (result.Issues.Length > 0)
        {
            Console.WriteLine("Issues:");
            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"- {issue}");
            }
        }

        if (toolOptions.TemplateStore is not null)
        {
            Console.WriteLine($"Stored the generated template in the configured template store as '{sourceStem}-generated'.");
        }
    }

    private static string? PromptForPresentationPath()
    {
        while (true)
        {
            Console.WriteLine("Enter the path to the PowerPoint file you want to convert into a template.");
            Console.Write("PowerPoint path (blank to cancel): ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine();
                return null;
            }

            var candidatePath = input.Trim('"');
            var fullPath = Path.GetFullPath(candidatePath);
            if (!File.Exists(fullPath))
            {
                Console.WriteLine("File not found. Enter an existing .pptx, .pptm, .potx, or .potm path.");
                Console.WriteLine();
                continue;
            }

            var extension = Path.GetExtension(fullPath);
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("Unsupported file type. Enter a .pptx, .pptm, .potx, or .potm file.");
                Console.WriteLine();
                continue;
            }

            Console.WriteLine();
            return fullPath;
        }
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "presentation";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "presentation" : sanitized;
    }
}