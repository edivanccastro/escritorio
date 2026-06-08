using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Escritorio.Shared.Formats;

/// <summary>
/// Abertura e gravacao de documentos de texto em multiplos formatos.
/// Documento OOXML (.docx), ODF Writer (.odt), Rich Text (.rtf) e Texto (.txt).
/// </summary>
public static class TextDocumentFormat
{
    public const string OpenFilter =
        "Todos os documentos|*.docx;*.odt;*.rtf;*.txt|" +
        "Documento OOXML (*.docx)|*.docx|" +
        "Documento ODF (*.odt)|*.odt|" +
        "Rich Text (*.rtf)|*.rtf|" +
        "Texto (*.txt)|*.txt";

    public const string SaveFilter =
        "Documento OOXML (*.docx)|*.docx|" +
        "Documento ODF (*.odt)|*.odt|" +
        "Rich Text (*.rtf)|*.rtf|" +
        "Texto (*.txt)|*.txt";

    public static void Save(FlowDocument document, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".docx":
                SaveDocx(document, path);
                break;
            case ".odt":
                SaveOdt(document, path);
                break;
            case ".txt":
                SaveRange(document, path, DataFormats.Text);
                break;
            default:
                SaveRange(document, path, DataFormats.Rtf);
                break;
        }
    }

    public static FlowDocument Load(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" => LoadDocx(path),
            ".odt" => LoadOdt(path),
            ".txt" => LoadRange(path, DataFormats.Text),
            _ => LoadRange(path, DataFormats.Rtf),
        };
    }

    private static void SaveRange(FlowDocument document, string path, string format)
    {
        using var stream = File.Open(path, FileMode.Create);
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Save(stream, format);
    }

    private static FlowDocument LoadRange(string path, string format)
    {
        var document = new FlowDocument();
        using var stream = File.OpenRead(path);
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Load(stream, format);
        return document;
    }

    // ---------- DOCX ----------

    private static void SaveDocx(FlowDocument document, string path)
    {
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = word.AddMainDocumentPart();
        main.Document = new W.Document();
        var body = main.Document.AppendChild(new W.Body());

        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            var p = new W.Paragraph();

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is not Run run)
                {
                    continue;
                }

                var r = new W.Run();
                var properties = new W.RunProperties();

                if (run.FontWeight == FontWeights.Bold)
                {
                    properties.Append(new W.Bold());
                }
                if (run.FontStyle == FontStyles.Italic)
                {
                    properties.Append(new W.Italic());
                }
                if (run.TextDecorations is { Count: > 0 })
                {
                    properties.Append(new W.Underline { Val = W.UnderlineValues.Single });
                }
                if (properties.HasChildren)
                {
                    r.Append(properties);
                }

                r.Append(new W.Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
                p.Append(r);
            }

            body.Append(p);
        }

        main.Document.Save();
    }

    private static FlowDocument LoadDocx(string path)
    {
        var document = new FlowDocument();
        using var word = WordprocessingDocument.Open(path, false);
        var body = word.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return document;
        }

        foreach (var p in body.Elements<W.Paragraph>())
        {
            var paragraph = new Paragraph();
            foreach (var r in p.Elements<W.Run>())
            {
                var text = string.Concat(r.Elements<W.Text>().Select(t => t.Text));
                if (text.Length == 0)
                {
                    continue;
                }

                var run = new Run(text);
                var rPr = r.RunProperties;
                if (rPr is not null)
                {
                    if (rPr.Bold is not null)
                    {
                        run.FontWeight = FontWeights.Bold;
                    }
                    if (rPr.Italic is not null)
                    {
                        run.FontStyle = FontStyles.Italic;
                    }
                    if (rPr.Underline is not null)
                    {
                        run.TextDecorations = TextDecorations.Underline;
                    }
                }

                paragraph.Inlines.Add(run);
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    // ---------- ODT ----------

    private static void SaveOdt(FlowDocument document, string path)
    {
        var paragraphs = document.Blocks.OfType<Paragraph>()
            .Select(p => new XElement(OdfPackage.Text + "p", GetParagraphText(p)));

        var content = OdfPackage.DocumentContent(
            new XElement(OdfPackage.Office + "text", paragraphs));

        OdfPackage.Write(path, "application/vnd.oasis.opendocument.text",
            new XDocument(content));
    }

    private static FlowDocument LoadOdt(string path)
    {
        var document = new FlowDocument();
        var xml = OdfPackage.ReadContent(path);

        foreach (var p in xml.Descendants(OdfPackage.Text + "p"))
        {
            document.Blocks.Add(new Paragraph(new Run(p.Value)));
        }

        if (!document.Blocks.Any())
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    private static string GetParagraphText(Paragraph paragraph)
    {
        return string.Concat(paragraph.Inlines.OfType<Run>().Select(r => r.Text));
    }
}
