using AIToolkit.Tools.Workbook.Excel;
using DocumentFormat.OpenXml.Packaging;
using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using System.Text;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace AIToolkit.Tools.Workbook.GoogleSheets;

/// <summary>
/// Encapsulates Google Drive operations for Google Sheets references and their managed payload sidecars.
/// </summary>
internal interface IGoogleSheetsWorkspaceClient
{
    ValueTask<GoogleSheetsWorkbookLocation?> ResolveAsync(
        string workbookReference,
        WorkbookToolOperation operation,
        CancellationToken cancellationToken = default);

    ValueTask<string?> TryReadManagedWorkbookDocAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default);

    ValueTask<GoogleSheetsNativeFeatureMetadata?> TryReadNativeFeatureMetadataAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenExportReadAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenUploadWriteAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a hosted Google Sheets location after reference normalization.
/// </summary>
internal sealed record GoogleSheetsWorkbookLocation(
    string? SpreadsheetId,
    string? FolderId,
    string? Title,
    string ResolvedReference,
    string ReadStateKey,
    bool Exists,
    string? Version,
    long? Length);

internal static class GoogleSheetsWorkspaceClientFactory
{
    public static IGoogleSheetsWorkspaceClient Create(GoogleSheetsWorkspaceOptions? options)
    {
        if (options?.Client is not null)
        {
            return options.Client;
        }

        return options?.Credential is not null || options?.HttpClientInitializer is not null || !string.IsNullOrWhiteSpace(options?.ApiKey)
            ? new GoogleDriveSheetsWorkspaceClient(options)
            : new UnconfiguredGoogleSheetsWorkspaceClient();
    }
}

internal sealed class UnconfiguredGoogleSheetsWorkspaceClient : IGoogleSheetsWorkspaceClient
{
    public ValueTask<GoogleSheetsWorkbookLocation?> ResolveAsync(
        string workbookReference,
        WorkbookToolOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (LooksLikeGoogleSheetsReference(workbookReference))
        {
            throw new InvalidOperationException(
                "Google Sheets hosted workbook support is not configured. Set GoogleSheetsWorkbookHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using gsheets:// references or Google Sheets URLs.");
        }

        return ValueTask.FromResult<GoogleSheetsWorkbookLocation?>(null);
    }

    public ValueTask<string?> TryReadManagedWorkbookDocAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Sheets hosted workbook support is not configured. Set GoogleSheetsWorkbookHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Sheets references.");

    public ValueTask<GoogleSheetsNativeFeatureMetadata?> TryReadNativeFeatureMetadataAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Sheets hosted workbook support is not configured. Set GoogleSheetsWorkbookHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Sheets references.");

    public ValueTask<Stream> OpenExportReadAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Sheets hosted workbook support is not configured. Set GoogleSheetsWorkbookHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Sheets references.");

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Sheets hosted workbook support is not configured. Set GoogleSheetsWorkbookHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Sheets references.");

    private static bool LooksLikeGoogleSheetsReference(string workbookReference) =>
        workbookReference.StartsWith("gsheets://", StringComparison.OrdinalIgnoreCase)
        || workbookReference.StartsWith("https://docs.google.com/spreadsheets/", StringComparison.OrdinalIgnoreCase);
}

internal sealed class GoogleDriveSheetsWorkspaceClient : IGoogleSheetsWorkspaceClient, IDisposable
{
    private static readonly string[] DefaultScopes =
    [
        DriveService.Scope.Drive,
        DriveService.Scope.DriveAppdata,
        SheetsService.Scope.Spreadsheets,
    ];

    private readonly DriveService _driveService;
    private readonly SheetsService _sheetsService;

    public GoogleDriveSheetsWorkspaceClient(GoogleSheetsWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credential = options.Credential;
        var httpClientInitializer = options.HttpClientInitializer ?? credential?.CreateScoped(NormalizeScopes(options.Scopes));
        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey;
        if (httpClientInitializer is null && apiKey is null)
        {
            throw new ArgumentException("GoogleSheetsWorkspaceOptions.Credential, GoogleSheetsWorkspaceOptions.HttpClientInitializer, or GoogleSheetsWorkspaceOptions.ApiKey is required when Google Sheets hosted workbook support is enabled.", nameof(options));
        }

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = string.IsNullOrWhiteSpace(options.ApplicationName)
                ? "AIToolkit.Tools.Workbook.GoogleSheets"
                : options.ApplicationName,
            ApiKey = apiKey,
            HttpClientInitializer = httpClientInitializer,
        });
        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            ApplicationName = string.IsNullOrWhiteSpace(options.ApplicationName)
                ? "AIToolkit.Tools.Workbook.GoogleSheets"
                : options.ApplicationName,
            ApiKey = apiKey,
            HttpClientInitializer = httpClientInitializer,
        });
    }

    public async ValueTask<GoogleSheetsWorkbookLocation?> ResolveAsync(
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

        if (TryParseSpreadsheetUrl(workbookReference, out var spreadsheetId))
        {
            return await ResolveBySpreadsheetIdAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        }

        if (TryParseSpreadsheetReference(workbookReference, out spreadsheetId))
        {
            return await ResolveBySpreadsheetIdAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        }

        if (TryParseFolderReference(workbookReference, out var folderId, out var title))
        {
            return await ResolveByFolderAndTitleAsync(folderId, title, operation, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async ValueTask<string?> TryReadManagedWorkbookDocAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!location.Exists || string.IsNullOrWhiteSpace(location.SpreadsheetId))
        {
            return null;
        }

        var payloadFile = await TryFindPayloadFileAsync(location.SpreadsheetId, cancellationToken).ConfigureAwait(false);
        if (payloadFile?.Id is null)
        {
            return null;
        }

        var request = _driveService.Files.Get(payloadFile.Id);
        await using var stream = new MemoryStream();
        _ = await request.DownloadAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new StreamReader(stream, leaveOpen: true);
        return NormalizeLineEndings(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<GoogleSheetsNativeFeatureMetadata?> TryReadNativeFeatureMetadataAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!location.Exists || string.IsNullOrWhiteSpace(location.SpreadsheetId))
        {
            return null;
        }

        var spreadsheet = await GetSpreadsheetForMetadataAsync(location.SpreadsheetId, cancellationToken).ConfigureAwait(false);
        var metadata = GoogleSheetsNativeFeatureBridge.ExtractMetadata(spreadsheet);
        return metadata.WorkbookDirectives.Count == 0 && metadata.SheetAdditions.Count == 0
            ? null
            : metadata;
    }

    public async ValueTask<Stream> OpenExportReadAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        if (!location.Exists || string.IsNullOrWhiteSpace(location.SpreadsheetId))
        {
            throw new FileNotFoundException($"The Google Sheet '{location.ResolvedReference}' was not found.");
        }

        var request = _driveService.Files.Export(location.SpreadsheetId, GoogleSheetsSupport.XlsxMimeType);
        var stream = new MemoryStream();
        _ = await request.DownloadAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return stream;
    }

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleSheetsWorkbookLocation location,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new GoogleSheetsUploadOnDisposeMemoryStream(
            (content, innerCancellationToken) => new ValueTask(PersistSnapshotAsync(location, content, innerCancellationToken)),
            cancellationToken));

    public void Dispose()
    {
        _driveService.Dispose();
        _sheetsService.Dispose();
    }

    private async Task SyncNativeFeaturesAsync(string spreadsheetId, string workbookDoc, CancellationToken cancellationToken)
    {
        var spreadsheet = await GetSpreadsheetForSyncAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        var requests = GoogleSheetsNativeFeatureBridge.CreateSyncRequests(spreadsheet, WorkbookDocParser.Parse(workbookDoc));
        if (requests.Count == 0)
        {
            return;
        }

        var request = _sheetsService.Spreadsheets.BatchUpdate(
            new BatchUpdateSpreadsheetRequest
            {
                Requests = requests,
            },
            spreadsheetId);
        _ = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Spreadsheet> GetSpreadsheetForSyncAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        var request = _sheetsService.Spreadsheets.Get(spreadsheetId);
        request.Fields = "namedRanges(namedRangeId,name,range),sheets(properties(sheetId,title),charts(chartId),conditionalFormats)";
        return await request.ExecuteAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Google Sheets did not return spreadsheet metadata for '{spreadsheetId}'.");
    }

    private async Task<Spreadsheet> GetSpreadsheetForMetadataAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        var request = _sheetsService.Spreadsheets.Get(spreadsheetId);
        request.IncludeGridData = true;
        request.Fields = "namedRanges(name,range),sheets(properties(sheetId,title),conditionalFormats,charts,data(startRow,startColumn,rowData(values(formattedValue,userEnteredValue,pivotTable))))";
        return await request.ExecuteAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Google Sheets did not return spreadsheet metadata for '{spreadsheetId}'.");
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes) =>
        scopes?.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray()
        ?? DefaultScopes;

    private async Task<GoogleSheetsWorkbookLocation> ResolveBySpreadsheetIdAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        var file = await TryGetGoogleSheetAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        return file is null
            ? new GoogleSheetsWorkbookLocation(
                SpreadsheetId: spreadsheetId,
                FolderId: null,
                Title: null,
                ResolvedReference: GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
                ReadStateKey: GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId),
                Exists: false,
                Version: null,
                Length: null)
            : CreateExistingLocation(file);
    }

    private async Task<GoogleSheetsWorkbookLocation> ResolveByFolderAndTitleAsync(
        string folderId,
        string title,
        WorkbookToolOperation operation,
        CancellationToken cancellationToken)
    {
        var files = await ListGoogleSheetsByFolderAndTitleAsync(folderId, title, cancellationToken).ConfigureAwait(false);
        if (files.Count > 1)
        {
            throw new InvalidOperationException(
                $"The Google Sheets reference '{GoogleSheetsSupport.CreateFolderReference(folderId, title)}' is ambiguous because multiple Google Sheets in that folder share the same title.");
        }

        if (files.Count == 1)
        {
            return CreateExistingLocation(files[0]);
        }

        var normalizedFolderId = NormalizeFolderId(folderId);
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

    private async Task PersistSnapshotAsync(
        GoogleSheetsWorkbookLocation location,
        Stream content,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var snapshot = buffer.ToArray();

        var workbookDoc = ExtractCanonicalWorkbookDoc(snapshot);
        var title = location.Title ?? ExtractWorkbookTitle(snapshot) ?? "AIToolkit Workbook";

        DriveFile storedFile;
        await using var uploadStream = new MemoryStream(snapshot, writable: false);
        if (location.Exists && !string.IsNullOrWhiteSpace(location.SpreadsheetId))
        {
            storedFile = await UpdateSpreadsheetAsync(location.SpreadsheetId, title, uploadStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            storedFile = await CreateSpreadsheetAsync(location.FolderId, title, uploadStream, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(storedFile.Id))
        {
            throw new InvalidOperationException("Google Drive did not return a spreadsheet ID after the Google Sheets write completed.");
        }

        await UpsertPayloadAsync(storedFile.Id, workbookDoc, cancellationToken).ConfigureAwait(false);
        await SyncNativeFeaturesAsync(storedFile.Id, workbookDoc, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractCanonicalWorkbookDoc(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        var payload = ExcelWorkbookDocPayload.TryRead(workbook.WorkbookPart);
        return !string.IsNullOrWhiteSpace(payload)
            ? NormalizeLineEndings(payload)
            : ExcelWorkbookImporter.Import(workbook, GoogleSheetsSupport.CreateSpreadsheetReference("import"));
    }

    private static string? ExtractWorkbookTitle(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        return workbook.PackageProperties.Title;
    }

    private async Task<DriveFile?> TryGetGoogleSheetAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        try
        {
            var request = _driveService.Files.Get(spreadsheetId);
            request.Fields = "id,name,mimeType,version,size,modifiedTime,trashed,parents";
            var file = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (file is null || string.Equals(file.Trashed?.ToString(), bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(file.MimeType, GoogleSheetsSupport.GoogleSheetsMimeType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The Google Drive file '{spreadsheetId}' is not a Google Sheets spreadsheet.");
            }

            return file;
        }
        catch (Google.GoogleApiException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IList<DriveFile>> ListGoogleSheetsByFolderAndTitleAsync(string folderId, string title, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Fields = "files(id,name,mimeType,version,size,modifiedTime,trashed,parents)";
        request.Q = $"mimeType='{GoogleSheetsSupport.GoogleSheetsMimeType}' and trashed=false and name='{EscapeQueryLiteral(title)}' and '{EscapeQueryLiteral(NormalizeFolderId(folderId))}' in parents";
        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return response.Files ?? [];
    }

    private async Task<DriveFile?> TryFindPayloadFileAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Spaces = "appDataFolder";
        request.Fields = "files(id,name,appProperties)";
        request.Q =
            $"trashed=false and '{EscapeQueryLiteral("appDataFolder")}' in parents and " +
            $"appProperties has {{ key='{GoogleSheetsSupport.PayloadProviderKey}' and value='{GoogleSheetsSupport.PayloadProviderValue}' }} and " +
            $"appProperties has {{ key='{GoogleSheetsSupport.PayloadSpreadsheetIdKey}' and value='{EscapeQueryLiteral(spreadsheetId)}' }}";

        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return response.Files?.SingleOrDefault();
    }

    private async Task<DriveFile> CreateSpreadsheetAsync(string? folderId, string title, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = title,
            MimeType = GoogleSheetsSupport.GoogleSheetsMimeType,
        };

        if (!string.IsNullOrWhiteSpace(folderId) && !string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase))
        {
            metadata.Parents = [folderId];
        }

        var request = _driveService.Files.Create(metadata, content, GoogleSheetsSupport.XlsxMimeType);
        request.Fields = "id,name,mimeType,version,size,modifiedTime,parents";
        return await ExecuteUploadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DriveFile> UpdateSpreadsheetAsync(string spreadsheetId, string title, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = title,
            MimeType = GoogleSheetsSupport.GoogleSheetsMimeType,
        };

        var request = _driveService.Files.Update(metadata, spreadsheetId, content, GoogleSheetsSupport.XlsxMimeType);
        request.Fields = "id,name,mimeType,version,size,modifiedTime,parents";
        return await ExecuteUploadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertPayloadAsync(string spreadsheetId, string workbookDoc, CancellationToken cancellationToken)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(workbookDoc);
        var existing = await TryFindPayloadFileAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        await using var stream = new MemoryStream(payloadBytes, writable: false);

        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            var metadata = new DriveFile
            {
                Name = $"aitoolkit-{spreadsheetId}.wbdoc",
                AppProperties = new Dictionary<string, string>
                {
                    [GoogleSheetsSupport.PayloadProviderKey] = GoogleSheetsSupport.PayloadProviderValue,
                    [GoogleSheetsSupport.PayloadSpreadsheetIdKey] = spreadsheetId,
                },
            };

            var updateRequest = _driveService.Files.Update(metadata, existing.Id, stream, "text/plain");
            updateRequest.Fields = "id";
            _ = await ExecuteUploadAsync(updateRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        var createMetadata = new DriveFile
        {
            Name = $"aitoolkit-{spreadsheetId}.wbdoc",
            MimeType = "text/plain",
            Parents = ["appDataFolder"],
            AppProperties = new Dictionary<string, string>
            {
                [GoogleSheetsSupport.PayloadProviderKey] = GoogleSheetsSupport.PayloadProviderValue,
                [GoogleSheetsSupport.PayloadSpreadsheetIdKey] = spreadsheetId,
            },
        };

        var createRequest = _driveService.Files.Create(createMetadata, stream, "text/plain");
        createRequest.Fields = "id";
        _ = await ExecuteUploadAsync(createRequest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DriveFile> ExecuteUploadAsync<TUpload>(TUpload request, CancellationToken cancellationToken)
        where TUpload : ResumableUpload<DriveFile, DriveFile>
    {
        var progress = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
        if (progress.Status is not UploadStatus.Completed)
        {
            throw new InvalidOperationException($"Google Drive upload did not complete successfully. Final status: {progress.Status.ToString()}. {progress.Exception?.Message}".Trim());
        }

        return request.ResponseBody
            ?? throw new InvalidOperationException("Google Drive did not return file metadata after the upload completed.");
    }

    private static GoogleSheetsWorkbookLocation CreateExistingLocation(DriveFile file)
    {
        var spreadsheetId = file.Id
            ?? throw new InvalidOperationException("Google Drive did not return a spreadsheet ID.");
        var resolvedReference = GoogleSheetsSupport.CreateSpreadsheetReference(spreadsheetId);
        var folderId = file.Parents?.FirstOrDefault();
        return new GoogleSheetsWorkbookLocation(
            SpreadsheetId: spreadsheetId,
            FolderId: folderId,
            Title: file.Name,
            ResolvedReference: resolvedReference,
            ReadStateKey: resolvedReference,
            Exists: true,
            Version: GoogleSheetsSupport.NormalizeVersion(file.Version, file.ModifiedTimeDateTimeOffset),
            Length: file.Size);
    }

    private static string NormalizeFolderId(string folderId) =>
        string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase) ? "root" : folderId;

    private static string EscapeQueryLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
}

internal sealed class GoogleSheetsUploadOnDisposeMemoryStream(
    Func<Stream, CancellationToken, ValueTask> persistAsync,
    CancellationToken cancellationToken) : MemoryStream
{
    private readonly Func<Stream, CancellationToken, ValueTask> _persistAsync = persistAsync;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly object _persistLock = new();
    private Task? _persistTask;

    public override async ValueTask DisposeAsync()
    {
        await BeginPersistAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ = BeginPersistAsync();
        }

        base.Dispose(disposing);
    }

    private Task BeginPersistAsync()
    {
        lock (_persistLock)
        {
            if (_persistTask is not null)
            {
                return _persistTask;
            }

            var snapshot = ToArray();
            _persistTask = PersistSnapshotAsync(snapshot, _cancellationToken);
            return _persistTask;
        }
    }

    private async Task PersistSnapshotAsync(byte[] snapshot, CancellationToken cancellationToken)
    {
        await using var snapshotStream = new MemoryStream(snapshot, writable: false);
        await _persistAsync(snapshotStream, cancellationToken).ConfigureAwait(false);
    }
}
