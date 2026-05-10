using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Exports one PNG per slide from a PowerPoint presentation using the same reference-resolution rules as the PowerPoint
/// deck tools.
/// </summary>
internal sealed class PowerPointSlideImageExportService
{
    private readonly DeckToolsOptions _options;
    private readonly string _defaultWorkingDirectory;
    private readonly IPowerPointSlideImageExporter _exporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerPointSlideImageExportService"/> class.
    /// </summary>
    public PowerPointSlideImageExportService(
        PowerPointDeckHandlerOptions handlerOptions,
        DeckToolsOptions? deckOptions = null,
        IPowerPointSlideImageExporter? exporter = null)
    {
        _options = PowerPointDeckToolSupport.CloneWithHandler(deckOptions, handlerOptions);
        _defaultWorkingDirectory = PowerPointDeckToolSupport.NormalizeDirectory(_options.WorkingDirectory);
        _exporter = exporter ?? CreateDefaultExporter();
    }

    private static IPowerPointSlideImageExporter CreateDefaultExporter() =>
        OperatingSystem.IsWindows()
            ? new ComPowerPointSlideImageExporter()
            : new UnsupportedPowerPointSlideImageExporter();

    /// <summary>
    /// Exports the supplied presentation reference to one PNG per slide.
    /// </summary>
    public async Task<PowerPointDeckSlideImageExportResult> ExportAsync(
        string deckReference,
        PowerPointDeckSlideImageExportOptions? options = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new PowerPointDeckSlideImageExportOptions();

        try
        {
            ValidateExportDimensions(effectiveOptions.Width, effectiveOptions.Height);

            var resolution = await ResolveDeckResolutionAsync(deckReference, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (resolution.FilePath is not null && Directory.Exists(resolution.FilePath))
            {
                return new PowerPointDeckSlideImageExportResult(
                    false,
                    resolution.ResolvedReference,
                    string.Empty,
                    [],
                    "The path refers to a directory. This operation can only export PowerPoint files.");
            }

            if (!await resolution.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return new PowerPointDeckSlideImageExportResult(false, resolution.ResolvedReference, string.Empty, [], "File not found.");
            }

            if (!PowerPointDeckHandler.SupportedFileExtensions.Contains(resolution.Extension, StringComparer.OrdinalIgnoreCase))
            {
                return new PowerPointDeckSlideImageExportResult(
                    false,
                    resolution.ResolvedReference,
                    string.Empty,
                    [],
                    "Input file must be a PowerPoint presentation or template (.pptx, .pptm, .potx, .potm).");
            }

            var outputDirectory = ResolveOutputDirectory(resolution, effectiveOptions.OutputDirectory);
            PrepareOutputDirectory(outputDirectory, effectiveOptions.Force);

            var (localPath, deleteWhenDone) = await MaterializeLocalPresentationAsync(resolution, cancellationToken).ConfigureAwait(false);
            try
            {
                var slides = await _exporter.ExportAsync(
                    localPath,
                    outputDirectory,
                    effectiveOptions.Width,
                    effectiveOptions.Height,
                    effectiveOptions.Force,
                    cancellationToken).ConfigureAwait(false);

                return new PowerPointDeckSlideImageExportResult(
                    true,
                    resolution.ResolvedReference,
                    outputDirectory,
                    [.. slides.OrderBy(static slide => slide.SlideNumber)]);
            }
            finally
            {
                if (deleteWhenDone)
                {
                    TryDeleteTemporaryPresentation(localPath);
                }
            }
        }
        catch (Exception exception)
        {
            return new PowerPointDeckSlideImageExportResult(
                false,
                string.IsNullOrWhiteSpace(deckReference) ? string.Empty : deckReference,
                options?.OutputDirectory ?? string.Empty,
                [],
                exception.Message);
        }
    }

    private async Task<DeckReferenceResolution> ResolveDeckResolutionAsync(
        string deckReference,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deckReference))
        {
            throw new ArgumentException("A deck reference is required.", nameof(deckReference));
        }

        var resolver = _options.ReferenceResolver
            ?? serviceProvider?.GetService(typeof(IDeckReferenceResolver)) as IDeckReferenceResolver;
        if (resolver is not null)
        {
            var resolutionContext = new DeckReferenceResolverContext(
                deckReference,
                _defaultWorkingDirectory,
                DeckToolOperation.Read,
                _options,
                serviceProvider);
            var resolution = await resolver.ResolveAsync(deckReference, resolutionContext, cancellationToken).ConfigureAwait(false);
            if (resolution is not null)
            {
                return resolution;
            }
        }

        return DeckReferenceResolution.CreateFile(PowerPointDeckToolSupport.ResolvePath(deckReference, _defaultWorkingDirectory));
    }

    private static void ValidateExportDimensions(int width, int height)
    {
        if ((width > 0 && height <= 0) || (height > 0 && width <= 0))
        {
            throw new InvalidOperationException("Width and Height must both be greater than zero when either value is provided.");
        }
    }

    private static async Task<(string LocalPath, bool DeleteWhenDone)> MaterializeLocalPresentationAsync(
        DeckReferenceResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.FilePath is not null && File.Exists(resolution.FilePath))
        {
            return (resolution.FilePath, false);
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "AIToolkit.Tools.Deck.PowerPoint",
            "slide-image-export",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var extension = string.IsNullOrWhiteSpace(resolution.Extension) ? ".pptx" : resolution.Extension;
        var tempPath = Path.Combine(tempDirectory, "presentation" + extension);

        await using var sourceStream = await resolution.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        await using var destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);

        return (tempPath, true);
    }

    private string ResolveOutputDirectory(DeckReferenceResolution resolution, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(
                Path.IsPathRooted(outputDirectory)
                    ? outputDirectory
                    : Path.Combine(_defaultWorkingDirectory, outputDirectory));
        }

        var parentDirectory = resolution.FilePath is not null
            ? Path.GetDirectoryName(resolution.FilePath)
            : _defaultWorkingDirectory;
        parentDirectory = string.IsNullOrWhiteSpace(parentDirectory) ? _defaultWorkingDirectory : parentDirectory;

        return Path.Combine(parentDirectory, GetReferenceStem(resolution) + "-png");
    }

    private static void PrepareOutputDirectory(string outputDirectory, bool force)
    {
        if (Directory.Exists(outputDirectory))
        {
            if (!force)
            {
                throw new InvalidOperationException($"Output folder already exists: {outputDirectory}. Use force=true to overwrite existing PNG files.");
            }

            foreach (var file in Directory.EnumerateFiles(outputDirectory, "Slide*.png", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }
        else
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static string GetReferenceStem(DeckReferenceResolution resolution)
    {
        if (resolution.FilePath is not null)
        {
            var fileName = Path.GetFileNameWithoutExtension(resolution.FilePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        if (Uri.TryCreate(resolution.ResolvedReference, UriKind.Absolute, out var uri))
        {
            var uriName = Path.GetFileNameWithoutExtension(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(uriName))
            {
                return SanitizeFileName(uriName);
            }
        }

        var fallback = Path.GetFileNameWithoutExtension(resolution.ResolvedReference);
        return string.IsNullOrWhiteSpace(fallback) ? "presentation" : SanitizeFileName(fallback);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(builder) ? "presentation" : builder;
    }

    private static void TryDeleteTemporaryPresentation(string localPath)
    {
        try
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }

            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}

/// <summary>
/// Adapts the slide-image export service to the stable public PowerPoint tool surface.
/// </summary>
internal sealed class PowerPointSlideImageExportToolService(PowerPointDeckHandlerOptions handlerOptions, DeckToolsOptions? deckOptions = null)
{
    private readonly PowerPointSlideImageExportService _service = new(handlerOptions, deckOptions);

    /// <summary>
    /// Exports one PNG per slide from a PowerPoint presentation.
    /// </summary>
    /// <param name="deck_reference">The local path or resolver-backed PowerPoint reference to export.</param>
    /// <param name="output_directory">The optional destination directory for PNG files.</param>
    /// <param name="width">Optional export width in pixels.</param>
    /// <param name="height">Optional export height in pixels.</param>
    /// <param name="force"><see langword="true"/> to overwrite an existing export directory.</param>
    /// <param name="serviceProvider">The optional service provider used for resolver-backed references.</param>
    /// <param name="cancellationToken">A token that cancels the export.</param>
    /// <returns>The exported slide image metadata.</returns>
    public Task<PowerPointDeckSlideImageExportResult> ExportSlideImagesAsync(
        string deck_reference,
        string? output_directory = null,
        int? width = null,
        int? height = null,
        bool force = false,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default) =>
        _service.ExportAsync(
            deck_reference,
            new PowerPointDeckSlideImageExportOptions
            {
                OutputDirectory = output_directory,
                Width = width ?? 0,
                Height = height ?? 0,
                Force = force,
            },
            serviceProvider,
            cancellationToken);
}

/// <summary>
/// Exports one PNG per slide from a local PowerPoint file.
/// </summary>
internal interface IPowerPointSlideImageExporter
{
    /// <summary>
    /// Exports the supplied local PowerPoint presentation to PNG files.
    /// </summary>
    Task<PowerPointDeckSlideImage[]> ExportAsync(
        string inputPath,
        string outputDirectory,
        int width,
        int height,
        bool force,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fails slide-image export on platforms where Microsoft PowerPoint automation is not available.
/// </summary>
internal sealed class UnsupportedPowerPointSlideImageExporter : IPowerPointSlideImageExporter
{
    /// <inheritdoc />
    public Task<PowerPointDeckSlideImage[]> ExportAsync(
        string inputPath,
        string outputDirectory,
        int width,
        int height,
        bool force,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("PowerPoint slide image export requires Windows with Microsoft PowerPoint installed.");
}

/// <summary>
/// Uses Microsoft PowerPoint COM automation to export one PNG per slide.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ComPowerPointSlideImageExporter : IPowerPointSlideImageExporter
{
    /// <inheritdoc />
    public Task<PowerPointDeckSlideImage[]> ExportAsync(
        string inputPath,
        string outputDirectory,
        int width,
        int height,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("PowerPoint slide image export requires Windows because it automates Microsoft PowerPoint.");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Presentation not found.", inputPath);
        }

        var applicationType = Type.GetTypeFromProgID("PowerPoint.Application");
        if (applicationType is null)
        {
            throw new InvalidOperationException("Microsoft PowerPoint must be installed on this Windows machine to export slides as PNG.");
        }

        object? application = null;
        object? presentation = null;

        try
        {
            application = Activator.CreateInstance(applicationType)
                ?? throw new InvalidOperationException("Unable to create the PowerPoint COM application.");
            dynamic powerPoint = application;
            presentation = powerPoint.Presentations.Open(inputPath, false, true, false);
            dynamic slides = ((dynamic)presentation).Slides;
            var count = (int)slides.Count;
            var exportedSlides = new List<PowerPointDeckSlideImage>(count);

            for (var index = 1; index <= count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                object? slideObject = null;
                try
                {
                    slideObject = slides[index];
                    dynamic slide = slideObject;
                    var slideNumber = (int)slide.SlideIndex;
                    var targetPath = Path.Combine(outputDirectory, $"Slide{slideNumber:000}.png");
                    if (File.Exists(targetPath) && !force)
                    {
                        throw new InvalidOperationException($"Output file already exists: {targetPath}. Use force=true to overwrite existing PNG files.");
                    }

                    if (width > 0 && height > 0)
                    {
                        slide.Export(targetPath, "PNG", width, height);
                    }
                    else
                    {
                        slide.Export(targetPath, "PNG");
                    }

                    exportedSlides.Add(new PowerPointDeckSlideImage(slideNumber, targetPath));
                }
                finally
                {
                    ReleaseComObject(slideObject);
                }
            }

            return Task.FromResult<PowerPointDeckSlideImage[]>([.. exportedSlides.OrderBy(static slide => slide.SlideNumber)]);
        }
        finally
        {
            if (presentation is not null)
            {
                ((dynamic)presentation).Close();
                ReleaseComObject(presentation);
            }

            if (application is not null)
            {
                ((dynamic)application).Quit();
                ReleaseComObject(application);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}