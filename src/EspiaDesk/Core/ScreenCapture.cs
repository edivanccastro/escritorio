using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace EspiaDesk.Core;

public static class ScreenCapture
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    private static readonly ImageCodecInfo JpegCodec = GetJpegCodec();

    public static (int W, int H) GetPrimaryScreenSize()
        => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    public static byte[] CaptureJpeg(int quality = 60)
    {
        var (w, h) = GetPrimaryScreenSize();
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);

        using var ms = new MemoryStream();
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        bmp.Save(ms, JpegCodec, ep);
        return ms.ToArray();
    }

    public static BitmapImage JpegToBitmapImage(byte[] jpeg)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(jpeg);
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static ImageCodecInfo GetJpegCodec()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.MimeType == "image/jpeg") return c;
        throw new InvalidOperationException("JPEG encoder not found");
    }
}
