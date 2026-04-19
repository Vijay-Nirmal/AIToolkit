using AIToolkit.Tools.Document;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using System.Text;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Resolves OneDrive and SharePoint Word document references through Microsoft Graph.
/// </summary>
internal sealed class WordM365DocumentReferenceResolver : IDocumentReferenceResolver, IDisposable
{
    private static readonly string[] DefaultScopes = ["https://graph.microsoft.com/.default"];

    private readonly GraphServiceClient _graphClient;

    public WordM365DocumentReferenceResolver(WordDocumentM365Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credential = options.Credential
            ?? throw new ArgumentException("WordDocumentM365Options.Credential is required when M365 hosted document support is enabled.", nameof(options));

        _graphClient = new GraphServiceClient(credential, NormalizeScopes(options.Scopes));
    }

    public async ValueTask<DocumentReferenceResolution?> ResolveAsync(
        string documentReference,
        DocumentReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryParseUnsupportedHostedAlias(documentReference))
        {
            throw new InvalidOperationException(
                $"The Microsoft 365 hosted document reference '{documentReference}' uses an unsupported alias scheme. " +
                "Use a SharePoint or OneDrive HTTPS URL, m365://drives/{driveId}/items/{itemId}, m365://drives/me/root/{path/to/file.docx} for the current user's OneDrive, or m365://drives/{driveId}/root/{path/to/file.docx} when you know the target drive ID. " +
                "To create a new OneDrive document, use m365://drives/me/root/Documents/IndianHistorySummary.docx when the drive ID is not known, or replace me with the target drive ID when you have it.");
        }

        if (TryParseHostedWebUrl(documentReference, out var hostedUrl))
        {
            var location = await ResolveHostedUrlAsync(hostedUrl, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, documentReference);
        }

        if (TryParseDriveItemReference(documentReference, out var driveId, out var itemId))
        {
            var location = await ResolveDriveItemAsync(driveId, itemId, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, documentReference);
        }

        if (TryParseDrivePathReference(documentReference, out driveId, out var itemPath))
        {
            var location = await ResolveDrivePathAsync(driveId, itemPath, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, documentReference);
        }

        return null;
    }

    private async Task<WordM365DocumentLocation> ResolveHostedUrlAsync(Uri hostedUrl, CancellationToken cancellationToken)
    {
        var item = await TryGetSharedDriveItemAsync(EncodeSharingUrl(hostedUrl.AbsoluteUri), cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"The Microsoft 365 hosted document '{hostedUrl.AbsoluteUri}' was not found.");

        return CreateExistingLocation(item, item.ParentReference?.DriveId, item.Id, null);
    }

    private async Task<WordM365DocumentLocation> ResolveDriveItemAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        var resolvedDriveId = await NormalizeDriveIdAsync(driveId, cancellationToken).ConfigureAwait(false);
        var item = await TryGetDriveItemAsync(resolvedDriveId, itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"The Microsoft 365 hosted document '{CreateDriveItemReference(driveId, itemId)}' was not found.");

        return CreateExistingLocation(item, resolvedDriveId, itemId, null);
    }

    private async Task<WordM365DocumentLocation> ResolveDrivePathAsync(string driveId, string itemPath, CancellationToken cancellationToken)
    {
        var resolvedDriveId = await NormalizeDriveIdAsync(driveId, cancellationToken).ConfigureAwait(false);
        var normalizedPath = NormalizeDrivePath(itemPath);
        var item = await TryGetDrivePathItemAsync(resolvedDriveId, normalizedPath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            var normalizedReference = CreateDrivePathReference(resolvedDriveId, normalizedPath);
            return new WordM365DocumentLocation(
                resolvedDriveId,
                ItemId: null,
                ItemPath: normalizedPath,
                ResolvedReference: normalizedReference,
                ReadStateKey: normalizedReference,
                Extension: Path.GetExtension(normalizedPath),
                Exists: false,
                Version: null,
                Length: null);
        }

        return CreateExistingLocation(item, resolvedDriveId, item.Id, normalizedPath);
    }

    private DocumentReferenceResolution CreateResolution(WordM365DocumentLocation location, string originalReference) =>
        DocumentReferenceResolution.CreateStreamBacked(
            resolvedReference: location.ResolvedReference,
            extension: location.Extension,
            existsAsync: cancellationToken => ValueTask.FromResult(location.Exists),
            openReadAsync: cancellationToken => OpenReadAsync(location, originalReference, cancellationToken),
            openWriteAsync: cancellationToken => OpenWriteAsync(location, originalReference, cancellationToken),
            version: location.Version,
            length: location.Length,
            state: location,
            readStateKey: location.ReadStateKey);

    private async ValueTask<Stream> OpenReadAsync(WordM365DocumentLocation location, string originalReference, CancellationToken cancellationToken)
    {
        if (!location.Exists)
        {
            throw new FileNotFoundException($"The Microsoft 365 hosted document '{originalReference}' was not found.");
        }

        Stream? stream;
        if (!string.IsNullOrWhiteSpace(location.ItemId))
        {
            stream = await _graphClient.Drives[location.DriveId].Items[location.ItemId].Content.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(location.ItemPath))
        {
            stream = await _graphClient.Drives[location.DriveId].Root.ItemWithPath(location.ItemPath).Content.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException("The hosted Microsoft 365 document reference is missing both item ID and drive path information.");
        }

        return stream ?? throw new InvalidOperationException($"Microsoft Graph returned no content stream for '{location.ResolvedReference}'.");
    }

    private ValueTask<Stream> OpenWriteAsync(WordM365DocumentLocation location, string originalReference, CancellationToken cancellationToken)
    {
        if (!location.Exists && string.IsNullOrWhiteSpace(location.ItemPath))
        {
            throw new InvalidOperationException($"The Microsoft 365 hosted document '{originalReference}' does not exist. Use m365://drives/me/root/{{path/to/file.docx}} when the current user's OneDrive is the target and you do not know the drive ID, or m365://drives/{{driveId}}/root/{{path/to/file.docx}} when you do know it.");
        }

        return ValueTask.FromResult<Stream>(new UploadOnDisposeMemoryStream(
            async (content, innerCancellationToken) =>
            {
                if (content.CanSeek)
                {
                    content.Position = 0;
                }

                if (!string.IsNullOrWhiteSpace(location.ItemId))
                {
                    _ = await _graphClient.Drives[location.DriveId].Items[location.ItemId].Content.PutAsync(content, cancellationToken: innerCancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(location.ItemPath))
                {
                    _ = await _graphClient.Drives[location.DriveId].Root.ItemWithPath(location.ItemPath).Content.PutAsync(content, cancellationToken: innerCancellationToken).ConfigureAwait(false);
                    return;
                }

                throw new InvalidOperationException("The hosted Microsoft 365 document reference is missing both item ID and drive path information.");
            },
            cancellationToken));
    }

    private async Task<DriveItem?> TryGetSharedDriveItemAsync(string encodedSharingUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _graphClient.Shares[encodedSharingUrl].DriveItem.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception) when (exception.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<DriveItem?> TryGetDriveItemAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        try
        {
            return await _graphClient.Drives[driveId].Items[itemId].GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception) when (exception.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<DriveItem?> TryGetDrivePathItemAsync(string driveId, string itemPath, CancellationToken cancellationToken)
    {
        try
        {
            return await _graphClient.Drives[driveId].Root.ItemWithPath(itemPath).GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException exception) when (exception.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<string> NormalizeDriveIdAsync(string driveId, CancellationToken cancellationToken)
    {
        if (!string.Equals(driveId, "me", StringComparison.OrdinalIgnoreCase))
        {
            return driveId;
        }

        var drive = await _graphClient.Me.Drive.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Microsoft Graph did not return the current user's OneDrive information.");

        return !string.IsNullOrWhiteSpace(drive.Id)
            ? drive.Id
            : throw new InvalidOperationException("Microsoft Graph did not return a drive ID for the current user's OneDrive.");
    }

    private static WordM365DocumentLocation CreateExistingLocation(DriveItem item, string? driveId, string? itemId, string? itemPath)
    {
        var resolvedDriveId = !string.IsNullOrWhiteSpace(driveId)
            ? driveId
            : throw new InvalidOperationException("Microsoft Graph did not return a drive ID for the hosted document.");
        var resolvedItemId = !string.IsNullOrWhiteSpace(itemId)
            ? itemId
            : throw new InvalidOperationException("Microsoft Graph did not return an item ID for the hosted document.");
        var name = !string.IsNullOrWhiteSpace(item.Name)
            ? item.Name
            : itemPath;

        return new WordM365DocumentLocation(
            resolvedDriveId,
            resolvedItemId,
            itemPath,
            CreateDriveItemReference(resolvedDriveId, resolvedItemId),
            CreateDriveItemReference(resolvedDriveId, resolvedItemId),
            Path.GetExtension(name ?? string.Empty),
            Exists: true,
            Version: item.ETag,
            Length: item.Size);
    }

    public void Dispose() =>
        _graphClient.Dispose();

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        var normalized = scopes?
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized is { Length: > 0 } ? normalized : DefaultScopes;
    }

    private static bool TryParseHostedWebUrl(string documentReference, out Uri hostedUrl)
    {
        hostedUrl = null!;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttps && candidate.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = candidate.Host;
        if (host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".sharepoint-df.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "onedrive.live.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "1drv.ms", StringComparison.OrdinalIgnoreCase))
        {
            hostedUrl = candidate;
            return true;
        }

        return false;
    }

    private static bool TryParseUnsupportedHostedAlias(string documentReference)
    {
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        return string.Equals(candidate.Scheme, "onedrive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Scheme, "sharepoint", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseDriveItemReference(string documentReference, out string driveId, out string itemId)
    {
        driveId = string.Empty;
        itemId = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var candidate)
            || !string.Equals(candidate.Scheme, "m365", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Host, "drives", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 3 || !string.Equals(segments[1], "items", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        driveId = Uri.UnescapeDataString(segments[0]);
        itemId = Uri.UnescapeDataString(segments[2]);
        return driveId.Length > 0 && itemId.Length > 0;
    }

    private static bool TryParseDrivePathReference(string documentReference, out string driveId, out string itemPath)
    {
        driveId = string.Empty;
        itemPath = string.Empty;
        if (!Uri.TryCreate(documentReference, UriKind.Absolute, out var candidate)
            || !string.Equals(candidate.Scheme, "m365", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Host, "drives", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3 || !string.Equals(segments[1], "root", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        driveId = Uri.UnescapeDataString(segments[0]);
        itemPath = string.Join('/', segments.Skip(2).Select(Uri.UnescapeDataString));
        return driveId.Length > 0 && itemPath.Length > 0;
    }

    private static string NormalizeDrivePath(string itemPath) =>
        itemPath.Trim().Replace('\\', '/').Trim('/');

    private static string CreateDriveItemReference(string driveId, string itemId) =>
        $"m365://drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}";

    private static string CreateDrivePathReference(string driveId, string itemPath) =>
        $"m365://drives/{Uri.EscapeDataString(driveId)}/root/{string.Join('/', NormalizeDrivePath(itemPath).Split('/').Select(Uri.EscapeDataString))}";

    private static string EncodeSharingUrl(string sharingUrl)
    {
        var base64Value = Convert.ToBase64String(Encoding.UTF8.GetBytes(sharingUrl));
        return "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');
    }
}

/// <summary>
/// Buffers generated Word content until it can be uploaded back to Microsoft Graph on disposal.
/// </summary>
internal sealed class UploadOnDisposeMemoryStream(
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

            // WordprocessingDocument closes the supplied Stream via synchronous Dispose,
            // so we snapshot the buffered content here, start persistence once, and let
            // DisposeAsync await the shared task without blocking sync disposal on network I/O.
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

internal sealed record WordM365DocumentLocation(
    string DriveId,
    string? ItemId,
    string? ItemPath,
    string ResolvedReference,
    string ReadStateKey,
    string Extension,
    bool Exists,
    string? Version,
    long? Length);