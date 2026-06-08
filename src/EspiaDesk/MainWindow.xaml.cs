using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EspiaDesk.Core;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace EspiaDesk;

public partial class MainWindow : Window
{
    private readonly HostServer _host = new();
    private string _myId;
    private readonly string _configPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".espiadisk.json");
    private Config _cfg;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<RecentEntry> _recents = [];

    public MainWindow()
    {
        InitializeComponent();
        _cfg = LoadConfig();
        _myId = SessionCrypto.GenerateSessionId();
        TxtMyId.Text = FormatId(_myId);
        PwdBox.Password = _cfg.Password;

        _host.OnAcceptRequest = AcceptRequestAsync;
        _host.OnConnected     = s => Dispatcher.Invoke(RefreshStatus);
        _host.OnDisconnected  = s => Dispatcher.Invoke(RefreshStatus);
        _host.OnChat          = (n, m) => Dispatcher.Invoke(() => SetStatus($"[Chat] {n}: {m}"));

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        RefreshRecents();
        StartServer_Click(this, null!);
    }

    // ── Servidor ──────────────────────────────────────────────────────────────
    private void StartServer_Click(object s, RoutedEventArgs e)
    {
        if (_host.Running) return;
        try
        {
            _host.Port = _cfg.Port;
            _host.PwdHash = string.IsNullOrEmpty(_cfg.Password) ? "" :
                            SessionCrypto.HashPassword(_cfg.Password);
            _host.Quality = _cfg.Quality;
            _host.Fps = _cfg.Fps;
            _host.Start();
            BtnStartServer.IsEnabled = false;
            BtnStopServer.IsEnabled  = true;
            ServerDot.Fill = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            TxtServerStatus.Text = $"Aguardando conexões — porta {_cfg.Port}";
            TxtLocalIp.Text = $"IP local: {_host.GetLocalIp()}";
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao iniciar servidor: {ex.Message}");
        }
    }

    private void StopServer_Click(object s, RoutedEventArgs e)
    {
        _host.Stop();
        BtnStartServer.IsEnabled = true;
        BtnStopServer.IsEnabled  = false;
        ServerDot.Fill = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        TxtServerStatus.Text = "Servidor parado";
        TxtLocalIp.Text = "";
    }

    private async Task<bool> AcceptRequestAsync(string name, string ip)
    {
        bool result = false;
        await Dispatcher.InvokeAsync(() =>
        {
            result = MessageBox.Show(
                $"'{name}' ({ip}) solicita acesso remoto.\n\nPermitir?",
                "EspiaDesk — Solicitação de Acesso",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        });
        return result;
    }

    // ── Conexão ───────────────────────────────────────────────────────────────
    private async void Connect_Click(object s, RoutedEventArgs e)
    {
        var target = TxtRemoteId.Text.Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(target))
        {
            MessageBox.Show("Digite o ID ou IP do host remoto.", "EspiaDesk", MessageBoxButton.OK);
            return;
        }

        BtnConnect.IsEnabled = false;
        BtnConnect.Content   = "⏳  Conectando...";
        SetStatus($"Conectando a {target}...");

        try
        {
            var client = new RemoteClient { LocalName = _cfg.YourName };
            var details = await client.ConnectAsync(target, _cfg.Port,
                                                    PwdRemote.Password,
                                                    CancellationToken.None);
            AddRecent(target, details.HostName);

            var viewer = new ViewerWindow(client, details);
            viewer.Show();

            _recents.Insert(0, new RecentEntry(details.HostName, target));
            _recents.RemoveAll(r => r.Host == target);
            RefreshRecents();
            SaveConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível conectar:\n{ex.Message}",
                            "EspiaDesk", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnConnect.IsEnabled = true;
            BtnConnect.Content   = "▶  CONECTAR";
            SetStatus("Pronto");
        }
    }

    private void TxtRemoteId_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect_Click(s, null!);
    }

    // ── ID ────────────────────────────────────────────────────────────────────
    private void CopyId_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        Clipboard.SetText(_myId);
        TxtMyId.Text = "Copiado! ✓";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { TxtMyId.Text = FormatId(_myId); timer.Stop(); };
        timer.Start();
    }

    // ── Senha ─────────────────────────────────────────────────────────────────
    private void PwdBox_Changed(object s, RoutedEventArgs e)
        => _cfg = _cfg with { Password = PwdBox.Password };

    private void ApplyPwd_Click(object s, RoutedEventArgs e)
    {
        _host.PwdHash = string.IsNullOrEmpty(_cfg.Password) ? "" :
                        SessionCrypto.HashPassword(_cfg.Password);
        SaveConfig();
        SetStatus("Senha aplicada.");
    }

    // ── Status ────────────────────────────────────────────────────────────────
    private void RefreshStatus()
    {
        int n = _host.Clients.Count;
        TxtSessions.Text = $"{n} sessão(ões) ativa(s)";
    }

    private void SetStatus(string msg)
        => TxtStatus.Text = msg;

    // ── Ribbon ───────────────────────────────────────────────────────────────
    private void History_Click(object s, RoutedEventArgs e)
    {
        var sb = string.Join("\n", _recents.Select(r => $"• {r.Display} ({r.Host})"));
        MessageBox.Show(string.IsNullOrEmpty(sb) ? "Sem histórico." : sb,
                        "Histórico de Conexões");
    }

    private void KickAll_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Encerrar todas as sessões ativas?", "EspiaDesk",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            _host.Stop();
    }

    private void Settings_Click(object s, RoutedEventArgs e)
        => new SettingsWindow(_cfg, cfg => { _cfg = cfg; SaveConfig(); }).ShowDialog();

    private void About_Click(object s, RoutedEventArgs e)
        => MessageBox.Show(
            "EspiaDesk v1.0\nAcesso Remoto seguro com criptografia E2E\n\nParte da suite Escritorio",
            "Sobre o EspiaDesk", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── Recentes ──────────────────────────────────────────────────────────────
    private void AddRecent(string host, string name)
    {
        _recents.RemoveAll(r => r.Host == host);
        _recents.Insert(0, new RecentEntry(name, host));
        if (_recents.Count > 10) _recents.RemoveAt(_recents.Count - 1);
    }

    private void RefreshRecents() => RecentList.ItemsSource = _recents.Take(5).ToList();

    private void Recent_Click(object s, RoutedEventArgs e)
    {
        TxtRemoteId.Text = ((FrameworkElement)s).Tag?.ToString() ?? "";
        Connect_Click(s, null!);
    }

    // ── Config ────────────────────────────────────────────────────────────────
    private Config LoadConfig()
    {
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(_configPath))!; }
        catch { return new Config(); }
    }

    private void SaveConfig()
    {
        _cfg = _cfg with { Recents = _recents.Select(r => r.Host).ToList() };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(_cfg));
    }

    private static string FormatId(string id)
        => $"{id[..3]} {id[3..6]} {id[6..]}";

    private void Window_Loaded(object s, RoutedEventArgs e)
    {
        // Ícone definido via XAML (pack://application:,,,/Assets/espiadisk.ico)
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _host.Stop();
        base.OnClosed(e);
    }
}

// ── DTOs simples ──────────────────────────────────────────────────────────────
record RecentEntry(string Display, string Host);

public record Config
{
    public string       Password  { get; init; } = "";
    public string       YourName  { get; init; } = System.Net.Dns.GetHostName();
    public int          Port      { get; init; } = 7070;
    public int          Quality   { get; init; } = 60;
    public int          Fps       { get; init; } = 20;
    public List<string> Recents   { get; init; } = [];
}

// ── Janela de configurações simples (inline) ──────────────────────────────────
public class SettingsWindow : Window
{
    public SettingsWindow(Config cfg, Action<Config> onSave)
    {
        Title = "Configurações — EspiaDesk";
        Width = 380; Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

        Config current = cfg;
        void Row(string label, System.Windows.UIElement ctrl)
        {
            sp.Children.Add(new System.Windows.Controls.TextBlock
                { Text = label, Margin = new Thickness(0, 8, 0, 2) });
            sp.Children.Add(ctrl);
        }

        var tbName = new System.Windows.Controls.TextBox { Text = cfg.YourName };
        var tbPort = new System.Windows.Controls.TextBox { Text = cfg.Port.ToString() };

        Row("Nome exibido:", tbName);
        Row("Porta TCP:", tbPort);

        var save = new System.Windows.Controls.Button
            { Content = "Salvar", Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(20, 6, 20, 6) };
        save.Click += (_, _) =>
        {
            onSave(cfg with
            {
                YourName = tbName.Text,
                Port = int.TryParse(tbPort.Text, out var p) ? p : 7070,
            });
            Close();
        };
        sp.Children.Add(save);
        Content = sp;
    }
}
