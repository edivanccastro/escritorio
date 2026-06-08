using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Escritorio.Shared.Formats;

/// <summary>
/// Abertura e gravacao de apresentacoes em multiplos formatos.
/// Apresentacao OOXML (.pptx), ODF Impress (.odp) e o formato nativo (.json).
/// </summary>
public static class PresentationFormat
{
    public const string OpenFilter =
        "Todas as apresentacoes|*.pptx;*.odp;*.json|" +
        "Apresentação OOXML (*.pptx)|*.pptx|" +
        "Apresentação ODF (*.odp)|*.odp|" +
        "Slidney (*.json)|*.json";

    public const string SaveFilter =
        "Apresentação OOXML (*.pptx)|*.pptx|" +
        "Apresentação ODF (*.odp)|*.odp|" +
        "Slidney (*.json)|*.json";

    public static void Save(IList<SlideData> slides, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pptx":
                SavePptx(slides, path);
                break;
            case ".odp":
                SaveOdp(slides, path);
                break;
            default:
                SaveJson(slides, path);
                break;
        }
    }

    public static List<SlideData> Load(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pptx" => LoadPptx(path),
            ".odp" => LoadOdp(path),
            _ => LoadJson(path),
        };
    }

    // ---------- JSON (nativo) ----------

    private static void SaveJson(IList<SlideData> slides, string path)
    {
        var json = JsonSerializer.Serialize(slides, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static List<SlideData> LoadJson(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SlideData>>(json) ?? new List<SlideData>();
    }

    // ---------- ODP ----------

    private static void SaveOdp(IList<SlideData> slides, string path)
    {
        var pages = slides.Select((slide, i) =>
            new XElement(OdfPackage.Draw + "page",
                new XAttribute(OdfPackage.Draw + "name", $"Slide {i + 1}"),
                TextFrame(slide.Title, "title"),
                TextFrame(slide.Content, "content")));

        var content = OdfPackage.DocumentContent(
            new XElement(OdfPackage.Office + "presentation", pages));

        OdfPackage.Write(path, "application/vnd.oasis.opendocument.presentation",
            new XDocument(content));
    }

    private static XElement TextFrame(string text, string kind)
    {
        var paragraphs = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => new XElement(OdfPackage.Text + "p", line));

        return new XElement(OdfPackage.Draw + "frame",
            new XAttribute(OdfPackage.Draw + "name", kind),
            new XElement(OdfPackage.Draw + "text-box", paragraphs));
    }

    private static List<SlideData> LoadOdp(string path)
    {
        var slides = new List<SlideData>();
        var xml = OdfPackage.ReadContent(path);

        foreach (var page in xml.Descendants(OdfPackage.Draw + "page"))
        {
            var frames = page.Elements(OdfPackage.Draw + "frame").ToList();
            var slide = new SlideData();

            var titleFrame = frames.FirstOrDefault(f => (string?)f.Attribute(OdfPackage.Draw + "name") == "title");
            var contentFrame = frames.FirstOrDefault(f => (string?)f.Attribute(OdfPackage.Draw + "name") == "content");

            titleFrame ??= frames.ElementAtOrDefault(0);
            contentFrame ??= frames.ElementAtOrDefault(1);

            slide.Title = FrameText(titleFrame);
            slide.Content = FrameText(contentFrame);
            slides.Add(slide);
        }

        return slides;
    }

    private static string FrameText(XElement? frame)
    {
        if (frame is null)
        {
            return string.Empty;
        }
        return string.Join("\n", frame.Descendants(OdfPackage.Text + "p").Select(p => p.Value));
    }

    // ---------- PPTX ----------

    private static void SavePptx(IList<SlideData> slides, string path)
    {
        using var presentationDocument = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = presentationDocument.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation();

        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rIdSlideMaster");
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rIdSlideLayout");
        var themePart = slideMasterPart.AddNewPart<ThemePart>("rIdTheme");

        themePart.Theme = BuildTheme();
        slideLayoutPart.SlideLayout = BuildSlideLayout();
        slideMasterPart.SlideMaster = BuildSlideMaster(slideMasterPart.GetIdOfPart(slideLayoutPart));

        var slideIdList = new P.SlideIdList();
        uint slideId = 256;
        var relId = 1;

        var safeSlides = slides.Count > 0 ? slides : new List<SlideData> { new() };
        foreach (var slide in safeSlides)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>($"rIdSlide{relId}");
            slidePart.Slide = BuildSlide(slide.Title, slide.Content);
            slidePart.AddPart(slideLayoutPart);

            slideIdList.Append(new P.SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart),
            });
            relId++;
        }

        presentationPart.Presentation.Append(slideIdList);
        presentationPart.Presentation.Append(new P.SlideMasterIdList(
            new P.SlideMasterId
            {
                Id = 2147483648,
                RelationshipId = presentationPart.GetIdOfPart(slideMasterPart),
            }));
        presentationPart.Presentation.Append(new P.SlideSize { Cx = 9144000, Cy = 6858000 });
        presentationPart.Presentation.Append(new P.NotesSize { Cx = 6858000, Cy = 9144000 });
        presentationPart.Presentation.Save();
    }

    private static List<SlideData> LoadPptx(string path)
    {
        var slides = new List<SlideData>();
        using var presentationDocument = PresentationDocument.Open(path, false);
        var presentationPart = presentationDocument.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
        {
            return slides;
        }

        foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<P.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relId)
            {
                continue;
            }

            var slidePart = (SlidePart)presentationPart.GetPartById(relId);
            var slideElement = slidePart.Slide;
            if (slideElement is null)
            {
                continue;
            }
            var slide = new SlideData();
            var contentLines = new List<string>();

            foreach (var shape in slideElement.Descendants<P.Shape>())
            {
                var text = string.Join("\n", shape.Descendants<D.Paragraph>()
                    .Select(p => string.Concat(p.Descendants<D.Text>().Select(t => t.Text))));

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var placeholder = shape.NonVisualShapeProperties?
                    .ApplicationNonVisualDrawingProperties?
                    .GetFirstChild<P.PlaceholderShape>();

                var isTitle = placeholder?.Type is not null &&
                    (placeholder.Type == P.PlaceholderValues.Title ||
                     placeholder.Type == P.PlaceholderValues.CenteredTitle);

                if (isTitle && string.IsNullOrEmpty(slide.Title))
                {
                    slide.Title = text;
                }
                else
                {
                    contentLines.Add(text);
                }
            }

            slide.Content = string.Join("\n", contentLines);
            slides.Add(slide);
        }

        return slides;
    }

    private static P.Slide BuildSlide(string title, string content)
    {
        var shapeTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new D.TransformGroup()));

        shapeTree.Append(BuildTextShape(2, "Titulo", title, P.PlaceholderValues.Title,
            838200, 365125, 7772400, 1470025, 3200, true));
        shapeTree.Append(BuildTextShape(3, "Conteudo", content, P.PlaceholderValues.Body,
            838200, 1825625, 7772400, 4351338, 1800, false));

        return new P.Slide(new P.CommonSlideData(shapeTree),
            new P.ColorMapOverride(new D.MasterColorMapping()));
    }

    private static P.Shape BuildTextShape(uint id, string name, string text,
        P.PlaceholderValues placeholder, long x, long y, long cx, long cy, int fontSize, bool bold)
    {
        var bodyParagraphs = new List<OpenXmlElement> { new D.BodyProperties(), new D.ListStyle() };

        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            bodyParagraphs.Add(new D.Paragraph(
                new D.Run(
                    new D.RunProperties { Language = "pt-BR", FontSize = fontSize, Bold = bold },
                    new D.Text(line))));
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = placeholder })),
            new P.ShapeProperties(
                new D.Transform2D(
                    new D.Offset { X = x, Y = y },
                    new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            new P.TextBody(bodyParagraphs));
    }

    private static P.SlideMaster BuildSlideMaster(string layoutRelId)
    {
        return new P.SlideMaster(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1,
                Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2,
                Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId { Id = 2147483649, RelationshipId = layoutRelId }));
    }

    private static P.SlideLayout BuildSlideLayout()
    {
        return new P.SlideLayout(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMapOverride(new D.MasterColorMapping()))
        {
            Type = P.SlideLayoutValues.Blank,
        };
    }

    private static D.Theme BuildTheme()
    {
        var colorScheme = new D.ColorScheme(
            new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
            new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
            new D.Dark2Color(new D.RgbColorModelHex { Val = "44546A" }),
            new D.Light2Color(new D.RgbColorModelHex { Val = "E7E6E6" }),
            new D.Accent1Color(new D.RgbColorModelHex { Val = "C43E1C" }),
            new D.Accent2Color(new D.RgbColorModelHex { Val = "ED7D31" }),
            new D.Accent3Color(new D.RgbColorModelHex { Val = "A5A5A5" }),
            new D.Accent4Color(new D.RgbColorModelHex { Val = "FFC000" }),
            new D.Accent5Color(new D.RgbColorModelHex { Val = "5B9BD5" }),
            new D.Accent6Color(new D.RgbColorModelHex { Val = "70AD47" }),
            new D.Hyperlink(new D.RgbColorModelHex { Val = "0563C1" }),
            new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = "954F72" }))
        { Name = "Escritorio" };

        var fontScheme = new D.FontScheme(
            new D.MajorFont(
                new D.LatinFont { Typeface = "Segoe UI" },
                new D.EastAsianFont { Typeface = string.Empty },
                new D.ComplexScriptFont { Typeface = string.Empty }),
            new D.MinorFont(
                new D.LatinFont { Typeface = "Segoe UI" },
                new D.EastAsianFont { Typeface = string.Empty },
                new D.ComplexScriptFont { Typeface = string.Empty }))
        { Name = "Escritorio" };

        D.Outline SolidOutline(int width) => new(
            new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
            new D.PresetDash { Val = D.PresetLineDashValues.Solid })
        { Width = width, CapType = D.LineCapValues.Flat, CompoundLineType = D.CompoundLineValues.Single };

        var formatScheme = new D.FormatScheme(
            new D.FillStyleList(
                new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
                new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
                new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor })),
            new D.LineStyleList(SolidOutline(6350), SolidOutline(12700), SolidOutline(19050)),
            new D.EffectStyleList(
                new D.EffectStyle(new D.EffectList()),
                new D.EffectStyle(new D.EffectList()),
                new D.EffectStyle(new D.EffectList())),
            new D.BackgroundFillStyleList(
            new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
            new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor }),
            new D.SolidFill(new D.SchemeColor { Val = D.SchemeColorValues.PhColor })))
        { Name = "Escritorio" };

        return new D.Theme(
            new D.ThemeElements(colorScheme, fontScheme, formatScheme),
            new D.ObjectDefaults())
        { Name = "Escritorio Theme" };
    }
}
