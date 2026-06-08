using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EspiaDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    }

    /// <summary>
    /// Carrega o ícone real do EspiaDesk a partir do recurso embarcado (.ico).
    /// Retorna o frame de maior resolução disponível.
    /// </summary>
    internal static ImageSource? BuildIconSource()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/espiadisk.ico",
                              UriKind.Absolute);
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return null;

            var decoder = new IconBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            // Escolhe o frame de maior resolução (ex.: 256×256)
            BitmapFrame? best = null;
            foreach (var frame in decoder.Frames)
            {
                if (best is null || frame.PixelWidth > best.PixelWidth)
                    best = frame;
            }

            best?.Freeze();
            return best;
        }
        catch { return null; }
    }
}
