using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Stores canonical DeckDoc inside a PowerPoint package so round-trips remain lossless.
/// </summary>
internal static class PowerPointDeckDocPayload
{
    private const string NamespaceUri = "urn:aitoolkit:deck:deckdoc:1";

    /// <summary>
    /// Reads the embedded DeckDoc payload when present.
    /// </summary>
    public static string? TryRead(PresentationPart? presentationPart)
    {
        if (presentationPart is null)
        {
            return null;
        }

        XNamespace ns = NamespaceUri;
        foreach (var part in presentationPart.CustomXmlParts)
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != ns + "deckdoc")
            {
                continue;
            }

            return document.Root.Element(ns + "content")?.Value;
        }

        return null;
    }

    /// <summary>
    /// Writes the canonical DeckDoc payload into the presentation package.
    /// </summary>
    public static void Write(PresentationPart presentationPart, string deckDoc)
    {
        foreach (var part in presentationPart.CustomXmlParts.ToList())
        {
            presentationPart.DeletePart(part);
        }

        var customXmlPart = presentationPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
        XNamespace ns = NamespaceUri;
        var document = new XDocument(
            new XElement(
                ns + "deckdoc",
                new XAttribute("version", "1"),
                new XElement(ns + "content", new XCData(deckDoc))));

        using var stream = customXmlPart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
