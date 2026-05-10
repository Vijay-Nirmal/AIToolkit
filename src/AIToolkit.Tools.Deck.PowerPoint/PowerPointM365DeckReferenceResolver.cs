using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using System.Text;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Resolves OneDrive and SharePoint PowerPoint presentation references through Microsoft Graph.
/// </summary>
/// <remarks>
/// This resolver understands hosted HTTPS URLs plus the package-specific <c>m365://</c> aliases for drive items and
/// drive-root paths. It translates those references into generic <see cref="DeckReferenceResolution"/> instances that
/// stream content through Microsoft Graph while preserving version metadata for stale-read detection. Uploads use
/// <see cref="DeckUploadOnDisposeMemoryStream"/> so <see cref="DocumentFormat.OpenXml.Packaging.PresentationDocument"/> can
/// dispose synchronously while the actual network persistence continues safely in the background.
/// </remarks>
internal sealed class PowerPointM365DeckReferenceResolver : IDeckReferenceResolver, IDisposable
{
    private static readonly string[] DefaultScopes = ["https://graph.microsoft.com/.default"];

    private readonly GraphServiceClient _graphClient;

    /// <summary>
    /// Initializes a Microsoft Graph-backed hosted PowerPoint presentation resolver.
    /// </summary>
    /// <param name="options">The Microsoft 365 connection settings to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> does not contain a credential.</exception>
    public PowerPointM365DeckReferenceResolver(PowerPointDeckM365Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credential = options.Credential
            ?? throw new ArgumentException("PowerPointDeckM365Options.Credential is required when M365 hosted presentation support is enabled.", nameof(options));

        _graphClient = new GraphServiceClient(credential, NormalizeScopes(options.Scopes));
    }

    /// <summary>
    /// Resolves a supported OneDrive or SharePoint reference into a stream-backed presentation resource.
    /// </summary>
    /// <param name="deckReference">The hosted HTTPS or <c>m365://</c> reference to resolve.</param>
    /// <param name="context">The shared resolver context for the current deck tool invocation.</param>
    /// <param name="cancellationToken">A token that cancels Graph lookups.</param>
    /// <returns>
    /// A stream-backed resolution when the reference matches a supported Microsoft 365 form; otherwise,
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">The reference uses an unsupported hosted alias format.</exception>
    /// <exception cref="FileNotFoundException">The referenced hosted PowerPoint presentation could not be found.</exception>
    public async ValueTask<DeckReferenceResolution?> ResolveAsync(
        string deckReference,
        DeckReferenceResolverContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryParseUnsupportedHostedAlias(deckReference))
        {
            throw new InvalidOperationException(
                $"The Microsoft 365 hosted presentation reference '{deckReference}' uses an unsupported alias scheme. " +
                "Use a SharePoint or OneDrive HTTPS URL, m365://drives/{driveId}/items/{itemId}, m365://drives/me/root/{path/to/file.pptx} for the current user's OneDrive, or m365://drives/{driveId}/root/{path/to/file.pptx} when you know the target drive ID. " +
                "To create a new OneDrive presentation, use m365://drives/me/root/Documents/QuarterlyBusinessReview.pptx when the drive ID is not known, or replace me with the target drive ID when you have it.");
        }

        if (TryParseHostedWebUrl(deckReference, out var hostedUrl))
        {
            var location = await ResolveHostedUrlAsync(hostedUrl, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, deckReference);
        }

        if (TryParseDriveItemReference(deckReference, out var driveId, out var itemId))
        {
            var location = await ResolveDriveItemAsync(driveId, itemId, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, deckReference);
        }

        if (TryParseDrivePathReference(deckReference, out driveId, out var itemPath))
        {
            var location = await ResolveDrivePathAsync(driveId, itemPath, cancellationToken).ConfigureAwait(false);
            return CreateResolution(location, deckReference);
        }

        return null;
    }

    private async Task<PowerPointM365DeckLocation> ResolveHostedUrlAsync(Uri hostedUrl, CancellationToken cancellationToken)
    {
        var item = await TryGetSharedDriveItemAsync(EncodeSharingUrl(hostedUrl.AbsoluteUri), cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"The Microsoft 365 hosted presentation '{hostedUrl.AbsoluteUri}' was not found.");

        return CreateExistingLocation(item, item.ParentReference?.DriveId, item.Id, null);
    }

    private async Task<PowerPointM365DeckLocation> ResolveDriveItemAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        var resolvedDriveId = await NormalizeDriveIdAsync(driveId, cancellationToken).ConfigureAwait(false);
        var item = await TryGetDriveItemAsync(resolvedDriveId, itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"The Microsoft 365 hosted presentation '{CreateDriveItemReference(driveId, itemId)}' was not found.");

        return CreateExistingLocation(item, resolvedDriveId, itemId, null);
    }

    private async Task<PowerPointM365DeckLocation> ResolveDrivePathAsync(string driveId, string itemPath, CancellationToken cancellationToken)
    {
        var resolvedDriveId = await NormalizeDriveIdAsync(driveId, cancellationToken).ConfigureAwait(false);
        var normalizedPath = NormalizeDrivePath(itemPath);
        var item = await TryGetDrivePathItemAsync(resolvedDriveId, normalizedPath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            var normalizedReference = CreateDrivePathReference(resolvedDriveId, normalizedPath);
            return new PowerPointM365DeckLocation(
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

    private DeckReferenceResolution CreateResolution(PowerPointM365DeckLocation location, string originalReference) =>
        DeckReferenceResolution.CreateStreamBacked(
            resolvedReference: location.ResolvedReference,
            extension: location.Extension,
            existsAsync: cancellationToken => ValueTask.FromResult(location.Exists),
            openReadAsync: cancellationToken => OpenReadAsync(location, originalReference, cancellationToken),
            openWriteAsync: cancellationToken => OpenWriteAsync(location, originalReference, cancellationToken),
            version: location.Version,
            length: location.Length,
            state: location,
            readStateKey: location.ReadStateKey);

    private async ValueTask<Stream> OpenReadAsync(PowerPointM365DeckLocation location, string originalReference, CancellationToken cancellationToken)
    {
        if (!location.Exists)
        {
            throw new FileNotFoundException($"The Microsoft 365 hosted presentation '{originalReference}' was not found.");
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
            throw new InvalidOperationException("The hosted Microsoft 365 presentation reference is missing both item ID and drive path information.");
        }

        return stream ?? throw new InvalidOperationException($"Microsoft Graph returned no content stream for '{location.ResolvedReference}'.");
    }

    private ValueTask<Stream> OpenWriteAsync(PowerPointM365DeckLocation location, string originalReference, CancellationToken cancellationToken)
    {
        if (!location.Exists && string.IsNullOrWhiteSpace(location.ItemPath))
        {
            throw new InvalidOperationException($"The Microsoft 365 hosted presentation '{originalReference}' does not exist. Use m365://drives/me/root/{{path/to/file.pptx}} when the current user's OneDrive is the target and you do not know the drive ID, or m365://drives/{{driveId}}/root/{{path/to/file.pptx}} when you do know it.");
        }

        return ValueTask.FromResult<Stream>(new DeckUploadOnDisposeMemoryStream(
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

                throw new InvalidOperationException("The hosted Microsoft 365 presentation reference is missing both item ID and drive path information.");
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

    private static PowerPointM365DeckLocation CreateExistingLocation(DriveItem item, string? driveId, string? itemId, string? itemPath)
    {
        var resolvedDriveId = !string.IsNullOrWhiteSpace(driveId)
            ? driveId
            : throw new InvalidOperationException("Microsoft Graph did not return a drive ID for the hosted presentation.");
        var resolvedItemId = !string.IsNullOrWhiteSpace(itemId)
            ? itemId
            : throw new InvalidOperationException("Microsoft Graph did not return an item ID for the hosted presentation.");
        var name = !string.IsNullOrWhiteSpace(item.Name)
            ? item.Name
            : itemPath;

        return new PowerPointM365DeckLocation(
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

    /// <summary>
    /// Disposes the underlying <see cref="GraphServiceClient"/>.
    /// </summary>
    /// <remarks>
    /// Dispose the resolver only after all pending read or write streams have completed.
    /// </remarks>
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

    private static bool TryParseHostedWebUrl(string deckReference, out Uri hostedUrl)
    {
        hostedUrl = null!;
        if (!Uri.TryCreate(deckReference, UriKind.Absolute, out var candidate)
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

    private static bool TryParseUnsupportedHostedAlias(string deckReference)
    {
        if (!Uri.TryCreate(deckReference, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        return string.Equals(candidate.Scheme, "onedrive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Scheme, "sharepoint", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseDriveItemReference(string deckReference, out string driveId, out string itemId)
    {
        driveId = string.Empty;
        itemId = string.Empty;
        if (!Uri.TryCreate(deckReference, UriKind.Absolute, out var candidate)
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

    private static bool TryParseDrivePathReference(string deckReference, out string driveId, out string itemPath)
    {
        driveId = string.Empty;
        itemPath = string.Empty;
        if (!Uri.TryCreate(deckReference, UriKind.Absolute, out var candidate)
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
/// Buffers generated presentation content until it can be uploaded back to Microsoft Graph on disposal.
/// </summary>
/// <remarks>
/// <see cref="PresentationDocument"/> disposes its target stream synchronously, so this wrapper snapshots the content,
/// starts the upload once, and lets <see cref="DisposeAsync"/> await the same persistence task without blocking the sync
/// disposal path on network I/O. The resolver uses it to bridge the Open XML save pipeline with Microsoft Graph's async
/// upload APIs.
/// </remarks>
internal sealed class DeckUploadOnDisposeMemoryStream(
    Func<Stream, CancellationToken, ValueTask> persistAsync,
    CancellationToken cancellationToken) : MemoryStream
{
    private readonly Func<Stream, CancellationToken, ValueTask> _persistAsync = persistAsync;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly object _persistLock = new();
    private Task? _persistTask;

    /// <summary>
    /// Uploads the buffered snapshot before disposing the stream asynchronously.
    /// </summary>
    /// <returns>A task that completes after the buffered content has been persisted and the stream has been disposed.</returns>
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

            // PresentationDocument closes the supplied Stream via synchronous Dispose,
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

/// <summary>
/// Describes a hosted Microsoft 365 PowerPoint presentation after reference normalization.
/// </summary>
/// <remarks>
/// This immutable snapshot is carried as <see cref="DeckReferenceResolution.State"/> so later reads and writes can
/// reopen the same hosted presentation without reparsing the original reference text.
/// </remarks>
internal sealed record PowerPointM365DeckLocation(
    string DriveId,
    string? ItemId,
    string? ItemPath,
    string ResolvedReference,
    string ReadStateKey,
    string Extension,
    bool Exists,
    string? Version,
    long? Length);



