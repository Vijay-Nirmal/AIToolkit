using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Stores canonical AsciiDoc inside a Word package so round-trips remain lossless.
/// </summary>
/// <remarks>
/// The payload lives in a custom XML part that is ignored by Word itself but can be recovered by the document tools on a
/// later read. This allows external document appearance to diverge from the canonical AsciiDoc without losing fidelity and
/// gives <see cref="WordDocumentHandler"/> a stable source of truth during edits.
/// </remarks>
internal static class WordAsciiDocPayload
{
    private const string NamespaceUri = "urn:aitoolkit:document:asciidoc:1";

    /// <summary>
    /// Tries to read the embedded canonical AsciiDoc payload from a Word main document part.
    /// </summary>
    /// <param name="mainPart">The main document part that owns any custom XML payload parts.</param>
    /// <returns>The embedded canonical AsciiDoc when present; otherwise, <see langword="null"/>.</returns>
    public static string? TryRead(MainDocumentPart? mainPart)
    {
        if (mainPart is null)
        {
            return null;
        }

        XNamespace ns = NamespaceUri;
        foreach (var part in mainPart.CustomXmlParts)
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != ns + "asciidoc")
            {
                continue;
            }

            return document.Root.Element(ns + "content")?.Value;
        }

        return null;
    }

    /// <summary>
    /// Replaces any existing payload with a fresh canonical AsciiDoc payload.
    /// </summary>
    /// <param name="mainPart">The main document part that will own the payload.</param>
    /// <param name="asciiDoc">The canonical AsciiDoc to persist inside the package.</param>
    public static void Write(MainDocumentPart mainPart, string asciiDoc)
    {
        foreach (var part in mainPart.CustomXmlParts.ToList())
        {
            mainPart.DeletePart(part);
        }

        var customXmlPart = mainPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
        XNamespace ns = NamespaceUri;
        var document = new XDocument(
            new XElement(
                ns + "asciidoc",
                new XAttribute("version", "1"),
                new XElement(ns + "content", new XCData(asciiDoc))));

        using var stream = customXmlPart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
