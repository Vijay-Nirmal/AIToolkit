using Azure.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace AIToolkit.Tools.Workbook.Excel.Tests;

[TestClass]
public class ExcelWorkbookToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "workbook_edit_file",
        "workbook_grep_search",
        "workbook_read_file",
        "workbook_spec_lookup",
        "workbook_write_file",
    ];

    private const string SimpleWorkbookDoc = """
        = Revenue Workbook
        :wbdoc: 4
        :date-system: 1900
        :active: Summary
        
        [style hdr bold bg=#D9E2F3]
        
        == Summary
        [view freeze=1,1 zoom=90 grid]
        [used A1:C4]
        @A1 [.hdr] | Month | Revenue | Margin
        @A2 | January | 1200 | [fmt="0.0%"] 0.35
        @A3 | February | [result=1350] =SUM(Data!B2:B5) | [fmt="0.0%"] 0.42
        @A4 | March | 980 | [fmt="0.0%"] 0.28
        [chart "Revenue Trend" type=column at=E2 size=480x280px]
        - series column "Revenue" cat=A2:A4 val=B2:B4
        [end]
        
        == Data
        [used A1:B5]
        @A1 [.hdr] | Item | Amount
        @A2 | Alpha | 250
        @A3 | Beta | 300
        @A4 | Gamma | 400
        @A5 | Delta | 400
        """;

    private const string CoreFeatureWorkbookDoc = """
        = Workbook Quality Sample
        :wbdoc: 4
        :date-system: 1900
        :active: Summary
        
        [style hdr bold bg=#D9E2F3]
        [name RevenueRange = Summary!D6:D8]
        
        == Summary
        [view freeze=1,1 zoom=90 grid]
        [used B2:G9]
        [merge B2:G2 align=center/middle]
        [fmt B5:G5 bold bg=#D9E2F3]
        [fmt B5:G8 border=all:thin:#BFBFBF]
        [fmt D6:D9 fmt=$#,##0]
        [fmt E6:E9 fmt=0.0%]
        [type C6:C8 date]
        @B2 | [bold bg=#1F4E78 fg=#FFFFFF] Workbook Quality Sample
        @B5 | Item | Date | Revenue | Margin | Approved | Region
        @B6 | Alpha | date("2024-01-15") | 1200 | 0.25 | true | North
        @B7 | Beta | date("2024-02-01") | 1500 | 0.30 | false | South
        @B8 | Gamma | date("2024-03-20") | 1800 | 0.35 | true | East
        @B9 | Total | blank | [result=4500] =SUM(D6:D8) | [result=0.3] =AVERAGE(E6:E8) | [result=2] =COUNTIF(F6:F8,TRUE) | blank
        
        [validate B6:B8 text-len between(3,10) reject]
        [validate C6:C8 date between(date("2024-01-01"),date("2024-12-31")) reject]
        [validate D6:D8 number between(0,5000) reject]
        [validate F6:F8 checkbox warn]
        [validate G6:G8 list("North","South","East","West") reject dropdown]
        [validate B6:B8 formula(=COUNTIF($B$6:$B$8,B6)=1) reject]
        
        [cf D6:D8 data-bar(color=#5B9BD5)]
        [cf E6:E8 scale(min:#F8696B,50%:#FFEB84,max:#63BE7B)]
        [cf F6:F8 when formula(=F6=TRUE) fill=#E2F0D9 fg=#006100 bold]
        """;

    private const string ComprehensiveWorkbookDoc = """
        = Workbook Feature Coverage
        :wbdoc: 4
        :date-system: 1900
        :active: Overview
        
        [style hdr bold bg=#D9E2F3 fg=#1F1F1F]
        [name RevenueRange = Overview!D6:D8]
        
        == Overview
        [view freeze=1,1 zoom=90 grid]
        [used B2:H9]
        [merge B2:H2 align=center/middle]
        [fmt B5:H5 bold bg=#D9E2F3]
        [fmt D6:D9 fmt=$#,##0]
        [fmt E6:E9 fmt=0.0%]
        [type C6:C8 date]
        @B2 | [bold bg=#1F4E78 fg=#FFFFFF] Workbook Feature Coverage
        @B5 | Item | Date | Revenue | Margin | Approved | Region | Trend
        @B6 | Alpha | date("2024-01-15") | 1200 | 0.25 | true | North | 10
        @B7 | Beta | date("2024-02-01") | 1500 | 0.30 | false | South | 12
        @B8 | Gamma | date("2024-03-20") | 1800 | 0.35 | true | East | 14
        @B9 | Total | blank | [result=4500] =SUM(D6:D8) | [result=0.3] =AVERAGE(E6:E8) | [result=2] =COUNTIF(F6:F8,TRUE) | blank | blank
        
        [validate G6:G8 list("North","South","East","West") reject dropdown]
        [cf D6:D8 data-bar(color=#5B9BD5)]
        [table RevenueTable B5:H8]
        [spark H9 source=D6:D8 type=line color=#C0504D]
        [chart "Revenue Trend" type=column at=J2 size=480x280px]
        - series column "Revenue" cat=B6:B8 val=D6:D8 color=#5B9BD5 labels
        [end]
        [pivot "Revenue Pivot" source=RevenueTable at=J20]
        - rows G
        - values sum D as "Revenue"
        [end]
        """;

    private const string PopulationShowcaseWorkbookDoc = """
        = Population Showcase
        
        == Population Data
        [used B2:N18]
        [merge B2:N2 align=center/middle]
        [fmt B2:N2 bold size=16 bg=#D9EAF7 align=center/middle]
        [fmt B4:N4 bold bg=#D9E2F3 border=all:thin:#D9D9D9]
        [fmt B5:N14 border=all:thin:#D9D9D9]
        [fmt D5:D14 fmt="#,##0"]
        [fmt E5:E14 fmt="0.0%"]
        [fmt F5:F14 fmt="0.0%"]
        [fmt G5:G14 fmt="#,##0"]
        [fmt H5:H14 fmt="#,##0"]
        [fmt I5:I14 fmt="#,##0"]
        [fmt J5:J14 fmt="0.0%"]
        [fmt K5:K14 fmt="#,##0"]
        [fmt L5:L14 fmt="0.0%"]
        [fmt M5:M14 fmt="yyyy-mm-dd"]
        [fmt N5:N14 fmt="yyyy-mm-dd hh:mm"]
        [type M5:M14 date]
        [type N5:N14 datetime]
        @B2 | Population Sample Data Showcase
        @B4 | Country | Region | Population | Annual Growth | Urbanization | Area Km2 | Density | Median Age | Literacy | GDP Per Capita | Water Access | Census Date | Updated At
        @B5 | India | Asia | 1428627663 | 0.8 | 36.6 | 3287263 | =D5/G5 | 28.4 | 77.7 | 2612 | 93.0 | 2023-03-01 | 2026-04-19T09:00:00
        @B6 | China | Asia | 1425671352 | -0.1 | 65.2 | 9596961 | =D6/G6 | 39.0 | 97.0 | 12614 | 96.0 | 2022-11-01 | 2026-04-19T09:15:00
        @B7 | United States | North America | 339996563 | 0.5 | 83.0 | 9833517 | =D7/G7 | 38.5 | 99.0 | 76399 | 99.0 | 2023-07-01 | 2026-04-19T09:30:00
        @B8 | Indonesia | Asia | 277534122 | 0.7 | 58.4 | 1904569 | =D8/G8 | 30.2 | 96.0 | 4788 | 91.0 | 2023-06-30 | 2026-04-19T09:45:00
        @B9 | Pakistan | Asia | 240485658 | 2.0 | 37.8 | 881913 | =D9/G9 | 20.6 | 58.0 | 1588 | 79.0 | 2023-03-15 | 2026-04-19T10:00:00
        @B10 | Nigeria | Africa | 223804632 | 2.4 | 54.3 | 923768 | =D10/G10 | 18.1 | 62.0 | 2162 | 77.0 | 2023-05-01 | 2026-04-19T10:15:00
        @B11 | Brazil | South America | 216422446 | 0.4 | 87.6 | 8515767 | =D11/G11 | 33.5 | 94.0 | 10312 | 97.0 | 2022-08-01 | 2026-04-19T10:30:00
        @B12 | Bangladesh | Asia | 172954319 | 1.0 | 40.4 | 148460 | =D12/G12 | 27.6 | 75.0 | 2688 | 98.0 | 2022-12-15 | 2026-04-19T10:45:00
        @B13 | Russia | Europe | 144444359 | -0.4 | 74.8 | 17098242 | =D13/G13 | 39.6 | 99.7 | 15345 | 97.0 | 2021-10-01 | 2026-04-19T11:00:00
        @B14 | Mexico | North America | 128455567 | 0.8 | 81.6 | 1964375 | =D14/G14 | 29.3 | 95.0 | 13304 | 96.0 | 2020-03-15 | 2026-04-19T11:15:00
        @B16 | Metric | Value | Notes
        @B17 | Total Population | =SUM(D5:D14) | Sum formula example
        @B18 | Average Growth | =AVERAGE(E5:E14) | Average formula example
        
        [validate C5:C14 list("Asia","Africa","Europe","North America","South America") reject dropdown]
        [validate E5:E14 number between(-5,5) warn]
        [validate F5:F14 number between(0,1) reject]
        [validate M5:M14 date between(date("2020-01-01"),date("2030-12-31")) reject]
        [validate B5:B14 text-len between(3,30) reject]
        
        [cf D5:D14 data-bar(color=#5B9BD5)]
        [cf E5:E14 when formula(=E5<0) fill=#F4CCCC fg=#9C0006 bold]
        [cf H5:H14 scale(min:#63BE7B,50%:#FFEB84,max:#F8696B)]
        [cf J5:J14 icon-set(3-arrows)]
        [cf B5:B14 when text contains "United" fill=#D9EAD3 bold]
        
        [chart "Population vs GDP" type=combo at=P4 size=720x360px]
        - series column "Population" cat=B5:B14 val=D5:D14 color=#5B9BD5
        - series line "GDP Per Capita" cat=B5:B14 val=K5:K14 axis=secondary color=#C0504D labels
        [end]
        
        [chart "Regional Distribution" type=pie at=P24 size=520x320px]
        - series pie "Population Share" cat=B5:B9 val=D5:D9 labels
        [end]
        
        == Validation Lists
        [used B2:B6]
        [fmt B2:B6 bg=#F7F7F7 border=all:thin:#D9D9D9]
        @B2 | Region
        @B3 | Asia
        @B4 | Africa
        @B5 | Europe
        @B6 | North America
        """;

    private const string CurrentRegressionWorkbookDoc = """
        = Population Showcase Sample

        == Population Data
        [used B4:P7]
        [fmt B4:P7 border=all:thin:#D9D9D9]
        [fmt E5:G7 fmt=#,##0]
        [fmt H5:H7 fmt=0.00%]
        [fmt J5:J7 fmt=#,##0]
        [fmt K5:K7 fmt=0.00%]
        [fmt N5:O7 fmt=#,##0]
        [fmt P5:P7 fmt=0.0]
        [type D5:D7 date]
        @B4 | Country | Continent | Income Group | Snapshot Date | Population 2000 | Population 2010 | Population 2020 | Growth 2000-2020 | Land Area km2 (M) | Urbanization 2020 | Birth Rate | Life Expectancy | Pop Density 2020 | Share of Sample 2020 | Trend Index
        @B5 | India | Asia | Lower Middle | 2000-01-01 | 1,053,000,000 | 1,234,000,000 | 1,380,000,000 | =(H5-E5)/E5 | 3.29 | 0.35 | 25 | 62 | =H5/(J5*1000000) | =H5/SUM($H$5:$H$7) | 62.0
        @B6 | China | Asia | Upper Middle | 2000-01-01 | 1,264,000,000 | 1,341,000,000 | 1,412,000,000 | =(H6-E6)/E6 | 9.60 | 0.61 | 12 | 77 | =H6/(J6*1000000) | =H6/SUM($H$5:$H$7) | 70.0
        @B7 | United States | North America | High | 2000-01-01 | 282,000,000 | 309,000,000 | 331,000,000 | =(H7-E7)/E7 | 9.83 | 0.83 | 11 | 79 | =H7/(J7*1000000) | =H7/SUM($H$5:$H$7) | 85.0
        [cf H5:H7 data-bar(color=#5B9BD5)]
        [cf I5:I7 scale(min:#F8696B,50%:#FFEB84,max:#63BE7B)]
        [cf K5:K7 cell(>=0.8) fill=#E2F0D9]
        [cf K5:K7 cell(<0.5) fill=#FCE4D6]

        == Charts
        [used B2:T12]
        [chart "Top Population" type=column at=B4 size=640x360px]
        - series column "Population 2020" cat='Population Data'!B5:B7 val='Population Data'!H5:H7 color=#5B9BD5 labels
        [end]
        [chart "Population Trend" type=line at=K4 size=640x360px]
        - series line "2000" cat='Population Data'!B5:B7 val='Population Data'!E5:E7 color=#A5A5A5
        - series line "2020" cat='Population Data'!B5:B7 val='Population Data'!H5:H7 color=#4472C4 labels
        [end]
        [chart "Continent Share" type=pie at=B20 size=420x280px]
        - series pie "Population 2020" cat='Population Data'!C5:C7 val='Population Data'!H5:H7 labels
        [end]
        [chart "Density" type=bar at=H20 size=520x280px]
        - series bar "Density 2020" cat='Population Data'!B5:B7 val='Population Data'!N5:N7 color=#70AD47 labels
        [end]
        [chart "Population and Urbanization" type=combo at=N20 size=620x300px]
        - series column "Population 2020" cat='Population Data'!B5:B7 val='Population Data'!H5:H7 color=#5B9BD5
        - series line "Urbanization" cat='Population Data'!B5:B7 val='Population Data'!K5:K7 axis=secondary color=#C00000 labels
        [end]
        """;

    private const string CompatibilityConditionalFormattingWorkbookDoc = """
        = Population Showcase Workbook

        == Population Data
        [used A1:J4]
        @A1 | Region | Country | Population 2010 | Population 2015 | Population 2020 | Population 2025 | Growth 2010-2025 | Growth % | Density/km2
        @A2 | Asia | India | 1234281170 | 1310152403 | 1380004385 | 1450000000 | =G2-D2 | =H2/D2 | 488
        @A3 | Asia | China | 1340968737 | 1371220000 | 1412600000 | 1415000000 | =G3-D3 | =H3/D3 | 153
        @A4 | Africa | Nigeria | 158578261 | 181137448 | 206139589 | 230000000 | =G4-D4 | =H4/D4 | 255
        [cf I2:I4 gt 0.15 fill=#C6EFCE fg=#006100]
        [cf I2:I4 lt 0 fill=#FFC7CE fg=#9C0006]
        [cf D2:G4 data-bar color=#5B9BD5]
        [cf J2:J4 color-scale min=#F8696B mid=#FFEB84 max=#63BE7B]
        """;

    private const string JsonEscapedWorkbookDoc = """
        = Population Showcase Workbook

        == Population Data
        [used B4:N6]
        @B4 | Country | Region | Population 2020 | Population 2021 | Population 2022 | Population 2023 | Area km\u00B2 | Urban % | Census Date | Last Updated | Growth 2023 | Density | Trend
        @B5 | India | Asia | 1380004385 | 1393409038 | 1406631776 | 1428627663 | 3287263 | 0.36 | 2023-03-01 | 2026-04-20T09:30:00 | =(G5-F5)/F5 | =G5/H5 | blank
        @B6 | Nigeria | Africa | 206139589 | 211400708 | 216746934 | 223804632 | 923768 | 0.54 | 2023-05-01 | 2026-04-20T09:30:00 | =(G6-F6)/F6 | =G6/H6 | blank
        [cf L5:L6 when cell \u003E 0.015 fill=#C6EFCE fg=#006100]

        == Charts
        [chart \u0022Population 2023 by Country\u0022 type=column at=B4 size=480x288px]
        - series column \u0022Population 2023\u0022 cat=\u0027Population Data\u0027!B5:B6 val=\u0027Population Data\u0027!G5:G6 color=#5B9BD5 labels
        [end]
        """;

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = FunctionTestUtilities.CreateFunctions();

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesExcelCapabilities()
    {
        var prompt = ExcelWorkbookTools.GetSystemPromptGuidance("Base prompt");

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "Local Excel file paths are supported");
        StringAssert.Contains(prompt, ".xlsx");
        StringAssert.Contains(prompt, "@B5 | Region | Revenue | Margin");
        StringAssert.Contains(prompt, "[chart \"Name\" type=column");
        StringAssert.Contains(prompt, "workbook_spec_lookup");
        Assert.IsFalse(prompt.Contains("validation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CreateM365ReferenceResolverRequiresCredential()
    {
        try
        {
            _ = ExcelWorkbookTools.CreateM365ReferenceResolver(new ExcelWorkbookM365Options());
            Assert.Fail("Expected missing M365 credentials to throw an ArgumentException.");
        }
        catch (ArgumentException exception)
        {
            StringAssert.Contains(exception.Message, "Credential");
        }
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesHostedExcelCapabilities()
    {
        var prompt = ExcelWorkbookTools.GetSystemPromptGuidance(
            "Base prompt",
            new ExcelWorkbookHandlerOptions
            {
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new WorkbookToolsOptions());

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "Local Excel file paths are supported");
        StringAssert.Contains(prompt, "m365://drives/me/root/path/to/file.xlsx");
        StringAssert.Contains(prompt, "workbook_references to workbook_grep_search");
        StringAssert.Contains(prompt, "[chart \"Name\" type=column");
    }

    [TestMethod]
    public void FlattenedOptionsExposeHostedOnlyConfiguration()
    {
        var prompt = ExcelWorkbookTools.GetSystemPromptGuidance(
            new ExcelWorkbookToolSetOptions
            {
                EnableLocalFileSupport = false,
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            });

        StringAssert.Contains(prompt, "Local Excel file paths are disabled in this tool set");
        StringAssert.Contains(prompt, "Hosted Excel workbooks are supported");
        StringAssert.Contains(prompt, "m365://drives/me/root/path/to/file.xlsx");
    }

    [TestMethod]
    public async Task FlattenedOptionsCanCreateHostedOnlyFunctions()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = ExcelWorkbookTools.CreateFunctions(
            new ExcelWorkbookToolSetOptions
            {
                WorkingDirectory = workingDirectory,
                EnableLocalFileSupport = false,
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = Path.Combine(workingDirectory, "local-disabled.xlsx"),
                content = SimpleWorkbookDoc,
            }));

        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message, "Local Excel file support is disabled");
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsStillSupportsLocalFiles()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(
            workingDirectory,
            new ExcelWorkbookHandlerOptions
            {
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            });
        var workbookPath = Path.Combine(workingDirectory, "local-with-m365.xlsx");

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
                content = SimpleWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
            }));

        Assert.AreEqual(SimpleWorkbookDoc.Replace("\r\n", "\n", StringComparison.Ordinal), FunctionTestUtilities.ReadWorkbookDocText(readContents));
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsRejectsUnsupportedOneDriveAlias()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var functions = FunctionTestUtilities.CreateFunctions(
            workingDirectory,
            new ExcelWorkbookHandlerOptions
            {
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            });

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = "OneDrive://workbooks/QuarterlyRevenue.xlsx",
                content = SimpleWorkbookDoc,
            }));

        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message, "unsupported alias scheme");
        StringAssert.Contains(writeResult.Message, "m365://drives/me/root/Documents/QuarterlyRevenue.xlsx");
    }

    [TestMethod]
    public async Task CreateFunctionsWithM365OptionsCanGrepExplicitHostedReferences()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        const string hostedUrl = "https://contoso.sharepoint.com/sites/Docs/Shared%20Documents/revenue.xlsx";
        const string resolvedReference = "m365://drives/drive-123/items/item-123";
        var hostedBytes = CreateHostedExcelWorkbookBytes();
        var functions = ExcelWorkbookTools.CreateFunctions(
            new ExcelWorkbookHandlerOptions
            {
                M365 = new ExcelWorkbookM365Options
                {
                    Credential = new FakeTokenCredential(),
                },
            },
            new WorkbookToolsOptions
            {
                WorkingDirectory = workingDirectory,
                MaxReadLines = 8_000,
                MaxSearchResults = 100,
                ReferenceResolver = new HostedExcelReferenceResolver(hostedUrl, resolvedReference, hostedBytes),
            });

        var result = await FunctionTestUtilities.InvokeAsync<WorkbookGrepSearchToolResult>(
            functions,
            "workbook_grep_search",
            FunctionTestUtilities.CreateArguments(new
            {
                pattern = "Hosted Revenue",
                workbook_references = new[] { hostedUrl },
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual(resolvedReference, result.Matches[0].Path);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = result.Matches[0].Path,
            }));

        StringAssert.Contains(FunctionTestUtilities.ReadWorkbookDocText(readContents), "Hosted Revenue");
    }

    [TestMethod]
    public async Task WriteThenReadRoundTripsEmbeddedWorkbookDoc()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "revenue.xlsx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
                content = SimpleWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.IsTrue(File.Exists(workbookPath));

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
            }));

        var workbookDoc = FunctionTestUtilities.ReadWorkbookDocText(readContents);
        Assert.AreEqual(SimpleWorkbookDoc.Replace("\r\n", "\n", StringComparison.Ordinal), workbookDoc);
    }

    [TestMethod]
    public async Task WriteThenReadRoundTripsAdvancedWorkbookDocDirectives()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "feature-coverage.xlsx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);

        var writeResult = await FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
                content = ComprehensiveWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var readContents = await FunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
            }));

        var workbookDoc = FunctionTestUtilities.ReadWorkbookDocText(readContents);
        Assert.AreEqual(ComprehensiveWorkbookDoc.Replace("\r\n", "\n", StringComparison.Ordinal), workbookDoc);
    }

    [TestMethod]
    public void WriteCoreFeaturesProducesOpenXmlValidWorkbook()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "quality-sample.xlsx");
        CreateExternalWorkbook(workbookPath, CoreFeatureWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var errors = new OpenXmlValidator().Validate(spreadsheet).ToArray();
        Assert.AreEqual(
            0,
            errors.Length,
            string.Join(Environment.NewLine, errors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));

        var workbookPart = spreadsheet.WorkbookPart ?? throw new InvalidOperationException("Workbook part missing.");
        var worksheetPart = workbookPart.WorksheetParts.Single();
        var worksheet = worksheetPart.Worksheet ?? throw new InvalidOperationException("Worksheet missing.");
        var dataValidations = worksheet.Elements<DataValidations>().SelectMany(static group => group.Elements<DataValidation>()).ToArray();
        var conditionalFormatting = worksheet.Elements<ConditionalFormatting>().ToArray();
        var mergeCells = worksheet.Elements<MergeCells>().SelectMany(static collection => collection.Elements<MergeCell>()).ToArray();
        var formulaCell = worksheet.Descendants<Cell>().Single(static cell => string.Equals(cell.CellReference?.Value, "D9", StringComparison.Ordinal));
        var styles = workbookPart.WorkbookStylesPart?.Stylesheet ?? throw new InvalidOperationException("Stylesheet missing.");
        var workbook = workbookPart.Workbook ?? throw new InvalidOperationException("Workbook missing.");

        Assert.AreEqual(1, workbook.DefinedNames?.Count() ?? 0);
        Assert.AreEqual(6, dataValidations.Length);
        Assert.AreEqual(3, conditionalFormatting.Length);
        Assert.AreEqual(1, mergeCells.Length);
        Assert.AreEqual("SUM(D6:D8)", formulaCell.CellFormula?.Text);
        Assert.IsNotNull(formulaCell.CellValue);
        Assert.IsTrue(styles.NumberingFormats?.Count?.Value > 0);
        Assert.IsTrue(workbookPart.Workbook.CalculationProperties?.ForceFullCalculation?.Value);
        Assert.IsTrue(workbookPart.Workbook.CalculationProperties?.FullCalculationOnLoad?.Value);
    }

    [TestMethod]
    public void ImportExternalWorkbookRecoversValidationsFormattingAndFormulaResults()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "quality-sample.xlsx");
        CreateExternalWorkbook(workbookPath, CoreFeatureWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var workbookDoc = ExcelWorkbookImporter.Import(spreadsheet, workbookPath);

        StringAssert.Contains(workbookDoc, "== Summary");
        StringAssert.Contains(workbookDoc, "[view freeze=1,1 zoom=90 grid]");
        StringAssert.Contains(workbookDoc, "[used B2:G9]");
        StringAssert.Contains(workbookDoc, "[merge B2:G2 align=center/middle]");
        StringAssert.Contains(workbookDoc, "[type C6:C8 date]");
        StringAssert.Contains(workbookDoc, "@B6 | Alpha | [fmt=yyyy-mm-dd] 2024-01-15 | [fmt=$#,##0] 1200 | [fmt=0.0%] 0.25 | true | North");
        StringAssert.Contains(workbookDoc, "@B9 | Total | blank | [fmt=$#,##0 result=4500] =SUM(D6:D8)");
        StringAssert.Contains(workbookDoc, "[validate B6:B8 text-len between(3,10) reject]");
        StringAssert.Contains(workbookDoc, "[validate C6:C8 date between(date(\"2024-01-01\"),date(\"2024-12-31\")) reject]");
        StringAssert.Contains(workbookDoc, "[validate F6:F8 checkbox warn]");
        StringAssert.Contains(workbookDoc, "[validate G6:G8 list(North,South,East,West) reject dropdown]");
        StringAssert.Contains(workbookDoc, "[cf D6:D8 data-bar(color=#5B9BD5)]");
        StringAssert.Contains(workbookDoc, "[cf E6:E8 scale(");
        StringAssert.Contains(workbookDoc, "[cf F6:F8 when formula(=F6=TRUE) fill=#E2F0D9 fg=#006100 bold]");
    }

    [TestMethod]
    public void WritePopulationShowcaseProducesValidConditionalFormattingAndCharts()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "population-showcase.xlsx");
        CreateExternalWorkbook(workbookPath, PopulationShowcaseWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var errors = new OpenXmlValidator().Validate(spreadsheet).ToArray();
        Assert.AreEqual(
            0,
            errors.Length,
            string.Join(Environment.NewLine, errors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));

        var workbookPart = spreadsheet.WorkbookPart ?? throw new InvalidOperationException("Workbook part missing.");
        var worksheetPart = workbookPart.WorksheetParts.First();
        Assert.IsNotNull(worksheetPart.DrawingsPart, "Expected charts to create a drawings part.");
        Assert.AreEqual(2, worksheetPart.DrawingsPart!.ChartParts.Count());
    }

    [TestMethod]
    public void ImportGeneratedPopulationShowcaseRecoversChartsAndConditionalFormatting()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "population-showcase.xlsx");
        CreateExternalWorkbook(workbookPath, PopulationShowcaseWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var workbookDoc = ExcelWorkbookImporter.Import(spreadsheet, workbookPath);

        StringAssert.Contains(workbookDoc, "[cf D5:D14 data-bar(color=#5B9BD5)]");
        StringAssert.Contains(workbookDoc, "[cf J5:J14 icon-set(3-arrows)]");
        StringAssert.Contains(workbookDoc, "[cf B5:B14 when text contains \"United\" fill=#D9EAD3 bold]");
        StringAssert.Contains(workbookDoc, "[chart \"Population vs GDP\" type=combo at=P4");
        StringAssert.Contains(workbookDoc, "[chart \"Regional Distribution\" type=pie at=P24");
    }

    [TestMethod]
    public void WriteCurrentRegressionSampleParsesGroupedNumbersAndCellConditionalFormatting()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "current-regression.xlsx");
        CreateExternalWorkbook(workbookPath, CurrentRegressionWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = spreadsheet.WorkbookPart ?? throw new InvalidOperationException("Workbook part missing.");
        var populationWorksheet = workbookPart.WorksheetParts.First();
        var errors = new OpenXmlValidator().Validate(spreadsheet).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));

        var populationWorksheetPart = populationWorksheet.Worksheet ?? throw new InvalidOperationException("Worksheet missing.");
        var populationCell = populationWorksheetPart.Descendants<Cell>().First(static cell => cell.CellReference?.Value == "F5");
        Assert.IsNull(populationCell.DataType?.Value, "Grouped numeric literals should be written as numbers, not strings.");
        Assert.AreEqual("1053000000", populationCell.CellValue?.Text);

        var cfRules = populationWorksheetPart.Descendants<ConditionalFormattingRule>().ToArray();
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.CellIs && rule.Operator?.Value == ConditionalFormattingOperatorValues.GreaterThanOrEqual), "Expected cell(>=...) shorthand to produce a cellIs rule.");
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.CellIs && rule.Operator?.Value == ConditionalFormattingOperatorValues.LessThan), "Expected cell(<...) shorthand to produce a cellIs rule.");
    }

    [TestMethod]
    public void WriteCurrentRegressionSampleCreatesAllChartSubtypes()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "current-regression-charts.xlsx");
        CreateExternalWorkbook(workbookPath, CurrentRegressionWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var chartWorksheet = spreadsheet.WorkbookPart!.WorksheetParts.Skip(1).First();
        Assert.IsNotNull(chartWorksheet.DrawingsPart, "Expected chart sheet drawings part.");
        Assert.AreEqual(5, chartWorksheet.DrawingsPart!.ChartParts.Count());
    }

    [TestMethod]
    public void WriteCompatibilityConditionalFormattingSyntaxProducesExpectedRules()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "compatibility-cf.xlsx");
        CreateExternalWorkbook(workbookPath, CompatibilityConditionalFormattingWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var worksheet = spreadsheet.WorkbookPart!.WorksheetParts.First().Worksheet ?? throw new InvalidOperationException("Worksheet missing.");
        var cfRules = worksheet.Descendants<ConditionalFormattingRule>().ToArray();

        Assert.AreEqual(4, cfRules.Length);
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.CellIs && rule.Operator?.Value == ConditionalFormattingOperatorValues.GreaterThan));
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.CellIs && rule.Operator?.Value == ConditionalFormattingOperatorValues.LessThan));
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.DataBar));
        Assert.IsTrue(cfRules.Any(static rule => rule.Type?.Value == ConditionalFormatValues.ColorScale));
    }

    [TestMethod]
    public void WriteJsonEscapedWorkbookDocParsesEscapedOperatorsAndQuotedRanges()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "json-escaped.xlsx");
        CreateExternalWorkbook(workbookPath, JsonEscapedWorkbookDoc);

        using var spreadsheet = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = spreadsheet.WorkbookPart ?? throw new InvalidOperationException("Workbook part missing.");
        var dataWorksheet = workbookPart.WorksheetParts.First().Worksheet ?? throw new InvalidOperationException("Worksheet missing.");
        var chartWorksheet = workbookPart.WorksheetParts.Skip(1).First();

        Assert.IsTrue(dataWorksheet.Descendants<ConditionalFormattingRule>().Any(static rule => rule.Type?.Value == ConditionalFormatValues.CellIs && rule.Operator?.Value == ConditionalFormattingOperatorValues.GreaterThan));
        Assert.IsNotNull(chartWorksheet.DrawingsPart, "Expected chart drawings part for escaped quoted ranges.");
        Assert.AreEqual(1, chartWorksheet.DrawingsPart!.ChartParts.Count());
    }

    private static byte[] CreateHostedExcelWorkbookBytes()
    {
        var workingDirectory = FunctionTestUtilities.CreateTemporaryDirectory();
        var workbookPath = Path.Combine(workingDirectory, "hosted.xlsx");
        var functions = FunctionTestUtilities.CreateFunctions(workingDirectory);
        var writeResult = FunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            FunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = workbookPath,
                content =
                """
                = Hosted Workbook
                :wbdoc: 4

                == Summary
                [used A1:B2]
                @A1 | Label | Value
                @A2 | Hosted Revenue | 1200
                """,
            })).GetAwaiter().GetResult();

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        return File.ReadAllBytes(workbookPath);
    }

    private static void CreateExternalWorkbook(string workbookPath, string workbookDoc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(workbookPath)!);
        using var spreadsheet = SpreadsheetDocument.Create(workbookPath, SpreadsheetDocumentType.Workbook);
        ExcelWorkbookWriter.Write(spreadsheet, WorkbookDocParser.Parse(workbookDoc));
    }

    private sealed class HostedExcelReferenceResolver(string originalReference, string resolvedReference, byte[] content) : IWorkbookReferenceResolver
    {
        public ValueTask<WorkbookReferenceResolution?> ResolveAsync(
            string workbookReference,
            WorkbookReferenceResolverContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkbookReferenceResolution?>(
                string.Equals(workbookReference, originalReference, StringComparison.Ordinal)
                || string.Equals(workbookReference, resolvedReference, StringComparison.Ordinal)
                    ? WorkbookReferenceResolution.CreateStreamBacked(
                        resolvedReference: resolvedReference,
                        extension: ".xlsx",
                        existsAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(true);
                        },
                        openReadAsync: innerCancellationToken =>
                        {
                            innerCancellationToken.ThrowIfCancellationRequested();
                            return ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
                        },
                        openWriteAsync: _ => throw new NotSupportedException("Hosted grep tests do not support writes through the fake resolver."),
                        version: "1",
                        length: content.LongLength,
                        readStateKey: resolvedReference)
                    : null);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
