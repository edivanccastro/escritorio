using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WpfMsgBox       = System.Windows.MessageBox;
using WpfMsgBoxButton = System.Windows.MessageBoxButton;
using WpfMsgBoxImage  = System.Windows.MessageBoxImage;
using WpfMsgBoxResult = System.Windows.MessageBoxResult;

namespace Vacinaldo;

public partial class MainWindow : Window
{
    // â”€â”€ DependÃªncias injetadas pelo TrayManager â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly ScanEngine _engine;
    private readonly EdrEngine  _edr;
    private CancellationTokenSource? _cts;
    private DateTime _scanStarted;

    // ViewModels â€” varredura
    public ObservableCollection<ThreatViewModel>     Threats    { get; } = [];
    public ObservableCollection<QuarantineViewModel> Quarantine { get; } = [];
    public ObservableCollection<HistoryViewModel>    History    { get; } = [];

    // ViewModels â€” EDR
    public ObservableCollection<ProcessVm>  EdrProcesses { get; } = [];
    public ObservableCollection<NetworkVm>  EdrNetwork   { get; } = [];
    public ObservableCollection<EventVm>    EdrTimeline  { get; } = [];
    public ObservableCollection<AuditVm>    EdrAudit     { get; } = [];

    // â”€â”€ Construtor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public MainWindow(ScanEngine engine, EdrEngine edr)
    {
        _engine = engine;
        _edr    = edr;

        InitializeComponent();

        ThreatList.ItemsSource     = Threats;
        QuarantineList.ItemsSource = Quarantine;
        HistoryList.ItemsSource    = History;

        EdrProcessList.ItemsSource  = EdrProcesses;
        EdrNetworkList.ItemsSource  = EdrNetwork;
        EdrTimelineList.ItemsSource = EdrTimeline;
        EdrAuditList.ItemsSource    = EdrAudit;

        // Associa o toggle DEPOIS do InitializeComponent para evitar disparo prematuro
        RealTimToggle.Checked   += RealTimeToggle_Changed;
        RealTimToggle.Unchecked += RealTimeToggle_Changed;

        // Sincroniza estado inicial do toggle com o engine
        RealTimToggle.IsChecked = _engine.IsRealTimeActive;

        RefreshQuarantine();
        RefreshHistory();
        UpdateDashboard();
    }

    // â”€â”€ SincronizaÃ§Ã£o do estado RTP (chamado pelo TrayManager) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Atualiza o toggle e os labels de status sem disparar o evento.</summary>
    public void SyncRtpState(bool active)
    {
        RealTimToggle.Checked   -= RealTimeToggle_Changed;
        RealTimToggle.Unchecked -= RealTimeToggle_Changed;
        RealTimToggle.IsChecked  = active;
        RealTimToggle.Checked   += RealTimeToggle_Changed;
        RealTimToggle.Unchecked += RealTimeToggle_Changed;
        ApplyRtpVisuals(active);
    }

    /// <summary>Chamado pelo TrayManager ao detectar ameaÃ§a em tempo real.</summary>
    public void OnRealTimeThreatDetected(ThreatInfo threat)
    {
        WpfMsgBox.Show(this,
            $"âš  AmeaÃ§a detectada em tempo real!\n\nArquivo: {threat.FilePath}\n" +
            $"AmeaÃ§a: {threat.ThreatName}\nNÃ­vel: {ScanEngine.ThreatLevelLabel(threat.Level)}\n\n" +
            "O arquivo foi quarentenado automaticamente.",
            "Vacinaldo â€” Alerta",
            WpfMsgBoxButton.OK, WpfMsgBoxImage.Warning);

        RefreshQuarantine();
        UpdateDashboard();
        StatusText.Text = $"âš  AmeaÃ§a quarentenada: {Path.GetFileName(threat.FilePath)}";
    }

    // â”€â”€ NavegaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageDashboard is null) return;

        PageDashboard.Visibility  = Visibility.Collapsed;
        PageScan.Visibility       = Visibility.Collapsed;
        PageQuarantine.Visibility = Visibility.Collapsed;
        PageHistory.Visibility    = Visibility.Collapsed;
        PageSettings.Visibility   = Visibility.Collapsed;
        PageEdr.Visibility        = Visibility.Collapsed;

        if (sender == NavDashboard)  { PageDashboard.Visibility  = Visibility.Visible; UpdateDashboard(); }
        if (sender == NavScan)       { PageScan.Visibility       = Visibility.Visible; }
        if (sender == NavQuarantine) { PageQuarantine.Visibility = Visibility.Visible; RefreshQuarantine(); }
        if (sender == NavHistory)    { PageHistory.Visibility    = Visibility.Visible; RefreshHistory(); }
        if (sender == NavSettings)   { PageSettings.Visibility   = Visibility.Visible; }
        if (sender == NavEdr)        { PageEdr.Visibility        = Visibility.Visible; RefreshEdr(); }
    }

    // â”€â”€ EDR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void EdrTab_Checked(object sender, RoutedEventArgs e)
    {
        if (EdrPageProcess is null) return;
        EdrPageProcess.Visibility  = Visibility.Collapsed;
        EdrPageNetwork.Visibility  = Visibility.Collapsed;
        EdrPageTimeline.Visibility = Visibility.Collapsed;
        EdrPageSaif.Visibility     = Visibility.Collapsed;

        if (sender == EdrTabProcess)  EdrPageProcess.Visibility  = Visibility.Visible;
        if (sender == EdrTabNetwork)  EdrPageNetwork.Visibility  = Visibility.Visible;
        if (sender == EdrTabTimeline) EdrPageTimeline.Visibility = Visibility.Visible;
        if (sender == EdrTabSaif)     { EdrPageSaif.Visibility   = Visibility.Visible; RefreshAuditLog(); }
    }

    private void EdrRefresh_Click(object sender, RoutedEventArgs e) => RefreshEdr();

    private void RefreshEdr()
    {
        // Processos
        EdrProcesses.Clear();
        foreach (var p in _edr.GetProcessSnapshot())
            EdrProcesses.Add(new ProcessVm(p));

        // Rede
        EdrNetwork.Clear();
        foreach (var c in _edr.GetNetworkConnections())
            EdrNetwork.Add(new NetworkVm(c));

        // Timeline
        EdrTimeline.Clear();
        foreach (var ev in _edr.Timeline)
            EdrTimeline.Add(new EventVm(ev));

        // Audit log (sÃ³ carrega quando aba SAIF estÃ¡ visÃ­vel)
        if (EdrPageSaif?.Visibility == Visibility.Visible)
            RefreshAuditLog();

        StatusText.Text = $"EDR: {EdrProcesses.Count} processos Â· {EdrNetwork.Count} conexÃµes Â· {EdrTimeline.Count} eventos na timeline";
    }

    private void RefreshAuditLog()
    {
        EdrAudit.Clear();
        var events = AuditLogger.ReadRecent(200);
        foreach (var ev in events)
            EdrAudit.Add(new AuditVm(ev));

        var bytes = AuditLogger.GetLogBytes();
        AuditLogSize.Text = bytes > 0
            ? $"Log: {events.Count} eventos Â· {bytes / 1024} KB"
            : "Log de auditoria vazio";
    }

    private void EdrClearTimeline_Click(object sender, RoutedEventArgs e)
    {
        EdrTimeline.Clear();
        StatusText.Text = "Timeline EDR limpa.";
    }

    private void EdrClearAudit_Click(object sender, RoutedEventArgs e)
    {
        AuditLogger.Clear();
        RefreshAuditLog();
        StatusText.Text = "Log de auditoria limpo.";
    }

    // â”€â”€ Varredura â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void QuickScan_OnClick(object sender, RoutedEventArgs e)  => StartScan("Varredura RÃ¡pida",    ScanEngine.QuickScanPaths);
    private void FullScan_OnClick(object sender, RoutedEventArgs e)   => StartScan("Varredura Completa",  ScanEngine.FullScanPaths);
    private void QuickScanCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => StartScan("Varredura RÃ¡pida",    ScanEngine.QuickScanPaths);
    private void FullScanCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)  => StartScan("Varredura Completa",  ScanEngine.FullScanPaths);
    private void CustomScanCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => CustomScan_OnClick(sender, e);

    private void CustomScan_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selecione a pasta para varredura"
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        StartScan("Varredura Personalizada", [dlg.SelectedPath]);
    }

    private async void StartScan(string scanType, IEnumerable<string> paths)
    {
        NavScan.IsChecked = true;

        _cts?.Cancel();
        _cts = null;

        Threats.Clear();
        ThreatCountLabel.Text       = "AmeaÃ§as encontradas: 0";
        QuarantineAllBtn.Visibility = Visibility.Collapsed;
        ScanStatusText.Text = $"Iniciando {scanType}â€¦";
        ScanProgress.Visibility = Visibility.Visible;
        StopButton.Visibility   = Visibility.Visible;
        StatusText.Text = $"{scanType} em andamentoâ€¦";
        _scanStarted = DateTime.Now;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.IsFinished)
            {
                ScanProgress.Visibility = Visibility.Collapsed;
                StopButton.Visibility   = Visibility.Collapsed;
                ScanFileText.Text       = string.Empty;
                ScanStatusText.Text = p.ThreatsFound == 0
                    ? $"âœ… Varredura concluÃ­da â€” {p.FilesScanned} arquivo(s). Nenhuma ameaÃ§a."
                    : $"âš  Varredura concluÃ­da â€” {p.FilesScanned} arquivo(s), {p.ThreatsFound} ameaÃ§a(s).";
                StatusText.Text = ScanStatusText.Text;
                QuarantineAllBtn.Visibility = Threats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                ScanEngine.AppendHistory(new ScanHistoryEntry(
                    Guid.NewGuid().ToString("N")[..8],
                    _scanStarted, DateTime.Now, scanType,
                    p.FilesScanned, p.ThreatsFound));
                RefreshHistory();
                UpdateDashboard();
            }
            else
            {
                ScanStatusText.Text   = $"Verificandoâ€¦ {p.FilesScanned} arquivo(s) | {p.ThreatsFound} ameaÃ§a(s)";
                ScanFileText.Text     = p.CurrentFile;
                ThreatCountLabel.Text = $"AmeaÃ§as encontradas: {p.ThreatsFound}";
            }
        });

        try
        {
            var found = await _engine.ScanAsync(paths, progress, token);
            foreach (var t in found) Threats.Add(new ThreatViewModel(t));
            ThreatCountLabel.Text = $"AmeaÃ§as encontradas: {found.Count}";
        }
        catch (OperationCanceledException)
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            StopButton.Visibility   = Visibility.Collapsed;
            ScanStatusText.Text = "â¹ Varredura interrompida pelo usuÃ¡rio.";
            StatusText.Text     = "Varredura interrompida.";
        }
        finally { _cts = null; }
    }

    private void StopScan_OnClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // â”€â”€ Quarentena a partir da lista de ameaÃ§as â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void QuarantineAll_OnClick(object sender, RoutedEventArgs e)
    {
        int count = 0;
        foreach (var vm in Threats.ToList())
        {
            if (ScanEngine.QuarantineFile(vm.Threat) is not null)
            {
                Threats.Remove(vm);
                count++;
            }
        }
        ThreatCountLabel.Text = $"AmeaÃ§as encontradas: {Threats.Count}";
        if (Threats.Count == 0) QuarantineAllBtn.Visibility = Visibility.Collapsed;
        StatusText.Text = $"{count} arquivo(s) quarentenado(s).";
        RefreshQuarantine();
        UpdateDashboard();
    }

    // â”€â”€ Quarentena â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RefreshQuarantine()
    {
        Quarantine.Clear();
        foreach (var e in ScanEngine.LoadQuarantine())
            Quarantine.Add(new QuarantineViewModel(e));
    }

    private void Restore_OnClick(object sender, RoutedEventArgs e)
    {
        if (QuarantineList.SelectedItem is not QuarantineViewModel vm) return;
        if (ScanEngine.RestoreFile(vm.Entry))
        {
            StatusText.Text = $"Restaurado: {Path.GetFileName(vm.Entry.OriginalPath)}";
            RefreshQuarantine(); UpdateDashboard();
        }
        else WpfMsgBox.Show(this, "NÃ£o foi possÃ­vel restaurar o arquivo.", "Vacinaldo",
            WpfMsgBoxButton.OK, WpfMsgBoxImage.Warning);
    }

    private void DeleteQuar_OnClick(object sender, RoutedEventArgs e)
    {
        if (QuarantineList.SelectedItem is not QuarantineViewModel vm) return;
        if (WpfMsgBox.Show(this, "Excluir definitivamente o arquivo da quarentena?",
            "Vacinaldo", WpfMsgBoxButton.YesNo, WpfMsgBoxImage.Question) != WpfMsgBoxResult.Yes) return;
        ScanEngine.DeleteQuarantineEntry(vm.Entry);
        StatusText.Text = "Arquivo excluÃ­do permanentemente.";
        RefreshQuarantine(); UpdateDashboard();
    }

    private void DeleteAllQuar_OnClick(object sender, RoutedEventArgs e)
    {
        if (Quarantine.Count == 0) return;
        if (WpfMsgBox.Show(this, $"Excluir definitivamente {Quarantine.Count} arquivo(s) da quarentena?",
            "Vacinaldo", WpfMsgBoxButton.YesNo, WpfMsgBoxImage.Question) != WpfMsgBoxResult.Yes) return;
        foreach (var vm in Quarantine.ToList())
            ScanEngine.DeleteQuarantineEntry(vm.Entry);
        RefreshQuarantine(); UpdateDashboard();
        StatusText.Text = "Quarentena limpa.";
    }

    // â”€â”€ HistÃ³rico â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var e in ScanEngine.LoadHistory())
            History.Add(new HistoryViewModel(e));
    }

    private void ClearHistory_OnClick(object sender, RoutedEventArgs e)
    {
        ScanEngine.ClearHistory();
        RefreshHistory(); UpdateDashboard();
        StatusText.Text = "HistÃ³rico limpo.";
    }

    // â”€â”€ ConfiguraÃ§Ãµes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RealTimeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (RealTimeStatus is null || RtpStatusBar is null) return;
        var on = RealTimToggle.IsChecked == true;
        if (on) _engine.StartRealTimeProtection();
        else    _engine.StopRealTimeProtection();
        ApplyRtpVisuals(on);
    }

    private void ApplyRtpVisuals(bool on)
    {
        if (RealTimeStatus is null || RtpStatusBar is null) return;
        var green = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 142, 60));
        var red   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
        RealTimeStatus.Text       = on ? "Ativa"    : "Inativa";
        RealTimeStatus.Foreground = on ? green      : red;
        RtpStatusBar.Text         = on ? "ðŸ›¡ ProteÃ§Ã£o: Ativa" : "âš  ProteÃ§Ã£o: Inativa";
        RtpStatusBar.Foreground   = on ? green      : red;
    }

    // â”€â”€ Dashboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void UpdateDashboard()
    {
        var quarCount = ScanEngine.LoadQuarantine().Count;
        var histCount = ScanEngine.LoadHistory().Count;
        var lastScan  = ScanEngine.LoadHistory().FirstOrDefault();

        QuarantineCount.Text = quarCount.ToString();
        ScanCount.Text       = histCount.ToString();
        LastScanText.Text    = lastScan is null
            ? "Ãšltima varredura: nunca"
            : $"Ãšltima varredura: {lastScan.StartedAt:dd/MM/yyyy HH:mm} ({lastScan.ScanType})";

        var orange = System.Windows.Media.Color.FromRgb(230, 81,   0);
        var green  = System.Windows.Media.Color.FromRgb( 46, 125, 50);
        var lGreen = System.Windows.Media.Color.FromRgb( 76, 175, 80);
        var lOrang = System.Windows.Media.Color.FromRgb(239, 108,  0);

        if (quarCount > 0)
        {
            StatusCard.Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 243, 224));
            StatusIcon.Text        = "âš ";
            StatusTitle.Text       = "AtenÃ§Ã£o";
            StatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(orange);
            StatusDesc.Text        = $"{quarCount} arquivo(s) em quarentena. Revise a quarentena.";
            StatusDesc.Foreground  = new System.Windows.Media.SolidColorBrush(lOrang);
        }
        else
        {
            StatusCard.Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
            StatusIcon.Text        = "âœ…";
            StatusTitle.Text       = "Protegido";
            StatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(green);
            StatusDesc.Text        = "Seu sistema estÃ¡ protegido. Nenhuma ameaÃ§a encontrada.";
            StatusDesc.Foreground  = new System.Windows.Media.SolidColorBrush(lGreen);
        }
    }

    // â”€â”€ Fechar: oculta na bandeja â€” nÃ£o encerra o processo â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // O handler real de Closing Ã© registrado pelo TrayManager ao criar a janela.
    // Este handler aqui serve apenas para cancelar varreduras em andamento.

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        // NÃƒO pÃ¡ra a proteÃ§Ã£o â€” o engine Ã© gerenciado pelo TrayManager/App
    }
}

// â”€â”€â”€ ViewModels â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class ThreatViewModel(ThreatInfo t)
{
    public ThreatInfo Threat        { get; } = t;
    public string FilePath          { get; } = t.FilePath;
    public string ThreatName        { get; } = t.ThreatName;
    public string LevelLabel        { get; } = ScanEngine.ThreatLevelLabel(t.Level);
    public string DetectedAtText    { get; } = t.DetectedAt.ToString("dd/MM HH:mm");
}

public sealed class QuarantineViewModel(QuarantineEntry e)
{
    public QuarantineEntry Entry    { get; } = e;
    public string OriginalPath      { get; } = e.OriginalPath;
    public string ThreatName        { get; } = e.ThreatName;
    public string LevelLabel        { get; } = ScanEngine.ThreatLevelLabel(e.Level);
    public string DateText          { get; } = e.QuarantinedAt.ToString("dd/MM/yyyy HH:mm");
}

public sealed class HistoryViewModel(ScanHistoryEntry e)
{
    public string StartText    { get; } = e.StartedAt.ToString("dd/MM/yyyy HH:mm");
    public string EndText      { get; } = e.FinishedAt.ToString("HH:mm");
    public string ScanType     { get; } = e.ScanType;
    public int    FilesScanned { get; } = e.FilesScanned;
    public int    ThreatsFound { get; } = e.ThreatsFound;
    public string Duration     { get; } = FormatDuration(e.FinishedAt - e.StartedAt);

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s" : $"{ts.Seconds}s";
}

// â”€â”€â”€ ViewModels â€” EDR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class ProcessVm(ProcessSnapshot p)
{
    public string Name      { get; } = p.Name;
    public int    Pid       { get; } = p.Pid;
    public string RiskLabel { get; } = RiskTag(p.Risk);
    public string Memory    { get; } = $"{p.MemoryMB} MB";
    public int    Threads   { get; } = p.Threads;
    public string Mitre     { get; } = p.MitreTechnique ?? "â€”";
    public string Reason    { get; } = p.RiskReason ?? "Normal";

    private static string RiskTag(EdrRisk r) => r switch
    {
        EdrRisk.Critical => "â›” CrÃ­tico",
        EdrRisk.High     => "ðŸ”´ Alto",
        EdrRisk.Medium   => "ðŸŸ  MÃ©dio",
        EdrRisk.Low      => "ðŸŸ¡ Baixo",
        _                => "âœ… Info"
    };
}

public sealed class NetworkVm(NetworkConnection c)
{
    public string LocalAddr  { get; } = c.LocalAddr;
    public string RemoteAddr { get; } = c.RemoteAddr;
    public int    RemotePort { get; } = c.RemotePort;
    public string State      { get; } = c.State;
    public string SuspLabel  { get; } = c.IsSuspicious ? "âš  Sim" : "â€”";
    public string Reason     { get; } = c.SuspiciousReason ?? string.Empty;
}

public sealed class EventVm(SecurityEvent ev)
{
    public string Time        { get; } = ev.Timestamp.ToString("HH:mm:ss");
    public string Source      { get; } = ev.Source;
    public string RiskLabel   { get; } = RiskTag(ev.Risk);
    public string Mitre       { get; } = ev.MitreTechnique ?? "â€”";
    public string Tactic      { get; } = ev.MitreTactic ?? "â€”";
    public string Description { get; } = ev.Description;

    private static string RiskTag(EdrRisk r) => r switch
    {
        EdrRisk.Critical => "â›” CrÃ­tico",
        EdrRisk.High     => "ðŸ”´ Alto",
        EdrRisk.Medium   => "ðŸŸ  MÃ©dio",
        EdrRisk.Low      => "ðŸŸ¡ Baixo",
        _                => "âœ… Info"
    };
}

public sealed class AuditVm(AuditEvent ev)
{
    public string Time        { get; } = ev.Timestamp.ToString("dd/MM HH:mm:ss");
    public string EventType   { get; } = ev.EventType;
    public string Source      { get; } = ev.Source;
    public string Mitre       { get; } = ev.MitreTechnique ?? "â€”";
    public string Confidence  { get; } = ev.Confidence.HasValue ? $"{ev.Confidence}%" : "â€”";
    public string Outcome     { get; } = ev.Outcome;
    public string Description { get; } = ev.Description;
}

