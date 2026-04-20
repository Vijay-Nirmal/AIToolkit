using AIToolkit.Tools.Document.Word;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using System.Text.Json;

namespace AIToolkit.Tools.Document.GoogleDocs.Tests;

internal sealed class FakeGoogleDocsWorkspaceClient : IGoogleDocsWorkspaceClient
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredGoogleDoc> _documents = new(StringComparer.Ordinal);
    private long _nextDocumentId = 1_000;

    public ValueTask<GoogleDocsDocumentLocation?> ResolveAsync(
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

        lock (_gate)
        {
            if (TryParseDocumentUrl(documentReference, out var documentId)
                || TryParseDocumentReference(documentReference, out documentId))
            {
                return ValueTask.FromResult<GoogleDocsDocumentLocation?>(ResolveByDocumentId(documentId));
            }

            if (TryParseFolderReference(documentReference, out var folderId, out var title))
            {
                return ValueTask.FromResult<GoogleDocsDocumentLocation?>(ResolveByFolderAndTitle(folderId, title));
            }
        }

        return ValueTask.FromResult<GoogleDocsDocumentLocation?>(null);
    }

    public ValueTask<string?> TryReadManagedAsciiDocAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                !string.IsNullOrWhiteSpace(location.DocumentId) && _documents.TryGetValue(location.DocumentId, out var stored)
                    ? stored.ManagedAsciiDoc
                    : null);
        }
    }

    public ValueTask<Stream> OpenExportReadAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(location.DocumentId) || !_documents.TryGetValue(location.DocumentId, out var stored))
            {
                throw new FileNotFoundException($"The Google Doc '{location.ResolvedReference}' was not found.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(stored.DocxBytes, writable: false));
        }
    }

    public ValueTask<Stream> OpenUploadWriteAsync(
        GoogleDocsDocumentLocation location,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new GoogleDocsUploadOnDisposeMemoryStream(
            (content, innerCancellationToken) => PersistSnapshotAsync(location, content, innerCancellationToken),
            cancellationToken));

    public string SeedExternalDocument(string title, byte[] docxBytes, string? managedAsciiDoc = null, string folderId = "root")
    {
        lock (_gate)
        {
            var documentId = Interlocked.Increment(ref _nextDocumentId).ToString(CultureInfo.InvariantCulture);
            _documents[documentId] = new StoredGoogleDoc(
                DocumentId: documentId,
                FolderId: folderId,
                Title: title,
                DocxBytes: docxBytes,
                ManagedAsciiDoc: managedAsciiDoc,
                Version: 1);
            return documentId;
        }
    }

    public static string CreateDocumentUrl(string documentId) =>
        $"https://docs.google.com/document/d/{documentId}/edit";

    public byte[] GetDocumentBytes(string resolvedReference)
    {
        lock (_gate)
        {
            var documentId = ResolveDocumentId(resolvedReference);
            return [.. _documents[documentId].DocxBytes];
        }
    }

    public string? GetManagedAsciiDoc(string resolvedReference)
    {
        lock (_gate)
        {
            var documentId = ResolveDocumentId(resolvedReference);
            return _documents[documentId].ManagedAsciiDoc;
        }
    }

    private ValueTask PersistSnapshotAsync(
        GoogleDocsDocumentLocation location,
        Stream content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var snapshot = buffer.ToArray();
        var managedAsciiDoc = ExtractManagedAsciiDoc(snapshot);
        var title = location.Title ?? ExtractTitle(snapshot) ?? "AIToolkit Document";

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(location.DocumentId) && _documents.TryGetValue(location.DocumentId, out var existing))
            {
                _documents[location.DocumentId] = existing with
                {
                    Title = title,
                    DocxBytes = snapshot,
                    ManagedAsciiDoc = managedAsciiDoc,
                    Version = existing.Version + 1,
                };
            }
            else
            {
                var documentId = Interlocked.Increment(ref _nextDocumentId).ToString(CultureInfo.InvariantCulture);
                _documents[documentId] = new StoredGoogleDoc(
                    DocumentId: documentId,
                    FolderId: location.FolderId ?? "root",
                    Title: title,
                    DocxBytes: snapshot,
                    ManagedAsciiDoc: managedAsciiDoc,
                    Version: 1);
            }
        }

        return ValueTask.CompletedTask;
    }

    private GoogleDocsDocumentLocation ResolveByDocumentId(string documentId)
    {
        if (_documents.TryGetValue(documentId, out var stored))
        {
            return new GoogleDocsDocumentLocation(
                DocumentId: stored.DocumentId,
                FolderId: stored.FolderId,
                Title: stored.Title,
                ResolvedReference: GoogleDocsSupport.CreateDocumentReference(stored.DocumentId),
                ReadStateKey: GoogleDocsSupport.CreateDocumentReference(stored.DocumentId),
                Exists: true,
                Version: stored.Version.ToString(CultureInfo.InvariantCulture),
                Length: stored.DocxBytes.Length);
        }

        return new GoogleDocsDocumentLocation(
            DocumentId: documentId,
            FolderId: null,
            Title: null,
            ResolvedReference: GoogleDocsSupport.CreateDocumentReference(documentId),
            ReadStateKey: GoogleDocsSupport.CreateDocumentReference(documentId),
            Exists: false,
            Version: null,
            Length: null);
    }

    private GoogleDocsDocumentLocation ResolveByFolderAndTitle(string folderId, string title)
    {
        var normalizedFolderId = NormalizeFolderId(folderId);
        var matches = _documents.Values
            .Where(document => string.Equals(document.FolderId, normalizedFolderId, StringComparison.OrdinalIgnoreCase))
            .Where(document => string.Equals(document.Title, title, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"The Google Docs reference '{GoogleDocsSupport.CreateFolderReference(normalizedFolderId, title)}' is ambiguous because multiple Google Docs in that folder share the same title.");
        }

        if (matches.Length == 1)
        {
            return ResolveByDocumentId(matches[0].DocumentId);
        }

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

    private static string ResolveDocumentId(string resolvedReference)
    {
        if (TryParseDocumentUrl(resolvedReference, out var documentId)
            || TryParseDocumentReference(resolvedReference, out documentId))
        {
            return documentId;
        }

        throw new InvalidOperationException($"The reference '{resolvedReference}' is not a Google Docs document reference.");
    }

    private static string? ExtractManagedAsciiDoc(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var payload = WordAsciiDocPayload.TryRead(document.MainDocumentPart);
        return payload is null ? null : WordAsciiDocTextUtilities.NormalizeLineEndings(payload);
    }

    private static string? ExtractTitle(byte[] snapshot)
    {
        using var stream = new MemoryStream(snapshot, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        return document.PackageProperties.Title;
    }

    private static string NormalizeFolderId(string folderId) =>
        string.Equals(folderId, "root", StringComparison.OrdinalIgnoreCase) ? "root" : folderId;

    private static bool TryParseUnsupportedHostedAlias(string documentReference) =>
        documentReference.StartsWith("GoogleDrive://", StringComparison.OrdinalIgnoreCase)
        || documentReference.StartsWith("GoogleDocs://", StringComparison.OrdinalIgnoreCase)
        || documentReference.StartsWith("gdrive://", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseDocumentUrl(string documentReference, out string documentId)
    {
        documentId = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
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
            || !string.Equals(uri.Host, "documents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        documentId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
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

    private sealed record StoredGoogleDoc(
        string DocumentId,
        string FolderId,
        string Title,
        byte[] DocxBytes,
        string? ManagedAsciiDoc,
        long Version);
}