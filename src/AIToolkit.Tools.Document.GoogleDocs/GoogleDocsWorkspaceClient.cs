using AIToolkit.Tools.Document.Word;
using DocumentFormat.OpenXml.Packaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Services;
using Google.Apis.Upload;
using System.Globalization;

namespace AIToolkit.Tools.Document.GoogleDocs;

/// <summary>
/// Encapsulates Google Drive operations for Google Docs references and their managed payload sidecars.
/// </summary>
internal interface IGoogleDocsWorkspaceClient
{
    ValueTask<GoogleDocsDocumentLocation?> ResolveAsync(
        string documentReference,
        DocumentToolOperation operation,
        CancellationToken cancellationToken = default);

    ValueTask<string?> TryReadManagedAsciiDocAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenExportReadAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenUploadWriteAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default);
}

internal sealed record GoogleDocsDocumentLocation(
    string? DocumentId,
    string? FolderId,
    string? Title,
    string ResolvedReference,
    string ReadStateKey,
    bool Exists,
    string? Version,
    long? Length);

internal static class GoogleDocsWorkspaceClientFactory
{
    public static IGoogleDocsWorkspaceClient Create(GoogleDocsWorkspaceOptions? options)
    {
        if (options?.Client is not null)
        {
            return options.Client;
        }

        return options?.Credential is not null || options?.HttpClientInitializer is not null || !string.IsNullOrWhiteSpace(options?.ApiKey)
            ? new GoogleDriveDocsWorkspaceClient(options)
            : new UnconfiguredGoogleDocsWorkspaceClient();
    }
}

internal sealed class UnconfiguredGoogleDocsWorkspaceClient : IGoogleDocsWorkspaceClient
{
    public ValueTask<GoogleDocsDocumentLocation?> ResolveAsync(
        string documentReference,
        DocumentToolOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (LooksLikeGoogleDocsReference(documentReference))
        {
            throw new InvalidOperationException(
                "Google Docs hosted document support is not configured. Set GoogleDocsDocumentHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using gdocs:// references or Google Docs URLs.");
        }

        return ValueTask.FromResult<GoogleDocsDocumentLocation?>(null);
    }

    public ValueTask<string?> TryReadManagedAsciiDocAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Docs hosted document support is not configured. Set GoogleDocsDocumentHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Docs references.");

    public ValueTask<Stream> OpenExportReadAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Docs hosted document support is not configured. Set GoogleDocsDocumentHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Docs references.");

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Google Docs hosted document support is not configured. Set GoogleDocsDocumentHandlerOptions.Workspace with a caller-supplied GoogleCredential, HttpClientInitializer, or ApiKey before using Google Docs references.");

    private static bool LooksLikeGoogleDocsReference(string documentReference) =>
        documentReference.StartsWith("gdocs://", StringComparison.OrdinalIgnoreCase)
        || documentReference.StartsWith("https://docs.google.com/document/", StringComparison.OrdinalIgnoreCase);
}

internal sealed class GoogleDriveDocsWorkspaceClient : IGoogleDocsWorkspaceClient, IDisposable
{
    private static readonly string[] DefaultScopes =
    [
        DriveService.Scope.Drive,
        DriveService.Scope.DriveAppdata,
    ];

    private readonly DriveService _driveService;

    public GoogleDriveDocsWorkspaceClient(GoogleDocsWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credential = options.Credential;
        var httpClientInitializer = options.HttpClientInitializer ?? credential?.CreateScoped(NormalizeScopes(options.Scopes));
        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey;
        if (httpClientInitializer is null && apiKey is null)
        {
            throw new ArgumentException("GoogleDocsWorkspaceOptions.Credential, GoogleDocsWorkspaceOptions.HttpClientInitializer, or GoogleDocsWorkspaceOptions.ApiKey is required when Google Docs hosted document support is enabled.", nameof(options));
        }

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = string.IsNullOrWhiteSpace(options.ApplicationName)
                ? "AIToolkit.Tools.Document.GoogleDocs"
                : options.ApplicationName,
            ApiKey = apiKey,
            HttpClientInitializer = httpClientInitializer,
        });
    }

    public async ValueTask<GoogleDocsDocumentLocation?> ResolveAsync(
        string documentReference,
        DocumentToolOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryParseUnsupportedHostedAlias(documentReference))
        {
            throw new InvalidOperationException(
                $"The Google Docs hosted document reference '{documentReference}' uses an unsupported alias scheme. " +
                "Use a Google Docs URL, gdocs://documents/{documentId}, or gdocs://folders/{folderId}/documents/{title}. " +
                "To create a new Google Doc in Drive root, use a reference such as gdocs://folders/root/documents/Release%20Notes.");
        }

        if (TryParseDocumentUrl(documentReference, out var documentId))
        {
            return await ResolveByDocumentIdAsync(documentId, cancellationToken).ConfigureAwait(false);
        }

        if (TryParseDocumentReference(documentReference, out documentId))
        {
            return await ResolveByDocumentIdAsync(documentId, cancellationToken).ConfigureAwait(false);
        }

        if (TryParseFolderReference(documentReference, out var folderId, out var title))
        {
            return await ResolveByFolderAndTitleAsync(folderId, title, operation, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async ValueTask<string?> TryReadManagedAsciiDocAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!location.Exists || string.IsNullOrWhiteSpace(location.DocumentId))
        {
            return null;
        }

        var payloadFile = await TryFindPayloadFileAsync(location.DocumentId, cancellationToken).ConfigureAwait(false);
        if (payloadFile?.Id is null)
        {
            return null;
        }

        var request = _driveService.Files.Get(payloadFile.Id);
        await using var stream = new MemoryStream();
        _ = await request.DownloadAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new StreamReader(stream, leaveOpen: true);
        return WordAsciiDocTextUtilities.NormalizeLineEndings(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<Stream> OpenExportReadAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default)
    {
        if (!location.Exists || string.IsNullOrWhiteSpace(location.DocumentId))
        {
            throw new FileNotFoundException($"The Google Doc '{location.ResolvedReference}' was not found.");
        }

        var request = _driveService.Files.Export(location.DocumentId, GoogleDocsSupport.DocxMimeType);
        var stream = new MemoryStream();
        _ = await request.DownloadAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return stream;
    }

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new GoogleDocsUploadOnDisposeMemoryStream(
            (content, innerCancellationToken) => new ValueTask(PersistSnapshotAsync(location, content, innerCancellationToken)),
            cancellationToken));

    public void Dispose() =>
        _driveService.Dispose();

    private static string[] NormalizeScopes(IEnumerable<string>? scopes) =>
        scopes?.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray()
        ?? DefaultScopes;

    private async Task<GoogleDocsDocumentLocation> ResolveByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        var file = await TryGetGoogleDocAsync(documentId, cancellationToken).ConfigureAwait(false);
        return file is null
            ? new GoogleDocsDocumentLocation(
                DocumentId: documentId,
                FolderId: null,
                Title: null,
                ResolvedReference: GoogleDocsSupport.CreateDocumentReference(documentId),
                ReadStateKey: GoogleDocsSupport.CreateDocumentReference(documentId),
                Exists: false,
                Version: null,
                Length: null)
            : CreateExistingLocation(file);
    }

    private async Task<GoogleDocsDocumentLocation> ResolveByFolderAndTitleAsync(
        string folderId,
        string title,
        DocumentToolOperation operation,
        CancellationToken cancellationToken)
    {
        var files = await ListGoogleDocsByFolderAndTitleAsync(folderId, title, cancellationToken).ConfigureAwait(false);
        if (files.Count > 1)
        {
            throw new InvalidOperationException(
                $"The Google Docs reference '{GoogleDocsSupport.CreateFolderReference(folderId, title)}' is ambiguous because multiple Google Docs in that folder share the same title.");
        }

        if (files.Count == 1)
        {
            return CreateExistingLocation(files[0]);
        }

        var normalizedFolderId = NormalizeFolderId(folderId);
        var resolvedReference = GoogleDocsSupport.CreateFolderReference(normalizedFolderId, title);
        return new GoogleDocsDocumentLocation(
            DocumentId: null,
            FolderId: normalizedFolderId,
            Title: title,
            ResolvedReference: resolvedReference,
            ReadStateKey: resolvedReference,
            Exists: false,
            Version: null,
            Length: null);
    }

    private async Task PersistSnapshotAsync(
        GoogleDocsDocumentLocation location,
        Stream content,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var snapshot = buffer.ToArray();

        var asciiDoc = ExtractCanonicalAsciiDoc(snapshot);
        var title = location.Title ?? ExtractDocumentTitle(snapshot) ?? "AIToolkit Document";

        DriveFile storedFile;
        await using var uploadStream = new MemoryStream(snapshot, writable: false);
        if (location.Exists && !string.IsNullOrWhiteSpace(location.DocumentId))
        {
            storedFile = await UpdateDocumentAsync(location.DocumentId, title, uploadStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            storedFile = await CreateDocumentAsync(location.FolderId, title, uploadStream, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(storedFile.Id))
        {
            throw new InvalidOperationException("Google Drive did not return a document ID after the Google Docs write completed.");
        }

        await UpsertPayloadAsync(storedFile.Id, asciiDoc, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractCanonicalAsciiDoc(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var payload = WordAsciiDocPayload.TryRead(document.MainDocumentPart);
        return !string.IsNullOrWhiteSpace(payload)
            ? WordAsciiDocTextUtilities.NormalizeLineEndings(payload)
            : WordAsciiDocTextUtilities.NormalizeLineEndings(WordAsciiDocImporter.Import(document));
    }

    private static string? ExtractDocumentTitle(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        return document.PackageProperties.Title;
    }

    private async Task<DriveFile?> TryGetGoogleDocAsync(string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var request = _driveService.Files.Get(documentId);
            request.Fields = "id,name,mimeType,version,size,modifiedTime,trashed";
            var file = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (file is null || string.Equals(file.Trashed?.ToString(), bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(file.MimeType, GoogleDocsSupport.GoogleDocsMimeType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The Google Drive file '{documentId}' is not a Google Docs document.");
            }

            return file;
        }
        catch (Google.GoogleApiException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IList<DriveFile>> ListGoogleDocsByFolderAndTitleAsync(string folderId, string title, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Fields = "files(id,name,mimeType,version,size,modifiedTime,trashed)";
        request.Q = $"mimeType='{GoogleDocsSupport.GoogleDocsMimeType}' and trashed=false and name='{EscapeQueryLiteral(title)}' and '{EscapeQueryLiteral(NormalizeFolderId(folderId))}' in parents";
        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return response.Files ?? [];
    }

    private async Task<DriveFile?> TryFindPayloadFileAsync(string documentId, CancellationToken cancellationToken)
    {
        var request = _driveService.Files.List();
        request.Spaces = "appDataFolder";
        request.Fields = "files(id,name,appProperties)";
        request.Q =
            $"trashed=false and '{EscapeQueryLiteral("appDataFolder")}' in parents and " +
            $"appProperties has {{ key='{GoogleDocsSupport.PayloadProviderKey}' and value='{GoogleDocsSupport.PayloadProviderValue}' }} and " +
            $"appProperties has {{ key='{GoogleDocsSupport.PayloadDocumentIdKey}' and value='{EscapeQueryLiteral(documentId)}' }}";

        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return response.Files?.SingleOrDefault();
    }

    private async Task<DriveFile> CreateDocumentAsync(string? folderId, string title, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = title,
            MimeType = GoogleDocsSupport.GoogleDocsMimeType,
        };

        if (!string.IsNullOrWhiteSpace(folderId) && !string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase))
        {
            metadata.Parents = [folderId];
        }

        var request = _driveService.Files.Create(metadata, content, GoogleDocsSupport.DocxMimeType);
        request.Fields = "id,name,mimeType,version,size,modifiedTime";
        return await ExecuteUploadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DriveFile> UpdateDocumentAsync(string documentId, string title, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = title,
            MimeType = GoogleDocsSupport.GoogleDocsMimeType,
        };

        var request = _driveService.Files.Update(metadata, documentId, content, GoogleDocsSupport.DocxMimeType);
        request.Fields = "id,name,mimeType,version,size,modifiedTime";
        return await ExecuteUploadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertPayloadAsync(string documentId, string asciiDoc, CancellationToken cancellationToken)
    {
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(asciiDoc);
        var existing = await TryFindPayloadFileAsync(documentId, cancellationToken).ConfigureAwait(false);
        await using var stream = new MemoryStream(payloadBytes, writable: false);

        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            var metadata = new DriveFile
            {
                Name = $"aitoolkit-{documentId}.adoc",
                AppProperties = new Dictionary<string, string>
                {
                    [GoogleDocsSupport.PayloadProviderKey] = GoogleDocsSupport.PayloadProviderValue,
                    [GoogleDocsSupport.PayloadDocumentIdKey] = documentId,
                },
            };

            var updateRequest = _driveService.Files.Update(metadata, existing.Id, stream, "text/plain");
            updateRequest.Fields = "id";
            _ = await ExecuteUploadAsync(updateRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        var createMetadata = new DriveFile
        {
            Name = $"aitoolkit-{documentId}.adoc",
            MimeType = "text/plain",
            Parents = ["appDataFolder"],
            AppProperties = new Dictionary<string, string>
            {
                [GoogleDocsSupport.PayloadProviderKey] = GoogleDocsSupport.PayloadProviderValue,
                [GoogleDocsSupport.PayloadDocumentIdKey] = documentId,
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

    private static GoogleDocsDocumentLocation CreateExistingLocation(DriveFile file)
    {
        var documentId = file.Id
            ?? throw new InvalidOperationException("Google Drive did not return a document ID.");
        var resolvedReference = GoogleDocsSupport.CreateDocumentReference(documentId);
        return new GoogleDocsDocumentLocation(
            DocumentId: documentId,
            FolderId: null,
            Title: file.Name,
            ResolvedReference: resolvedReference,
            ReadStateKey: resolvedReference,
            Exists: true,
            Version: GoogleDocsSupport.NormalizeVersion(file.Version, file.ModifiedTimeDateTimeOffset),
            Length: file.Size);
    }

    private static string NormalizeFolderId(string folderId) =>
        string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase) ? "root" : folderId;

    private static string EscapeQueryLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static bool TryParseUnsupportedHostedAlias(string documentReference) =>
        documentReference.StartsWith("GoogleDrive://", StringComparison.OrdinalIgnoreCase)
        || documentReference.StartsWith("GoogleDocs://", StringComparison.OrdinalIgnoreCase)
        || documentReference.StartsWith("gdrive://", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseDocumentUrl(string documentReference, out string documentId)
    {
        documentId = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "document", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "d", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        documentId = Uri.UnescapeDataString(segments[2]);
        return documentId.Length > 0;
    }

    private static bool TryParseDocumentReference(string documentReference, out string documentId)
    {
        documentId = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "gdocs", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.Equals(uri.Host, "documents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            return false;
        }

        documentId = Uri.UnescapeDataString(path);
        return documentId.Length > 0;
    }

    private static bool TryParseFolderReference(string documentReference, out string folderId, out string title)
    {
        folderId = string.Empty;
        title = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "gdocs", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "folders", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !string.Equals(segments[1], "documents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        folderId = Uri.UnescapeDataString(segments[0]);
        title = Uri.UnescapeDataString(string.Join('/', segments.Skip(2)));
        return folderId.Length > 0 && title.Length > 0;
    }
}

/// <summary>
/// Buffers generated DOCX content until it can be uploaded and converted back to Google Docs on disposal.
/// </summary>
internal sealed class GoogleDocsUploadOnDisposeMemoryStream(
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