using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Escritorio.Shared.Formats;

/// <summary>
/// Leitura e escrita de pacotes OpenDocument (ODT/ODS/ODP), que sao arquivos ZIP
/// contendo "mimetype", "content.xml" e o manifesto "META-INF/manifest.xml".
/// </summary>
public static class OdfPackage
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public static void Write(string path, string mimeType, XDocument content)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // O "mimetype" deve ser a primeira entrada e armazenado sem compressao.
        var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(mimeEntry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(mimeType);
        }

        WriteEntry(archive, "content.xml", content.ToString());
        WriteEntry(archive, "META-INF/manifest.xml", BuildManifest(mimeType).ToString());
    }

    public static XDocument ReadContent(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("content.xml")
                    ?? throw new InvalidDataException("Pacote ODF sem content.xml.");
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static XDocument BuildManifest(string mimeType)
    {
        return new XDocument(
            new XElement(Manifest + "manifest",
                new XAttribute(XNamespace.Xmlns + "manifest", Manifest.NamespaceName),
                new XAttribute(Manifest + "version", "1.2"),
                new XElement(Manifest + "file-entry",
                    new XAttribute(Manifest + "full-path", "/"),
                    new XAttribute(Manifest + "media-type", mimeType)),
                new XElement(Manifest + "file-entry",
                    new XAttribute(Manifest + "full-path", "content.xml"),
                    new XAttribute(Manifest + "media-type", "text/xml"))));
    }

    public static XElement DocumentContent(params object[] bodyChildren)
    {
        return new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "draw", Draw.NamespaceName),
            new XAttribute(Office + "version", "1.2"),
            new XElement(Office + "body", bodyChildren));
    }
}
