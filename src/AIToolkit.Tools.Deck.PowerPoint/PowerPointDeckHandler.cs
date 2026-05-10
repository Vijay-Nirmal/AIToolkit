using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Converts Open XML PowerPoint presentations to and from canonical DeckDoc.
/// </summary>
internal sealed class PowerPointDeckHandler(PowerPointDeckHandlerOptions options) : IDeckHandler
{
    internal static readonly string[] SupportedFileExtensions = [".pptx", ".pptm", ".potx", ".potm"];

    private readonly PowerPointDeckHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public string ProviderName => "powerpoint";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions => SupportedFileExtensions;

    /// <inheritdoc />
    public bool CanHandle(DeckHandlerContext context) =>
        SupportedFileExtensions.Contains(context.Extension, StringComparer.OrdinalIgnoreCase)
        && (_options.EnableLocalFileSupport || context.FilePath is null);

    /// <inheritdoc />
    public async ValueTask<DeckReadResponse> ReadAsync(DeckHandlerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var sourceStream = await context.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var bufferedStream = sourceStream.CanSeek ? null : await EnsureReadableStreamAsync(sourceStream, cancellationToken).ConfigureAwait(false);
        var workingStream = bufferedStream ?? sourceStream;
        if (workingStream.CanSeek)
        {
            workingStream.Position = 0;
        }

        using var presentation = PresentationDocument.Open(workingStream, false);
        var payload = _options.PreferEmbeddedDeckDoc
            ? PowerPointDeckDocPayload.TryRead(presentation.PresentationPart)
            : null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return new DeckReadResponse(
                DeckDoc: payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'),
                IsLosslessRoundTrip: true,
                SourceFormat: NormalizeFormat(context.Extension));
        }

        if (!_options.EnableBestEffortImport)
        {
            return new DeckReadResponse(
                DeckDoc: string.Empty,
                IsLosslessRoundTrip: false,
                SourceFormat: NormalizeFormat(context.Extension),
                Message: "This PowerPoint presentation does not contain embedded canonical DeckDoc.");
        }

        var imported = PowerPointDeckImporter.Import(presentation, context.ResolvedReference);
        return new DeckReadResponse(
            DeckDoc: imported,
            IsLosslessRoundTrip: false,
            SourceFormat: NormalizeFormat(context.Extension),
            Message: "Imported external PowerPoint presentation into best-effort DeckDoc.");
    }

    /// <inheritdoc />
    public async ValueTask<DeckWriteResponse> WriteAsync(DeckHandlerContext context, string deckDoc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var destinationStream = await context.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = destinationStream.CanSeek ? null : new MemoryStream();
        var writeStream = bufferedStream ?? destinationStream;
        if (writeStream.CanSeek)
        {
            writeStream.Position = 0;
            writeStream.SetLength(0);
        }

        var document = DeckDocParser.Parse(deckDoc);
        var resolvedImages = await ResolveImagesAsync(document, context, cancellationToken).ConfigureAwait(false);

        using (var presentation = PresentationDocument.Create(writeStream, ResolveDocumentType(context.Extension)))
        {
            PowerPointDeckWriter.Write(presentation, document, resolvedImages.Images);
            PowerPointDeckDocPayload.Write(presentation.PresentationPart!, deckDoc);
            presentation.PackageProperties.Creator = "AIToolkit.Tools.Deck.PowerPoint";
            presentation.PackageProperties.Description = "Canonical DeckDoc round-trip presentation";
            presentation.PackageProperties.Title = document.Title;
        }

        if (bufferedStream is not null)
        {
            bufferedStream.Position = 0;
            await bufferedStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DeckWriteResponse(
            PreservesDeckDocRoundTrip: true,
            OutputFormat: NormalizeFormat(context.Extension),
            Message: resolvedImages.Message ?? "Stored canonical DeckDoc inside the PowerPoint package for lossless round-tripping.");
    }

    private static async ValueTask<MemoryStream> EnsureReadableStreamAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        var bufferedStream = new MemoryStream();
        await sourceStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
        bufferedStream.Position = 0;
        return bufferedStream;
    }

    private static PresentationDocumentType ResolveDocumentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".pptm" => PresentationDocumentType.MacroEnabledPresentation,
            ".potx" => PresentationDocumentType.Template,
            ".potm" => PresentationDocumentType.MacroEnabledTemplate,
            _ => PresentationDocumentType.Presentation,
        };

    private static string NormalizeFormat(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? "powerpoint" : extension.TrimStart('.').ToLowerInvariant();

    private static async Task<(Dictionary<string, ResolvedDeckImage> Images, string? Message)> ResolveImagesAsync(
        DeckDocDocument document,
        DeckHandlerContext context,
        CancellationToken cancellationToken)
    {
        var images = new Dictionary<string, ResolvedDeckImage>(StringComparer.OrdinalIgnoreCase);
        var missingAssets = new List<string>();

        foreach (var request in CollectImageRequests(document))
        {
            if (request.AssetName is string assetName)
            {
                if (!document.SharedAssets.TryGetValue(assetName, out var assetReference))
                {
                    missingAssets.Add(assetName);
                    continue;
                }

                var resolvedImage = await ResolveImageAsync(assetReference, context, cancellationToken).ConfigureAwait(false);
                if (resolvedImage is null)
                {
                    missingAssets.Add(assetName);
                    continue;
                }

                images[assetName] = resolvedImage;
                continue;
            }

            if (request.DirectReference is string directReference)
            {
                var resolvedImage = await ResolveImageAsync(directReference, context, cancellationToken).ConfigureAwait(false);
                if (resolvedImage is null)
                {
                    missingAssets.Add(directReference);
                    continue;
                }

                images[$"ref:{directReference}"] = resolvedImage;
            }
        }

        if (missingAssets.Count == 0)
        {
            return (images, null);
        }

        return (
            images,
            $"Stored canonical DeckDoc inside the PowerPoint package for lossless round-tripping. Skipped {missingAssets.Count} unresolved asset reference(s): {string.Join(", ", missingAssets)}.");
    }

    private static IEnumerable<ImageRequest> CollectImageRequests(DeckDocDocument document)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arguments in EnumerateImageArgumentSources(document))
        {
            if (arguments.GetValue("asset") is string assetName && seen.Add($"asset:{assetName}"))
            {
                yield return new ImageRequest(assetName, null);
            }

            if (arguments.GetValue("ref") is string directReference && seen.Add($"ref:{directReference}"))
            {
                yield return new ImageRequest(null, directReference);
            }
        }
    }

    private static IEnumerable<DeckDirectiveArguments> EnumerateImageArgumentSources(DeckDocDocument document)
    {
        foreach (var layout in document.Layouts)
        {
            if (layout.Background is not null)
            {
                yield return layout.Background;
            }

            foreach (var target in layout.Targets)
            {
                if (target is DeckAreaTargetDefinition area)
                {
                    yield return area.Arguments;
                }
                else if (target is DeckGridTargetDefinition grid)
                {
                    yield return grid.Arguments;
                }
                else if (target is DeckStackTargetDefinition stack)
                {
                    yield return stack.Arguments;
                }
            }

            foreach (var fixedObject in layout.FixedObjects)
            {
                yield return fixedObject.Arguments;
            }

            foreach (var slot in layout.Slots)
            {
                yield return slot.Arguments;
            }
        }

        foreach (var slide in document.Slides)
        {
            if (slide.Background is not null)
            {
                yield return slide.Background;
            }

            foreach (var target in slide.Targets)
            {
                if (target is DeckAreaTargetDefinition area)
                {
                    yield return area.Arguments;
                }
                else if (target is DeckGridTargetDefinition grid)
                {
                    yield return grid.Arguments;
                }
                else if (target is DeckStackTargetDefinition stack)
                {
                    yield return stack.Arguments;
                }
            }

            foreach (var deckObject in slide.Objects)
            {
                yield return deckObject.Arguments;
            }

            foreach (var table in slide.Tables)
            {
                yield return table.Arguments;
            }

            foreach (var chart in slide.Charts)
            {
                yield return chart.Arguments;
            }
        }
    }

    private static async Task<ResolvedDeckImage?> ResolveImageAsync(string assetReference, DeckHandlerContext context, CancellationToken cancellationToken)
    {
        var interceptor = context.Options.AssetInterceptor;
        if (interceptor is not null)
        {
            var resolution = await interceptor.ResolveAsync(assetReference, context.Options.AssetSessionId, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                await using var stream = await resolution.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                return new ResolvedDeckImage(await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false), GuessContentType(assetReference, resolution.MediaType));
            }
        }

        var localPath = ResolveLocalAssetPath(assetReference, context);
        if (localPath is null || !File.Exists(localPath))
        {
            return null;
        }

        await using var fileStream = File.OpenRead(localPath);
        return new ResolvedDeckImage(await ReadAllBytesAsync(fileStream, cancellationToken).ConfigureAwait(false), GuessContentType(localPath, mediaType: null));
    }

    private static string? ResolveLocalAssetPath(string assetReference, DeckHandlerContext context)
    {
        if (string.IsNullOrWhiteSpace(assetReference))
        {
            return null;
        }

        if (Uri.TryCreate(assetReference, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return uri.LocalPath;
            }

            return null;
        }

        var candidateDirectories = new[]
        {
            context.Options.WorkingDirectory,
            context.FilePath is not null ? Path.GetDirectoryName(context.FilePath) : null,
        };

        foreach (var baseDirectory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                continue;
            }

            var candidatePath = Path.GetFullPath(Path.Combine(baseDirectory, assetReference));
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        var fallbackBaseDirectory = context.Options.WorkingDirectory
            ?? (context.FilePath is not null ? Path.GetDirectoryName(context.FilePath) : null);
        if (string.IsNullOrWhiteSpace(fallbackBaseDirectory))
        {
            fallbackBaseDirectory = Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(Path.Combine(fallbackBaseDirectory, assetReference));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private static string GuessContentType(string pathOrReference, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            return mediaType;
        }

        return Path.GetExtension(pathOrReference).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };
    }

    /// <summary>
    /// Describes an asset request discovered while scanning the parsed DeckDoc model.
    /// </summary>
    private readonly record struct ImageRequest(string? AssetName, string? DirectReference);
}
