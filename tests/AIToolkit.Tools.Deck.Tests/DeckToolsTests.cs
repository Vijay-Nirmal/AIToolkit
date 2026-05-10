using Microsoft.Extensions.Logging;
using System.Text;

namespace AIToolkit.Tools.Deck.Tests;

[TestClass]
public class DeckToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "deck_asset_create",
        "deck_asset_search",
        "deck_edit_file",
        "deck_grep_search",
        "deck_read_file",
        "deck_spec_lookup",
        "deck_write_file",
    ];

    private static readonly string[] GuideDeckReferences = ["deck:guide"];
    private static readonly string[] ExactSpecificationLookupIds = ["table-block", "slide-transition"];
    private static readonly string[] SyntaxClarificationSectionIds = ["document-attributes", "shared-theme", "shared-style", "shared-motion", "slide-background", "slide-notes", "slide-state", "layout-block-background", "layout-block-grid", "addressing-geometry", "layout-slot-lines", "object-lines-target", "object-lines-geometry", "object-attrlists", "payload-forms", "pattern-icon", "pattern-shape", "pattern-line", "pattern-numbered-list", "table-block", "chart-block", "grouping", "explicit-object-overrides", "animate"];
    private static readonly string[] RelatedSectionsLookupIds = ["shared-motion", "object-lines-target", "layout-slot-lines", "object-lines-geometry", "object-attrlists", "grouping", "chart-block", "animate"];

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        CollectionAssert.AreEquivalent(ExpectedToolNames, FunctionTestUtilities.GetNames(functions).ToArray());
    }

    [TestMethod]
    public async Task ReadFileReturnsSlideRangeAndSlideSummaries()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "notes.stubdeck");
        await File.WriteAllTextAsync(filePath, "= Sample\n[asset hero \"./hero.png\"]\n\n== Cover\n@title | Welcome\n\n== Agenda\n@body [text .body] | Item one");

        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
                MaxReadSlides = 5,
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                slide_offset = 2,
                slide_limit = 1,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.TotalSlideCount);
        Assert.AreEqual(2, result.ReturnedSlideOffset);
        Assert.AreEqual(1, result.ReturnedSlideCount);
        Assert.AreEqual(1, result.Slides.Length);
        Assert.AreEqual("Agenda", result.Slides[0].Title);
        StringAssert.Contains(result.Content, "== Agenda");
    }

    [TestMethod]
    public async Task EditFileSupportsSlideOperations()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "slides.stubdeck");
        await File.WriteAllTextAsync(filePath, "= Sample\n\n== Cover\n@title | Welcome\n\n== Close\n@body [text .body] | Thanks");

        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        _ = await FunctionTestUtilities.InvokeAsync<DeckReadFileToolResult>(
            functions,
            "deck_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
            }));

        var edit = await FunctionTestUtilities.InvokeAsync<DeckEditFileToolResult>(
            functions,
            "deck_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                slide_operations = new[]
                {
                    new DeckSlideEditOperation("insert_after", 1, "== Metrics\n@title | Metrics\n@body [text .body] | Margin improved"),
                },
            }));

        Assert.IsTrue(edit.Success, edit.Message);
        Assert.AreEqual(1, edit.ChangesApplied);
        StringAssert.Contains(edit.UpdatedDeckDoc!, "== Metrics");
    }

    [TestMethod]
    public async Task WriteFileReportsParseErrorsWithGuidance()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid.stubdeck");
        var logs = new List<string>();
        using var loggerFactory = new ListLoggerFactory(logs);
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
                LoggerFactory = loggerFactory,
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = "= Missing Slides",
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "DeckDoc syntax error");
        StringAssert.Contains(result.Message, "Slide Heading");
        Assert.IsTrue(logs.Any(static entry => entry.Contains("deck_write_file", StringComparison.Ordinal)));
        Assert.IsTrue(logs.Any(static entry => entry.Contains("DeckDoc syntax error", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task EditFileLogsFailureDetails()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "existing.stubdeck");
        await File.WriteAllTextAsync(filePath, "= Sample\n\n== Cover\n@title | Welcome");

        var logs = new List<string>();
        using var loggerFactory = new ListLoggerFactory(logs);
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
                LoggerFactory = loggerFactory,
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckEditFileToolResult>(
            functions,
            "deck_edit_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                old_string = "Welcome",
                new_string = "Updated",
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "File has not been read yet");
        Assert.IsTrue(logs.Any(static entry => entry.Contains("deck_edit_file", StringComparison.Ordinal)));
        Assert.IsTrue(logs.Any(static entry => entry.Contains("File has not been read yet", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GrepSearchOnExplicitReferencesReturnsSlideMetadata()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var store = new InMemoryDeckStore();
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                ReferenceResolver = new StubDeckReferenceResolver(store),
                Handlers = [new StubDeckHandler()],
            });

        var write = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = "deck:guide",
                content = "= Guide\n\n== Cover\n@title | Alpha\n\n== Metrics\n@body [text .body] | beta",
            }));

        Assert.IsTrue(write.Success, write.Message);

        var result = await FunctionTestUtilities.InvokeAsync<DeckGrepSearchToolResult>(
            functions,
            "deck_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "beta",
                deck_references = GuideDeckReferences,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual(2, result.Matches[0].SlideNumber);
        Assert.AreEqual("Metrics", result.Matches[0].SlideTitle);
    }

    [TestMethod]
    public async Task AssetCreateAndSearchUseDefaultLocalInterceptor()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var sourcePath = Path.Combine(workingDirectory, "hero.png");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var created = await FunctionTestUtilities.InvokeAsync<DeckAssetCreateToolResult>(
            functions,
            "deck_asset_create",
            FunctionTestUtilities.CreateArguments(new
            {
                source_reference = sourcePath,
                asset_path = "hero/team.png",
                description = "Hero image",
            }));

        Assert.IsTrue(created.Success, created.Message);
        Assert.IsNotNull(created.Asset);

        var search = await FunctionTestUtilities.InvokeAsync<DeckAssetSearchToolResult>(
            functions,
            "deck_asset_search",
            FunctionTestUtilities.CreateArguments(new
            {
                query = "hero",
            }));

        Assert.IsTrue(search.Success, search.Message);
        Assert.AreEqual(1, search.Assets.Length);
        Assert.AreEqual("hero/team.png", search.Assets[0].AssetPath);
    }

    [TestMethod]
    public async Task SpecificationLookupFindsTransitionGuidance()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckSpecificationLookupToolResult>(
            functions,
            "deck_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                keywords = "transition fade",
                maxResults = 3,
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Matches.Length >= 1);
        var transitionMatch = result.Matches.Single(static match => match.SectionId == "slide-transition");
        CollectionAssert.Contains(transitionMatch.CommonlyUsedWith, "animate");
    }

    [TestMethod]
    public async Task SpecificationLookupCommonlyUsedWithReferencesOnlyKnownSections()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        var allSectionIds = DeckSpecificationCatalog.GetSectionIds().ToArray();
        var result = await FunctionTestUtilities.InvokeAsync<DeckSpecificationLookupToolResult>(
            functions,
            "deck_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                section_ids = allSectionIds,
                maxResults = 1,
            }));

        Assert.IsTrue(result.Success, result.Message);
        var knownIds = allSectionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var match in result.Matches)
        {
            foreach (var relatedSectionId in match.CommonlyUsedWith)
            {
                Assert.IsTrue(
                    knownIds.Contains(relatedSectionId),
                    $"Section '{match.SectionId}' references unknown commonlyUsedWith section '{relatedSectionId}'.");
            }
        }
    }

    [TestMethod]
    public async Task SpecificationLookupExposesUpdatedCommonlyUsedWithRelationships()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckSpecificationLookupToolResult>(
            functions,
            "deck_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                section_ids = RelatedSectionsLookupIds,
                maxResults = 1,
            }));

        Assert.IsTrue(result.Success, result.Message);
        var sections = result.Matches.ToDictionary(static match => match.SectionId, StringComparer.OrdinalIgnoreCase);

        CollectionAssert.Contains(sections["shared-motion"].CommonlyUsedWith, "object-lines-target");
        CollectionAssert.Contains(sections["object-lines-target"].CommonlyUsedWith, "grouping");
        CollectionAssert.Contains(sections["object-lines-target"].CommonlyUsedWith, "animate");
        CollectionAssert.Contains(sections["layout-slot-lines"].CommonlyUsedWith, "slide-use");
        CollectionAssert.Contains(sections["layout-slot-lines"].CommonlyUsedWith, "layout-grid");
        CollectionAssert.Contains(sections["layout-slot-lines"].CommonlyUsedWith, "layout-stack");
        CollectionAssert.Contains(sections["object-lines-geometry"].CommonlyUsedWith, "grouping");
        CollectionAssert.Contains(sections["object-lines-geometry"].CommonlyUsedWith, "animate");
        CollectionAssert.Contains(sections["object-attrlists"].CommonlyUsedWith, "grouping");
        CollectionAssert.Contains(sections["grouping"].CommonlyUsedWith, "object-lines-target");
        CollectionAssert.Contains(sections["chart-block"].CommonlyUsedWith, "quoted-strings");
        CollectionAssert.Contains(sections["chart-block"].CommonlyUsedWith, "layout-split");
        CollectionAssert.Contains(sections["animate"].CommonlyUsedWith, "object-lines-target");
        CollectionAssert.Contains(sections["animate"].CommonlyUsedWith, "object-lines-geometry");
    }

    [TestMethod]
    public async Task SpecificationLookupAcceptsSectionIds()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckSpecificationLookupToolResult>(
            functions,
            "deck_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                section_ids = ExactSpecificationLookupIds,
                maxResults = 1,
            }));

        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.AreEqual(
            ExactSpecificationLookupIds,
            result.Matches.Select(static match => match.SectionId).Take(2).ToArray());
        Assert.AreEqual(ExactSpecificationLookupIds.Length, result.Matches.Length);
    }

    [TestMethod]
    public async Task SpecificationLookupIncludesSyntaxClarificationsForCommonMistakes()
    {
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                Handlers = [new StubDeckHandler()],
            });

        var result = await FunctionTestUtilities.InvokeAsync<DeckSpecificationLookupToolResult>(
            functions,
            "deck_spec_lookup",
            FunctionTestUtilities.CreateArguments(new
            {
                section_ids = SyntaxClarificationSectionIds,
                maxResults = 1,
            }));

        Assert.IsTrue(result.Success, result.Message);

        var sections = result.Matches.ToDictionary(static match => match.SectionId, StringComparer.OrdinalIgnoreCase);
        StringAssert.Contains(string.Join(" ", sections["document-attributes"].Content), "[state hidden]");
        StringAssert.Contains(string.Join(" ", sections["shared-theme"].Content), "raw value lists");
        StringAssert.Contains(string.Join(" ", sections["shared-style"].Content), "'color=#2C3E50'");
        StringAssert.Contains(string.Join(" ", sections["shared-motion"].Content), "[shared motion ...]");
        StringAssert.Contains(string.Join(" ", sections["slide-background"].Content), "[bg fill=#0F172A]");
        StringAssert.Contains(string.Join(" ", sections["slide-notes"].Content), "multi-line '[notes]' block");
        StringAssert.Contains(string.Join(" ", sections["layout-block-grid"].Content), "':grid: 40x22'");
        StringAssert.Contains(string.Join(" ", sections["addressing-geometry"].Content), "B6.5");
        StringAssert.Contains(string.Join(" ", sections["object-lines-geometry"].Content), "empty trailing '|'");
        StringAssert.Contains(string.Join(" ", sections["object-attrlists"].Content), "@box1 B6 13x5");
        StringAssert.Contains(string.Join(" ", sections["layout-block-background"].Content), "[bg ...]");
        StringAssert.Contains(string.Join(" ", sections["layout-slot-lines"].Content), "they overlap visually");
        StringAssert.Contains(string.Join(" ", sections["object-lines-target"].Content), "@panel = right [shape ...]");
        StringAssert.Contains(string.Join(" ", sections["object-attrlists"].Content), "[icon name=TrendingUp]");
        StringAssert.Contains(string.Join(" ", sections["object-attrlists"].Content), "width=4");
        StringAssert.Contains(string.Join(" ", sections["payload-forms"].Content), "- item [shape ...]");
        StringAssert.Contains(string.Join(" ", sections["pattern-icon"].Content), "asset=' or 'ref='");
        StringAssert.Contains(string.Join(" ", sections["pattern-shape"].Content), "stroke=none");
        StringAssert.Contains(string.Join(" ", sections["pattern-line"].Content), "width=4");
        StringAssert.Contains(string.Join(" ", sections["pattern-numbered-list"].Content), "bullet=numbered");
        StringAssert.Contains(string.Join(" ", sections["pattern-numbered-list"].Content), "1. Discover");
        StringAssert.Contains(string.Join(" ", sections["table-block"].Content), "at=left");
        StringAssert.Contains(string.Join(" ", sections["chart-block"].Content), "at=right");
        StringAssert.Contains(string.Join(" ", sections["grouping"].Content), "not a block");
        StringAssert.Contains(string.Join(" ", sections["explicit-object-overrides"].Content), "not followed by a closing '[end]'");
        StringAssert.Contains(string.Join(" ", sections["explicit-object-overrides"].Content), "[b]...[/b]");
        StringAssert.Contains(string.Join(" ", sections["animate"].Content), "omits the compact-object prefix '@'");
    }

    [TestMethod]
    public async Task WriteFileAcceptsSpecShapedCanonicalMiniExample()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "spec-mini.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Demo
            :deckdoc: 1
            :locale: en-US
            :size: wide
            :grid: 32x18

            [theme primary=#0B5FFF ink=#0F172A surface=#F8FAFC display="Aptos Display" body="Aptos"]
            [style .title font=$display size=28 bold fg=$ink]
            [style .body font=$body size=16 fg=$ink]
            [asset hero "./hero.png"]
            [motion fade-in enter=fade dur=0.35s]

            [layout cover]
            [grid 40x22]
            [background fill=$surface]
            [transition fade dur=0.25s]
            @title = B3:M4 [.title]
            @hero = N3:Y12 [image fit=cover]
            [end]

            == Cover
            [use cover]
            [section Highlights]
            [notes "Lead with the headline."]
            @title [text .title] | Hello world
            @hero [image asset=hero alt="Team photo"]
            @body [list .body bullet=disc] | First point | Second point
            [group summary title hero]
            [animate title preset=fade-in order=1]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public void ParseKeepsRolePrefixedStyleNameWhenBareTokensFollow()
    {
        var document = DeckDocSyntaxParser.Parse(
            """
            = Style Parsing
            :deckdoc: 1

            [style .title size=28 bold fg=#FFFFFF]

            == Cover
            """);

        Assert.IsTrue(document.Styles.ContainsKey("title"));
        Assert.IsFalse(document.Styles.ContainsKey("bold"));
        Assert.AreEqual("#FFFFFF", document.Styles["title"].GetValue("fg"));
        Assert.AreEqual(1, document.Styles["title"].BareTokens.Count);
        Assert.AreEqual("bold", document.Styles["title"].BareTokens[0]);
    }

    [TestMethod]
    public async Task WriteFileAcceptsRangeShorthandAndAreaEqualsForms()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "range-shorthand.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Range Shorthand Deck
            :deckdoc: 1
            :grid: 32x18

            [style .title size=28 bold]

            [layout dashboard]
            [grid 40x22]
            [area main = B4:M20]
            @header = B2:AM3 [.title]
            @slot1 = B4:M11 [shape fill=#FFFFFF stroke=#005A9C]
            [end]

            == Summary
            [use dashboard]
            @header [text .title] | Quarterly Snapshot
            [table Metrics at=B4:M11 .title]
            | KPI | Value |
            | --- | --- |
            | Growth | 12% |
            [end]
            [chart "Trend" type=column at=O4:AA11]
            - series column "Revenue" cat=(Q1,Q2) val=(10,12)
            [end]
            [obj B17:Y18 rich="Footer text"]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsLowercaseNamedTargetsThatEndWithDigits()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "named-targets.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Named Targets Deck
            :deckdoc: 1
            :grid: 32x18

            [layout content-stack]
            [grid 40x22]
            [area main = B3:AK19]
            [stack items in=main dir=down count=3 gap=1]
            @title = B2:AK3 [.title]
            @row1 = B4:AK8
            @row2 = B9:AK13
            @row3 = B14:AK18
            [end]

            == Agenda
            [use content-stack]
            @title [text] | Strategic Priorities
            @row1 [list bullet=disc] | Morning: Market Analysis | Afternoon: Product Roadmap
            @row2 [list bullet=dash] | Objective 1: Define targets | Objective 2: Align resources
            @row3 [list bullet=check] | Pre-read complete | Budget approved
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileRejectsSlideSlotBindingSyntaxWithHelpfulMessage()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid-slide-slot.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Invalid Slide Slot
            :deckdoc: 1

            == Cover
            @badge = AD2 2x2 [image asset=badge alt="Strategy Badge"]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "do not use '=' slot-binding syntax");
        StringAssert.Contains(result.Message, "layout slot lines");
    }

    [TestMethod]
    public async Task WriteFileRejectsChartAttrlistOnObjectLinesWithHelpfulMessage()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid-inline-chart.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Invalid Inline Chart
            :deckdoc: 1

            == Metrics
            @cards[2] [chart "Revenue Growth" type=column at=cards[2]]
            - series column "Revenue" cat=(Q1,Q2,Q3,Q4) val=(420,480,510,590)
            [end]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "standalone block directive");
        StringAssert.Contains(result.Message, "[chart ...]");
    }

    [TestMethod]
    public async Task WriteFileRejectsNonPositiveLineSpanWithHelpfulMessage()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid-line-span.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Invalid Line Span
            :deckdoc: 1

            [layout sample]
            !B4 30x0 [line stroke=#CBD5E1]
            [end]

            == Cover
            [use sample]
            @title [text] | Hello
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "span height");
        StringAssert.Contains(result.Message, "greater than zero");
    }

    [TestMethod]
    public async Task WriteFileAcceptsNamedGeometryObjectsForGrouping()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "named-geometry-group.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Named Geometry Group Deck
            :deckdoc: 1
            :grid: 32x18

            == Decision Framing
            @pillar1 B4 8x10 [shape rect fill=#004A99]
            @pillar2 K4 8x10 [shape rect fill=#00A3E0]
            @pillar3 T4 8x10 [shape rect fill=#FFB81C]
            [group StrategicPillars pillar1 pillar2 pillar3]
            [animate StrategicPillars enter=zoom dur=0.5s order=1]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsExplicitObjectOverridesWithTrailingAttrlistOrPayload()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "override-compat.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Override Compatibility Deck
            :deckdoc: 1
            :grid: 32x18

            == Cover
            [obj B3 8x5] [image asset=badge alt="Achievement badge"]
            [obj N3 4x4 [shape circle fill=#005A9C]]
            [obj B10:M11] [Quarterly Operations]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsMultiLineCompactListContinuations()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "multiline-list.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Multiline Lists
            :deckdoc: 1

            == Agenda
            @content [list .body bullet=disc]
            | Day 1: Market Dynamics and Competitive Analysis
            | Day 2: Internal Optimization and Product Roadmap
            | Evening: Team Building at The Grove

            == Appendix
            @body [list .body bullet=dash]
            - Regional sub-totals available in SFDC
            - Raw survey data exported to Excel
            - Audit logs verified
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsImplicitListContinuationsForLayoutBoundTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "implicit-list-targets.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Implicit List Targets
            :deckdoc: 1

            [layout content]
            [grid 40x22]
            [area body B6:AM18]
            @content = body [list .body bullet=disc]
            [end]

            == Strategic Decisions
            [use content]
            @content
            - Invest: Automation and AI Infrastructure
            - Maintain: Legacy Core Reliability
            - Divest: Non-core regional hubs

            == Metadata Appendix
            [use content]
            @content
            x Completed: Historical Index 1998-2023
            1. Regional Compliance Logs
            2. Partner Audit Summaries
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsTrailingPlacementAfterObjectAttrlist()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "trailing-placement.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Trailing Placement Deck
            :deckdoc: 1
            :grid: 32x18

            == Performance
            @caption [text .caption] at=V16:AM17 | Fig 1: Q4 Performance Summary

            == Strategy
            @icon1 [shape star fill=#FFB81C] at=B6 2x2
            @icon2 [shape star fill=#FFB81C] at=V6 2x2
            [group g1 icon1 icon2]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsBareTrailingGeometryAfterObjectAttrlist()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "bare-trailing-placement.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Bare Trailing Placement Deck
            :deckdoc: 1
            :grid: 32x18

            == Decision Framing
            @box [shape rect fill=#3B82F6 stroke=#0F172A text .h2 fg=#FFFFFF] B8 10x4 | High Reward
            [group decision-box box]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsLayoutFixedObjectsBoundToNamedTargetsWithoutSlotBinding()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "layout-target-fixed-object.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Layout Target Fixed Object Deck
            :deckdoc: 1

            [layout content]
            [grid 40x24]
            [stack rail in=T2:Y22 dir=down count=3 gap=1]
            @rail[1] [shape rect fill=#0D9488 stroke=#0F172A]
            @title = B2:S5 [.title]
            [end]

            == Agenda
            [use content]
            @title | Day 1 and Day 2 Focus
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsSplitWithoutExplicitSource()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "implicit-split-source.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Implicit Split Source Deck
            :deckdoc: 1

            [layout content]
            [grid 40x24]
            [split cols=(16,24) gap=1 as=left,right]
            @title = left [.title]
            [end]

            == Overview
            [use content]
            @title | Parser accepts default split source
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsFixedLayoutObjectsBoundToNamedTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "fixed-layout-target.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Fixed Layout Target Deck
            :deckdoc: 1

            [layout content]
            [grid 40x24]
            [split cols=(16,24) gap=1 as=left,right]
            !right [shape rect fill=#E2E8F0 stroke=#94A3B8]
            @title = left [.title]
            [end]

            == Agenda
            [use content]
            @title | Named target fixed object
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsFixedLayoutRangeShorthandAndNamedPlacement()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "fixed-layout-range.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Fixed Layout Range Deck
            :deckdoc: 1

            [layout cover]
            [grid 40x22]
            !B2:Y22 [shape rect fill=#0F172A]
            !heroPanel Z2:AM22 [shape rect fill=#1E293B]
            @title = B8:T12 [.title]
            [end]

            == Cover
            [use cover]
            @title | Fixed range shorthand
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsAnchorToColumnHeightRangeShorthand()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "anchor-column-height.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Anchor Column Height Deck
            :deckdoc: 1

            [layout content]
            [grid 32x18]
            @title = B4:AD3 [.title]
            @subtitle = B7:AD1 [.subtitle]
            @body = B9:AD8 [list .body bullet=disc]
            [end]

            == Overview
            [use content]
            @title | Anchor-column-height shorthand
            @subtitle | Compact layout shorthand
            @body
            | First point
            | Second point
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsNamedSlideObjectsBoundToNamedTargets()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "named-target-object.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Named Target Object Deck
            :deckdoc: 1

            [layout content]
            [grid 40x24]
            [split cols=(16,24) gap=1 as=left,right]
            [end]

            == Day 1
            [use content]
            @day1_bg right [shape rect fill=#DBEAFE stroke=#93C5FD]
            @day1_title left [text bold] | Operating priorities
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsLineDirectiveInsideLayoutBlock()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "layout-line-directive.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Layout Line Directive Deck
            :deckdoc: 1

            [layout content]
            [grid 32x18]
            [area head B2:AE4]
            @title = head [.title]
            [line B4:AE4 stroke=#0D9488 weight=2]
            [end]

            == Overview
            [use content]
            @title | Quarterly Review
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileRejectsGroupBlockGeometryFormWithHelpfulMessage()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid-group-block.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Invalid Group Block
            :deckdoc: 1

            == Investment
            [group sidebar at=V7:AM17]
            @cap [text .caption] | Project Alpha remains high priority.
            @box [shape rect fill=#FFB81C opacity=0.1]
            [end]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "Grouping is a single directive, not a geometry or nested block form");
        StringAssert.Contains(result.Message, "[group name member1 member2 ...]");
    }

    [TestMethod]
    public void SystemPromptIncludesAllDeckSpecificationSectionIds()
    {
        var systemPrompt = DeckTools.GetSystemPromptGuidance();

        foreach (var sectionId in DeckSpecificationCatalog.GetSectionIds())
        {
            StringAssert.Contains(systemPrompt, sectionId);
        }

        StringAssert.Contains(systemPrompt, "prefer section_ids over free-text keywords");
        StringAssert.Contains(systemPrompt, "Small example:");
        StringAssert.Contains(systemPrompt, ":grid: 32x18");
        StringAssert.Contains(systemPrompt, "[grid 40x22]");
        StringAssert.Contains(systemPrompt, "[theme primary=#0B5FFF ink=#0F172A]");
        StringAssert.Contains(systemPrompt, "[style .title ...]");
        StringAssert.Contains(systemPrompt, "[layout cover]");
        StringAssert.Contains(systemPrompt, "[asset hero \"./hero.png\"]");
        StringAssert.Contains(systemPrompt, "Grid/layout basics:");
        StringAssert.Contains(systemPrompt, "Common syntax traps:");
        StringAssert.Contains(systemPrompt, "Do not probe with a header-only or layout-only fragment");
        StringAssert.Contains(systemPrompt, "Before deck_write_file or deck_edit_file");
        StringAssert.Contains(systemPrompt, "[background ...]' rather than '[bg ...]'");
        StringAssert.Contains(systemPrompt, "bullet=numbered");
        StringAssert.Contains(systemPrompt, "icon name=TrendingUp");
        StringAssert.Contains(systemPrompt, "they overlap");
        StringAssert.Contains(systemPrompt, "at=left");
        StringAssert.Contains(systemPrompt, "[line ...] at=...");
        StringAssert.Contains(systemPrompt, "- item [shape ...]");
        StringAssert.Contains(systemPrompt, "1.' or '2.' prefixes");
        StringAssert.Contains(systemPrompt, "[b]...[/b]");
        StringAssert.Contains(systemPrompt, "B6.5");
        StringAssert.Contains(systemPrompt, "slide directives, not layout-block directives");
        StringAssert.Contains(systemPrompt, "'@zone B6:Z17 [split ...]'");
        StringAssert.Contains(systemPrompt, "rows=(...)' and 'cols=(...)'");
        StringAssert.Contains(systemPrompt, "text does not ride directly on the border");
        StringAssert.Contains(systemPrompt, "'[obj \"Chart Name\" at=...]'");
        StringAssert.Contains(systemPrompt, "not '[shared motion ...]'");
        StringAssert.Contains(systemPrompt, "rather than '@name = target [...]'");
        StringAssert.Contains(systemPrompt, "@box1 B6 13x5");
        StringAssert.Contains(systemPrompt, "blank '|'");
        StringAssert.Contains(systemPrompt, "[state hidden]");
        StringAssert.Contains(systemPrompt, "[notes \"Lead with the headline.\"]");
    }

    [TestMethod]
    public async Task WriteFileReportsSharedMotionSyntaxWithHelpfulMessage()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "invalid-shared-motion.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Invalid Shared Motion
            :deckdoc: 1

            [shared motion fade-in enter=fade dur=0.35s]

            == Cover
            @title | Hello
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Message);
        StringAssert.Contains(result.Message, "[shared motion ...]");
        StringAssert.Contains(result.Message, "[motion <name> <entry>...]");
    }

    [TestMethod]
    public async Task WriteFileAcceptsSharedStylesDeclaredWithDottedRoleNames()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "dotted-style.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Dotted Style Deck
            :deckdoc: 1

            [style .title font="Aptos Display" size=28 bold fg=#0F172A]
            [style .body font="Aptos" size=16 fg=#334155]

            == Cover
            @title [text .title] | Quarterly Operations
            @body [list .body bullet=disc] | Lower churn | Raise renewal quality
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public async Task WriteFileAcceptsComprehensiveDeckDocSyntax()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var filePath = Path.Combine(workingDirectory, "comprehensive.stubdeck");
        var functions = DeckTools.CreateFunctions(
            new DeckToolsOptions
            {
                WorkingDirectory = workingDirectory,
                Handlers = [new StubDeckHandler()],
            });

        var deckDoc = """
            = Comprehensive Deck
            :deckdoc: 1
            :locale: en-US
            :size: wide
            :grid: 32x18

            [theme primary=#0B5FFF accent=#F97316 ink=#0F172A muted=#5B6472 surface=#F8FAFC display="Aptos Display" body="Aptos"]
            [style title font=$display size=28 bold fg=$ink]
            [style body font=$body size=16 fg=$ink align=left/top]
            [asset hero "./hero.png"]
            [asset logo "brand://logo"]
            [motion fade-in enter=fade dur=0.35s]
            [layout cover]
            [grid 40x22]
            [background fill=$surface]
            [transition fade dur=0.25s]
            [area stage B2:Y14]
            [split stage cols=(7,17) gap=1 as=rail,body]
            [grid cards in=body cols=3 rows=1 gap=0.5]
            [stack chips in=rail dir=down count=2 gap=0.25]
            !A1 32x18 [shape rect fill=$surface layer=back]
            @title = B3 20x2 [.title]
            @hero = body [image fit=cover]
            [x powerpoint layout-tag="cover"]
            [end]

            == Opening
            [use cover]
            [section Highlights]
            [state hidden]
            [background fill=#0F172A]
            [notes "Lead with the headline."]
            [transition push dir=left dur=0.35s advance=after(1s)]
            [area footer A17:H18]
            [split footer cols=(1fr,1fr) gap=0.5 as=leftnote,rightnote]
            [grid metrics in=body cols=2 rows=1 gap=0.5]
            [stack bullets in=rail dir=down count=2 gap=0.2]
            @title [text .title] | Quarterly Operations
            @hero [image asset=hero fit=cover alt="Hero image"]
            @metrics[1] [shape roundrect fill=#FFFFFF stroke=#CBD5E1 radius=0.18in] | Revenue up
            @metrics[2] [line stroke=#CBD5E1 weight=1 arrow]
            @bullets[1] [list .body bullet=disc] | Lower churn | Raise renewal quality
            @bullets[2] [icon asset=logo fit=contain fg=$primary]
            @footer [text .caption] | Source: Finance
            [obj title rich="Quarterly [b]Operations[/b]"]
            [group summary title hero]
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
            [animate title enter=fade dur=0.35s order=1]
            [x powerpoint section-collapsed]
            """;

        var result = await FunctionTestUtilities.InvokeAsync<DeckWriteFileToolResult>(
            functions,
            "deck_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                deck_reference = filePath,
                content = deckDoc,
            }));

        Assert.IsTrue(result.Success, result.Message);
    }

    private sealed class StubDeckReferenceResolver(InMemoryDeckStore store) : IDeckReferenceResolver
    {
        public ValueTask<DeckReferenceResolution?> ResolveAsync(string deckReference, DeckReferenceResolverContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DeckReferenceResolution?>(
                string.Equals(deckReference, "deck:guide", StringComparison.OrdinalIgnoreCase)
                    ? DeckReferenceResolution.CreateStreamBacked(
                        resolvedReference: "deck:guide",
                        extension: ".stubdeck",
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

    private sealed class StubDeckHandler : IDeckHandler
    {
        public string ProviderName => "stub";

        public IReadOnlyCollection<string> SupportedExtensions => [".stubdeck"];

        public bool CanHandle(DeckHandlerContext context) =>
            string.Equals(context.Extension, ".stubdeck", StringComparison.OrdinalIgnoreCase);

        public async ValueTask<DeckReadResponse> ReadAsync(DeckHandlerContext context, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenReadAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return new DeckReadResponse(content, true, "stub");
        }

        public async ValueTask<DeckWriteResponse> WriteAsync(DeckHandlerContext context, string deckDoc, CancellationToken cancellationToken = default)
        {
            await using var stream = await context.OpenWriteAsync(cancellationToken);
            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.SetLength(0);
            }

            using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: false);
            await writer.WriteAsync(deckDoc.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return new DeckWriteResponse(true, "stub");
        }
    }

    private sealed class InMemoryDeckStore
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

    private sealed class CommitOnDisposeMemoryStream(InMemoryDeckStore store) : MemoryStream
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
