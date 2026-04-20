using AIToolkit.Tools.Workbook.Excel;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;

namespace AIToolkit.Tools.Workbook.GoogleSheets.Tests;

internal sealed class FakeGoogleSheetsWorkspaceClient : IGoogleSheetsWorkspaceClient
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredGoogleSheet> _spreadsheets = new(StringComparer.Ordinal);
    private long _nextSpreadsheetId = 1_000;

    public ValueTask<GoogleSheetsWorkbookLocation?> ResolveAsync(
        string workbookReference,
        WorkbookToolOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryParseUnsupportedHostedAlias(workbookReference))
        {
            throw new InvalidOperationException(
                $"The Google Sheets hosted workbook reference '{workbookReference}' uses an unsupported alias scheme. " +
                "Use a Google Sheets URL, gsheets://spreadsheets/{spreadsheetId}, or gsheets://folders/{folderId}/spreadsheets/{title}. " +
                "To create a new Google Sheet in Drive root, use a reference such as gsheets://folders/root/spreadsheets/Quarterly%20Revenue.");
        }

        lock (_gate)
        {
            if (TryParseSpreadsheetUrl(workbookReference, out var spreadsheetId)
                || TryParseSpreadsheetReference(workbookReference, out spreadsheetId))
            {
                return ValueTask.FromResult<GoogleSheetsWorkbookLocation?>(ResolveBySpreadsheetId(spreadsheetId));
            }

            if (TryParseFolderReference(workbookReference, out var folderId, out var title))
            {
                return ValueTask.FromResult<GoogleSheetsWorkbookLocation?>(ResolveByFolderAndTitle(folderId, title));
            }
        }

        return ValueTask.FromResult<GoogleSheetsWorkbookLocation?>(null);
    }

    public ValueTask<string?> TryReadManagedWorkbookDocAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                !string.IsNullOrWhiteSpace(location.SpreadsheetId) && _spreadsheets.TryGetValue(location.SpreadsheetId, out var stored)
                    ? stored.ManagedWorkbookDoc
                    : null);
        }
    }

    public ValueTask<GoogleSheetsNativeFeatureMetadata?> TryReadNativeFeatureMetadataAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                !string.IsNullOrWhiteSpace(location.SpreadsheetId) && _spreadsheets.TryGetValue(location.SpreadsheetId, out var stored)
                    ? stored.NativeFeatureMetadata
                    : null);
        }
    }

    public ValueTask<Stream> OpenExportReadAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(location.SpreadsheetId) || !_spreadsheets.TryGetValue(location.SpreadsheetId, out var stored))
            {
                throw new FileNotFoundException($"The Google Sheet '{location.ResolvedReference}' was not found.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(stored.XlsxBytes, writable: false));
        }
    }

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new GoogleSheetsUploadOnDisposeMemoryStream(
            (content, innerCancellationToken) => PersistSnapshotAsync(location, content, innerCancellationToken),
            cancellationToken));

    public string SeedExternalWorkbook(
        string title,
        byte[] xlsxBytes,
        string? managedWorkbookDoc = null,
        string folderId = "root",
        GoogleSheetsNativeFeatureMetadata? nativeFeatureMetadata = null)
    {
        lock (_gate)
        {
            var spreadsheetId = Interlocked.Increment(ref _nextSpreadsheetId).ToString(CultureInfo.InvariantCulture);
            _spreadsheets[spreadsheetId] = new StoredGoogleSheet(
                SpreadsheetId: spreadsheetId,
                FolderId: folderId,
                Title: title,
                XlsxBytes: xlsxBytes,
                ManagedWorkbookDoc: managedWorkbookDoc,
                NativeFeatureMetadata: nativeFeatureMetadata,
                Version: 1);
            return spreadsheetId;
        }
    }

    public static string CreateSpreadsheetUrl(string spreadsheetId) =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";

    public byte[] GetWorkbookBytes(string resolvedReference)
    {
        lock (_gate)
        {
            var spreadsheetId = ResolveSpreadsheetId(resolvedReference);
            return [.. _spreadsheets[spreadsheetId].XlsxBytes];
        }
    }

    public string? GetManagedWorkbookDoc(string resolvedReference)
    {
        lock (_gate)
        {
            var spreadsheetId = ResolveSpreadsheetId(resolvedReference);
            return _spreadsheets[spreadsheetId].ManagedWorkbookDoc;
        }
    }

    public GoogleSheetsNativeFeatureMetadata? GetNativeFeatureMetadata(string resolvedReference)
    {
        lock (_gate)
        {
            var spreadsheetId = ResolveSpreadsheetId(resolvedReference);
            return _spreadsheets[spreadsheetId].NativeFeatureMetadata;
        }
    }

    private ValueTask PersistSnapshotAsync(
        GoogleSheetsWorkbookLocation location,
        Stream content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var snapshot = buffer.ToArray();
        var managedWorkbookDoc = ExtractManagedWorkbookDoc(snapshot);
        var nativeFeatureMetadata = managedWorkbookDoc is null ? null : GoogleSheetsNativeFeatureMetadata.FromWorkbookDoc(managedWorkbookDoc);
        var title = location.Title ?? ExtractTitle(snapshot) ?? "AIToolkit Workbook";

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(location.SpreadsheetId) && _spreadsheets.TryGetValue(location.SpreadsheetId, out var existing))
            {
                _spreadsheets[location.SpreadsheetId] = existing with
                {
                    Title = title,
                    XlsxBytes = snapshot,
                    ManagedWorkbookDoc = managedWorkbookDoc,
                    NativeFeatureMetadata = nativeFeatureMetadata,
                    Version = existing.Version + 1,
                };
            }
            else
            {
                var spreadsheetId = Interlocked.Increment(ref _nextSpreadsheetId).ToString(CultureInfo.InvariantCulture);
                _spreadsheets[spreadsheetId] = new StoredGoogleSheet(
                    SpreadsheetId: spreadsheetId,
                    FolderId: location.FolderId ?? "root",
                    Title: title,
                    XlsxBytes: snapshot,
                    ManagedWorkbookDoc: managedWorkbookDoc,
                    NativeFeatureMetadata: nativeFeatureMetadata,
                    Version: 1);
            }
        }

        return ValueTask.CompletedTask;
    }

    private GoogleSheetsWorkbookLocation ResolveBySpreadsheetId(string spreadsheetId)
    {
        if (_spreadsheets.TryGetValue(spreadsheetId, out var stored))
        {
            return new GoogleSheetsWorkbookLocation(
                SpreadsheetId: stored.SpreadsheetId,
                FolderId: stored.FolderId,
                Title: stored.Title,
                ResolvedReference: GoogleSheetsSupport.CreateSpreadsheetReference(stored.SpreadsheetId),
                ReadStateKey: GoogleSheetsSupport.CreateSpreadsheetReference(stored.SpreadsheetId),
                Exists: true,
                Version: stored.Version.ToString(CultureInfo.InvariantCulture),
                Length: stored.XlsxBytes.Length);
        }

        return new GoogleSheetsWorkbookLocation(
            SpreadsheetId: spreadsheetId,
            FolderId: null,
            Title: null,
            ResolvedReference: GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
            ReadStateKey: GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
            Exists: false,
            Version: null,
            Length: null);
    }

    private GoogleSheetsWorkbookLocation ResolveByFolderAndTitle(string folderId, string title)
    {
        var normalizedFolderId = NormalizeFolderId(folderId);
        var matches = _spreadsheets.Values
            .Where(sheet => string.Equals(sheet.FolderId, normalizedFolderId, StringComparison.OrdinalIgnoreCase))
            .Where(sheet => string.Equals(sheet.Title, title, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"The Google Sheets reference '{GoogleSheetsSupport.CreateFolderReference(normalizedFolderId, title)}' is ambiguous because multiple Google Sheets in that folder share the same title.");
        }

        if (matches.Length == 1)
        {
            return ResolveBySpreadsheetId(matches[0].SpreadsheetId);
        }

        var resolvedReference = GoogleSheetsSupport.CreateFolderReference(normalizedFolderId, title);
        return new GoogleSheetsWorkbookLocation(
            SpreadsheetId: null,
            FolderId: normalizedFolderId,
            Title: title,
            ResolvedReference: resolvedReference,
            ReadStateKey: resolvedReference,
            Exists: false,
            Version: null,
            Length: null);
    }

    private static string ResolveSpreadsheetId(string resolvedReference)
    {
        if (TryParseSpreadsheetUrl(resolvedReference, out var spreadsheetId)
            || TryParseSpreadsheetReference(resolvedReference, out spreadsheetId))
        {
            return spreadsheetId;
        }

        throw new InvalidOperationException($"The reference '{resolvedReference}' is not a Google Sheets spreadsheet reference.");
    }

    private static string? ExtractManagedWorkbookDoc(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        var payload = ExcelWorkbookDocPayload.TryRead(workbook.WorkbookPart);
        return payload is null ? null : GoogleSheetsFunctionTestUtilities.NormalizeLineEndings(payload);
    }

    private static string? ExtractTitle(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        return workbook.PackageProperties.Title;
    }

    private static string NormalizeFolderId(string folderId) =>
        string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase) ? "root" : folderId;

    private static bool TryParseUnsupportedHostedAlias(string workbookReference) =>
        workbookReference.StartsWith("GoogleDrive://", StringComparison.OrdinalIgnoreCase)
        || workbookReference.StartsWith("GoogleSheets://", StringComparison.OrdinalIgnoreCase)
        || workbookReference.StartsWith("gsheet://", StringComparison.OrdinalIgnoreCase)
        || workbookReference.StartsWith("gdrive://", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseSpreadsheetUrl(string workbookReference, out string spreadsheetId)
    {
        spreadsheetId = string.Empty;
        if (!Uri.TryCreate(workbookReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "spreadsheets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "d", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        spreadsheetId = Uri.UnescapeDataString(segments[2]);
        return spreadsheetId.Length > 0;
    }

    private static bool TryParseSpreadsheetReference(string workbookReference, out string spreadsheetId)
    {
        spreadsheetId = string.Empty;
        if (!Uri.TryCreate(workbookReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "gsheets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        spreadsheetId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        return spreadsheetId.Length > 0;
    }

    private static bool TryParseFolderReference(string workbookReference, out string folderId, out string title)
    {
        folderId = string.Empty;
        title = string.Empty;
        if (!Uri.TryCreate(workbookReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "gsheets", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "folders", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !string.Equals(segments[1], "spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        folderId = Uri.UnescapeDataString(segments[0]);
        title = Uri.UnescapeDataString(string.Join('/', segments.Skip(2)));
        return folderId.Length > 0 && title.Length > 0;
    }

    private sealed record StoredGoogleSheet(
        string SpreadsheetId,
        string FolderId,
        string Title,
        byte[] XlsxBytes,
        string? ManagedWorkbookDoc,
        GoogleSheetsNativeFeatureMetadata? NativeFeatureMetadata,
        long Version);
}
