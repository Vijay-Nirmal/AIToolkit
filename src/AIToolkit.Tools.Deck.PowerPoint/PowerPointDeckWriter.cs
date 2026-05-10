using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using AIToolkit.Tools.Deck;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Writes a PowerPoint presentation from the shared DeckDoc model.
/// </summary>
internal static class PowerPointDeckWriter
{
    private const long SlideWidth = 12_192_000L;
    private const long SlideHeight = 6_858_000L;

    /// <summary>
    /// Writes the supplied model into the target presentation document.
    /// </summary>
    public static void Write(
        PresentationDocument presentationDocument,
        DeckDocDocument document,
        IReadOnlyDictionary<string, ResolvedDeckImage> resolvedImages)
    {
        ArgumentNullException.ThrowIfNull(presentationDocument);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolvedImages);

        var presentationPart = presentationDocument.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation();

        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        var themePart = slideMasterPart.AddNewPart<ThemePart>();

        WriteTheme(themePart, document);
        WriteSlideLayout(slideLayoutPart, slideMasterPart);
        WriteSlideMaster(slideMasterPart, slideLayoutPart);

        var slideIdList = new P.SlideIdList();
        uint slideId = 256U;
        foreach (var slide in document.Slides)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.AddPart(slideLayoutPart);
            slidePart.Slide = PowerPointDeckRenderer.CreateSlide(slidePart, document, slide, resolvedImages);
            slidePart.Slide.Save();
            slideIdList.Append(new P.SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart),
            });
        }

        presentationPart.Presentation.Append(
            new P.SlideMasterIdList(
                new P.SlideMasterId
                {
                    Id = 2_147_483_648U,
                    RelationshipId = presentationPart.GetIdOfPart(slideMasterPart),
                }),
            slideIdList,
            new P.SlideSize { Cx = (int)SlideWidth, Cy = (int)SlideHeight },
            new P.NotesSize { Cx = 6_858_000, Cy = 9_144_000 },
            new P.DefaultTextStyle());

        presentationPart.Presentation.Save();
    }

    private static P.ShapeTree CreateShapeTree() =>
        new(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(
                new A.TransformGroup(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = 0L, Cy = 0L },
                    new A.ChildOffset { X = 0L, Y = 0L },
                    new A.ChildExtents { Cx = 0L, Cy = 0L })));

    private static P.Shape CreateTextShape(uint id, string name, long x, long y, long cx, long cy, IEnumerable<string> lines, int fontSize, bool bold)
    {
        var textBody = new P.TextBody(
            new A.BodyProperties
            {
                Anchor = A.TextAnchoringTypeValues.Top,
            },
            new A.ListStyle());

        foreach (var line in lines)
        {
            textBody.Append(CreateParagraph(line, fontSize, bold));
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill(),
                new A.Outline(new A.NoFill())),
            textBody);
    }

    private static A.Paragraph CreateParagraph(string line, int fontSize, bool bold)
    {
        var paragraph = new A.Paragraph();
        paragraph.Append(new A.ParagraphProperties());
        paragraph.Append(
            new A.Run(
                new A.RunProperties
                {
                    Language = "en-US",
                    FontSize = fontSize,
                    Bold = bold,
                    Dirty = false,
                },
                new A.Text(line ?? string.Empty)));
        paragraph.Append(new A.EndParagraphRunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Dirty = false,
        });
        return paragraph;
    }

    private static P.Picture CreatePicture(SlidePart slidePart, uint id, string name, long x, long y, long cx, long cy, ResolvedDeckImage image)
    {
        var imagePart = slidePart.AddImagePart(image.ContentType);
        using (var stream = imagePart.GetStream(FileMode.Create, FileAccess.Write))
        {
            stream.Write(image.Content, 0, image.Content.Length);
        }

        var relationshipId = slidePart.GetIdOfPart(imagePart);
        return new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties
                {
                    Id = id,
                    Name = name,
                    Description = name,
                },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(
                new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                new A.Stretch(new A.FillRectangle())),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    private static void WriteSlideLayout(SlideLayoutPart slideLayoutPart, SlideMasterPart slideMasterPart)
    {
        slideLayoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(CreateShapeTree())
            {
                Name = "Blank",
            },
            new P.ColorMapOverride(new A.MasterColorMapping()))
        {
            Type = P.SlideLayoutValues.Blank,
            Preserve = true,
        };
        slideLayoutPart.AddPart(slideMasterPart);
        slideLayoutPart.SlideLayout.Save();
    }

    private static void WriteSlideMaster(SlideMasterPart slideMasterPart, SlideLayoutPart slideLayoutPart)
    {
        slideMasterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(CreateShapeTree()),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId
                {
                    Id = 2_147_483_649U,
                    RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart),
                }),
            new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));
        slideMasterPart.SlideMaster.Save();
    }

    private static void WriteTheme(ThemePart themePart, DeckDocDocument document)
    {
        var dark1 = ResolveThemeColor(document, "ink", "000000");
        var light1 = ResolveThemeColor(document, "surface", "FFFFFF");
        var accent1 = ResolveThemeColor(document, "primary", "0F766E");
        var accent2 = ResolveThemeColor(document, "accent", "2563EB");
        var accent3 = ResolveThemeColor(document, "muted", "475569");
        var displayFont = document.ThemeTokens.GetValueOrDefault("display") ?? "Aptos Display";
        var bodyFont = document.ThemeTokens.GetValueOrDefault("body") ?? "Aptos";

        var colorScheme = new A.ColorScheme { Name = "AIToolkit" };
        colorScheme.Append(
            new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = dark1 }),
            new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = light1 }),
            new A.Dark2Color(new A.RgbColorModelHex { Val = dark1 }),
            new A.Light2Color(new A.RgbColorModelHex { Val = light1 }),
            new A.Accent1Color(new A.RgbColorModelHex { Val = accent1 }),
            new A.Accent2Color(new A.RgbColorModelHex { Val = accent2 }),
            new A.Accent3Color(new A.RgbColorModelHex { Val = accent3 }),
            new A.Accent4Color(new A.RgbColorModelHex { Val = "F59E0B" }),
            new A.Accent5Color(new A.RgbColorModelHex { Val = "7C3AED" }),
            new A.Accent6Color(new A.RgbColorModelHex { Val = "475569" }),
            new A.Hyperlink(new A.RgbColorModelHex { Val = accent2 }),
            new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = accent1 }));

        var fontScheme = new A.FontScheme { Name = "AIToolkit" };
        fontScheme.Append(
            new A.MajorFont(
                new A.LatinFont { Typeface = displayFont },
                new A.EastAsianFont { Typeface = string.Empty },
                new A.ComplexScriptFont { Typeface = string.Empty }),
            new A.MinorFont(
                new A.LatinFont { Typeface = bodyFont },
                new A.EastAsianFont { Typeface = string.Empty },
                new A.ComplexScriptFont { Typeface = string.Empty }));

        var formatScheme = new A.FormatScheme { Name = "AIToolkit" };
        formatScheme.Append(
            new A.FillStyleList(
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Accent1 }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Accent2 })),
            new A.LineStyleList(
                CreateOutline(9_525),
                CreateOutline(25_400),
                CreateOutline(38_100)),
            new A.EffectStyleList(
                new A.EffectStyle(new A.EffectList()),
                new A.EffectStyle(new A.EffectList()),
                new A.EffectStyle(new A.EffectList())),
            new A.BackgroundFillStyleList(
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Light1 }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Light2 })));

        themePart.Theme = new A.Theme(
            new A.ThemeElements(colorScheme, fontScheme, formatScheme),
            new A.ObjectDefaults(),
            new A.ExtraColorSchemeList())
        {
            Name = "AIToolkit Theme",
        };
        themePart.Theme.Save();
    }

    private static string ResolveThemeColor(DeckDocDocument document, string token, string fallback)
    {
        var value = document.ThemeTokens.GetValueOrDefault(token) ?? fallback;
        return value.Trim().TrimStart('#');
    }

    private static A.Outline CreateOutline(int width)
    {
        var outline = new A.Outline
        {
            Width = width,
            CapType = A.LineCapValues.Flat,
            CompoundLineType = A.CompoundLineValues.Single,
            Alignment = A.PenAlignmentValues.Center,
        };
        outline.Append(
            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
            new A.PresetDash { Val = A.PresetLineDashValues.Solid },
            new A.Round());
        return outline;
    }
}

/// <summary>
/// Holds resolved image bytes ready for slide generation.
/// </summary>
internal sealed record ResolvedDeckImage(byte[] Content, string ContentType);
