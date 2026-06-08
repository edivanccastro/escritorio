using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Gera icones profissionais (.ico) para cada aplicativo da suite Escritorio.
// Cada icone e um "tile" com gradiente na cor do app e um simbolo branco
// representando a funcao (documento, planilha, apresentacao).
// Uso: dotnet run --project tools/IconGen -- <pastaRaizDoRepositorio>

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

var apps = new IconSpec[]
{
    new("document",
        ColorTranslator.FromHtml("#4A82D6"), ColorTranslator.FromHtml("#1E3F73"),
        Path.Combine(root, "src", "Ordi", "Assets", "letrucio.ico")),
    new("grid",
        ColorTranslator.FromHtml("#33B56A"), ColorTranslator.FromHtml("#13632F"),
        Path.Combine(root, "src", "Planilha", "Assets", "planilson.ico")),
    new("presentation",
        ColorTranslator.FromHtml("#EE7B43"), ColorTranslator.FromHtml("#A6340F"),
        Path.Combine(root, "src", "PowerRanger", "Assets", "slidney.ico")),
};

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var previewDir = Path.Combine(root, "branding", "preview");
Directory.CreateDirectory(previewDir);

foreach (var app in apps)
{
    Directory.CreateDirectory(Path.GetDirectoryName(app.OutputPath)!);

    var frames = new List<byte[]>();
    foreach (var size in sizes)
    {
        frames.Add(Render(size, app));
    }

    WriteIco(app.OutputPath, sizes, frames);

    var previewPath = Path.Combine(previewDir,
        Path.GetFileNameWithoutExtension(app.OutputPath) + ".png");
    File.WriteAllBytes(previewPath, frames[^1]);

    Console.WriteLine($"Icone gerado: {app.OutputPath}");
}

static byte[] Render(int size, IconSpec spec)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
        var radius = size * 0.22f;

        using (var path = RoundedRect(rect, radius))
        {
            using var bg = new LinearGradientBrush(
                new PointF(0, 0), new PointF(size, size), spec.Light, spec.Dark);
            g.FillPath(bg, path);

            // Brilho suave no topo para dar profundidade.
            using var gloss = new LinearGradientBrush(
                new PointF(0, 0), new PointF(0, size * 0.6f),
                Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
            g.SetClip(path);
            g.FillRectangle(gloss, 0, 0, size, size * 0.6f);
            g.ResetClip();
        }

        switch (spec.Kind)
        {
            case "document":
                DrawDocument(g, size, spec.Dark);
                break;
            case "grid":
                DrawGrid(g, size, spec.Dark);
                break;
            case "presentation":
                DrawPresentation(g, size, spec.Dark);
                break;
        }
    }

    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

// Documento branco com canto dobrado e linhas de texto (editor de textos).
static void DrawDocument(Graphics g, int size, Color accent)
{
    var pageW = size * 0.42f;
    var pageH = size * 0.56f;
    var pageX = (size - pageW) / 2f;
    var pageY = (size - pageH) / 2f;
    var page = new RectangleF(pageX, pageY, pageW, pageH);
    var fold = size * 0.14f;

    DrawShadow(g, page, size * 0.05f);

    using (var pagePath = PageWithFold(page, fold))
    {
        using var white = new SolidBrush(Color.White);
        g.FillPath(white, pagePath);
    }

    // Triangulo do canto dobrado.
    using (var foldBrush = new SolidBrush(Color.FromArgb(225, 225, 230)))
    {
        var pts = new[]
        {
            new PointF(page.Right - fold, page.Top),
            new PointF(page.Right, page.Top + fold),
            new PointF(page.Right - fold, page.Top + fold),
        };
        g.FillPolygon(foldBrush, pts);
    }

    var lineX = page.Left + pageW * 0.16f;
    var lineW = pageW * 0.60f;
    var lineH = Math.Max(1f, size * 0.022f);
    var startY = page.Top + pageH * 0.30f;
    var gap = pageH * 0.13f;

    // Linha de titulo na cor do app.
    using (var titleBrush = new SolidBrush(accent))
    {
        g.FillRectangle(titleBrush, lineX, startY, lineW * 0.7f, lineH * 1.4f);
    }

    using var lineBrush = new SolidBrush(Color.FromArgb(178, 184, 196));
    for (var i = 1; i <= 3; i++)
    {
        var w = i == 3 ? lineW * 0.6f : lineW;
        g.FillRectangle(lineBrush, lineX, startY + gap * i, w, lineH);
    }
}

// Planilha branca com cabecalhos coloridos e grade (folha de calculo).
static void DrawGrid(Graphics g, int size, Color accent)
{
    var w = size * 0.54f;
    var h = size * 0.46f;
    var x = (size - w) / 2f;
    var y = (size - h) / 2f;
    var sheet = new RectangleF(x, y, w, h);

    DrawShadow(g, sheet, size * 0.05f);

    using (var white = new SolidBrush(Color.White))
    using (var path = RoundedRect(sheet, size * 0.04f))
    {
        g.FillPath(white, path);
    }

    var cols = 4;
    var rows = 3;
    var cellW = w / cols;
    var cellH = h / rows;

    // Cabecalho (primeira linha e primeira coluna) na cor do app.
    using (var header = new SolidBrush(accent))
    {
        g.FillRectangle(header, x, y, w, cellH);
        g.FillRectangle(header, x, y, cellW, h);
    }

    using var pen = new Pen(Color.FromArgb(150, 200, 170), Math.Max(1f, size * 0.012f));
    for (var c = 1; c < cols; c++)
    {
        g.DrawLine(pen, x + cellW * c, y, x + cellW * c, y + h);
    }
    for (var r = 1; r < rows; r++)
    {
        g.DrawLine(pen, x, y + cellH * r, x + w, y + cellH * r);
    }

    using var border = new Pen(Color.FromArgb(120, 160, 135), Math.Max(1f, size * 0.012f));
    using var borderPath = RoundedRect(sheet, size * 0.04f);
    g.DrawPath(border, borderPath);
}

// Slide branco com barra de titulo e grafico de barras (apresentacao).
static void DrawPresentation(Graphics g, int size, Color accent)
{
    var w = size * 0.56f;
    var h = size * 0.40f;
    var x = (size - w) / 2f;
    var y = (size - h) / 2f - size * 0.02f;
    var slide = new RectangleF(x, y, w, h);

    DrawShadow(g, slide, size * 0.05f);

    using (var white = new SolidBrush(Color.White))
    using (var path = RoundedRect(slide, size * 0.04f))
    {
        g.FillPath(white, path);
    }

    // Barra de titulo do slide.
    using (var titleBrush = new SolidBrush(accent))
    {
        g.FillRectangle(titleBrush, x + w * 0.12f, y + h * 0.16f, w * 0.5f, Math.Max(1.5f, h * 0.1f));
    }

    // Grafico de barras.
    using var barBrush = new SolidBrush(accent);
    var baseY = y + h * 0.78f;
    var barW = w * 0.12f;
    var gap = w * 0.07f;
    var startX = x + w * 0.16f;
    float[] heights = { 0.22f, 0.40f, 0.30f, 0.50f };
    for (var i = 0; i < heights.Length; i++)
    {
        var bh = h * heights[i];
        var bx = startX + i * (barW + gap);
        g.FillRectangle(barBrush, bx, baseY - bh, barW, bh);
    }

    // Pe do slide (base de projecao).
    using var standPen = new Pen(Color.FromArgb(235, 235, 240), Math.Max(1.5f, size * 0.02f));
    g.DrawLine(standPen, x + w * 0.5f, slide.Bottom, x + w * 0.5f, slide.Bottom + size * 0.05f);
}

static void DrawShadow(Graphics g, RectangleF rect, float offset)
{
    var shadow = new RectangleF(rect.X, rect.Y + offset, rect.Width, rect.Height);
    using var path = RoundedRect(shadow, rect.Width * 0.08f);
    using var brush = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
    g.FillPath(brush, path);
}

static GraphicsPath PageWithFold(RectangleF page, float fold)
{
    var path = new GraphicsPath();
    path.AddLines(new[]
    {
        new PointF(page.Left, page.Top),
        new PointF(page.Right - fold, page.Top),
        new PointF(page.Right, page.Top + fold),
        new PointF(page.Right, page.Bottom),
        new PointF(page.Left, page.Bottom),
    });
    path.CloseFigure();
    return path;
}

static GraphicsPath RoundedRect(RectangleF bounds, float radius)
{
    var path = new GraphicsPath();
    if (radius <= 0)
    {
        path.AddRectangle(bounds);
        return path;
    }

    var d = radius * 2;
    path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
    path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
    path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
    path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

static void WriteIco(string path, int[] sizes, List<byte[]> pngFrames)
{
    using var fs = new FileStream(path, FileMode.Create);
    using var writer = new BinaryWriter(fs);

    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)pngFrames.Count);

    var offset = 6 + (16 * pngFrames.Count);
    for (var i = 0; i < pngFrames.Count; i++)
    {
        var dimension = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
        writer.Write(dimension);
        writer.Write(dimension);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(pngFrames[i].Length);
        writer.Write(offset);
        offset += pngFrames[i].Length;
    }

    foreach (var frame in pngFrames)
    {
        writer.Write(frame);
    }
}

internal readonly record struct IconSpec(string Kind, Color Light, Color Dark, string OutputPath);
