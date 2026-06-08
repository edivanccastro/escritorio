using System.Drawing;
using System.Drawing.Drawing2D;

var outPath = args.Length > 0 ? args[0] : "zefaxina.ico";
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var pngs = new byte[sizes.Length][];

for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    using var bmp = new Bitmap(s, s);
    using var g   = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);
    float p = s;

    // Orange background circle
    using (var bg = new SolidBrush(Color.FromArgb(255, 230, 81, 0)))
        g.FillEllipse(bg, 2, 2, s - 4, s - 4);

    // Broom handle (white diagonal line, top-right to bottom-left)
    using (var hp = new Pen(Color.White, p * .12f))
    {
        hp.StartCap = LineCap.Round; hp.EndCap = LineCap.Round;
        g.DrawLine(hp, p * .70f, p * .10f, p * .28f, p * .72f);
    }

    // Broom head (white trapezoid at bottom-left)
    var head = new PointF[]
    {
        new(p * .10f, p * .68f),
        new(p * .46f, p * .52f),
        new(p * .56f, p * .74f),
        new(p * .18f, p * .88f),
    };
    using (var hbr = new SolidBrush(Color.White))
        g.FillPolygon(hbr, head);

    // Bristle lines on broom head
    using var bp = new Pen(Color.FromArgb(255, 230, 81, 0), p * .04f);
    bp.StartCap = LineCap.Round; bp.EndCap = LineCap.Round;
    for (int b = 0; b < 4; b++)
    {
        float t = 0.25f + b * 0.18f;
        var p1 = new PointF(
            head[0].X + (head[1].X - head[0].X) * t,
            head[0].Y + (head[1].Y - head[0].Y) * t);
        var p2 = new PointF(
            head[3].X + (head[2].X - head[3].X) * t,
            head[3].Y + (head[2].Y - head[3].Y) * t);
        g.DrawLine(bp, p1, p2);
    }

    // Sparkles (small circles top-right)
    using var spbr = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
    float r1 = p * .05f, r2 = p * .035f, r3 = p * .025f;
    g.FillEllipse(spbr, p * .78f - r1, p * .16f - r1, r1 * 2, r1 * 2);
    g.FillEllipse(spbr, p * .87f - r2, p * .28f - r2, r2 * 2, r2 * 2);
    g.FillEllipse(spbr, p * .82f - r3, p * .38f - r3, r3 * 2, r3 * 2);

    using var ms = new MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    pngs[i] = ms.ToArray();
}

using var fs = File.Create(outPath);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    bw.Write((byte)(s < 256 ? s : 0)); bw.Write((byte)(s < 256 ? s : 0));
    bw.Write((byte)0); bw.Write((byte)0);
    bw.Write((ushort)1); bw.Write((ushort)32);
    bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
    offset += pngs[i].Length;
}
foreach (var d in pngs) bw.Write(d);
Console.WriteLine($"Gerado: {outPath} ({new FileInfo(outPath).Length} bytes)");
