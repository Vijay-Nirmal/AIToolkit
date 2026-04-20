using System.Globalization;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Collects Google Sheets-specific constants and reference-format helpers.
/// </summary>
internal static class GoogleSheetsSupport
{
    internal const string GoogleSheetsMimeType = "application/vnd.google-apps.spreadsheet";
    internal const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    internal const string PayloadProviderKey = "aitoolkitProvider";
    internal const string PayloadProviderValue = "google-sheets";
    internal const string PayloadSpreadsheetIdKey = "aitoolkitSpreadsheetId";

    public static string CreateSpreadsheetReference(string spreadsheetId) =>
        $"gsheets://spreadsheets/{Uri.EscapeDataString(spreadsheetId)}";

    public static string CreateFolderReference(string folderId, string title) =>
        $"gsheets://folders/{Uri.EscapeDataString(folderId)}/spreadsheets/{Uri.EscapeDataString(title)}";

    public static string NormalizeVersion(long? version, DateTimeOffset? modifiedTimeUtc) =>
        version is long numericVersion
            ? numericVersion.ToString(CultureInfo.InvariantCulture)
            : modifiedTimeUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture)
            ?? string.Empty;
}
