using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace AIToolkit.Tools.Document.Word;

/// <summary>
/// Stores canonical AsciiDoc inside a Word package so round-trips remain lossless.
/// </summary>
internal static class WordAsciiDocPayload
{
    private const string NamespaceUri = "urn:aitoolkit:document:asciidoc:1";

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