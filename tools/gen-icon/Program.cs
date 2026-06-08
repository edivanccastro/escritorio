using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string outPath = args.Length > 0 ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "espiadisk.ico");

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

var sizes = new[] { 256, 64, 48, 32, 16 };
var pngList = new List<byte[]>();

foreach (var sz in sizes)
{
    using var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode      = SmoothingMode.AntiAlias;
    g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
    g.TextRenderingHint  = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    g.Clear(Color.Transparent);

    float S = sz;        // scale factor shorthand

    // ── Fundo: gradiente roxo escuro com cantos arredondados ──────────────
    int r = Math.Max(3, (int)(S * 0.16f));
    var bgRect = new RectangleF(0, 0, S - 1, S - 1);
    using var bgPath = RoundedRect(bgRect, r);
    using var bgGrad = new LinearGradientBrush(
        new PointF(0, 0), new PointF(S, S),
        Color.FromArgb(255, 0x2D, 0x00, 0x6F),   // índigo escuro
        Color.FromArgb(255, 0x8E, 0x24, 0xAA));   // roxo médio
    g.FillPath(bgGrad, bgPath);

    // Brilho sutil no topo (reflexo)
    using var glowBrush = new LinearGradientBrush(
        new PointF(0, 0), new PointF(0, S * 0.45f),
        Color.FromArgb(60, 255, 255, 255),
        Color.FromArgb(0, 255, 255, 255));
    g.FillPath(glowBrush, bgPath);

    if (sz >= 32)
    {
        // ── Monitor: corpo ────────────────────────────────────────────────
        float mw  = S * 0.72f;   // largura do monitor
        float mh  = S * 0.52f;   // altura da tela (moldura)
        float mx  = (S - mw) / 2f;
        float my  = S * 0.09f;
        float mr  = Math.Max(2, S * 0.05f);

        // Sombra do monitor
        using var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));
        var shadowRect = new RectangleF(mx + S*0.02f, my + S*0.02f, mw, mh);
        using var shadowPath = RoundedRect(shadowRect, (int)mr);
        g.FillPath(shadowBrush, shadowPath);

        // Moldura branca do monitor
        var monRect = new RectangleF(mx, my, mw, mh);
        using var monPath = RoundedRect(monRect, (int)mr);
        using var monBrush = new SolidBrush(Color.FromArgb(245, 250, 255));
        g.FillPath(monBrush, monPath);

        // Tela (interior)
        float bz  = S * 0.05f;   // espessura do bisel
        var screenRect = new RectangleF(mx + bz, my + bz, mw - bz*2, mh - bz*2);
        using var screenPath = RoundedRect(screenRect, Math.Max(1, (int)mr - 2));
        using var screenGrad = new LinearGradientBrush(
            new PointF(mx, my), new PointF(mx + mw, my + mh),
            Color.FromArgb(255, 0x1A, 0x00, 0x4E),
            Color.FromArgb(255, 0x6A, 0x1B, 0x9A));
        g.FillPath(screenGrad, screenPath);

        // ── Olho estilizado na tela ───────────────────────────────────────
        float cx  = S / 2f;
        float cy  = my + mh * 0.46f;
        float ew  = screenRect.Width  * 0.62f;
        float eh  = screenRect.Height * 0.40f;

        // Contorno do olho (branco)
        using var eyeOutPen = new Pen(Color.White, Math.Max(1.5f, S * 0.025f));
        eyeOutPen.LineJoin = LineJoin.Round;
        DrawEye(g, cx, cy, ew, eh, eyeOutPen, null);

        // Íris (gradiente roxo-violeta)
        float ir = eh * 0.42f;
        using var irisGrad = new PathGradientBrush(CirclePath(cx, cy, ir));
        irisGrad.CenterColor   = Color.FromArgb(255, 0xE0, 0xB0, 0xFF);
        irisGrad.SurroundColors = new[] { Color.FromArgb(255, 0x9C, 0x27, 0xB0) };
        g.FillEllipse(irisGrad, cx - ir, cy - ir, ir*2, ir*2);

        // Pupila preta
        float pr = ir * 0.46f;
        using var pupilBrush = new SolidBrush(Color.FromArgb(255, 0x12, 0x00, 0x38));
        g.FillEllipse(pupilBrush, cx - pr, cy - pr, pr*2, pr*2);

        // Brilho da pupila
        float gr2 = pr * 0.38f;
        using var gleamBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.FillEllipse(gleamBrush, cx - pr*0.55f, cy - pr*0.55f, gr2*2, gr2*2);

        // Cílios do olho (preenchimento branco)
        DrawEye(g, cx, cy, ew, eh, null, new SolidBrush(Color.FromArgb(0, 0, 0, 0)));

        // ── Ondas de sinal (wifi-like) abaixo do olho ────────────────────
        if (sz >= 48)
        {
            float wcy = cy + eh * 0.62f + S * 0.025f;
            float wgap = S * 0.032f;
            int   arcs = sz >= 64 ? 3 : 2;
            for (int i = arcs; i >= 1; i--)
            {
                float wr = S * 0.05f * i;
                float wt = Math.Max(1f, S * 0.018f);
                using var wPen = new Pen(Color.FromArgb(220 - i*30, 255, 255, 255), wt);
                wPen.LineJoin = LineJoin.Round;
                float startAngle = 200f;
                float sweepAngle = 140f;
                g.DrawArc(wPen,
                    cx - wr - wgap, wcy - wr - wgap,
                    (wr + wgap)*2, (wr + wgap)*2,
                    startAngle, sweepAngle);
            }
            // Ponto central das ondas
            float dotR = S * 0.022f;
            using var dotBrush = new SolidBrush(Color.White);
            g.FillEllipse(dotBrush, cx - dotR, wcy + wgap*0.3f - dotR, dotR*2, dotR*2);
        }

        // ── Base/pedestal do monitor ──────────────────────────────────────
        float stx  = S * 0.42f;
        float sty  = my + mh;
        float stw  = S * 0.16f;
        float sth  = S * 0.09f;
        using var standBrush = new SolidBrush(Color.FromArgb(220, 235, 245));
        g.FillRectangle(standBrush, stx, sty, stw, sth);

        // Base (plataforma)
        float bx   = S * 0.28f;
        float by   = sty + sth;
        float bwid = S * 0.44f;
        float bht  = S * 0.05f;
        using var basePath = RoundedRect(new RectangleF(bx, by, bwid, bht), (int)(bht*0.4f));
        g.FillPath(standBrush, basePath);
    }
    else
    {
        // Ícone 16px simplificado: apenas olho branco no centro
        float cx = S / 2f;
        float cy = S / 2f;
        float ew = S * 0.68f;
        float eh = S * 0.42f;
        using var eyePen  = new Pen(Color.White, Math.Max(1.2f, S * 0.08f));
        DrawEye(g, cx, cy, ew, eh, eyePen, null);
        float ir = eh * 0.40f;
        using var irisBr  = new SolidBrush(Color.FromArgb(255, 0xE0, 0xB0, 0xFF));
        g.FillEllipse(irisBr, cx - ir, cy - ir, ir*2, ir*2);
        float pr = ir * 0.50f;
        using var pupilBr = new SolidBrush(Color.FromArgb(255, 0x12, 0x00, 0x38));
        g.FillEllipse(pupilBr, cx - pr, cy - pr, pr*2, pr*2);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    pngList.Add(ms.ToArray());
}

// ── Escrever arquivo ICO ──────────────────────────────────────────────────
using var ico  = new FileStream(outPath, FileMode.Create);
using var icoW = new BinaryWriter(ico);

icoW.Write((short)0);
icoW.Write((short)1);
icoW.Write((short)sizes.Length);

int dataOffset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    icoW.Write((byte)(s >= 256 ? 0 : s));
    icoW.Write((byte)(s >= 256 ? 0 : s));
    icoW.Write((byte)0);
    icoW.Write((byte)0);
    icoW.Write((short)1);
    icoW.Write((short)32);
    icoW.Write(pngList[i].Length);
    icoW.Write(dataOffset);
    dataOffset += pngList[i].Length;
}
foreach (var data in pngList) icoW.Write(data);

Console.WriteLine($"[OK] Icone gerado: {outPath}  ({new FileInfo(outPath).Length:N0} bytes)");

// ── Helpers ───────────────────────────────────────────────────────────────

static GraphicsPath RoundedRect(RectangleF r, int radius)
{
    radius = Math.Max(1, radius);
    var p = new GraphicsPath();
    int d = radius * 2;
    p.AddArc(r.X,           r.Y,            d, d, 180, 90);
    p.AddArc(r.Right - d,   r.Y,            d, d, 270, 90);
    p.AddArc(r.Right - d,   r.Bottom - d,   d, d,   0, 90);
    p.AddArc(r.X,           r.Bottom - d,   d, d,  90, 90);
    p.CloseFigure();
    return p;
}

static GraphicsPath CirclePath(float cx, float cy, float r)
{
    var p = new GraphicsPath();
    p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
    return p;
}

static void DrawEye(Graphics g, float cx, float cy, float ew, float eh,
                    Pen? pen, Brush? fill)
{
    // Formato de olho amêndoa via Bézier
    var pts = new[]
    {
        new PointF(cx - ew/2, cy),                      // ponta esquerda
        new PointF(cx - ew/4, cy - eh/2),               // cima esquerdo
        new PointF(cx + ew/4, cy - eh/2),               // cima direito
        new PointF(cx + ew/2, cy),                      // ponta direita
        new PointF(cx + ew/4, cy + eh/2),               // baixo direito
        new PointF(cx - ew/4, cy + eh/2),               // baixo esquerdo
    };

    var eyePath = new GraphicsPath();
    eyePath.AddBezier(pts[0], pts[1], pts[2], pts[3]);
    eyePath.AddBezier(pts[3], pts[4], pts[5], pts[0]);
    eyePath.CloseFigure();

    if (fill != null) g.FillPath(fill, eyePath);
    if (pen  != null) g.DrawPath(pen,  eyePath);

    eyePath.Dispose();
}
