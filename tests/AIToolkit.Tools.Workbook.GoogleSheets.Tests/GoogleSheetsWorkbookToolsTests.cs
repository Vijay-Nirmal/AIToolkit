using AIToolkit.Tools.Workbook.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Google.Apis.Sheets.v4.Data;
using SheetsProperties = Google.Apis.Sheets.v4.Data.SheetProperties;
using SheetsSheet = Google.Apis.Sheets.v4.Data.Sheet;
using SheetsSpreadsheet = Google.Apis.Sheets.v4.Data.Spreadsheet;

namespace AIToolkit.Tools.Workbook.GoogleSheets.Tests;

[TestClass]
public class GoogleSheetsWorkbookToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "workbook_edit_file",
        "workbook_grep_search",
        "workbook_read_file",
        "workbook_spec_lookup",
        "workbook_write_file",
    ];

    private static readonly string[] CompatibilityPivotValueNames =
    [
        "Sum of Population 2025",
        "Sum of Growth 2010-2025",
        "Average Growth %",
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
        [spark P5 source=E5:G5 type=line color=#4472C4]
        [spark P6 source=E6:G6 type=line color=#4472C4]
        [spark P7 source=E7:G7 type=line color=#4472C4]

        == Pivot Analysis
        [used B2:J12]
        [pivot PopulationByContinent source='Population Data'!B4:O7 at=B4]
        - row Continent
        - col Income Group
        - val Population 2020 sum
        - val Growth 2000-2020 avg
        - filter Snapshot Date
        [end]

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

    private const string CompatibilitySyntaxWorkbookDoc = """
        = Population Showcase Workbook

        == Population Data
        [used A1:N4]
        @A1 | Region | Country | Census Date | Population 2010 | Population 2015 | Population 2020 | Population 2025 | Growth 2010-2025 | Growth % | Density/km2 | Urban % | Active | Last Updated | Trend
        @A2 | Asia | India | 2010-07-01 | 1234281170 | 1310152403 | 1380004385 | 1450000000 | =G2-D2 | =H2/D2 | 488 | 36.6% | TRUE | 2026-04-20T09:00:00 | blank
        @A3 | Asia | China | 2010-07-01 | 1340968737 | 1371220000 | 1412600000 | 1415000000 | =G3-D3 | =H3/D3 | 153 | 64.7% | TRUE | 2026-04-20T09:05:00 | blank
        @A4 | Africa | Nigeria | 2010-07-01 | 158578261 | 181137448 | 206139589 | 230000000 | =G4-D4 | =H4/D4 | 255 | 54.3% | TRUE | 2026-04-20T09:25:00 | blank
        [cf I2:I4 gt 0.15 fill=#C6EFCE fg=#006100]
        [cf I2:I4 lt 0 fill=#FFC7CE fg=#9C0006]
        [cf D2:G4 data-bar color=#5B9BD5]
        [cf J2:J4 color-scale min=#F8696B mid=#FFEB84 max=#63BE7B]
        [spark N2 source=D2:G2 type=line color=#4472C4]
        [spark N3 source=D3:G3 type=line color=#4472C4]
        [spark N4 source=D4:G4 type=line color=#4472C4]

        == Regional Pivot
        [pivot "Population by Region" source='Population Data'!A1:N4 at=A3]
        - row Region
        - val Population 2025 sum "Sum of Population 2025"
        - val Growth 2010-2025 sum "Sum of Growth 2010-2025"
        - val Growth % avg "Average Growth %"
        - filter Active
        [end]
        """;

    private const string JsonEscapedWorkbookDoc = """
        = Population Showcase Workbook

        == Population Data
        [used B4:N6]
        @B4 | Country | Region | Population 2020 | Population 2021 | Population 2022 | Population 2023 | Area km\u00B2 | Urban % | Census Date | Last Updated | Growth 2023 | Density | Trend
        @B5 | India | Asia | 1380004385 | 1393409038 | 1406631776 | 1428627663 | 3287263 | 0.36 | 2023-03-01 | 2026-04-20T09:30:00 | =(G5-F5)/F5 | =G5/H5 | blank
        @B6 | Nigeria | Africa | 206139589 | 211400708 | 216746934 | 223804632 | 923768 | 0.54 | 2023-05-01 | 2026-04-20T09:30:00 | =(G6-F6)/F6 | =G6/H6 | blank
        [cf L5:L6 when cell \u003E 0.015 fill=#C6EFCE fg=#006100]
        [spark N5 source=D5:G5 type=line color=#4472C4]
        [spark N6 source=D6:G6 type=line color=#4472C4]

        == Pivot Region Summary
        [pivot \u0022Region Population Pivot\u0022 at=B3 source=\u0027Population Data\u0027!B4:M6]
        - row Region
        - val Population 2023 sum as \u0022Sum of Population 2023\u0022
        [end]

        == Charts
        [chart \u0022Population 2023 by Country\u0022 type=column at=B4 size=480x288px]
        - series column \u0022Population 2023\u0022 cat=\u0027Population Data\u0027!B5:B6 val=\u0027Population Data\u0027!G5:G6 color=#5B9BD5 labels
        [end]
        """;

    [TestMethod]
    public void CreateFunctionsUsesExpectedToolNames()
    {
        var functions = GoogleSheetsWorkbookTools.CreateFunctions(new GoogleSheetsWorkbookToolSetOptions());

        CollectionAssert.AreEquivalent(
            ExpectedToolNames,
            functions.Select(static function => function.Name).ToArray());
    }

    [TestMethod]
    public void GetSystemPromptGuidanceIncludesGoogleSheetsCapabilities()
    {
        var prompt = GoogleSheetsWorkbookTools.GetSystemPromptGuidance("Base prompt");

        StringAssert.Contains(prompt, "Base prompt");
        StringAssert.Contains(prompt, "Hosted Google Sheets");
        StringAssert.Contains(prompt, "gsheets://spreadsheets/{spreadsheetId}");
        StringAssert.Contains(prompt, "@B5 | Region | Revenue | Margin");
        StringAssert.Contains(prompt, "[chart \"Name\" type=column");
        StringAssert.Contains(prompt, "workbook_spec_lookup");
    }

    [TestMethod]
    public void CreateReferenceResolverRequiresCredentialInitializerApiKeyOrClient()
    {
        try
        {
            _ = GoogleSheetsWorkbookTools.CreateReferenceResolver(new GoogleSheetsWorkspaceOptions());
            Assert.Fail("Expected missing Google Sheets workspace configuration to throw an ArgumentException.");
        }
        catch (ArgumentException exception)
        {
            StringAssert.Contains(exception.Message, "Credential");
        }
    }

    [TestMethod]
    public async Task CreateFunctionsWithWorkspaceOptionsRejectsUnsupportedAlias()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = "GoogleDrive://Spreadsheets/QuarterlyRevenue",
                content = SimpleWorkbookDoc,
            }));

        Assert.IsFalse(writeResult.Success);
        StringAssert.Contains(writeResult.Message!, "unsupported alias scheme");
        StringAssert.Contains(writeResult.Message!, "gsheets://folders/root/spreadsheets/Quarterly%20Revenue");
    }

    [TestMethod]
    [DataRow("spreadsheet")]
    [DataRow("url")]
    public async Task WriteAndReadRoundTripAcrossSupportedReferenceForms(string readForm)
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleSheetsSupport.CreateFolderReference("root", "roundtrip");

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = createReference,
                content = SimpleWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.IsTrue(writeResult.PreservesWorkbookDocRoundTrip);
        Assert.AreEqual("google-sheets", writeResult.ProviderName);
        Assert.AreEqual(GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(SimpleWorkbookDoc), workspace.GetManagedWorkbookDoc(writeResult.Path));

        var readReference = readForm switch
        {
            "spreadsheet" => writeResult.Path,
            "url" => FakeGoogleSheetsWorkspaceClient.CreateSpreadsheetUrl(ExtractSpreadsheetId(writeResult.Path)),
            _ => throw new AssertInconclusiveException($"Unsupported read form '{readForm}'."),
        };

        var contents = await GoogleSheetsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = readReference,
            }));

        Assert.AreEqual(GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(SimpleWorkbookDoc), GoogleSheetsFunctionTestUtilities.ReadWorkbookDocText(contents));
    }

    [TestMethod]
    public async Task WriteThenReadRoundTripsAdvancedWorkbookDoc()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleSheetsSupport.CreateFolderReference("root", "coverage");

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = createReference,
                content = ComprehensiveWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);

        var contents = await GoogleSheetsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = writeResult.Path,
            }));

        Assert.AreEqual(GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(ComprehensiveWorkbookDoc), GoogleSheetsFunctionTestUtilities.ReadWorkbookDocText(contents));
    }

    [TestMethod]
    public async Task GeneratedGoogleSheetsBridgeProducesValidWorkbookAndManagedPayload()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = GoogleSheetsSupport.CreateFolderReference("root", "bridge"),
                content = SimpleWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        Assert.AreEqual(GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(SimpleWorkbookDoc), workspace.GetManagedWorkbookDoc(writeResult.Path));

        using var stream = new MemoryStream(workspace.GetWorkbookBytes(writeResult.Path), writable: false);
        using var spreadsheet = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(spreadsheet).ToArray();
        Assert.AreEqual(
            0,
            errors.Length,
            string.Join(Environment.NewLine, errors.Select(static error => $"{error.Path?.XPath}: {error.Description}")));

        Assert.AreEqual(GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(SimpleWorkbookDoc), ExcelWorkbookDocPayload.TryRead(spreadsheet.WorkbookPart));
        Assert.IsTrue(spreadsheet.WorkbookPart!.WorksheetParts.Any(static worksheetPart => worksheetPart.DrawingsPart is not null), "Expected chart drawing part to be generated.");
    }

    [TestMethod]
    public async Task ReadCanImportExternalSpreadsheetWithoutManagedPayload()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var spreadsheetId = workspace.SeedExternalWorkbook("external", CreateExternalWorkbookBytes());
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);

        var contents = await GoogleSheetsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
            }));

        var workbookDoc = GoogleSheetsFunctionTestUtilities.ReadWorkbookDocText(contents);
        StringAssert.Contains(workbookDoc, "== Imported");
        StringAssert.Contains(workbookDoc, "@A1 | Quarter | Revenue");
        StringAssert.Contains(workbookDoc, "@A2 | Q1 | 1200");
    }

    [TestMethod]
    public async Task ReadCanOverlayNativeMetadataForPivotsSparklinesAndConditionalFormatting()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var spreadsheetId = workspace.SeedExternalWorkbook(
            "population-showcase",
            CreateWorkbookBytesWithoutPayload(CurrentRegressionWorkbookDoc),
            nativeFeatureMetadata: GoogleSheetsNativeFeatureMetadata.FromWorkbookDoc(CurrentRegressionWorkbookDoc));
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);

        var contents = await GoogleSheetsFunctionTestUtilities.InvokeContentAsync(
            functions,
            "workbook_read_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
            }));

        var workbookDoc = GoogleSheetsFunctionTestUtilities.ReadWorkbookDocText(contents);
        StringAssert.Contains(workbookDoc, "[spark P5 source=E5:G5 type=line color=#4472C4]");
        StringAssert.Contains(workbookDoc, "[pivot PopulationByContinent source='Population Data'!B4:O7 at=B4]");
        StringAssert.Contains(workbookDoc, "[cf K5:K7 cell(>=0.8) fill=#E2F0D9]");
    }

    [TestMethod]
    public async Task HostedGrepCanSearchExplicitReferences()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var spreadsheetId = workspace.SeedExternalWorkbook("external", CreateWorkbookBytes(SimpleWorkbookDoc));
        var hostedUrl = FakeGoogleSheetsWorkspaceClient.CreateSpreadsheetUrl(spreadsheetId);
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace, GoogleSheetsFunctionTestUtilities.CreateTemporaryDirectory());

        var result = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookGrepSearchToolResult>(
            functions,
            "workbook_grep_search",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                pattern = "Revenue Trend",
                workbook_references = new[] { hostedUrl },
            }));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.Matches.Length);
        Assert.AreEqual(GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId), result.Matches[0].Path);
    }

    [TestMethod]
    public async Task EditHostedSpreadsheetUpdatesManagedPayload()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);
        var createReference = GoogleSheetsSupport.CreateFolderReference("root", "editable");

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = createReference,
                content = SimpleWorkbookDoc,
            }));

        var editResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookEditFileToolResult>(
            functions,
            "workbook_edit_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = writeResult.Path,
                old_string = "@A4 | March | 980 | [fmt=\"0.0%\"] 0.28",
                new_string = "@A4 | March | 1100 | [fmt=\"0.0%\"] 0.31",
            }));

        Assert.IsTrue(editResult.Success, editResult.Message);
        StringAssert.Contains(workspace.GetManagedWorkbookDoc(writeResult.Path)!, "1100");
        StringAssert.Contains(editResult.UpdatedWorkbookDoc!, "0.31");
    }

    [TestMethod]
    public async Task WriteStoresNativeFeatureMetadataForRegressionWorkbook()
    {
        var workspace = new FakeGoogleSheetsWorkspaceClient();
        var functions = GoogleSheetsFunctionTestUtilities.CreateFunctions(workspace);

        var writeResult = await GoogleSheetsFunctionTestUtilities.InvokeAsync<WorkbookWriteFileToolResult>(
            functions,
            "workbook_write_file",
            GoogleSheetsFunctionTestUtilities.CreateArguments(new
            {
                workbook_reference = GoogleSheetsSupport.CreateFolderReference("root", "native-features"),
                content = CurrentRegressionWorkbookDoc,
            }));

        Assert.IsTrue(writeResult.Success, writeResult.Message);
        var metadata = workspace.GetNativeFeatureMetadata(writeResult.Path);
        Assert.IsNotNull(metadata);
        CollectionAssert.Contains(metadata.SheetAdditions["Population Data"], "[spark P5 source=E5:G5 type=line color=#4472C4]");
        CollectionAssert.Contains(metadata.SheetAdditions["Population Data"], "[cf H5:H7 data-bar(color=#5B9BD5)]");
        StringAssert.Contains(string.Join('\n', metadata.SheetAdditions["Pivot Analysis"]), "[pivot PopulationByContinent source='Population Data'!B4:O7 at=B4]");
    }

    [TestMethod]
    public void NativeFeatureBridgeCreateSyncRequestsSupportsCurrentRegressionSample()
    {
        var spreadsheet = new SheetsSpreadsheet
        {
            NamedRanges = [],
            Sheets =
            [
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 1, Title = "Population Data" } },
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 2, Title = "Pivot Analysis" } },
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 3, Title = "Charts" } },
            ],
        };

        var requests = GoogleSheetsNativeFeatureBridge.CreateSyncRequests(
            spreadsheet,
            WorkbookDocParser.Parse(CurrentRegressionWorkbookDoc));

        Assert.AreEqual(4, requests.Count(static request => request.AddConditionalFormatRule is not null), "Expected all conditional-format rules from the regression sample.");
        Assert.AreEqual(3, requests.Count(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.UserEnteredValue?.FormulaValue?.StartsWith("=SPARKLINE(", StringComparison.OrdinalIgnoreCase) == true), "Expected each spark directive to be materialized.");
        Assert.AreEqual(5, requests.Count(static request => request.AddChart is not null), "Expected all chart blocks to be materialized.");
        var pivotRequest = requests.SingleOrDefault(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.PivotTable is not null);
        Assert.IsNotNull(pivotRequest, "Expected pivot directive to be materialized.");
        Assert.AreEqual(1, pivotRequest.UpdateCells!.Rows![0].Values![0].PivotTable!.Rows!.Count);
        Assert.AreEqual(1, pivotRequest.UpdateCells.Rows[0].Values[0].PivotTable!.Columns!.Count);
        Assert.AreEqual(2, pivotRequest.UpdateCells.Rows[0].Values[0].PivotTable!.Values!.Count);
        Assert.AreEqual(1, pivotRequest.UpdateCells.Rows[0].Values[0].PivotTable!.FilterSpecs!.Count);
    }

    [TestMethod]
    public void NativeFeatureBridgeCreateSyncRequestsSupportsCompatibilitySyntax()
    {
        var spreadsheet = new SheetsSpreadsheet
        {
            NamedRanges = [],
            Sheets =
            [
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 1, Title = "Population Data" } },
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 2, Title = "Regional Pivot" } },
            ],
        };

        var requests = GoogleSheetsNativeFeatureBridge.CreateSyncRequests(
            spreadsheet,
            WorkbookDocParser.Parse(CompatibilitySyntaxWorkbookDoc));

        Assert.AreEqual(4, requests.Count(static request => request.AddConditionalFormatRule is not null));
        Assert.AreEqual(3, requests.Count(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.UserEnteredValue?.FormulaValue?.StartsWith("=SPARKLINE(", StringComparison.OrdinalIgnoreCase) == true));
        var pivotRequest = requests.Single(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.PivotTable is not null);
        var pivot = pivotRequest.UpdateCells!.Rows![0].Values![0].PivotTable!;
        Assert.AreEqual(1, pivot.Rows!.Count);
        Assert.AreEqual(3, pivot.Values!.Count);
        CollectionAssert.AreEqual(
            CompatibilityPivotValueNames,
            pivot.Values.Select(static value => value.Name).ToArray());
        Assert.AreEqual(1, pivot.FilterSpecs!.Count);
    }

    [TestMethod]
    public void NativeFeatureBridgeCreateSyncRequestsSupportsJsonEscapedWorkbookDoc()
    {
        var spreadsheet = new SheetsSpreadsheet
        {
            NamedRanges = [],
            Sheets =
            [
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 1, Title = "Population Data" } },
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 2, Title = "Pivot Region Summary" } },
                new SheetsSheet { Properties = new SheetsProperties { SheetId = 3, Title = "Charts" } },
            ],
        };

        var requests = GoogleSheetsNativeFeatureBridge.CreateSyncRequests(
            spreadsheet,
            WorkbookDocParser.Parse(JsonEscapedWorkbookDoc));

        Assert.AreEqual(1, requests.Count(static request => request.AddConditionalFormatRule is not null));
        Assert.AreEqual(2, requests.Count(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.UserEnteredValue?.FormulaValue?.StartsWith("=SPARKLINE(", StringComparison.OrdinalIgnoreCase) == true));
        Assert.AreEqual(1, requests.Count(static request => request.AddChart is not null));
        var pivotRequest = requests.Single(static request => request.UpdateCells?.Rows?.FirstOrDefault()?.Values?.FirstOrDefault()?.PivotTable is not null);
        Assert.AreEqual(1, pivotRequest.UpdateCells!.Rows![0].Values![0].PivotTable!.Rows!.Count);
        Assert.AreEqual(1, pivotRequest.UpdateCells.Rows[0].Values[0].PivotTable.Values!.Count);
    }

    private static string ExtractSpreadsheetId(string spreadsheetReference)
    {
        if (!Uri.TryCreate(spreadsheetReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "gsheets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The reference '{spreadsheetReference}' is not a gsheets spreadsheet reference.");
        }

        return Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
    }

    private static byte[] CreateWorkbookBytes(string workbookDoc)
    {
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var model = WorkbookDocParser.Parse(workbookDoc);
            ExcelWorkbookWriter.Write(spreadsheet, model);
            ExcelWorkbookDocPayload.Write(spreadsheet.WorkbookPart!, workbookDoc);
            spreadsheet.PackageProperties.Title = ExtractTitle(workbookDoc);
        }

        return stream.ToArray();
    }

    private static byte[] CreateWorkbookBytesWithoutPayload(string workbookDoc)
    {
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var model = WorkbookDocParser.Parse(workbookDoc);
            ExcelWorkbookWriter.Write(spreadsheet, model);
            spreadsheet.PackageProperties.Title = ExtractTitle(workbookDoc);
        }

        return stream.ToArray();
    }

    private static byte[] CreateExternalWorkbookBytes()
    {
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheet.AddWorkbookPart();
            workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData(
                new Row(
                    new Cell { CellReference = "A1", DataType = CellValues.String, InlineString = new InlineString(new Text("Quarter")) },
                    new Cell { CellReference = "B1", DataType = CellValues.String, InlineString = new InlineString(new Text("Revenue")) }),
                new Row(
                    new Cell { CellReference = "A2", DataType = CellValues.String, InlineString = new InlineString(new Text("Q1")) },
                    new Cell { CellReference = "B2", CellValue = new CellValue("1200") }),
                new Row(
                    new Cell { CellReference = "A3", DataType = CellValues.String, InlineString = new InlineString(new Text("Q2")) },
                    new Cell { CellReference = "B3", CellValue = new CellValue("1400") }));
            worksheetPart.Worksheet = new Worksheet(new SheetDimension { Reference = "A1:B3" }, sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Imported",
            });
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static string ExtractTitle(string workbookDoc)
    {
        var firstLine = workbookDoc.Split('\n').FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
        return firstLine is not null && firstLine.StartsWith("= ", StringComparison.Ordinal)
            ? firstLine[2..].Trim()
            : "AIToolkit Workbook";
    }
}
