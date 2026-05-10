using System.Collections.Concurrent;
using System.Text.Json;

namespace AIToolkit.Tools.Deck;

/// <summary>
/// Well-known asset scope names used by the default deck asset interceptor.
/// </summary>
public static class DeckAssetScopes
{
    /// <summary>
    /// Global assets are available across sessions.
    /// </summary>
    public const string Global = "global";

    /// <summary>
    /// Session assets are visible only to the matching session identifier.
    /// </summary>
    public const string Session = "session";
}

/// <summary>
/// Describes a stored deck asset that can be referenced by <c>[asset ...]</c> directives.
/// </summary>
/// <param name="AssetPath">The stable asset path used from DeckDoc, such as <c>hero/team.png</c>.</param>
/// <param name="Kind">The asset kind, such as <c>image</c>, <c>video</c>, or <c>audio</c>.</param>
/// <param name="Description">Searchable descriptive text for the asset.</param>
/// <param name="SourceReference">The provider-specific stored location used to open the asset later.</param>
/// <param name="Scope">The asset scope, typically <c>global</c> or <c>session</c>.</param>
/// <param name="SessionId">The owning session identifier when <paramref name="Scope"/> is <c>session</c>.</param>
/// <param name="DisplayName">Optional human-readable display name.</param>
/// <param name="MediaType">Optional MIME type such as <c>image/png</c>.</param>
public sealed record DeckAssetRecord(
    string AssetPath,
    string Kind,
    string Description,
    string SourceReference,
    string Scope = DeckAssetScopes.Global,
    string? SessionId = null,
    string? DisplayName = null,
    string? MediaType = null);

/// <summary>
/// Describes a request to create or register a deck asset.
/// </summary>
/// <param name="AssetPath">The stable asset path to reserve.</param>
/// <param name="SourceReference">The source location of the asset to ingest.</param>
/// <param name="Description">Searchable descriptive text for the asset.</param>
/// <param name="Kind">The asset kind, such as <c>image</c>, <c>video</c>, or <c>audio</c>.</param>
/// <param name="Scope">The asset scope, typically <c>global</c> or <c>session</c>.</param>
/// <param name="SessionId">The owning session identifier when the scope is session-based.</param>
/// <param name="DisplayName">Optional human-readable display name.</param>
/// <param name="MediaType">Optional MIME type such as <c>image/png</c>.</param>
public sealed record DeckAssetCreateRequest(
    string AssetPath,
    string SourceReference,
    string Description,
    string Kind = "image",
    string Scope = DeckAssetScopes.Session,
    string? SessionId = null,
    string? DisplayName = null,
    string? MediaType = null);

/// <summary>
/// Describes a search request over stored deck assets.
/// </summary>
/// <param name="Query">Optional free-text query. When omitted, all matching assets are returned.</param>
/// <param name="SessionId">Optional session identifier used to include session-scoped assets.</param>
/// <param name="MaxResults">The maximum number of results to return.</param>
public sealed record DeckAssetSearchRequest(
    string? Query = null,
    string? SessionId = null,
    int MaxResults = 20);

/// <summary>
/// Represents a resolved asset stream used by a deck provider while rendering a deck.
/// </summary>
public sealed class DeckAssetResolution
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;

    /// <summary>
    /// Initializes a resolved asset stream.
    /// </summary>
    public DeckAssetResolution(
        string assetPath,
        string sourceReference,
        string kind,
        string? mediaType,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync)
    {
        AssetPath = string.IsNullOrWhiteSpace(assetPath)
            ? throw new ArgumentException("An asset path is required.", nameof(assetPath))
            : assetPath;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference)
            ? throw new ArgumentException("A source reference is required.", nameof(sourceReference))
            : sourceReference;
        Kind = string.IsNullOrWhiteSpace(kind) ? "image" : kind;
        MediaType = mediaType;
        _openReadAsync = openReadAsync ?? throw new ArgumentNullException(nameof(openReadAsync));
    }

    /// <summary>
    /// Gets the stable DeckDoc asset path.
    /// </summary>
    public string AssetPath { get; }

    /// <summary>
    /// Gets the stored source reference used by the underlying interceptor.
    /// </summary>
    public string SourceReference { get; }

    /// <summary>
    /// Gets the asset kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the optional MIME type.
    /// </summary>
    public string? MediaType { get; }

    /// <summary>
    /// Opens a readable stream for the resolved asset.
    /// </summary>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _openReadAsync(cancellationToken);
}

/// <summary>
/// Intercepts deck asset registration, search, export/import, and stream resolution.
/// </summary>
/// <remarks>
/// Hosts can implement this abstraction to keep deck assets in session storage, blob storage, or any other backing
/// system while provider packages remain storage-agnostic.
/// </remarks>
public interface IDeckAssetInterceptor
{
    /// <summary>
    /// Creates or registers a new deck asset.
    /// </summary>
    ValueTask<DeckAssetRecord> CreateAsync(DeckAssetCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches known assets by free-text query.
    /// </summary>
    ValueTask<DeckAssetRecord[]> SearchAsync(DeckAssetSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single asset by its DeckDoc asset path.
    /// </summary>
    ValueTask<DeckAssetRecord?> GetAsync(string assetPath, string? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stored asset to a readable stream.
    /// </summary>
    ValueTask<DeckAssetResolution?> ResolveAsync(string assetPath, string? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports all known asset metadata for host-managed persistence.
    /// </summary>
    ValueTask<DeckAssetRecord[]> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports previously exported asset metadata.
    /// </summary>
    ValueTask ImportAsync(IEnumerable<DeckAssetRecord> assets, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configures the built-in local-file deck asset interceptor.
/// </summary>
public sealed class LocalFileDeckAssetInterceptorOptions
{
    /// <summary>
    /// Gets or sets the root directory used to store copied asset files.
    /// </summary>
    public string RootDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), ".deck-assets");
}

/// <summary>
/// Stores deck assets in the local file system while keeping searchable metadata in memory.
/// </summary>
/// <remarks>
/// This default implementation copies registered source files into a root folder, partitions session assets beneath a
/// per-session directory, and supports export/import of the metadata catalog so hosts can persist it across restarts.
/// </remarks>
public sealed class LocalFileDeckAssetInterceptor : IDeckAssetInterceptor
{
    private readonly ConcurrentDictionary<string, DeckAssetRecord> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootDirectory;

    /// <summary>
    /// Initializes the built-in local-file asset interceptor.
    /// </summary>
    public LocalFileDeckAssetInterceptor(LocalFileDeckAssetInterceptorOptions? options = null)
    {
        var normalizedOptions = options ?? new LocalFileDeckAssetInterceptorOptions();
        _rootDirectory = Path.GetFullPath(normalizedOptions.RootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    /// <inheritdoc />
    public async ValueTask<DeckAssetRecord> CreateAsync(DeckAssetCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedPath = NormalizeAssetPath(request.AssetPath);
        var normalizedScope = NormalizeScope(request.Scope, request.SessionId);
        var key = CreateKey(normalizedPath, normalizedScope, request.SessionId);
        if (_assets.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException($"Asset path '{normalizedPath}' already exists in scope '{normalizedScope}'. Existing asset: {JsonSerializer.Serialize(existing, ToolJsonSerializerOptions.CreateWeb())}");
        }

        var sourcePath = Path.GetFullPath(request.SourceReference);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"The source asset '{request.SourceReference}' was not found.", request.SourceReference);
        }

        var destinationPath = BuildStoragePath(normalizedPath, normalizedScope, request.SessionId);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"Asset path '{normalizedPath}' already exists on disk at '{destinationPath}'.");
        }

        await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }

        var stored = new DeckAssetRecord(
            normalizedPath,
            string.IsNullOrWhiteSpace(request.Kind) ? "image" : request.Kind,
            request.Description ?? string.Empty,
            destinationPath,
            normalizedScope,
            string.Equals(normalizedScope, DeckAssetScopes.Session, StringComparison.OrdinalIgnoreCase) ? request.SessionId : null,
            request.DisplayName,
            request.MediaType);

        if (!_assets.TryAdd(key, stored))
        {
            File.Delete(destinationPath);
            throw new InvalidOperationException($"Asset path '{normalizedPath}' already exists in scope '{normalizedScope}'.");
        }

        return stored;
    }

    /// <inheritdoc />
    public ValueTask<DeckAssetRecord[]> SearchAsync(DeckAssetSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var limit = Math.Clamp(request.MaxResults, 1, 200);
        var query = request.Query?.Trim();

        var results = _assets.Values
            .Where(asset => IsVisibleToSession(asset, request.SessionId))
            .Where(asset => string.IsNullOrWhiteSpace(query)
                || asset.AssetPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || asset.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || asset.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(asset.DisplayName) && asset.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(asset => asset.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return ValueTask.FromResult(results);
    }

    /// <inheritdoc />
    public ValueTask<DeckAssetRecord?> GetAsync(string assetPath, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeAssetPath(assetPath);
        if (!string.IsNullOrWhiteSpace(sessionId)
            && _assets.TryGetValue(CreateKey(normalizedPath, DeckAssetScopes.Session, sessionId), out var sessionAsset))
        {
            return ValueTask.FromResult<DeckAssetRecord?>(sessionAsset);
        }

        return ValueTask.FromResult(
            _assets.TryGetValue(CreateKey(normalizedPath, DeckAssetScopes.Global, sessionId: null), out var globalAsset)
                ? globalAsset
                : null);
    }

    /// <inheritdoc />
    public async ValueTask<DeckAssetResolution?> ResolveAsync(string assetPath, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        var asset = await GetAsync(assetPath, sessionId, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return null;
        }

        return new DeckAssetResolution(
            asset.AssetPath,
            asset.SourceReference,
            asset.Kind,
            asset.MediaType,
            innerCancellationToken =>
            {
                innerCancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<Stream>(new FileStream(asset.SourceReference, FileMode.Open, FileAccess.Read, FileShare.Read));
            });
    }

    /// <inheritdoc />
    public ValueTask<DeckAssetRecord[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_assets.Values
            .OrderBy(asset => asset.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.SessionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <inheritdoc />
    public ValueTask ImportAsync(IEnumerable<DeckAssetRecord> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizeAssetPath(asset.AssetPath);
            var normalizedScope = NormalizeScope(asset.Scope, asset.SessionId);
            var key = CreateKey(normalizedPath, normalizedScope, asset.SessionId);
            _assets[key] = asset with
            {
                AssetPath = normalizedPath,
                Scope = normalizedScope,
            };
        }

        return ValueTask.CompletedTask;
    }

    private string BuildStoragePath(string assetPath, string scope, string? sessionId)
    {
        var sanitizedPath = assetPath.Replace('/', Path.DirectorySeparatorChar);
        return string.Equals(scope, DeckAssetScopes.Session, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_rootDirectory, DeckAssetScopes.Session, sessionId ?? "default", sanitizedPath)
            : Path.Combine(_rootDirectory, DeckAssetScopes.Global, sanitizedPath);
    }

    private static bool IsVisibleToSession(DeckAssetRecord asset, string? sessionId) =>
        string.Equals(asset.Scope, DeckAssetScopes.Global, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(sessionId)
            && string.Equals(asset.Scope, DeckAssetScopes.Session, StringComparison.OrdinalIgnoreCase)
            && string.Equals(asset.SessionId, sessionId, StringComparison.Ordinal));

    private static string NormalizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new ArgumentException("An asset path is required.", nameof(assetPath));
        }

        var normalized = assetPath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("An asset path is required.", nameof(assetPath));
        }

        return normalized;
    }

    private static string NormalizeScope(string scope, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return string.IsNullOrWhiteSpace(sessionId) ? DeckAssetScopes.Global : DeckAssetScopes.Session;
        }

        var normalized = scope.Trim().ToLowerInvariant();
        return normalized switch
        {
            DeckAssetScopes.Global => DeckAssetScopes.Global,
            DeckAssetScopes.Session => !string.IsNullOrWhiteSpace(sessionId)
                ? DeckAssetScopes.Session
                : throw new ArgumentException("A session-scoped asset requires a session identifier.", nameof(sessionId)),
            _ => throw new ArgumentException($"Unsupported asset scope '{scope}'. Use '{DeckAssetScopes.Global}' or '{DeckAssetScopes.Session}'.", nameof(scope)),
        };
    }

    private static string CreateKey(string assetPath, string scope, string? sessionId) =>
        string.Equals(scope, DeckAssetScopes.Session, StringComparison.OrdinalIgnoreCase)
            ? $"{scope}:{sessionId}:{assetPath}"
            : $"{DeckAssetScopes.Global}:{assetPath}";
}

/// <summary>
/// Describes a stored deck template that can be surfaced to template tools.
/// </summary>
/// <param name="Name">The unique template name.</param>
/// <param name="Description">A concise description of the template style or intended use.</param>
/// <param name="DeckDoc">The DeckDoc content for the template.</param>
/// <param name="Source">Optional source label such as <c>builtin</c> or <c>user</c>.</param>
public sealed record DeckTemplateRecord(
    string Name,
    string Description,
    string DeckDoc,
    string Source = "user");

/// <summary>
/// Stores named DeckDoc templates for optional template-list and template-get tools.
/// </summary>
public interface IDeckTemplateStore
{
    /// <summary>
    /// Stores or replaces a named template.
    /// </summary>
    ValueTask StoreAsync(DeckTemplateRecord templateRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists templates, optionally filtered by free-text query.
    /// </summary>
    ValueTask<DeckTemplateRecord[]> ListAsync(string? query = null, int maxResults = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one template by exact name.
    /// </summary>
    ValueTask<DeckTemplateRecord?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports all known templates.
    /// </summary>
    ValueTask<DeckTemplateRecord[]> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports one or more templates.
    /// </summary>
    ValueTask ImportAsync(IEnumerable<DeckTemplateRecord> templates, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores deck templates in memory for hosts that load them during startup.
/// </summary>
public sealed class InMemoryDeckTemplateStore : IDeckTemplateStore
{
    private readonly ConcurrentDictionary<string, DeckTemplateRecord> _templates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask StoreAsync(DeckTemplateRecord templateRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateRecord);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(templateRecord.Name))
        {
            throw new ArgumentException("A template name is required.", nameof(templateRecord));
        }

        _templates[templateRecord.Name.Trim()] = templateRecord with { Name = templateRecord.Name.Trim() };
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<DeckTemplateRecord[]> ListAsync(string? query = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedQuery = query?.Trim();
        var limit = Math.Clamp(maxResults, 1, 200);

        var results = _templates.Values
            .Where(template => string.IsNullOrWhiteSpace(normalizedQuery)
                || template.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || template.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || template.Source.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return ValueTask.FromResult(results);
    }

    /// <inheritdoc />
    public ValueTask<DeckTemplateRecord?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValueTask.FromResult<DeckTemplateRecord?>(null);
        }

        return ValueTask.FromResult(
            _templates.TryGetValue(name.Trim(), out var template)
                ? template
                : null);
    }

    /// <inheritdoc />
    public ValueTask<DeckTemplateRecord[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_templates.Values
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <inheritdoc />
    public async ValueTask ImportAsync(IEnumerable<DeckTemplateRecord> templates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templates);

        foreach (var template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StoreAsync(template, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Provides JSON helpers for exported asset catalogs.
/// </summary>
public static class DeckAssetCatalogJson
{
    /// <summary>
    /// Serializes asset metadata to JSON.
    /// </summary>
    public static string Serialize(IEnumerable<DeckAssetRecord> assets) =>
        JsonSerializer.Serialize(assets, ToolJsonSerializerOptions.CreateWeb());

    /// <summary>
    /// Deserializes asset metadata from JSON.
    /// </summary>
    public static DeckAssetRecord[] Deserialize(string json) =>
        JsonSerializer.Deserialize<DeckAssetRecord[]>(json, ToolJsonSerializerOptions.CreateWeb()) ?? [];
}

/// <summary>
/// Provides JSON helpers for exported template catalogs.
/// </summary>
public static class DeckTemplateCatalogJson
{
    /// <summary>
    /// Serializes template metadata to JSON.
    /// </summary>
    public static string Serialize(IEnumerable<DeckTemplateRecord> templates) =>
        JsonSerializer.Serialize(templates, ToolJsonSerializerOptions.CreateWeb());

    /// <summary>
    /// Deserializes template metadata from JSON.
    /// </summary>
    public static DeckTemplateRecord[] Deserialize(string json) =>
        JsonSerializer.Deserialize<DeckTemplateRecord[]>(json, ToolJsonSerializerOptions.CreateWeb()) ?? [];
}