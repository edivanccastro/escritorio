using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace Vacinaldo;

/// <summary>
/// Gerencia o Ã­cone na bandeja do sistema, o menu de contexto e
/// a criaÃ§Ã£o/exibiÃ§Ã£o lazy da janela principal.
/// </summary>
internal sealed class TrayManager : IDisposable
{
    private readonly App _app;
    private NotifyIcon   _tray = null!;
    private MainWindow?  _window;

    // Itens de menu que precisam ser atualizados dinamicamente
    private ToolStripMenuItem _rtpItem  = null!;
    private ToolStripMenuItem _openItem = null!;

    public ScanEngine Engine { get; } = new ScanEngine();
    public EdrEngine   Edr    { get; } = new EdrEngine();

    public TrayManager(App app)
    {
        _app = app;
        Engine.ThreatDetected += OnThreatDetected;
        Edr.AlertRaised       += OnEdrAlert;
    }

    // â”€â”€ InicializaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void Initialize()
    {
        var icon = LoadIcon();

        _openItem = new ToolStripMenuItem("Abrir Vacinaldo", null, (_, _) => ShowWindow());
        _rtpItem  = new ToolStripMenuItem("ProteÃ§Ã£o em Tempo Real", null, (_, _) => ToggleRtp())
        {
            Checked      = true,
            CheckOnClick = true,
        };

        var exitItem = new ToolStripMenuItem("Sair", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_rtpItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon             = icon,
            Text             = "Vacinaldo â€” ProteÃ§Ã£o Ativa",
            ContextMenuStrip = menu,
            Visible          = true,
        };

        _tray.DoubleClick  += (_, _) => ShowWindow();
        _tray.MouseClick   += OnTrayClick;

        // Inicia proteÃ§Ã£o em tempo real e motor EDR
        Engine.StartRealTimeProtection();
        Edr.Start();

        ShowBalloon("Vacinaldo iniciado", "ProteÃ§Ã£o em tempo real + EDR ativos.", ToolTipIcon.Info);
    }

    // â”€â”€ Clicar uma vez no Ã­cone exibe o menu; duplo-clique abre a janela â”€â”€â”€â”€â”€

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowWindow();
    }

    // â”€â”€ Mostrar / ocultar janela principal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void ShowWindow()
    {
        _app.Dispatcher.Invoke(() =>
        {
            if (_window is null || !_window.IsLoaded)
            {
                _window = new MainWindow(Engine, Edr);
                _window.Closing += (_, ce) =>
                {
                    ce.Cancel = true;   // nÃ£o fecha â€” oculta na bandeja
                    _window.Hide();
                };
            }

            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Focus();
        });
    }

    // â”€â”€ Alternar proteÃ§Ã£o em tempo real â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void ToggleRtp()
    {
        var on = _rtpItem.Checked;
        if (on)
        {
            Engine.StartRealTimeProtection();
            _tray.Text = "Vacinaldo â€” ProteÃ§Ã£o Ativa";
            ShowBalloon("Vacinaldo", "ProteÃ§Ã£o em tempo real ativada.", ToolTipIcon.Info);
        }
        else
        {
            Engine.StopRealTimeProtection();
            _tray.Text = "Vacinaldo â€” ProteÃ§Ã£o Inativa";
            ShowBalloon("Vacinaldo", "ProteÃ§Ã£o em tempo real desativada.", ToolTipIcon.Warning);
        }

        // Atualiza o toggle na janela principal, se estiver aberta
        _app.Dispatcher.Invoke(() =>
        {
            if (_window is { IsLoaded: true })
                _window.SyncRtpState(on);
        });
    }

    // â”€â”€ AmeaÃ§a detectada em tempo real â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnThreatDetected(ThreatInfo threat)
    {
        _app.Dispatcher.Invoke(() =>
        {
            // NotificaÃ§Ã£o em balÃ£o na bandeja (nÃ£o interrompe o usuÃ¡rio)
            ShowBalloon(
                "âš  AmeaÃ§a detectada!",
                $"{System.IO.Path.GetFileName(threat.FilePath)}\n{threat.ThreatName}",
                ToolTipIcon.Warning);

            // Quarentena automÃ¡tica
            ScanEngine.QuarantineFile(threat);

            // Atualiza janela se estiver aberta
            if (_window is { IsLoaded: true, IsVisible: true })
                _window.OnRealTimeThreatDetected(threat);
        });
    }

    // â”€â”€ Alerta EDR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnEdrAlert(SecurityEvent evt)
    {
        if (evt.Risk < EdrRisk.High) return;
        _app.Dispatcher.Invoke(() =>
        {
            var desc = evt.Description.Length > 100 ? evt.Description[..100] + "â€¦" : evt.Description;
            ShowBalloon(
                $"ðŸŽ¯ EDR {evt.Risk} â€” {evt.MitreTechnique ?? ""}",
                desc, ToolTipIcon.Warning);
        });
    }

    // â”€â”€ Encerrar aplicaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void ExitApp()
    {
        _tray.Visible = false;
        Engine.StopRealTimeProtection();
        Edr.Stop();
        _app.Shutdown();
    }

    // â”€â”€ BalÃ£o de notificaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.None)
    {
        try { _tray?.ShowBalloonTip(4000, title, text, icon); }
        catch { }
    }

    // â”€â”€ UtilitÃ¡rio: carrega o Ã­cone do prÃ³prio executÃ¡vel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Icon LoadIcon()
    {
        try
        {
            var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(loc))
                loc = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(loc))
                return Icon.ExtractAssociatedIcon(loc) ?? SystemIcons.Shield;
        }
        catch { }
        return SystemIcons.Shield;
    }

    // â”€â”€ Dispose â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void Dispose()
    {
        _tray?.Dispose();
        Engine.StopRealTimeProtection();
        Edr.Stop();
    }
}

