using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.AI;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace AIToolkit.Tools.Deck.PowerPoint.Tests;

[TestClass]
public class PowerPointDeckToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "deck_asset_create",
        "deck_asset_search",
        "deck_edit_file",
        "deck_export_slide_images",
        "deck_grep_search",
        "deck_read_file",
        "deck_spec_lookup",
        "deck_template_get",
        "deck_template_list",
        "deck_write_file",
    ];

    private static readonly string[] ExpectedRepeatedSlotTexts =
    [
        "First item",
        "Second item",
        "Third item",
    ];

    private static readonly string[] ExpectedRepeatedSlotTargets =
    [
        "title",
        "sidebar[1]",
        "sidebar[2]",
        "sidebar[3]",
    ];

    private static readonly string[] ExpectedQuarterCategories = ["Q1", "Q2", "Q3"];
    private static readonly string[] ExpectedQuarterValues = ["450", "520", "610"];
    private static readonly string[] ExpectedQuarterCategoriesFour = ["Q1", "Q2", "Q3", "Q4"];
    private static readonly string[] ExpectedActualsValues = ["12", "15", "18", "22"];
    private static readonly string[] ExpectedTargetValues = ["12", "14", "16", "20"];

    [TestMethod]
    public void CreateFunctionsIncludesAssetAndTemplateToolsByDefault()
    {
        var functions = FunctionTestUtilities.CreateFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void BuiltInTemplatesParseWithDeckSyntaxParser()
    {
        var templates = PowerPointDeckTemplates.CreateDefaultTemplates();

        Assert.IsTrue(templates.Count > 0);
        foreach (var template in templates)
        {
            var parsed = DeckDocParser.Parse(template.DeckDoc);
            Assert.IsNotNull(parsed, template.Name);
            Assert.IsTrue(parsed.Slides.Count > 0, template.Name);
        }
    }

    [TestMethod]
    public async Task WriteAndReadRoundTripsEmbeddedDeckDoc()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "roundtrip.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = "= Sample Review\n\n== Cover\n@title | Sample Review\n@body [text .body] | A concise intro\n\n== Metrics\n@title | Metrics\n@body [text .body] | Margin improved";

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        Assert.AreEqual(2, read.TotalSlideCount);
        Assert.AreEqual(deckDoc, FunctionTestUtilities.ReadDeckDocText(new ReadOnlyCollection<Microsoft.Extensions.AI.AIContent>([
            new Microsoft.Extensions.AI.TextContent(read.Content),
        ])));

        using var presentation = PresentationDocument.Open(filePath, false);
        Assert.AreEqual(2, presentation.PresentationPart?.Presentation?.SlideIdList?.Count() ?? 0);
        Assert.AreEqual(deckDoc, PowerPointDeckDocPayload.TryRead(presentation.PresentationPart));
    }

    [TestMethod]
    public async Task ReadFileImportsExternalPresentationWithoutPayload()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "external.pptx");
        var sourceDeckDoc = "= External Deck\n\n== Cover\n@title | External Deck\n@body [text .body] | Intro copy\n\n== Detail\n@title | Detail\n@body [text .body] | Imported body line";
        var model = DeckDocParser.Parse(sourceDeckDoc);

        using (var presentation = PresentationDocument.Create(filePath, PresentationDocumentType.Presentation))
        {
            PowerPointDeckWriter.Write(presentation, model, new Dictionary<string, ResolvedDeckImage>());
        }

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        Assert.IsFalse(read.PreservesDeckDocRoundTrip);
        StringAssert.Contains(read.Message!, "best-effort");
        StringAssert.Contains(read.Content, "== External Deck");
        StringAssert.Contains(read.Content, "Imported body line");
    }

    [TestMethod]
    public async Task ConvertPresentationToDeckDocAsyncReturnsCanonicalDeckDoc()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "convert-read.pptx");
        var deckDoc = "= Conversion Demo\n\n== Cover\n@title | Conversion Demo\n@body [text .body] | Source body";

        var writeResult = await PowerPointDeckTemplateUtilities.ConvertDeckDocToPresentationAsync(
            filePath,
            deckDoc,
            new PowerPointDeckToolSetOptions
            {
                WorkingDirectory = workingDirectory,
            });

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var readResult = await PowerPointDeckTemplateUtilities.ConvertPresentationToDeckDocAsync(
            filePath,
            new PowerPointDeckToolSetOptions
            {
                WorkingDirectory = workingDirectory,
            });

        Assert.IsTrue(readResult.Success, readResult.Message);
        Assert.AreEqual(1, readResult.TotalSlideCount);
        Assert.AreEqual(deckDoc, readResult.DeckDoc);
    }

    [TestMethod]
    public async Task ExportSlidesToImagesAsyncRejectsPartialDimensionsBeforePowerPointAutomation()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "export-invalid-size.pptx");
        var deckDoc = "= Export Demo\n\n== Cover\n@title | Export Demo";

        var writeResult = await PowerPointDeckTemplateUtilities.ConvertDeckDocToPresentationAsync(
            filePath,
            deckDoc,
            new PowerPointDeckToolSetOptions
            {
                WorkingDirectory = workingDirectory,
            });

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var exportResult = await PowerPointDeckTemplateUtilities.ExportSlidesToImagesAsync(
            filePath,
            new PowerPointDeckSlideImageExportOptions
            {
                ToolOptions = new PowerPointDeckToolSetOptions
                {
                    WorkingDirectory = workingDirectory,
                },
                Width = 1600,
                Height = 0,
            });

        Assert.IsFalse(exportResult.Success);
        StringAssert.Contains(exportResult.Message!, "Width and Height must both be greater than zero");
    }

    [TestMethod]
    public async Task CreateTemplateAsyncUsesChatLoopAndWritesPreviewPresentation()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var sourcePath = Path.Combine(workingDirectory, "source-template.pptx");
        var sourceDeckDoc = "= Template Source\n\n== Cover\n@title | Template Source\n@body [text .body] | Specific source copy";

        var initialWrite = await PowerPointDeckTemplateUtilities.ConvertDeckDocToPresentationAsync(
            sourcePath,
            sourceDeckDoc,
            new PowerPointDeckToolSetOptions
            {
                WorkingDirectory = workingDirectory,
            });

        Assert.IsTrue(initialWrite.Success, initialWrite.Message);

        var generatedPath = Path.Combine(workingDirectory, "generated-template-preview.pptx");
        var chatClient = new SequentialChatClient(
            "{" +
            "\"templateDeckDoc\":\"= Reusable Template\\n\\n== Cover\\n@title | {Presentation Title}\\n@body [text .body] | {Subtitle or supporting copy}\"," +
            "\"summary\":\"Created a reusable one-slide template.\"}",
            "{" +
            "\"isSimilarEnough\":true," +
            "\"templateDeckDoc\":\"= Reusable Template\\n\\n== Cover\\n@title | {Presentation Title}\\n@body [text .body] | {Subtitle or supporting copy}\"," +
            "\"summary\":\"Preview matches closely enough for reuse.\"," +
            "\"issues\":[\"No further issues.\"]}");

        var result = await PowerPointDeckTemplateUtilities.CreateTemplateAsync(
            chatClient,
            sourcePath,
            new PowerPointDeckTemplateGenerationOptions
            {
                ToolOptions = new PowerPointDeckToolSetOptions
                {
                    WorkingDirectory = workingDirectory,
                },
                GeneratedPresentationReference = generatedPath,
                AttachSlideImagesToPrompts = false,
            },
            serviceProvider: null,
            exporter: new StubSlideImageExporter(),
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.SimilarEnough);
        Assert.AreEqual(generatedPath, result.GeneratedPresentationReference);
        Assert.IsTrue(File.Exists(generatedPath));
        StringAssert.Contains(result.TemplateDeckDoc!, "{Presentation Title}");
        Assert.AreEqual(1, result.IterationCount);
        Assert.AreEqual(2, chatClient.CallCount);
        Assert.IsTrue(result.SourceSlideImages?.Slides.Length > 0);
        Assert.IsTrue(result.GeneratedSlideImages?.Slides.Length > 0);
    }

    [TestMethod]
    public async Task ReadFileBestEffortImportsHiddenSlidesNotesTransitionsAndTables()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "external-rich.pptx");
        var sourceDeckDoc = """
            = External Rich Deck

            == Cover
            [notes "Lead with the table."]
            [transition fade dur=0.35s]
            @title | External Rich Deck
            @body [text .body] | Intro copy
            [table Risks at=B8 size=18x6 .body header banded]
            | Risk | Owner | Status |
            | --- | --- | --- |
            | Vendor delay | Ops | Open |
            [end]

            == Appendix
            [state hidden]
            [transition wipe dir=left dur=0.3s]
            @title | Appendix
            @body [text .body] | Hidden detail
            """;
        var model = DeckDocParser.Parse(sourceDeckDoc);

        using (var presentation = PresentationDocument.Create(filePath, PresentationDocumentType.Presentation))
        {
            PowerPointDeckWriter.Write(presentation, model, new Dictionary<string, ResolvedDeckImage>());
        }

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        Assert.IsFalse(read.PreservesDeckDocRoundTrip);
        StringAssert.Contains(read.Content, "[notes \"Lead with the table.\"]");
        StringAssert.Contains(read.Content, "[transition fade dur=0.35s]");
        StringAssert.Contains(read.Content, "[transition wipe dir=left dur=0.3s]");
        StringAssert.Contains(read.Content, "[state hidden]");
        StringAssert.Contains(read.Content, "[table Risks");
        StringAssert.Contains(read.Content, "| Vendor delay | Ops | Open |");
    }

    [TestMethod]
    public async Task WriteFileEmbedsResolvedImageAssets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "assets.pptx");
        var imagePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(imagePath, MinimalPngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
            functions,
            "deck_asset_create",
            FunctionTestUtilities.CreateArguments(new
            {
                source_reference = imagePath,
                asset_path = "hero/team.png",
                description = "Hero image",
            }));

        Assert.IsTrue(assetCreate.Success, assetCreate.Message);

        var deckDoc = "= Asset Deck\n[asset hero \"hero/team.png\"]\n\n== Cover\n@title | Asset Deck\n@hero [image asset=hero fit=cover alt=\"Hero\"]";
        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.AreEqual(1, slidePart!.Slide?.CommonSlideData?.ShapeTree?.Elements<Picture>().Count() ?? 0);

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileResolvesImageAssetsFromWorkingDirectoryWhenOutputIsOutsideWorkspace()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var outputDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(outputDirectory, "assets-outside-workspace.pptx");
        var imagePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(imagePath, MinimalPngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
            functions,
            "deck_asset_create",
            FunctionTestUtilities.CreateArguments(new
            {
                source_reference = imagePath,
                asset_path = "hero/team.png",
                description = "Hero image",
            }));

        Assert.IsTrue(assetCreate.Success, assetCreate.Message);

        var deckDoc = "= Asset Deck\n[asset hero \"hero/team.png\"]\n\n== Cover\n@title | Asset Deck\n@hero [image asset=hero fit=cover alt=\"Hero\"]";
        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.AreEqual(1, slidePart!.Slide?.CommonSlideData?.ShapeTree?.Elements<Picture>().Count() ?? 0);

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileAppliesImageFitModesAndCrop()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "image-fit.pptx");
        var imagePath = Path.Combine(workingDirectory, "wide.png");
        await File.WriteAllBytesAsync(imagePath, WidePngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
            functions,
            "deck_asset_create",
            FunctionTestUtilities.CreateArguments(new
            {
                source_reference = imagePath,
                asset_path = "images/wide.png",
                description = "Wide image",
            }));

        Assert.IsTrue(assetCreate.Success, assetCreate.Message);

        var deckDoc = "= Image Fit\n:deckdoc: 1\n:grid: 32x18\n\n[asset wide \"images/wide.png\"]\n\n== Fit\n@B2 6x6 [image asset=wide fit=contain alt=\"Contain\"]\n@J2 6x6 [image asset=wide fit=cover alt=\"Cover\"]\n@R2 6x6 [image asset=wide fit=stretch crop=10,0,10,0 alt=\"Crop\"]";
        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);

        var pictures = slidePart!.Slide?.CommonSlideData?.ShapeTree?.Elements<Picture>().ToArray();
        Assert.IsNotNull(pictures);
        Assert.AreEqual(3, pictures!.Length);

        var contain = pictures.Single(static picture => picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value == "Contain");
        var cover = pictures.Single(static picture => picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value == "Cover");
        var crop = pictures.Single(static picture => picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value == "Crop");

        var containExtents = contain.ShapeProperties?.Transform2D?.Extents;
        var coverExtents = cover.ShapeProperties?.Transform2D?.Extents;
        Assert.IsNotNull(containExtents);
        Assert.IsNotNull(coverExtents);
        Assert.IsTrue(containExtents!.Cy!.Value < coverExtents!.Cy!.Value, "contain should letterbox the non-square image inside the square target");

        var coverSourceRect = cover.BlipFill?.SourceRectangle;
        Assert.IsNotNull(coverSourceRect);
        Assert.IsTrue((coverSourceRect!.Top?.Value ?? 0) > 0 || (coverSourceRect.Bottom?.Value ?? 0) > 0 || (coverSourceRect.Left?.Value ?? 0) > 0 || (coverSourceRect.Right?.Value ?? 0) > 0, "cover should crop the source image to fill the target");

        var cropSourceRect = crop.BlipFill?.SourceRectangle;
        Assert.IsNotNull(cropSourceRect);
        Assert.AreEqual(10_000, cropSourceRect!.Left?.Value ?? 0);
        Assert.AreEqual(10_000, cropSourceRect.Right?.Value ?? 0);

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task TemplateListAndGetReturnBuiltInTemplates()
    {
        var functions = FunctionTestUtilities.CreateFunctions();

        var list = await FunctionTestUtilities.InvokeAsync<DeckTemplateListToolResult>(
            functions,
            "deck_template_list",
            FunctionTestUtilities.CreateArguments(new
            {
                query = "brief",
            }));

        Assert.IsTrue(list.Success, list.Message);
        Assert.IsTrue(list.Templates.Any(static template => template.Name == "signal-brief"));

        var template = await FunctionTestUtilities.InvokeAsync<DeckTemplateGetToolResult>(
            functions,
            "deck_template_get",
            FunctionTestUtilities.CreateArguments(new
            {
                name = "signal-brief",
            }));

        Assert.IsTrue(template.Success, template.Message);
        StringAssert.Contains(template.DeckDoc!, "== Cover");
        StringAssert.Contains(template.DeckDoc!, "[asset hero");
    }

    [TestMethod]
    public async Task WriteAndReadRoundTripsComprehensiveDeckDocSyntax()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "comprehensive.pptx");
        var imagePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(imagePath, MinimalPngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
            functions,
            "deck_asset_create",
            FunctionTestUtilities.CreateArguments(new
            {
                source_reference = imagePath,
                asset_path = "hero/cover.png",
                description = "Cover image",
            }));

        Assert.IsTrue(assetCreate.Success, assetCreate.Message);

        var deckDoc = """
            = Comprehensive Deck
            :deckdoc: 1
            :locale: en-US
            :size: wide
            :grid: 32x18

            [theme primary=#0B5FFF accent=#F97316 ink=#0F172A muted=#5B6472 surface=#F8FAFC display="Aptos Display" body="Aptos"]
            [style title font=$display size=28 bold fg=$ink]
            [style body font=$body size=16 fg=$ink align=left/top]
            [asset hero "hero/cover.png"]
            [motion fade-in enter=fade dur=0.35s]
            [layout cover]
            [grid 40x22]
            [background fill=$surface]
            [transition fade dur=0.25s]
            [area stage B2:Y14]
            [split stage cols=(7,17) gap=1 as=rail,body]
            [grid cards in=body cols=2 rows=1 gap=0.5]
            [stack chips in=rail dir=down count=2 gap=0.25]
            !A1 32x18 [shape rect fill=$surface layer=back]
            @title = B3 20x2 [.title]
            @subtitle = B6 18x1 [text .body]
            @hero = body [image fit=cover]
            [end]

            == Opening
            [use cover]
            [section Highlights]
            [notes "Lead with the headline."]
            [transition fade dur=0.35s]
            @title | Quarterly Operations
            @subtitle [text .body] | Portfolio and execution updates
            @hero [image asset=hero fit=cover alt="Hero image"]
            @cards[1] [shape roundrect fill=#FFFFFF stroke=#CBD5E1 radius=0.18in] | Revenue up
            @cards[2] [list .body bullet=disc] | Lower churn | Raise renewal quality
            [table Risks at=B8 size=18x6 .body header banded]
            | Risk | Owner | Status |
            | --- | --- | --- |
            | Vendor delay | Ops | Open |
            | Scope creep | Product | Mitigated |
            [end]
            [chart "Revenue Trend" type=combo at=P5 size=12x7]
            - series column "Revenue" cat=(Q1,Q2,Q3,Q4) val=(12,14,17,19) color=$primary
            - series line "Margin %" cat=(Q1,Q2,Q3,Q4) val=(0.22,0.24,0.27,0.29) axis=secondary color=$accent labels
            [end]
            [obj title link="https://example.com/qor"]
            [group opening title hero cards[1] cards[2]]
            [animate title enter=fade dur=0.35s order=1]
            [x powerpoint section-collapsed]

            == Appendix
            [section Appendix]
            [state hidden]
            [background fill=#F8FAFC]
            @B3 12x2 [text .subtitle] | "  Quoted appendix  "
            @B6 10x1 [line stroke=#CBD5E1 weight=1]
            @Q4 4x4 [icon asset=hero fit=contain fg=$primary alt="Hero icon"]
            @B9 10x3 [list .body bullet=dash] | Vendor delay | Scope creep
            @N9 8x3 [list .body bullet=check] | Security review complete | Demo approved
            @V9 8x3 [list .body bullet=number start=4] | Launch | Measure
            [animate Q4 emphasis=pulse dur=0.25s on=click]
            [x document direction=rtl]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        Assert.AreEqual(
            deckDoc.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'),
            FunctionTestUtilities.ReadDeckDocText(new ReadOnlyCollection<Microsoft.Extensions.AI.AIContent>([
                new Microsoft.Extensions.AI.TextContent(read.Content),
            ])));

        using var presentation = PresentationDocument.Open(filePath, false);
        var slideParts = presentation.PresentationPart?.SlideParts.ToArray();
        Assert.IsNotNull(slideParts);
        Assert.AreEqual(2, slideParts!.Length);

        var shapeTree = slideParts[0].Slide?.CommonSlideData?.ShapeTree;
        Assert.IsNotNull(shapeTree);
        Assert.IsTrue(shapeTree!.Descendants<Picture>().Any());
        Assert.IsTrue(shapeTree.Descendants<Shape>().Count() >= 3);
        Assert.IsTrue(shapeTree.Descendants<GraphicFrame>().Any());
        Assert.IsTrue(shapeTree.Elements<GroupShape>().Any());
        Assert.IsTrue(slideParts[0].ChartParts.Any());
        Assert.IsNotNull(slideParts[0].NotesSlidePart);
        StringAssert.Contains(slideParts[0].Slide?.OuterXml ?? string.Empty, "<p:transition", StringComparison.Ordinal);
        Assert.IsNotNull(slideParts[0].Slide?.GetFirstChild<Timing>());
        Assert.IsTrue(slideParts[0].Slide?.GetFirstChild<Timing>()?.Descendants<ShapeTarget>().Any() ?? false);
        Assert.IsNotNull(slideParts[1].Slide?.GetFirstChild<Timing>());
        Assert.AreEqual(false, slideParts[1].Slide?.Show?.Value ?? true);

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileRendersRepeatedSlotFamilyTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "slot-family.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Slot Family Deck

            [layout repeated-rail]
            [area stage B2:Y15]
            [split stage cols=(8,14) gap=1 as=rail,body]
            [stack items in=rail dir=down count=3 gap=0.5]
            @sidebar = items [text .body]
            @title = body [text .title]
            [end]

            == Coverage
            [use repeated-rail]
            @title | Slot family rendering
            @sidebar[1] | First item
            @sidebar[2] | Second item
            @sidebar[3] | Third item
            """;
        var parsed = DeckDocParser.Parse(deckDoc);
        var parsedTargets = parsed.Slides.Single().Objects.Select(static item => item.Placement.TargetIndex is null ? item.Placement.TargetName : $"{item.Placement.TargetName}[{item.Placement.TargetIndex}]").ToArray();
        CollectionAssert.IsSubsetOf(ExpectedRepeatedSlotTargets, parsedTargets, $"Parsed targets: {string.Join(" | ", parsedTargets)}");

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var shapeTexts = presentation.PresentationPart!
            .SlideParts
            .Single()
            .Slide
            ?.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
            .Select(static text => text.Text)
            .ToArray();

        CollectionAssert.IsSubsetOf(
            ExpectedRepeatedSlotTexts,
            shapeTexts!,
            $"Rendered texts: {string.Join(" | ", shapeTexts!)}");

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileEmitsAnimationTimingForTitleAndGeometryTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "animations-evidence.pptx");

        const string deckDoc = """
            = Animation Evidence
            [theme canvas=#FFFFFF ink=#0F172A primary=#2563EB accent=#F97316]

            == Opening
            [animate title enter=fade dur=0.35s order=1]

            == Appendix
            @Q4 4x2 [text] | Pulse target
            [animate Q4 emphasis=pulse dur=0.25s on=click]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slideParts = presentation.PresentationPart?.SlideParts.ToArray();
        Assert.IsNotNull(slideParts);
        Assert.AreEqual(2, slideParts!.Length);

        var openingTitleShapeId = slideParts[0]
            .Slide?
            .CommonSlideData?
            .ShapeTree?
            .Elements<Shape>()
            .First(shape => shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.StartsWith("Title ", StringComparison.Ordinal) ?? false)
            .NonVisualShapeProperties?
            .NonVisualDrawingProperties?
            .Id?
            .Value;
        Assert.IsNotNull(openingTitleShapeId);

        var openingTiming = slideParts[0].Slide?.GetFirstChild<Timing>();
        Assert.IsNotNull(openingTiming);
        Assert.IsTrue(openingTiming!.Descendants<ShapeTarget>().Any(target => string.Equals(target.ShapeId?.Value, openingTitleShapeId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
        Assert.IsTrue(openingTiming.Descendants<CommonTimeNode>().Any(node => node.PresetClass?.Value == TimeNodePresetClassValues.Entrance && node.PresetId?.Value == 10));

        var appendixIconShapeId = slideParts[1]
            .Slide?
            .CommonSlideData?
            .ShapeTree?
            .Elements<Shape>()
            .First(shape => string.Equals(shape.InnerText, "Pulse target", StringComparison.Ordinal))
            .NonVisualShapeProperties?
            .NonVisualDrawingProperties?
            .Id?
            .Value;
        Assert.IsNotNull(appendixIconShapeId);

        var appendixTiming = slideParts[1].Slide?.GetFirstChild<Timing>();
        Assert.IsNotNull(appendixTiming);
        Assert.IsTrue(appendixTiming!.Descendants<ShapeTarget>().Any(target => string.Equals(target.ShapeId?.Value, appendixIconShapeId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
        Assert.IsTrue(appendixTiming.Descendants<CommonTimeNode>().Any(node => node.PresetClass?.Value == TimeNodePresetClassValues.Emphasis && node.PresetId?.Value == 26 && node.NodeType?.Value == TimeNodeValues.ClickEffect));

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileGroupsNamedObjectsAndAnimatesTheGroup()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "group-animation-evidence.pptx");
        const string deckDoc = """
            = Group Animation Evidence

            == Grouped
            @box B4 8x4 [shape rect fill=#E2E8F0]
            @label B5 6x1 [text .body] | Group label
            [group callout box label]
            [animate callout enter=fade dur=0.4s]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);

        var groupShape = slidePart!.Slide?.CommonSlideData?.ShapeTree?.Elements<GroupShape>().SingleOrDefault();
        Assert.IsNotNull(groupShape);
        Assert.AreEqual("callout", groupShape!.NonVisualGroupShapeProperties?.NonVisualDrawingProperties?.Name?.Value);
        Assert.AreEqual(2, groupShape.ChildElements.OfType<OpenXmlElement>().Count(child => child is Shape or Picture or GraphicFrame));

        var groupShapeId = groupShape.NonVisualGroupShapeProperties?.NonVisualDrawingProperties?.Id?.Value;
        Assert.IsNotNull(groupShapeId);

        var timing = slidePart.Slide?.GetFirstChild<Timing>();
        Assert.IsNotNull(timing);
        Assert.IsTrue(timing!.Descendants<ShapeTarget>().Any(target => string.Equals(target.ShapeId?.Value, groupShapeId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));

        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        StringAssert.Contains(read.Content, "[group callout box label]");

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteAndReadFilePreservesExactNotesText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "notes-evidence.pptx");
        const string notesText = "Lead with the headline. Pause before the roadmap.";
        var deckDoc = $"""
            = Notes Evidence

            == Notes
            [notes "{notesText}"]
            @B4 10x2 [text] | Notes validation
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using (var presentation = PresentationDocument.Open(filePath, false))
        {
            var notesSlide = presentation.PresentationPart?.SlideParts.Single().NotesSlidePart?.NotesSlide;
            Assert.IsNotNull(notesSlide);
            StringAssert.Contains(notesSlide!.InnerText, notesText);

            var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
            Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
        }

        var read = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        Assert.IsTrue(read.Success, read.Message);
        StringAssert.Contains(read.Content, $"[notes \"{notesText}\"]");
    }

    [TestMethod]
    public async Task WriteFileAppliesTransitionDirectionDurationAndAdvance()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "transition-evidence.pptx");
        const string deckDoc = """
            = Transition Evidence

            == Timed Wipe
            [transition wipe dir=up dur=0.3s advance=after(4.5s)]
            @B4 10x2 [text] | Transition validation
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slideXml = presentation.PresentationPart?.SlideParts.Single().Slide?.OuterXml;
        Assert.IsNotNull(slideXml);
        StringAssert.Contains(slideXml, "p14:dur=\"300\"");
        StringAssert.Contains(slideXml, "advTm=\"4500\"");
        StringAssert.Contains(slideXml, "advClick=\"0\"");
        StringAssert.Contains(slideXml, "<p:wipe dir=\"u\"");

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileRendersRangeBasedChartWithQuotedCategories()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "range-chart.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Range Chart Deck
            :deckdoc: 1
            :locale: en-US
            :size: wide
            :grid: 32x18

            [theme primary=#0F172A accent=#0284C7 ink=#1E293B paper=#F8FAFC]

            == Proof Points
            @title B2 18x2 [text] | Performance Metrics
            [chart "Quarterly Trend" type=column at=W6:AM16]
            - series column "Revenue" cat=("Q1","Q2","Q3") val=(450, 520, 610) color=$accent
            [end]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        var chartPart = slidePart!.ChartParts.SingleOrDefault();
        Assert.IsNotNull(chartPart);

        var chartSpace = chartPart!.ChartSpace;
        Assert.IsNotNull(chartSpace);
        var barSeries = chartSpace!.Descendants<DocumentFormat.OpenXml.Drawing.Charts.BarChartSeries>().Single();
        var categoryPoints = barSeries.Descendants<DocumentFormat.OpenXml.Drawing.Charts.StringPoint>().ToArray();
        var valuePoints = barSeries.Descendants<DocumentFormat.OpenXml.Drawing.Charts.NumericPoint>().ToArray();
        CollectionAssert.AreEqual(ExpectedQuarterCategories, categoryPoints.Select(static point => point.NumericValue?.Text).ToArray());
        CollectionAssert.AreEqual(ExpectedQuarterValues, valuePoints.Select(static point => point.NumericValue?.Text).ToArray());

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileRendersTableAndChartBlocksBoundToNamedTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "target-bound-blocks.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Target Bound Blocks
            :deckdoc: 1
            :grid: 32x18

            [layout content]
            [grid 32x18]
            [split cols=(14,16) gap=2 as=table,chart]
            [end]

            == Summary
            [use content]
            [table TopRisks at=table]
            | Risk | Owner
            | Scope creep | PMO
            [end]
            [chart Momentum type=column at=chart]
            - series column "Velocity" cat=("Q1","Q2") val=(10,12) color=#0284C7
            [end]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        var slide = slidePart!.Slide;
        Assert.IsNotNull(slide);
        Assert.AreEqual(1, slidePart!.ChartParts.Count());
        Assert.AreEqual(1, slide!.Descendants<DocumentFormat.OpenXml.Drawing.Table>().Count());

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileAppliesReadableLightTitleColorsOnDarkLayouts()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "light-title-colors.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Light Title Colors
            :deckdoc: 1
            :grid: 40x22

            [theme dark=#0F172A light=#F8FAFC text-main=#1E293B]
            [style .h1-light size=44 bold fg=$light]
            [style .p-light size=16 fg=$light]

            [layout cover]
            [grid 40x22]
            !B2:Y22 [shape rect fill=$dark]
            @title = B8:T12 [text .h1-light]
            @subtitle = B14:T16 [text .p-light]
            [end]

            == Cover
            [use cover]
            @title | Driving strategic growth in the coming year.
            @subtitle | Two-Day Leadership Summit
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);
        var slide = slidePart.Slide;
        var shapes = slide.Descendants<Shape>().ToArray();

        static string? GetFirstRunColor(Shape shape) =>
            shape.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
                .Select(static properties => properties.GetFirstChild<DocumentFormat.OpenXml.Drawing.SolidFill>()?.GetFirstChild<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>()?.Val?.Value)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        var titleShape = shapes.Single(static shape => shape.InnerText.Contains("Driving strategic growth in the coming year.", StringComparison.Ordinal));
        var subtitleShape = shapes.Single(static shape => shape.InnerText.Contains("Two-Day Leadership Summit", StringComparison.Ordinal));

        Assert.AreEqual("F8FAFC", GetFirstRunColor(titleShape));
        Assert.AreEqual("F8FAFC", GetFirstRunColor(subtitleShape));
    }

    [TestMethod]
    public async Task WriteFileAppliesSharedTitleStyleForegroundOnDarkSplitLayouts()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "dark-split-title-style.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Dark Split Title Style
            :deckdoc: 1
            :grid: 32x18

            [theme bright=#FFFFFF dark=#020617 primary=#1E3A8A surface=#F8FAFC]
            [style .title size=44 bold fg=$bright]
            [style .body-light size=18 fg=$surface]

            [layout split-dark]
            [background fill=$dark]
            [split cols=(16,16) gap=0 as=left,right]
            !rightBg right [shape rect fill=$primary]
            [split left rows=(1,2,14) as=marT,head,body]
            [split head cols=(2,12,2) as=padL,text,padR]
            [split body cols=(2,12,2) as=padL,content,padR]
            @title = text [.title]
            @bodyLeft = content [.body-light]
            [end]

            == Agenda
            [use split-dark]
            @title | Two-Day Agenda
            @bodyLeft [list .body-light bullet=disc] | Day 1 | Day 2
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);
        var slide = slidePart.Slide;
        var titleShape = slide.Descendants<Shape>().Single(static shape => shape.InnerText.Contains("Two-Day Agenda", StringComparison.Ordinal));

        var firstRunColor = titleShape.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
            .Select(static properties => properties.GetFirstChild<DocumentFormat.OpenXml.Drawing.SolidFill>()?.GetFirstChild<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>()?.Val?.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        Assert.AreEqual("FFFFFF", firstRunColor);
    }

    [TestMethod]
    public async Task WriteFileEnablesShrinkToFitForLargeSplitDarkTitles()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "dark-split-title-autofit.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Dark Split Title Autofit
            :deckdoc: 1
            :grid: 32x18

            [theme bright=#FFFFFF dark=#020617 primary=#1E3A8A surface=#F8FAFC]
            [style .title size=44 bold fg=$bright]
            [style .body-light size=18 fg=$surface]

            [layout split-dark]
            [background fill=$dark]
            [split cols=(16,16) gap=0 as=left,right]
            !rightBg right [shape rect fill=$primary]
            [split left rows=(1,2,14) as=marT,head,body]
            [split head cols=(2,12,2) as=padL,text,padR]
            [split body cols=(2,12,2) as=padL,content,padR]
            @title = text [.title]
            @bodyLeft = content [.body-light]
            [end]

            == Agenda
            [use split-dark]
            @title | Two-Day Agenda
            @bodyLeft [list .body-light bullet=disc] | Day 1 | Day 2 | Day 3
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);

        var titleShape = slidePart.Slide.Descendants<Shape>()
            .Single(static shape => shape.InnerText.Contains("Two-Day Agenda", StringComparison.Ordinal));

        Assert.IsNotNull(titleShape.TextBody);
        Assert.IsNotNull(titleShape.TextBody!.BodyProperties);
        Assert.AreEqual(DocumentFormat.OpenXml.Drawing.TextWrappingValues.Square, titleShape.TextBody.BodyProperties.Wrap?.Value);

        var autoFit = titleShape.TextBody.BodyProperties.GetFirstChild<DocumentFormat.OpenXml.Drawing.NormalAutoFit>();
        Assert.IsNotNull(autoFit);
        Assert.AreEqual(70000, autoFit!.FontScale?.Value);
        Assert.AreEqual(20000, autoFit.LineSpaceReduction?.Value);
    }

    [TestMethod]
    public async Task WriteFileRendersHorizontalRuleLinesWithoutDiagonalExtents()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "rule-line-orientation.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Rule Line Orientation
            :deckdoc: 1
            :grid: 32x18

            == Slide
            !B4:AG4 [line stroke=#CBD5E1]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);

        var lineShape = slidePart.Slide!.Descendants<Shape>()
            .Single(static shape => shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.Contains("Line", StringComparison.OrdinalIgnoreCase) ?? false);

        var transform = lineShape.ShapeProperties?.Transform2D;
        Assert.IsNotNull(transform);
        Assert.AreEqual(0L, transform!.Extents?.Cy?.Value ?? -1L);
        Assert.IsTrue((transform.Extents?.Cx?.Value ?? 0L) > 0L);
    }

    [TestMethod]
    public async Task WriteFileStacksSharedTargetLayoutSlotsWithoutOverlap()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "shared-target-layout-slots.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Shared Target Layout Slots
            :deckdoc: 1
            :grid: 32x18

            [layout visual-split]
            [background fill=#F8FAFC]
            @title = C2 26x3 [.title]
            [split rows=(6,11) gap=1 as=head,content]
            [split content cols=(12,18) gap=2 as=left,right]
            @subtitle = left [.subtitle]
            @body = left [list .body bullet=disc]
            @visual = right
            [end]

            == Market Position
            [use visual-split]
            @title | Market Position Analysis
            @subtitle | Favorable winds, but increasing pressure.
            @body
            | Competitor consolidation is creating a gap.
            | We hold an edge in enterprise retention.
            | Margins are compressing in the mid-market.
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);
        var slide = slidePart.Slide;
        var shapes = slide.Descendants<Shape>().ToArray();
        var subtitleShape = shapes.Single(static shape => shape.InnerText.Contains("Favorable winds, but increasing pressure.", StringComparison.Ordinal));
        var bodyShape = shapes.Single(static shape => shape.InnerText.Contains("Competitor consolidation is creating a gap.", StringComparison.Ordinal));

        var subtitleTransform = subtitleShape.ShapeProperties?.Transform2D;
        var bodyTransform = bodyShape.ShapeProperties?.Transform2D;
        Assert.IsNotNull(subtitleTransform);
        Assert.IsNotNull(bodyTransform);
        Assert.IsNotNull(subtitleTransform!.Offset);
        Assert.IsNotNull(subtitleTransform.Extents);
        Assert.IsNotNull(bodyTransform!.Offset);

        var subtitleBottom = subtitleTransform.Offset!.Y!.Value + subtitleTransform.Extents!.Cy!.Value;
        var bodyTop = bodyTransform.Offset!.Y!.Value;
        Assert.IsTrue(subtitleBottom <= bodyTop, "subtitle and body should be stacked in separate rectangles when they share a source target");
    }

    [TestMethod]
    public async Task WriteFileAppliesObjectOverridePlacementToExistingNamedObject()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "object-override-placement.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Object Override Placement
            :deckdoc: 1
            :size: wide
            :grid: 32x18

            [layout scorecard]
            [background fill=#F8FAFC]
            @panel = B7:O16
            [end]

            == Metrics
            [use scorecard]
            @panel [shape roundrect fill=#FFFFFF stroke=#CBD5E1 radius=0.18in]
            @metric_label panel [text fg=#F8FAFC] | Net Revenue Retention
            [obj metric_label at=C10:K11 text fg=#0F172A]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);

        var labelShape = slidePart.Slide.Descendants<Shape>()
            .Single(static shape => shape.InnerText.Contains("Net Revenue Retention", StringComparison.Ordinal));

        var transform = labelShape.ShapeProperties?.Transform2D;
        Assert.IsNotNull(transform);
        Assert.IsNotNull(transform!.Offset);
        Assert.IsNotNull(transform.Extents);
        Assert.IsTrue(transform.Offset!.Y!.Value > 2_500_000L, "override should move the label below the top of the original panel target");
        Assert.IsTrue(transform.Extents!.Cy!.Value < 1_000_000L, "override should replace the full panel height with the explicit override rectangle");

        var firstRunColor = labelShape.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
            .Select(static properties => properties.GetFirstChild<DocumentFormat.OpenXml.Drawing.SolidFill>()?.GetFirstChild<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>()?.Val?.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        Assert.AreEqual("0F172A", firstRunColor);
    }

    [TestMethod]
    public async Task WriteFileKeepsInheritedSlotStyleWhenObjectOverrideRelocatesSlotBoundText()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "object-override-slot-style.pptx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var deckDoc = """
            = Object Override Slot Style
            :deckdoc: 1
            :size: wide
            :grid: 40x22

            [theme dark=#0F172A white=#FFFFFF accent=#0284C7]
            [style .head size=36 bold fg=$white]

            [layout cover]
            [background fill=$dark]
            [split cols=(16,24) gap=0 as=left,right]
            [split right rows=(7,8,7) as=top,middle,bottom]
            @title = middle [.head]
            [end]

            == Cover
            [use cover]
            @title | FY2026 Strategy Offsite
            [obj title at=U7:AL11]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slidePart = presentation.PresentationPart?.SlideParts.Single();
        Assert.IsNotNull(slidePart);
        Assert.IsNotNull(slidePart!.Slide);

        var titleShape = slidePart.Slide.Descendants<Shape>()
            .Single(static shape => shape.InnerText.Contains("FY2026 Strategy Offsite", StringComparison.Ordinal));

        var firstRunColor = titleShape.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
            .Select(static properties => properties.GetFirstChild<DocumentFormat.OpenXml.Drawing.SolidFill>()?.GetFirstChild<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>()?.Val?.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        Assert.AreEqual("FFFFFF", firstRunColor);
    }

    [TestMethod]
    public async Task WriteFileRendersChartForSampleStyleDeck()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "sample-style-deck.pptx");
        var imagePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(imagePath, MinimalPngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        foreach (var assetPath in new[] { "sample/hero.png", "sample/logo.png", "sample/badge.png" })
        {
            var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
                functions,
                "deck_asset_create",
                FunctionTestUtilities.CreateArguments(new
                {
                    source_reference = imagePath,
                    asset_path = assetPath,
                    description = assetPath,
                }));

            Assert.IsTrue(assetCreate.Success, assetCreate.Message);
        }

        var deckDoc = """
            = Strategy Offsite 2025
            :deckdoc: 1
            :locale: en-US
            :size: wide
            :grid: 32x18

            [theme primary=#0F172A accent=#0284C7 ink=#1E293B paper=#F8FAFC]
            [style .title font="Aptos Display" size=36 bold fg=$primary]
            [style .subtitle font="Aptos" size=20 fg=$accent]
            [style .body font="Aptos" size=16 fg=$ink line=1.2]
            [style .caption font="Aptos" size=11 italic fg=#64748B]

            [asset hero "sample/hero.png"]
            [asset logo "sample/logo.png"]
            [asset badge "sample/badge.png"]

            [layout cover]
            [grid 32x18]
            [background fill=$paper]
            [transition fade dur=0.5s]
            [area stage B3:AE15]
            [split stage cols=(14,16) gap=1 as=left,right]
            @logo = B17 3x1 [image fit=contain]
            @title = left [.title]
            @subtitle = left [.subtitle]
            @hero = right [image fit=cover radius=0.2]
            [end]

            [layout content]
            [grid 40x22]
            [area head B2:AM4]
            [area body B6:AM18]
            [split head cols=(34,4) as=title,badge]
            @title = title [.title size=28]
            @badge = badge [image fit=contain]
            @main = body [text .body]
            [end]

            == Cover
            [use cover]
            [notes "Welcome the team and set the tone for a high-energy two days."]
            @title | 2025 Strategy Planning
            @subtitle | Accelerating our lead in the next frontier.
            @hero [image asset=hero alt="Modern office space with collaborative atmosphere"]
            @logo [image asset=logo alt="Company corporate logo"]

            == Agenda
            [use content]
            [notes "Quickly walk through the flow of both days."]
            @title | The Roadmap
            @badge [image asset=badge alt="Official session badge"]
            @main [list .body bullet=disc] | Day 1: Market Realities & Competitive Gaps | Day 2: Strategic Pillars & Resourcing
            [stack agenda in=B10:T16 dir=down count=3 gap=0.5]
            @agenda[1] [shape rect fill=$accent opacity=0.1]
            @agenda[1] [text .body bold] | Morning: State of the Union
            @agenda[2] [shape rect fill=$accent opacity=0.1]
            @agenda[2] [text .body bold] | Afternoon: Deep Dive - Global Trends
            @agenda[3] [shape rect fill=$accent opacity=0.1]
            @agenda[3] [text .body bold] | Evening: Team Dinner

            == Proof Points
            [use content]
            [notes "Show the data that proves our current strategy is working."]
            @title | Performance Metrics
            @badge [image asset=badge]
            [table Metrics at=B6:U16 .body]
            | Segment | Growth | Retention |
            | --- | --- | --- |
            | Enterprise | +22% | 98% |
            | Mid-Market | +15% | 92% |
            | SMB | +8% | 85% |
            [end]
            [chart "Quarterly Trend" type=column at=W6:AM16]
            - series column "Revenue" cat=("Q1","Q2","Q3") val=(450, 520, 610) color=$accent
            [end]

            == Decision Framing
            [use content]
            [notes "This slide is for the interactive portion of the session."]
            @title | Key Strategic Decisions
            @badge [image asset=badge]
            @main [list .body bullet=check] | Confirm Phase 2 expansion budget | Approve leadership hire in EMEA | Sunset legacy support for v1.0
            [obj B20:AM21 rich="[b]Confidential:[/b] For internal leadership use only during the October offsite session."]

            == Appendix
            [use content]
            [state hidden]
            [notes "This is a hidden slide for reference if questions arise."]
            @title | Appendix: Data Sources
            @main [list .body bullet=dash] | H1 Internal Audit Report | Gartner 2024 Magic Quadrant | Customer Exit Interview Metadata
            @caption B20 24x2 [.caption] | Source: Strategy & Analytics Division
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slideParts = presentation.PresentationPart?.SlideParts.ToArray();
        Assert.IsNotNull(slideParts);
        Assert.AreEqual(5, slideParts!.Length);
        Assert.IsTrue(slideParts[2].ChartParts.Any());
        var chartPart = slideParts[2].ChartParts.Single();
        var chartSeries = chartPart.ChartSpace?.Descendants<DocumentFormat.OpenXml.Drawing.Charts.BarChartSeries>().SingleOrDefault();
        Assert.IsNotNull(chartSeries);
        CollectionAssert.AreEqual(
            ExpectedQuarterCategories,
            chartSeries!.Descendants<DocumentFormat.OpenXml.Drawing.Charts.StringPoint>().Select(static point => point.NumericValue?.Text).ToArray());
        CollectionAssert.AreEqual(
            ExpectedQuarterValues,
            chartSeries.Descendants<DocumentFormat.OpenXml.Drawing.Charts.NumericPoint>().Select(static point => point.NumericValue?.Text).ToArray());

        var proofPointsTable = slideParts[2].Slide?.CommonSlideData?.ShapeTree?.Descendants<DocumentFormat.OpenXml.Drawing.Table>().SingleOrDefault();
        Assert.IsNotNull(proofPointsTable);
        Assert.AreEqual(4, proofPointsTable!.Elements<DocumentFormat.OpenXml.Drawing.TableRow>().Count());
        Assert.AreEqual(3, proofPointsTable.Elements<DocumentFormat.OpenXml.Drawing.TableGrid>().Single().Elements<DocumentFormat.OpenXml.Drawing.GridColumn>().Count());

        var tableRows = proofPointsTable.Elements<DocumentFormat.OpenXml.Drawing.TableRow>().ToArray();
        var headerRunProperties = tableRows[0]
            .Elements<DocumentFormat.OpenXml.Drawing.TableCell>()
            .First()
            .TextBody?
            .Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
            .FirstOrDefault();
        var bodyRunProperties = tableRows[1]
            .Elements<DocumentFormat.OpenXml.Drawing.TableCell>()
            .First()
            .TextBody?
            .Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>()
            .FirstOrDefault();
        Assert.IsNotNull(headerRunProperties);
        Assert.IsNotNull(bodyRunProperties);
        Assert.IsTrue(headerRunProperties!.Bold?.Value ?? false);
        Assert.IsTrue((headerRunProperties.FontSize?.Value ?? 0) >= (bodyRunProperties!.FontSize?.Value ?? 0));
        Assert.IsNotNull(tableRows[0].Elements<DocumentFormat.OpenXml.Drawing.TableCell>().First().TableCellProperties?.GetFirstChild<DocumentFormat.OpenXml.Drawing.SolidFill>());

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    [TestMethod]
    public async Task WriteFileRendersLoggedStrategyDeckWithoutChartCorruption()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "strategy-offsite.pptx");
        var imagePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(imagePath, MinimalPngBytes());

        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        foreach (var assetPath in new[] { "sample/hero.png", "sample/logo.png", "sample/badge.png" })
        {
            var assetCreate = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
                functions,
                "deck_asset_create",
                FunctionTestUtilities.CreateArguments(new
                {
                    source_reference = imagePath,
                    asset_path = assetPath,
                    description = assetPath,
                }));

            Assert.IsTrue(assetCreate.Success, assetCreate.Message);
        }

        var deckDoc = """
            = Q3 Leadership Strategy Offsite
            :deckdoc: 1
            :grid: 40x22

            [theme surface=#FFFFFF primary=#0F172A accent=#3B82F6 secondary=#E2E8F0 text=#1E293B text-light=#64748B]

            [style .title size=28 bold fg=$primary]
            [style .subtitle size=18 fg=$text-light]
            [style .body size=16 fg=$text]
            [style .caption size=12 fg=$text-light]
            [style .callout-text size=16 bold fg=$primary]

            [asset hero "sample/hero.png"]
            [asset logo "sample/logo.png"]
            [asset badge "sample/badge.png"]

            [layout cover]
            [background fill=$surface]
            !B2 4x2 [image asset=logo fit=contain]
            !AH2 2x2 [image asset=badge fit=contain]
            !B20 36x1 [shape rect fill=$accent]
            @title = B6 20x4 [.title size=36]
            @subtitle = B10 20x2 [.subtitle]
            @hero = X6 14x12 [image fit=cover]
            [end]

            [layout standard]
            [background fill=$surface]
            !B2 4x2 [image asset=logo fit=contain]
            !B4 36x1 [shape rect fill=$secondary]
            !B20 36x1 [shape rect fill=$accent]
            @title = G2 31x2 [.title]
            @body = B6 36x13 [.body]
            [end]

            == Q3 Strategy & Alignment Offsite
            [use cover]
            [transition fade dur=0.5s]
            @title [text] | Leading the Next Phase of Growth
            @subtitle [text] | Executive Leadership Team | Offsite 2024
            @hero [image asset=hero]
            [notes "Welcome everyone. Today we align on our key priorities and operating model for the upcoming fiscal cycle."]

            == Agenda & Roadmap
            [use standard]
            [transition push dir=left dur=0.5s]
            @title [text] | Roadmap to Execution
            @body [list bullet=number]
            | Opening and YTD Review
            | Core Proof Points & Metrics
            | Executive Lunch
            | Decision Framing: Strategic Choices
            | Operating Model Alignment
            | Wrap-up and Next Steps
            [animate body enter=fade delay=0.2s order=1]
            [notes "We have a packed day. The focus is making concrete decisions, not just reviewing data."]

            == Operating Results & Proof Points
            [use standard]
            [transition push dir=left dur=0.5s]
            @title [text] | Core Metrics and Operating Results

            @left_lst B6 16x5 [list bullet=disc .body]
            | Growth accelerated in Q2
            | Margin expansion on track
            | Retention remains high
            - Net dollar retention > 110%

            @right_lst T6 18x5 [list bullet=check .body]
            | Market share increased
            | Product milestones delivered
            | Key talent retained

            [table Financials at=B12 size=16x6 .body banded]
            | Metric | Target | Actual |
            | Revenue | $45M | $48M |
            | Margin | 62% | 64% |
            [end]

            [chart "Growth" type=column at=T12 size=16x7]
            - series column "Actuals" cat=("Q1", "Q2", "Q3", "Q4") val=(12, 15, 18, 22)
            - series column "Target" cat=("Q1", "Q2", "Q3", "Q4") val=(12, 14, 16, 20)
            [end]

            [notes "The table highlights financial outperformance, and the chart shows our growth trajectory pulling ahead of target."]
            """;

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(write.Success, write.Message);

        using var presentation = PresentationDocument.Open(filePath, false);
        var slideParts = presentation.PresentationPart?.SlideParts.ToArray();
        Assert.IsNotNull(slideParts);
        var proofSlide = slideParts![2];
        var chartPart = proofSlide.ChartParts.Single();
        var series = chartPart.ChartSpace?.Descendants<DocumentFormat.OpenXml.Drawing.Charts.BarChartSeries>().ToArray();
        Assert.IsNotNull(series);
        Assert.AreEqual(2, series!.Length);
        CollectionAssert.AreEqual(
            ExpectedQuarterCategoriesFour,
            series[0].Descendants<DocumentFormat.OpenXml.Drawing.Charts.StringPoint>().Select(static point => point.NumericValue?.Text).ToArray());
        CollectionAssert.AreEqual(
            ExpectedActualsValues,
            series[0].Descendants<DocumentFormat.OpenXml.Drawing.Charts.NumericPoint>().Select(static point => point.NumericValue?.Text).ToArray());
        CollectionAssert.AreEqual(
            ExpectedTargetValues,
            series[1].Descendants<DocumentFormat.OpenXml.Drawing.Charts.NumericPoint>().Select(static point => point.NumericValue?.Text).ToArray());

        var table = proofSlide.Slide?.CommonSlideData?.ShapeTree?.Descendants<DocumentFormat.OpenXml.Drawing.Table>().SingleOrDefault();
        Assert.IsNotNull(table);
        var rows = table!.Elements<DocumentFormat.OpenXml.Drawing.TableRow>().ToArray();
        var headerRunProperties = rows[0].Elements<DocumentFormat.OpenXml.Drawing.TableCell>().First().TextBody?.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>().FirstOrDefault();
        var bodyRunProperties = rows[1].Elements<DocumentFormat.OpenXml.Drawing.TableCell>().First().TextBody?.Descendants<DocumentFormat.OpenXml.Drawing.RunProperties>().FirstOrDefault();
        Assert.IsTrue(headerRunProperties?.Bold?.Value ?? false);
        Assert.IsTrue((headerRunProperties?.FontSize?.Value ?? 0) >= (bodyRunProperties?.FontSize?.Value ?? 0));

        var validationErrors = new OpenXmlValidator().Validate(presentation).ToArray();
        Assert.AreEqual(0, validationErrors.Length, string.Join(Environment.NewLine, validationErrors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private static byte[] WidePngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAMgAAABkCAYAAADDhn8LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAEdSURBVHhe7dMxDQAhAMBA1CHsNeEPxk8IqYIbbuneMb+1gbdxB+BnEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgWAQCAaBYBAIBoFgEAgGgXAA2q3UsM16uiAAAAAASUVORK5CYII=");
}

internal sealed class SequentialChatClient(params string[] responses) : IChatClient
{
    private readonly Queue<string> _responses = new(responses);

    public int CallCount { get; private set; }

    public ChatClientMetadata Metadata { get; } = new("test", new Uri("https://example.test"), "test-model");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No chat responses remain.");
        }

        var responseText = _responses.Dequeue();
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)])
        {
            FinishReason = ChatFinishReason.Stop,
            ModelId = "test-model",
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(response.Text)]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

internal sealed class StubSlideImageExporter : IPowerPointSlideImageExporter
{
    public async Task<PowerPointDeckSlideImage[]> ExportAsync(
        string inputPath,
        string outputDirectory,
        int width,
        int height,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);
        var targetPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + "-001.png");
        if (File.Exists(targetPath) && !force)
        {
            throw new InvalidOperationException("Export target already exists.");
        }

        await File.WriteAllBytesAsync(targetPath, MinimalPngBytes(), cancellationToken);
        return [new PowerPointDeckSlideImage(1, targetPath)];
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xBC, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
