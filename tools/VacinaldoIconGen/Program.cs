using System.Drawing;
using System.Drawing.Drawing2D;

var outPath = args.Length > 0 ? args[0] : "vacinaldo.ico";

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var pngs = new byte[sizes.Length][];

for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    using var bmp = new Bitmap(s, s);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    float p = s;

    // Dark red circle background
    using (var br = new SolidBrush(Color.FromArgb(255, 198, 40, 40)))
        g.FillEllipse(br, 2, 2, s - 4, s - 4);

    // White shield polygon
    var shield = new PointF[]
    {
        new(p * .50f, p * .12f),
        new(p * .88f, p * .25f),
        new(p * .88f, p * .55f),
        new(p * .50f, p * .88f),
        new(p * .12f, p * .55f),
        new(p * .12f, p * .25f),
    };
    using (var sbr = new SolidBrush(Color.White))
        g.FillPolygon(sbr, shield);

    // Red checkmark inside shield
    using var pen = new Pen(Color.FromArgb(255, 198, 40, 40), p * .13f);
    pen.StartCap = LineCap.Round;
    pen.EndCap   = LineCap.Round;
    pen.LineJoin = LineJoin.Round;
    g.DrawLines(pen, new PointF[]
    {
        new(p * .28f, p * .50f),
        new(p * .44f, p * .66f),
        new(p * .72f, p * .35f),
    });

    using var ms = new MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    pngs[i] = ms.ToArray();
}

// Write ICO container
using var fs = File.Create(outPath);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);
bw.Write((ushort)1);
bw.Write((ushort)sizes.Length);

int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    bw.Write((byte)(s < 256 ? s : 0));
    bw.Write((byte)(s < 256 ? s : 0));
    bw.Write((byte)0);
    bw.Write((byte)0);
    bw.Write((ushort)1);
    bw.Write((ushort)32);
    bw.Write((uint)pngs[i].Length);
    bw.Write((uint)offset);
    offset += pngs[i].Length;
}
foreach (var d in pngs) bw.Write(d);

Console.WriteLine($"Gerado: {outPath} ({new FileInfo(outPath).Length} bytes)");
