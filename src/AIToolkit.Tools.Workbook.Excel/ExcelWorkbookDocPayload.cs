using DocumentFormat.OpenXml.Packaging;
using System.Xml.Linq;

namespace AIToolkit.Tools.Workbook.Excel;

/// <summary>
/// Stores canonical WorkbookDoc inside an Excel package so round-trips remain lossless.
/// </summary>
internal static class ExcelWorkbookDocPayload
{
    private const string NamespaceUri = "urn:aitoolkit:workbook:workbookdoc:1";

    public static string? TryRead(WorkbookPart? workbookPart)
    {
        if (workbookPart is null)
        {
            return null;
        }

        XNamespace ns = NamespaceUri;
        foreach (var part in workbookPart.CustomXmlParts)
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != ns + "workbookdoc")
            {
                continue;
            }

            return document.Root.Element(ns + "content")?.Value;
        }

        return null;
    }

    public static void Write(WorkbookPart workbookPart, string workbookDoc)
    {
        foreach (var part in workbookPart.CustomXmlParts.ToList())
        {
            workbookPart.DeletePart(part);
        }

        var customXmlPart = workbookPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
        XNamespace ns = NamespaceUri;
        var document = new XDocument(
            new XElement(
                ns + "workbookdoc",
                new XAttribute("version", "1"),
                new XElement(ns + "content", new XCData(workbookDoc))));

        using var stream = customXmlPart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
