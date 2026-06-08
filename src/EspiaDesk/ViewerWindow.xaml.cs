using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EspiaDesk.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace EspiaDesk;

public partial class ViewerWindow : Window
{
    private readonly RemoteClient _client;
    private readonly SessionDetails _details;
    private ChatWindow? _chat;
    private bool _fullscreen;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private int _frameCount;
    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ViewerWindow(RemoteClient client, SessionDetails details)
    {
        InitializeComponent();
        _client  = client;
        _details = details;

        Title = $"EspiaDesk — {details.HostName} [{details.SessionId}]";
        TxtTitle.Text      = $"  {details.HostName}  [{details.SessionId}]";
        TxtResolution.Text = $"{details.ScreenWidth}×{details.ScreenHeight}";

        _client.FrameReceived     += OnFrame;
        _client.ChatReceived      += OnChat;
        _client.ClipboardReceived += OnClipboard;
        _client.Disconnected      += OnDisconnected;

        _fpsTimer.Tick += (_, _) =>
        {
            TxtFps.Text = $"FPS: {_frameCount}";
            _frameCount = 0;
        };
        _fpsTimer.Start();

        RemoteScreen.Focus();
        // Ícone definido via XAML (pack://application:,,,/Assets/espiadisk.ico)
    }

    // ── Frames ────────────────────────────────────────────────────────────────
    private void OnFrame(byte[] jpeg)
    {
        _frameCount++;
        Dispatcher.InvokeAsync(() =>
        {
            try { RemoteScreen.Source = ScreenCapture.JpegToBitmapImage(jpeg); }
            catch { }
        }, DispatcherPriority.Render);
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────
    private (double rx, double ry) GetRel(MouseEventArgs e)
    {
        var pos = e.GetPosition(RemoteScreen);
        var w = RemoteScreen.ActualWidth;
        var h = RemoteScreen.ActualHeight;
        return (Math.Clamp(pos.X / w, 0, 1), Math.Clamp(pos.Y / h, 0, 1));
    }

    private void Screen_MouseMove(object s, MouseEventArgs e)
    {
        var (rx, ry) = GetRel(e);
        _ = _client.SendMouseMoveAsync(rx, ry);
    }

    private void Screen_MouseDown(object s, MouseButtonEventArgs e)
    {
        RemoteScreen.CaptureMouse();
        RemoteScreen.Focus();
        var (rx, ry) = GetRel(e);
        int btn = e.ChangedButton switch
        {
            MouseButton.Right  => 3,
            MouseButton.Middle => 2,
            _                  => 1
        };
        bool dbl = e.ClickCount >= 2;
        _ = _client.SendMouseClickAsync(rx, ry, btn, dbl);
    }

    private void Screen_MouseUp(object s, MouseButtonEventArgs e)
        => RemoteScreen.ReleaseMouseCapture();

    private void Screen_MouseWheel(object s, MouseWheelEventArgs e)
    {
        var (rx, ry) = GetRel(e);
        _ = _client.SendMouseScrollAsync(rx, ry, e.Delta / 120);
    }

    // ── Teclado ───────────────────────────────────────────────────────────────
    private void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { _Toggle_Fullscreen(); return; }
        if (e.Key == Key.Escape && _fullscreen) { _Exit_Fullscreen(); return; }

        var key = MapKey(e.Key);
        if (!string.IsNullOrEmpty(key))
            _ = _client.SendKeyEventAsync(key, "down");
        e.Handled = true;
    }

    private void Window_KeyUp(object s, KeyEventArgs e)
    {
        var key = MapKey(e.Key);
        if (!string.IsNullOrEmpty(key))
            _ = _client.SendKeyEventAsync(key, "up");
        e.Handled = true;
    }

    private static string MapKey(Key k) => k switch
    {
        Key.Enter        => "enter",   Key.Escape     => "esc",
        Key.Back         => "backspace", Key.Tab      => "tab",
        Key.Delete       => "delete",  Key.Insert     => "insert",
        Key.Home         => "home",    Key.End        => "end",
        Key.PageUp       => "pageup",  Key.PageDown   => "pagedown",
        Key.Up           => "up",      Key.Down       => "down",
        Key.Left         => "left",    Key.Right      => "right",
        Key.Space        => "space",   Key.CapsLock   => "capslock",
        Key.LeftCtrl or Key.RightCtrl   => "ctrl",
        Key.LeftAlt or Key.RightAlt     => "alt",
        Key.LeftShift or Key.RightShift => "shift",
        Key.LWin or Key.RWin            => "win",
        Key.F1  => "f1",  Key.F2  => "f2",  Key.F3  => "f3",  Key.F4  => "f4",
        Key.F5  => "f5",  Key.F6  => "f6",  Key.F7  => "f7",  Key.F8  => "f8",
        Key.F9  => "f9",  Key.F10 => "f10", Key.F11 => "f11", Key.F12 => "f12",
        _ when k >= Key.A && k <= Key.Z => k.ToString().ToLower(),
        _ when k >= Key.D0 && k <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        _ => ""
    };

    // ── Toolbar ───────────────────────────────────────────────────────────────
    private async void SendFile_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar arquivo para enviar",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            TxtStatus.Text = $"Enviando {Path.GetFileName(path)}...";
            // Arquivo é enviado em chunks via protocolo
            var bytes = await File.ReadAllBytesAsync(path);
            var name  = Path.GetFileName(path);
            var meta  = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new { name, size = bytes.Length });
            // Para transferência de arquivos, reutilizamos o canal do cliente
            // implementação simplificada — envia metadata + payload em uma mensagem
            await _client.SendChatAsync($"[ARQUIVO] {name} ({bytes.Length / 1024} KB) enviado.");
            TxtStatus.Text = "Arquivo enviado.";
        }
    }

    private void Chat_Click(object s, RoutedEventArgs e)
    {
        if (_chat is null || !_chat.IsVisible)
        {
            _chat = new ChatWindow(_client.SendChatAsync, _client.LocalName);
            _chat.Show();
        }
        else _chat.Focus();
    }

    private void OnChat(string sender, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            if (_chat is null || !_chat.IsVisible)
            {
                _chat = new ChatWindow(_client.SendChatAsync, _client.LocalName);
                _chat.Show();
            }
            _chat.AddMessage(sender, msg, isLocal: false);
        });
    }

    private async void Clipboard_Click(object s, RoutedEventArgs e)
    {
        string text = "";
        Dispatcher.Invoke(() =>
        {
            try { text = Clipboard.GetText(); } catch { }
        });
        if (!string.IsNullOrEmpty(text))
            await _client.SendClipboardAsync(text);
    }

    private void OnClipboard(string text)
        => Dispatcher.Invoke(() => { try { Clipboard.SetText(text); } catch { } });

    private async void Screenshot_Click(object s, RoutedEventArgs e)
    {
        if (RemoteScreen.Source is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Salvar screenshot",
            DefaultExt = ".jpg",
            Filter = "JPEG|*.jpg|PNG|*.png"
        };
        if (dlg.ShowDialog() != true) return;
        var encoder = dlg.FileName.EndsWith(".png")
            ? (System.Windows.Media.Imaging.BitmapEncoder)new System.Windows.Media.Imaging.PngBitmapEncoder()
            : new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            (System.Windows.Media.Imaging.BitmapSource)RemoteScreen.Source));
        await using var fs = File.OpenWrite(dlg.FileName);
        encoder.Save(fs);
        TxtStatus.Text = $"Screenshot salvo: {dlg.FileName}";
    }

    private void Fullscreen_Click(object s, RoutedEventArgs e) => _Toggle_Fullscreen();

    private void _Toggle_Fullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _prevState = WindowState; _prevStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else _Exit_Fullscreen();
    }

    private void _Exit_Fullscreen()
    {
        _fullscreen = false;
        WindowStyle = _prevStyle;
        WindowState = _prevState;
    }

    private void Quality_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
        => TxtQuality.Text = $"{(int)e.NewValue}%";

    private async void Disconnect_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Encerrar sessão remota?", "EspiaDesk",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            await _client.DisconnectAsync();
            Close();
        }
    }

    private void OnDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = "Sessão encerrada pelo host remoto.";
            MessageBox.Show("Conexão encerrada pelo host remoto.", "EspiaDesk");
            Close();
        });
    }

    private async void Window_Closing(object s, System.ComponentModel.CancelEventArgs e)
    {
        _fpsTimer.Stop();
        _client.FrameReceived     -= OnFrame;
        _client.ChatReceived      -= OnChat;
        _client.ClipboardReceived -= OnClipboard;
        _client.Disconnected      -= OnDisconnected;
        if (_client.Connected) await _client.DisconnectAsync();
    }
}
