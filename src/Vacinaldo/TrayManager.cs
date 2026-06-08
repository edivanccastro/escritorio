using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace Vacinaldo;

/// <summary>
/// Gerencia o ícone na bandeja do sistema, o menu de contexto e
/// a criação/exibição lazy da janela principal.
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

    //  -- ? -- ? Inicialização  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public void Initialize()
    {
        var icon = LoadIcon();

        _openItem = new ToolStripMenuItem("Abrir Vacinaldo", null, (_, _) => ShowWindow());
        _rtpItem  = new ToolStripMenuItem("Proteção em Tempo Real", null, (_, _) => ToggleRtp())
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
            Text             = "Vacinaldo  --  Proteção Ativa",
            ContextMenuStrip = menu,
            Visible          = true,
        };

        _tray.DoubleClick  += (_, _) => ShowWindow();
        _tray.MouseClick   += OnTrayClick;

        // Inicia proteção em tempo real e motor EDR
        Engine.StartRealTimeProtection();
        Edr.Start();

        ShowBalloon("Vacinaldo iniciado", "Proteção em tempo real + EDR ativos.", ToolTipIcon.Info);
    }

    //  -- ? -- ? Clicar uma vez no ícone exibe o menu; duplo-clique abre a janela  -- ? -- ? -- ? -- ? -- ?

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowWindow();
    }

    //  -- ? -- ? Mostrar / ocultar janela principal  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public void ShowWindow()
    {
        _app.Dispatcher.Invoke(() =>
        {
            if (_window is null || !_window.IsLoaded)
            {
                _window = new MainWindow(Engine, Edr);
                _window.Closing += (_, ce) =>
                {
                    ce.Cancel = true;   // não fecha  --  oculta na bandeja
                    _window.Hide();
                };
            }

            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Focus();
        });
    }

    //  -- ? -- ? Alternar proteção em tempo real  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private void ToggleRtp()
    {
        var on = _rtpItem.Checked;
        if (on)
        {
            Engine.StartRealTimeProtection();
            _tray.Text = "Vacinaldo  --  Proteção Ativa";
            ShowBalloon("Vacinaldo", "Proteção em tempo real ativada.", ToolTipIcon.Info);
        }
        else
        {
            Engine.StopRealTimeProtection();
            _tray.Text = "Vacinaldo  --  Proteção Inativa";
            ShowBalloon("Vacinaldo", "Proteção em tempo real desativada.", ToolTipIcon.Warning);
        }

        // Atualiza o toggle na janela principal, se estiver aberta
        _app.Dispatcher.Invoke(() =>
        {
            if (_window is { IsLoaded: true })
                _window.SyncRtpState(on);
        });
    }

    //  -- ? -- ? Ameaça detectada em tempo real  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private void OnThreatDetected(ThreatInfo threat)
    {
        _app.Dispatcher.Invoke(() =>
        {
            // Notificação em balão na bandeja (não interrompe o usuário)
            ShowBalloon(
                "�s� Ameaça detectada!",
                $"{System.IO.Path.GetFileName(threat.FilePath)}\n{threat.ThreatName}",
                ToolTipIcon.Warning);

            // Quarentena automática
            ScanEngine.QuarantineFile(threat);

            // Atualiza janela se estiver aberta
            if (_window is { IsLoaded: true, IsVisible: true })
                _window.OnRealTimeThreatDetected(threat);
        });
    }

    //  -- ? -- ? Alerta EDR  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private void OnEdrAlert(SecurityEvent evt)
    {
        if (evt.Risk < EdrRisk.High) return;
        _app.Dispatcher.Invoke(() =>
        {
            var desc = evt.Description.Length > 100 ? evt.Description[..100] + "..." : evt.Description;
            ShowBalloon(
                $"EDR {evt.Risk} -- {evt.MitreTechnique ?? string.Empty}",
                desc, ToolTipIcon.Warning);
        });
    }

    //  -- ? -- ? Encerrar aplicação  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    private void ExitApp()
    {
        _tray.Visible = false;
        Engine.StopRealTimeProtection();
        Edr.Stop();
        _app.Shutdown();
    }

    //  -- ? -- ? Balão de notificação  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.None)
    {
        try { _tray?.ShowBalloonTip(4000, title, text, icon); }
        catch { }
    }

    //  -- ? -- ? Utilitário: carrega o ícone do próprio executável  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

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

    //  -- ? -- ? Dispose  -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ? -- ?

    public void Dispose()
    {
        _tray?.Dispose();
        Engine.StopRealTimeProtection();
        Edr.Stop();
    }
}

