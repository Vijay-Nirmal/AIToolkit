using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Provides non-tool helpers for converting PowerPoint decks, exporting slide images, and generating reusable DeckDoc templates.
/// </summary>
/// <remarks>
/// <para>
/// These helpers reuse the same PowerPoint tool implementations that back the public <c>deck_*</c> functions, but they
/// expose a direct host API for callers that do not want to orchestrate those tools manually.
/// </para>
/// <para>
/// Template generation uses an <see cref="IChatClient"/> for the authoring and correction loop while still relying on
/// the PowerPoint read, write, specification-lookup, and slide-image-export tool behavior under the hood.
/// </para>
/// </remarks>
public static class PowerPointDeckTemplateUtilities
{
    /// <summary>
    /// Converts an existing PowerPoint presentation into canonical DeckDoc.
    /// </summary>
    /// <param name="presentationReference">The local path or resolver-backed PowerPoint reference to read.</param>
    /// <param name="options">The PowerPoint tool options used during conversion.</param>
    /// <param name="serviceProvider">The optional service provider used for resolver-backed references.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>The canonical DeckDoc recovered from the presentation.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var readResult = await PowerPointDeckTemplateUtilities.ConvertPresentationToDeckDocAsync(
    ///     "presentations/brand-template.pptx",
    ///     new PowerPointDeckToolSetOptions
    ///     {
    ///         WorkingDirectory = Environment.CurrentDirectory,
    ///         EnableBestEffortImport = true,
    ///     });
    /// ]]></code>
    /// </example>
    public static async Task<PowerPointDeckReadResult> ConvertPresentationToDeckDocAsync(
        string presentationReference,
        PowerPointDeckToolSetOptions? options = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = CreateFullReadOptions(options);
        var readFunction = PowerPointDeckTools.CreateReadFileFunction(effectiveOptions);
        var arguments = new AIFunctionArguments { Services = serviceProvider };
        arguments["deck_reference"] = presentationReference;

        var readResult = await InvokeFunctionAsync<DeckReadFileToolResult>(readFunction, arguments, cancellationToken).ConfigureAwait(false);
        return new PowerPointDeckReadResult(
            readResult.Success,
            readResult.Path,
            readResult.Success ? ExtractDeckDoc(readResult.Content) : null,
            readResult.TotalSlideCount,
            readResult.PreservesDeckDocRoundTrip,
            readResult.Message);
    }

    /// <summary>
    /// Converts canonical DeckDoc into a PowerPoint presentation.
    /// </summary>
    /// <param name="presentationReference">The local path or resolver-backed PowerPoint reference to write.</param>
    /// <param name="deckDoc">The canonical DeckDoc to render.</param>
    /// <param name="options">The PowerPoint tool options used during conversion.</param>
    /// <param name="serviceProvider">The optional service provider used for resolver-backed references.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>The canonical DeckDoc recovered after the write completed.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var writeResult = await PowerPointDeckTemplateUtilities.ConvertDeckDocToPresentationAsync(
    ///     "presentations/team-template-preview.pptx",
    ///     deckDoc,
    ///     new PowerPointDeckToolSetOptions
    ///     {
    ///         WorkingDirectory = Environment.CurrentDirectory,
    ///     });
    /// ]]></code>
    /// </example>
    public static async Task<PowerPointDeckWriteResult> ConvertDeckDocToPresentationAsync(
        string presentationReference,
        string deckDoc,
        PowerPointDeckToolSetOptions? options = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        var writeFunction = PowerPointDeckTools.CreateWriteFileFunction(options ?? new PowerPointDeckToolSetOptions());
        var arguments = new AIFunctionArguments { Services = serviceProvider };
        arguments["deck_reference"] = presentationReference;
        arguments["content"] = deckDoc;

        var writeResult = await InvokeFunctionAsync<DeckWriteFileToolResult>(writeFunction, arguments, cancellationToken).ConfigureAwait(false);
        return new PowerPointDeckWriteResult(
            writeResult.Success,
            writeResult.Path,
            writeResult.DeckDoc,
            writeResult.PreservesDeckDocRoundTrip,
            writeResult.Message);
    }

    /// <summary>
    /// Exports one PNG per slide from a PowerPoint presentation.
    /// </summary>
    /// <param name="presentationReference">The local path or resolver-backed PowerPoint reference to export.</param>
    /// <param name="options">The slide-export options.</param>
    /// <param name="serviceProvider">The optional service provider used for resolver-backed references.</param>
    /// <param name="cancellationToken">A token that cancels the export.</param>
    /// <returns>The exported slide image metadata.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var exportResult = await PowerPointDeckTemplateUtilities.ExportSlidesToImagesAsync(
    ///     "presentations/team-template-preview.pptx",
    ///     new PowerPointDeckSlideImageExportOptions
    ///     {
    ///         OutputDirectory = "artifacts/template-preview-slides",
    ///         Width = 1600,
    ///         Height = 900,
    ///         Force = true,
    ///     });
    /// ]]></code>
    /// </example>
    public static Task<PowerPointDeckSlideImageExportResult> ExportSlidesToImagesAsync(
        string presentationReference,
        PowerPointDeckSlideImageExportOptions? options = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default) =>
        ExportSlidesToImagesAsync(
            presentationReference,
            options,
            serviceProvider,
            exporter: null,
            cancellationToken);

    /// <summary>
    /// Generates a reusable DeckDoc template from an input PowerPoint presentation and renders a preview presentation that
    /// is iteratively corrected until it is visually close to the original deck.
    /// </summary>
    /// <param name="chatClient">The chat client used to draft and revise the template DeckDoc.</param>
    /// <param name="presentationReference">The local path or resolver-backed PowerPoint reference to analyze.</param>
    /// <param name="options">The template-generation options.</param>
    /// <param name="serviceProvider">The optional service provider used for resolver-backed references.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated template DeckDoc, rendered preview presentation, and exported slide-comparison artifacts.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var result = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
    ///     chatClient,
    ///     "presentations/brand-template-source.pptx",
    ///     new PowerPointDeckTemplateGenerationOptions
    ///     {
    ///         ToolOptions = new PowerPointDeckToolSetOptions
    ///         {
    ///             WorkingDirectory = Environment.CurrentDirectory,
    ///             EnableBestEffortImport = true,
    ///         },
    ///         GeneratedPresentationReference = "presentations/brand-template-preview.pptx",
    ///     });
    ///
    /// if (result.Success && result.TemplateDeckDoc is not null)
    /// {
    ///     await templateStore.StoreAsync(
    ///         new DeckTemplateRecord("brand-template", "Imported from brand-template-source.pptx", result.TemplateDeckDoc, "generated"));
    /// }
    /// ]]></code>
    /// </example>
    public static Task<PowerPointDeckTemplateGenerationResult> CreateTemplateAsync(
        IChatClient chatClient,
        string presentationReference,
        PowerPointDeckTemplateGenerationOptions? options = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default) =>
        CreateTemplateAsync(
            chatClient,
            presentationReference,
            options,
            serviceProvider,
            exporter: null,
            cancellationToken);

    internal static async Task<PowerPointDeckSlideImageExportResult> ExportSlidesToImagesAsync(
        string presentationReference,
        PowerPointDeckSlideImageExportOptions? options,
        IServiceProvider? serviceProvider,
        IPowerPointSlideImageExporter? exporter,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = options ?? new PowerPointDeckSlideImageExportOptions();
        var toolOptions = effectiveOptions.ToolOptions ?? new PowerPointDeckToolSetOptions();
        var (handlerOptions, deckOptions) = PowerPointDeckToolSupport.Split(toolOptions);
        var service = new PowerPointSlideImageExportService(handlerOptions, deckOptions, exporter);

        return await service.ExportAsync(presentationReference, effectiveOptions, serviceProvider, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PowerPointDeckTemplateGenerationResult> CreateTemplateAsync(
        IChatClient chatClient,
        string presentationReference,
        PowerPointDeckTemplateGenerationOptions? options,
        IServiceProvider? serviceProvider,
        IPowerPointSlideImageExporter? exporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        var effectiveOptions = options ?? new PowerPointDeckTemplateGenerationOptions();
        var toolOptions = effectiveOptions.ToolOptions ?? new PowerPointDeckToolSetOptions();
        var maxCorrectionRounds = Math.Max(1, effectiveOptions.MaxCorrectionRounds);

        var readResult = await ConvertPresentationToDeckDocAsync(
            presentationReference,
            toolOptions,
            serviceProvider,
            cancellationToken).ConfigureAwait(false);
        if (!readResult.Success || string.IsNullOrWhiteSpace(readResult.DeckDoc))
        {
            return new PowerPointDeckTemplateGenerationResult(
                false,
                readResult.PresentationReference,
                ResolveGeneratedPresentationReference(presentationReference, effectiveOptions.GeneratedPresentationReference, toolOptions.WorkingDirectory),
                null,
                0,
                false,
                string.Empty,
                [],
                null,
                null,
                readResult.Message ?? "The source presentation could not be converted into DeckDoc.");
        }

        var specificationLookup = await LookupTemplateSpecificationGuidanceAsync(
            effectiveOptions.SpecificationLookupQuery,
            toolOptions,
            serviceProvider,
            cancellationToken).ConfigureAwait(false);
        var templateDraft = await RequestTemplateDraftAsync(
            chatClient,
            readResult,
            specificationLookup,
            effectiveOptions,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(templateDraft.TemplateDeckDoc))
        {
            return new PowerPointDeckTemplateGenerationResult(
                false,
                readResult.PresentationReference,
                ResolveGeneratedPresentationReference(presentationReference, effectiveOptions.GeneratedPresentationReference, toolOptions.WorkingDirectory),
                null,
                0,
                false,
                templateDraft.Summary,
                [],
                null,
                null,
                "The chat client did not return a template DeckDoc draft.");
        }

        var currentTemplateDeckDoc = DeckSupport.NormalizeLineEndings(templateDraft.TemplateDeckDoc);
        var sourceSlideImages = await ExportSlidesToImagesAsync(
            presentationReference,
            new PowerPointDeckSlideImageExportOptions
            {
                ToolOptions = toolOptions,
                OutputDirectory = effectiveOptions.SourceSlideImageOutputDirectory,
                Width = effectiveOptions.ExportWidth,
                Height = effectiveOptions.ExportHeight,
                Force = true,
            },
            serviceProvider,
            exporter,
            cancellationToken).ConfigureAwait(false);
        if (!sourceSlideImages.Success)
        {
            return new PowerPointDeckTemplateGenerationResult(
                false,
                readResult.PresentationReference,
                ResolveGeneratedPresentationReference(presentationReference, effectiveOptions.GeneratedPresentationReference, toolOptions.WorkingDirectory),
                currentTemplateDeckDoc,
                0,
                false,
                templateDraft.Summary,
                [],
                sourceSlideImages,
                null,
                sourceSlideImages.Message);
        }

        var generatedPresentationReference = ResolveGeneratedPresentationReference(
            presentationReference,
            effectiveOptions.GeneratedPresentationReference,
            toolOptions.WorkingDirectory);

        PowerPointDeckSlideImageExportResult? generatedSlideImages = null;
        TemplateReviewResponse? lastReview = null;

        for (var iteration = 1; iteration <= maxCorrectionRounds; iteration++)
        {
            var writeResult = await ConvertDeckDocToPresentationAsync(
                generatedPresentationReference,
                currentTemplateDeckDoc,
                toolOptions,
                serviceProvider,
                cancellationToken).ConfigureAwait(false);
            if (!writeResult.Success || string.IsNullOrWhiteSpace(writeResult.PresentationReference))
            {
                return new PowerPointDeckTemplateGenerationResult(
                    false,
                    readResult.PresentationReference,
                    generatedPresentationReference,
                    currentTemplateDeckDoc,
                    iteration - 1,
                    false,
                    templateDraft.Summary,
                    lastReview?.Issues ?? [],
                    sourceSlideImages,
                    generatedSlideImages,
                    writeResult.Message ?? "The generated template preview presentation could not be written.");
            }

            generatedPresentationReference = writeResult.PresentationReference;
            currentTemplateDeckDoc = writeResult.DeckDoc ?? currentTemplateDeckDoc;

            generatedSlideImages = await ExportSlidesToImagesAsync(
                generatedPresentationReference,
                new PowerPointDeckSlideImageExportOptions
                {
                    ToolOptions = toolOptions,
                    OutputDirectory = effectiveOptions.GeneratedSlideImageOutputDirectory,
                    Width = effectiveOptions.ExportWidth,
                    Height = effectiveOptions.ExportHeight,
                    Force = true,
                },
                serviceProvider,
                exporter,
                cancellationToken).ConfigureAwait(false);
            if (!generatedSlideImages.Success)
            {
                return new PowerPointDeckTemplateGenerationResult(
                    false,
                    readResult.PresentationReference,
                    generatedPresentationReference,
                    currentTemplateDeckDoc,
                    iteration,
                    false,
                    templateDraft.Summary,
                    lastReview?.Issues ?? [],
                    sourceSlideImages,
                    generatedSlideImages,
                    generatedSlideImages.Message);
            }

            lastReview = await RequestTemplateReviewAsync(
                chatClient,
                readResult,
                currentTemplateDeckDoc,
                sourceSlideImages,
                generatedSlideImages,
                effectiveOptions,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(lastReview.TemplateDeckDoc))
            {
                currentTemplateDeckDoc = DeckSupport.NormalizeLineEndings(lastReview.TemplateDeckDoc);
            }

            if (lastReview.IsSimilarEnough)
            {
                return new PowerPointDeckTemplateGenerationResult(
                    true,
                    readResult.PresentationReference,
                    generatedPresentationReference,
                    currentTemplateDeckDoc,
                    iteration,
                    true,
                    lastReview.Summary,
                    lastReview.Issues,
                    sourceSlideImages,
                    generatedSlideImages);
            }
        }

        return new PowerPointDeckTemplateGenerationResult(
            false,
            readResult.PresentationReference,
            generatedPresentationReference,
            currentTemplateDeckDoc,
            maxCorrectionRounds,
            false,
            lastReview?.Summary ?? templateDraft.Summary,
            lastReview?.Issues ?? [],
            sourceSlideImages,
            generatedSlideImages,
            "The generated preview was still not similar enough after the configured correction rounds.");
    }

    private static PowerPointDeckToolSetOptions CreateFullReadOptions(PowerPointDeckToolSetOptions? options)
    {
        var effectiveOptions = options ?? new PowerPointDeckToolSetOptions();
        return new PowerPointDeckToolSetOptions
        {
            WorkingDirectory = effectiveOptions.WorkingDirectory,
            ReferenceResolver = effectiveOptions.ReferenceResolver,
            MaxReadLines = effectiveOptions.MaxReadLines,
            MaxReadSlides = Math.Max(effectiveOptions.MaxReadSlides, 10_000),
            MaxEditFileBytes = effectiveOptions.MaxEditFileBytes,
            MaxSearchResults = effectiveOptions.MaxSearchResults,
            EnableLocalFileSupport = effectiveOptions.EnableLocalFileSupport,
            PreferEmbeddedDeckDoc = effectiveOptions.PreferEmbeddedDeckDoc,
            EnableBestEffortImport = effectiveOptions.EnableBestEffortImport,
            M365 = effectiveOptions.M365,
            AssetInterceptor = effectiveOptions.AssetInterceptor,
            AssetSessionId = effectiveOptions.AssetSessionId,
            TemplateStore = effectiveOptions.TemplateStore,
            LoggerFactory = effectiveOptions.LoggerFactory,
            LogContentParameters = effectiveOptions.LogContentParameters,
            AdditionalHandlers = effectiveOptions.AdditionalHandlers,
            AdditionalPromptProviders = effectiveOptions.AdditionalPromptProviders,
        };
    }

    private static async Task<DeckSpecificationLookupToolResult?> LookupTemplateSpecificationGuidanceAsync(
        string query,
        PowerPointDeckToolSetOptions toolOptions,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var function = PowerPointDeckTools.CreateSpecificationLookupFunction(toolOptions);
        var arguments = new AIFunctionArguments { Services = serviceProvider };
        arguments["query"] = query;

        var result = await InvokeFunctionAsync<DeckSpecificationLookupToolResult>(function, arguments, cancellationToken).ConfigureAwait(false);
        return result.Success ? result : null;
    }

    private static async Task<TemplateDraftResponse> RequestTemplateDraftAsync(
        IChatClient chatClient,
        PowerPointDeckReadResult readResult,
        DeckSpecificationLookupToolResult? specificationLookup,
        PowerPointDeckTemplateGenerationOptions options,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(
                ChatRole.User,
                $$"""
                Convert the following PowerPoint deck into a reusable DeckDoc template.

                Requirements:
                - Preserve the visual layout, slide sequencing, theme, backgrounds, static design elements, and reusable structure from the source deck.
                - Replace one-off business copy with reusable placeholder copy while keeping the slide intent obvious.
                - Keep reusable titles, sections, slots, fixed objects, notes, transitions, tables, charts, groups, and animations when they are part of the design.
                - Prefer canonical DeckDoc that can be written back to PowerPoint without further manual fixes.
                - Do not explain the syntax in the returned DeckDoc.

                Source presentation: {{readResult.PresentationReference}}
                Total slides: {{readResult.TotalSlideCount}}

                Source DeckDoc:
                {{readResult.DeckDoc}}

                {{FormatSpecificationGuidance(specificationLookup)}}

                {{FormatAdditionalInstructions(options.AdditionalInstructions)}}
                """
            ),
        };

        return await RequestStructuredResponseAsync<TemplateDraftResponse>(
            chatClient,
            messages,
            "You create reusable DeckDoc templates from existing PowerPoint decks. Return JSON only.",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TemplateReviewResponse> RequestTemplateReviewAsync(
        IChatClient chatClient,
        PowerPointDeckReadResult readResult,
        string currentTemplateDeckDoc,
        PowerPointDeckSlideImageExportResult sourceSlideImages,
        PowerPointDeckSlideImageExportResult generatedSlideImages,
        PowerPointDeckTemplateGenerationOptions options,
        CancellationToken cancellationToken)
    {
        var comparisonContents = new List<AIContent>
        {
            new TextContent(
                $$"""
                Compare the original slide images with the generated template preview slide images.

                Decide whether the generated preview is visually similar enough to the source deck.
                If it is not similar enough, return a revised template DeckDoc that would bring the next preview closer.

                Requirements:
                - Focus on layout fidelity, spacing, background treatment, text block placement, image treatment, table and chart placement, and reusable visual motifs.
                - Preserve the reusable template structure instead of reintroducing one-off business data.
                - Return JSON only.

                Source presentation: {{readResult.PresentationReference}}
                Source slide count: {{sourceSlideImages.Slides.Length}}
                Generated preview: {{generatedSlideImages.PresentationReference}}
                Generated slide count: {{generatedSlideImages.Slides.Length}}

                Current template DeckDoc:
                {{currentTemplateDeckDoc}}

                Imported source DeckDoc:
                {{readResult.DeckDoc}}
                """),
        };

        AppendSlideComparisonContents(comparisonContents, sourceSlideImages, generatedSlideImages, options.AttachSlideImagesToPrompts, cancellationToken);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, comparisonContents),
        };

        return await RequestStructuredResponseAsync<TemplateReviewResponse>(
            chatClient,
            messages,
            "You review PowerPoint template previews against the original deck. Return JSON only.",
            cancellationToken).ConfigureAwait(false);
    }

    private static void AppendSlideComparisonContents(
        List<AIContent> contents,
        PowerPointDeckSlideImageExportResult sourceSlideImages,
        PowerPointDeckSlideImageExportResult generatedSlideImages,
        bool attachImages,
        CancellationToken cancellationToken)
    {
        var sourceBySlide = sourceSlideImages.Slides.ToDictionary(static slide => slide.SlideNumber);
        var generatedBySlide = generatedSlideImages.Slides.ToDictionary(static slide => slide.SlideNumber);
        var slideNumbers = sourceBySlide.Keys.Concat(generatedBySlide.Keys).Distinct().OrderBy(static slideNumber => slideNumber);

        foreach (var slideNumber in slideNumbers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sourceBySlide.TryGetValue(slideNumber, out var sourceSlide);
            generatedBySlide.TryGetValue(slideNumber, out var generatedSlide);

            contents.Add(new TextContent($"Original slide {slideNumber}."));
            if (sourceSlide is not null)
            {
                contents.Add(new TextContent($"Source image path: {sourceSlide.ImagePath}"));
                if (attachImages)
                {
                    contents.Add(CreateImageContent(sourceSlide.ImagePath));
                }
            }
            else
            {
                contents.Add(new TextContent("Source image is missing for this slide number."));
            }

            contents.Add(new TextContent($"Generated slide {slideNumber}."));
            if (generatedSlide is not null)
            {
                contents.Add(new TextContent($"Generated image path: {generatedSlide.ImagePath}"));
                if (attachImages)
                {
                    contents.Add(CreateImageContent(generatedSlide.ImagePath));
                }
            }
            else
            {
                contents.Add(new TextContent("Generated preview image is missing for this slide number."));
            }
        }
    }

    private static DataContent CreateImageContent(string imagePath)
    {
        var bytes = File.ReadAllBytes(imagePath);
        return new DataContent(bytes, "image/png")
        {
            Name = Path.GetFileName(imagePath),
        };
    }

    private static async Task<T> RequestStructuredResponseAsync<T>(
        IChatClient chatClient,
        IEnumerable<ChatMessage> messages,
        string instructions,
        CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(
            messages,
            new ChatOptions
            {
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(ToolJsonSerializerOptions.CreateWeb()),
            },
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("The chat client returned an empty response.");
        }

        return JsonSerializer.Deserialize<T>(response.Text, ToolJsonSerializerOptions.CreateWeb())
            ?? throw new InvalidOperationException($"The chat client returned invalid JSON for {typeof(T).Name}.");
    }

    private static string ExtractDeckDoc(string numberedContent)
    {
        if (string.IsNullOrWhiteSpace(numberedContent))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            DeckSupport.NormalizeLineEndings(numberedContent)
                .Split('\n')
                .Select(static line =>
                {
                    var tabIndex = line.IndexOf('\t', StringComparison.Ordinal);
                    return tabIndex >= 0 ? line[(tabIndex + 1)..] : line;
                }));
    }

    private static string ResolveGeneratedPresentationReference(string sourcePresentationReference, string? generatedPresentationReference, string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(generatedPresentationReference))
        {
            var defaultWorkingDirectory = PowerPointDeckToolSupport.NormalizeDirectory(workingDirectory);
            return Path.IsPathRooted(generatedPresentationReference)
                ? Path.GetFullPath(generatedPresentationReference)
                : Path.GetFullPath(Path.Combine(defaultWorkingDirectory, generatedPresentationReference));
        }

        var defaultDirectory = PowerPointDeckToolSupport.NormalizeDirectory(workingDirectory);
        var sourceFileName = Path.GetFileNameWithoutExtension(sourcePresentationReference);
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            sourceFileName = "presentation";
        }

        return Path.Combine(defaultDirectory, sourceFileName + "-template-preview.pptx");
    }

    private static string FormatSpecificationGuidance(DeckSpecificationLookupToolResult? result)
    {
        if (result is null || result.Matches.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Relevant DeckDoc guidance:");
        foreach (var match in result.Matches.Take(8))
        {
            builder.Append("- ");
            builder.Append(match.SectionId);
            builder.Append(": ");
            builder.AppendLine(match.Title);
            foreach (var line in match.Content.Take(4))
            {
                builder.Append("  ");
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatAdditionalInstructions(string? additionalInstructions) =>
        string.IsNullOrWhiteSpace(additionalInstructions)
            ? string.Empty
            : $"Additional instructions:\n{additionalInstructions.Trim()}";

    private static async Task<T> InvokeFunctionAsync<T>(
        AIFunction function,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var invocationResult = await function.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        return invocationResult switch
        {
            JsonElement json => json.Deserialize<T>(ToolJsonSerializerOptions.CreateWeb())
                ?? throw new InvalidOperationException($"Unable to deserialize {function.Name} result."),
            T typed => typed,
            _ => throw new InvalidOperationException($"Unexpected result type '{invocationResult?.GetType().FullName ?? "null"}' for {function.Name}."),
        };
    }

    /// <summary>
    /// Represents the initial template draft returned by the chat client.
    /// </summary>
    internal sealed record TemplateDraftResponse(
        string TemplateDeckDoc,
        string Summary);

    /// <summary>
    /// Represents one template review iteration returned by the chat client.
    /// </summary>
    internal sealed record TemplateReviewResponse(
        bool IsSimilarEnough,
        string TemplateDeckDoc,
        string Summary,
        string[] Issues);
}