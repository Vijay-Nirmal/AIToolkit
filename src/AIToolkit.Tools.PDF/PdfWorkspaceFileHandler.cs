using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AIToolkit.Tools.PDF;

/// <summary>
/// Extracts text and embedded images from PDF files for workspace reads.
/// </summary>
/// <remarks>
/// The handler is intentionally read-only. It interprets the <c>pages</c> selector, ignores line-based offset/limit
/// parameters that make sense for text files, extracts text with PdfPig, and best-effort converts embedded image payloads
/// into <see cref="DataContent"/> parts when configured. <see cref="PdfWorkspaceTools"/> exposes this handler through the
/// generic workspace tool surface.
/// </remarks>
internal sealed class PdfWorkspaceFileHandler(PdfWorkspaceFileHandlerOptions options) : IWorkspaceFileHandler
{
    private readonly PdfWorkspaceFileHandlerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Determines whether the workspace request targets a PDF file.
    /// </summary>
    /// <param name="context">The workspace read context to inspect.</param>
    /// <returns><see langword="true"/> when the request targets a <c>.pdf</c> file; otherwise, <see langword="false"/>.</returns>
    public bool CanHandle(WorkspaceFileReadContext context) =>
        string.Equals(context.Extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads selected PDF pages and returns extracted text and optional image content.
    /// </summary>
    /// <param name="context">The workspace read context that provides the PDF bytes and request options.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>
    /// A sequence of <see cref="AIContent"/> parts that starts with a summary message and then includes extracted page text
    /// plus any decoded images that fit within the configured limits.
    /// </returns>
    public async ValueTask<IEnumerable<AIContent>> ReadAsync(
        WorkspaceFileReadContext context,
        CancellationToken cancellationToken = default)
    {
        var documentBytes = await context.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
        using var document = PdfDocument.Open(
            documentBytes,
            new ParsingOptions
            {
                UseLenientParsing = _options.UseLenientParsing,
            });

        // Page selection is handled up front so invalid selectors fail with a clear text response instead of partial data.
        if (!TryResolvePageNumbers(context.Request.Pages, document.NumberOfPages, _options.MaxPages, out var pageNumbers, out var pageMessage))
        {
            return
            [
                new TextContent(pageMessage),
            ];
        }

        var summary = new StringBuilder();
        summary.Append("PDF extraction for ");
        summary.Append(context.Request.FilePath);
        summary.Append(". Document pages: ");
        summary.Append(document.NumberOfPages.ToString(CultureInfo.InvariantCulture));
        summary.Append(". Selected pages: ");
        summary.Append(FormatPageNumbers(pageNumbers));
        summary.Append('.');

        if (!string.IsNullOrWhiteSpace(pageMessage))
        {
            summary.Append(' ');
            summary.Append(pageMessage);
        }

        if (context.Request.Offset is not null || context.Request.Limit is not null)
        {
            summary.Append(" The PDF handler ignores offset and limit. Use the pages parameter to select PDF pages.");
        }

        var contents = new List<AIContent>
        {
            new TextContent(summary.ToString()),
        };

        var imageParts = new List<AIContent>();
        var returnedImages = 0;
        var skippedImages = 0;
        var effectiveMaxImageBytes = Math.Min(_options.MaxImageBytes, context.Options.MaxReadBytes);

        foreach (var pageNumber in pageNumbers)
        {
            var page = document.GetPage(pageNumber);

            if (_options.IncludeText)
            {
                contents.Add(new TextContent(FormatPageText(pageNumber, page)));
            }

            if (!_options.IncludeImages)
            {
                continue;
            }

            var pageImageIndex = 0;
            foreach (var image in page.GetImages())
            {
                pageImageIndex++;
                if (returnedImages >= _options.MaxImages)
                {
                    skippedImages++;
                    continue;
                }

                // Image extraction is best-effort because PdfPig can surface raw image bytes in different shapes depending
                // on how the PDF was authored.
                if (!TryCreateImageContent(
                    image,
                    context.Request.FilePath,
                    pageNumber,
                    pageImageIndex,
                    effectiveMaxImageBytes,
                    out var label,
                    out var dataContent))
                {
                    imageParts.Add(new TextContent(label));
                    skippedImages++;
                    continue;
                }

                imageParts.Add(new TextContent(label));
                imageParts.Add(dataContent);
                returnedImages++;
            }
        }

        if (_options.IncludeImages)
        {
            contents.Add(new TextContent($"Returned {returnedImages.ToString(CultureInfo.InvariantCulture)} extracted image(s) from the selected PDF pages."));
            if (skippedImages > 0)
            {
                contents.Add(new TextContent($"Skipped {skippedImages.ToString(CultureInfo.InvariantCulture)} image(s) because they exceeded limits or could not be converted to a supported byte representation."));
            }
        }

        contents.AddRange(imageParts);

        if (contents.Count == 1)
        {
            contents.Add(new TextContent("The selected PDF pages did not contain any extractable text or images."));
        }

        return contents;
    }

    private string FormatPageText(int pageNumber, Page page)
    {
        var text = NormalizeExtractedText(ContentOrderTextExtractor.GetText(page));
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} text:\n<no extractable text>";
        }

        if (text.Length > _options.MaxTextCharactersPerPage)
        {
            text = text[.._options.MaxTextCharactersPerPage].TrimEnd() + "\n[Truncated page text]";
        }

        return $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} text:\n{text}";
    }

    private static string NormalizeExtractedText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static bool TryResolvePageNumbers(
        string? pages,
        int totalPages,
        int maxPages,
        out IReadOnlyList<int> pageNumbers,
        out string message)
    {
        if (maxPages <= 0)
        {
            pageNumbers = [];
            message = "PdfWorkspaceFileHandlerOptions.MaxPages must be greater than 0.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(pages))
        {
            var selected = Enumerable.Range(1, Math.Min(totalPages, maxPages)).ToArray();
            pageNumbers = selected;
            message = selected.Length < totalPages
                ? $"Page selection was limited to the first {selected.Length.ToString(CultureInfo.InvariantCulture)} page(s) by MaxPages."
                : string.Empty;
            return true;
        }

        var selectedPages = new SortedSet<int>();
        foreach (var segment in pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dashIndex = segment.IndexOf('-', StringComparison.Ordinal);
            if (dashIndex >= 0)
            {
                var startText = segment[..dashIndex].Trim();
                var endText = segment[(dashIndex + 1)..].Trim();
                if (!int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                    || !int.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out var end)
                    || start <= 0
                    || end <= 0
                    || start > end)
                {
                    pageNumbers = [];
                    message = $"Invalid pages value '{pages}'. Use formats such as '1', '1,3,5', or '2-4'.";
                    return false;
                }

                if (end > totalPages)
                {
                    pageNumbers = [];
                    message = $"Requested page range '{segment}' exceeds the PDF page count of {totalPages.ToString(CultureInfo.InvariantCulture)}.";
                    return false;
                }

                for (var page = start; page <= end; page++)
                {
                    selectedPages.Add(page);
                }
            }
            else
            {
                if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
                    || page <= 0)
                {
                    pageNumbers = [];
                    message = $"Invalid pages value '{pages}'. Use formats such as '1', '1,3,5', or '2-4'.";
                    return false;
                }

                if (page > totalPages)
                {
                    pageNumbers = [];
                    message = $"Requested page '{page.ToString(CultureInfo.InvariantCulture)}' exceeds the PDF page count of {totalPages.ToString(CultureInfo.InvariantCulture)}.";
                    return false;
                }

                selectedPages.Add(page);
            }
        }

        if (selectedPages.Count == 0)
        {
            pageNumbers = [];
            message = "The pages parameter did not resolve to any PDF pages.";
            return false;
        }

        pageNumbers = selectedPages.Take(maxPages).ToArray();
        message = selectedPages.Count > maxPages
            ? $"Page selection was limited to the first {maxPages.ToString(CultureInfo.InvariantCulture)} page(s) by MaxPages."
            : string.Empty;
        return true;
    }

    private static string FormatPageNumbers(IReadOnlyList<int> pageNumbers) =>
        string.Join(", ", pageNumbers.Select(static page => page.ToString(CultureInfo.InvariantCulture)));

    private static bool TryCreateImageContent(
        IPdfImage image,
        string filePath,
        int pageNumber,
        int imageIndex,
        int maxImageBytes,
        out string label,
        out DataContent dataContent)
    {
        byte[]? bytes = null;
        string mediaType;

        if (image.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
        {
            bytes = pngBytes;
            mediaType = "image/png";
        }
        else if (image.TryGetBytesAsMemory(out var memory) && !memory.IsEmpty)
        {
            bytes = memory.ToArray();
            mediaType = GuessMediaType(bytes);
        }
        else if (!image.RawMemory.IsEmpty)
        {
            bytes = image.RawMemory.ToArray();
            mediaType = GuessMediaType(bytes);
        }
        else
        {
            label = $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} image {imageIndex.ToString(CultureInfo.InvariantCulture)} could not be converted to bytes.";
            dataContent = null!;
            return false;
        }

        if (bytes.Length > maxImageBytes)
        {
            label = $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} image {imageIndex.ToString(CultureInfo.InvariantCulture)} was {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes and exceeded the configured limit of {maxImageBytes.ToString(CultureInfo.InvariantCulture)} bytes.";
            dataContent = null!;
            return false;
        }

        var extension = GetExtension(mediaType);
        label = $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} image {imageIndex.ToString(CultureInfo.InvariantCulture)} ({mediaType}).";
        dataContent = new DataContent(bytes, mediaType)
        {
            Name = $"{Path.GetFileNameWithoutExtension(filePath)}-page-{pageNumber.ToString(CultureInfo.InvariantCulture)}-image-{imageIndex.ToString(CultureInfo.InvariantCulture)}{extension}",
        };

        return true;
    }

    private static string GuessMediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6
            && bytes[0] == 0x47
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x38)
        {
            return "image/gif";
        }

        if (bytes.Length >= 2
            && bytes[0] == 0x42
            && bytes[1] == 0x4D)
        {
            return "image/bmp";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x46
            && bytes[8] == 0x57
            && bytes[9] == 0x45
            && bytes[10] == 0x42
            && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }

    private static string GetExtension(string mediaType) =>
        mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            _ => ".bin",
        };
}
