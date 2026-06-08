using System.Collections.ObjectModel;
using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfMsgBox = System.Windows.MessageBox;

namespace ZeFaxina;

public partial class MainWindow : Window
{
    // â”€â”€ Estado â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly CleanEngine _engine = new();
    private CancellationTokenSource? _cts;
    private long _sessionFreed;

    private readonly List<WpfCheckBox> _cleanCheckBoxes = [];
    private List<CleanResultVm> _lastResults = [];

    // ViewModels
    private readonly ObservableCollection<CleanResultVm>  _cleanResults  = [];
    private readonly ObservableCollection<RegIssueVm>     _regIssues     = [];
    private readonly ObservableCollection<StartupVm>      _startupItems  = [];
    private readonly ObservableCollection<ProgramVm>      _programItems  = [];
    private readonly ObservableCollection<ProgramVm>      _programFilter = [];
    private readonly ObservableCollection<HistoryVm>      _historyItems  = [];

    // â”€â”€ Construtor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public MainWindow()
    {
        InitializeComponent();
        CleanResultList.ItemsSource  = _cleanResults;
        RegistryIssueList.ItemsSource = _regIssues;
        StartupList.ItemsSource      = _startupItems;
        ProgramList.ItemsSource      = _programFilter;
        HistoryList.ItemsSource      = _historyItems;
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildCleanPanel();
        RefreshHistory();
    }

    // â”€â”€ Painel de Limpeza â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BuildCleanPanel()
    {
        CleanPanel.Children.Clear();
        _cleanCheckBoxes.Clear();

        var groups = CleanEngine.AllTargets.GroupBy(t => t.Group);
        foreach (var g in groups)
        {
            var header = new System.Windows.Controls.TextBlock { Text = g.Key, Style = (Style)Resources["GroupHeader"] };
            CleanPanel.Children.Add(header);

            foreach (var target in g)
            {
                var cb = new WpfCheckBox
                {
                    Tag     = target.Category,
                    Content = target.Label,
                    IsChecked = target.DefaultOn,
                    Style   = (Style)Resources["CleanCheck"],
                };
                _cleanCheckBoxes.Add(cb);
                CleanPanel.Children.Add(cb);
            }
        }
    }

    private IEnumerable<CleanCategory> SelectedCategories() =>
        _cleanCheckBoxes
            .Where(c => c.IsChecked == true)
            .Select(c => (CleanCategory)c.Tag!);

    // â”€â”€ Analisar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async void Analyze_OnClick(object sender, RoutedEventArgs e)
    {
        var cats = SelectedCategories().ToList();
        if (cats.Count == 0) { WpfMsgBox.Show("Selecione ao menos uma categoria.", "ZÃ© Faxina"); return; }

        SetBusy(true, "Analisandoâ€¦");
        _cleanResults.Clear();
        _lastResults.Clear();
        CleanBtn.IsEnabled = false;
        SummaryFiles.Text = SummarySize.Text = SummaryCategories.Text = "â€¦";

        _cts = new CancellationTokenSource();
        var progress = new Progress<CleanProgress>(p =>
        {
            ProgressLabel.Text  = $"Verificando: {p.CurrentItem}";
            ProgressDetail.Text = $"{p.Done}/{p.Total}";
        });

        try
        {
            var results = await _engine.AnalyzeAsync(cats, progress, _cts.Token);
            _lastResults = results.Select(r => new CleanResultVm(r, "Encontrado")).ToList();
            foreach (var r in _lastResults) _cleanResults.Add(r);

            long totalBytes = results.Sum(r => r.BytesFound);
            int  totalFiles = results.Sum(r => r.FilesFound);
            SummaryFiles.Text      = totalFiles.ToString("N0");
            SummarySize.Text       = CleanEngine.FormatSize(totalBytes);
            SummaryCategories.Text = results.Count(r => r.FilesFound > 0).ToString();

            ProgressLabel.Text  = $"AnÃ¡lise concluÃ­da â€” {CleanEngine.FormatSize(totalBytes)} a liberar.";
            ProgressDetail.Text = "";
            StatusText.Text     = $"AnÃ¡lise: {totalFiles} arquivos, {CleanEngine.FormatSize(totalBytes)}";
            CleanBtn.IsEnabled = totalBytes > 0;
        }
        catch (OperationCanceledException)
        {
            ProgressLabel.Text = "AnÃ¡lise cancelada.";
        }
        catch (Exception ex)
        {
            WpfMsgBox.Show($"Erro durante anÃ¡lise:\n{ex.Message}", "ZÃ© Faxina");
        }
        finally { SetBusy(false, null); }
    }

    // â”€â”€ Limpar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async void Clean_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = ConfirmToggle.IsChecked == true;
        if (confirm)
        {
            long bytes = _lastResults.Sum(r => r.BytesFound);
            var resp = WpfMsgBox.Show(
                $"Isso vai excluir {CleanEngine.FormatSize(bytes)} de arquivos temporÃ¡rios.\n\nDeseja continuar?",
                "ZÃ© Faxina â€” Confirmar Limpeza",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (resp != MessageBoxResult.Yes) return;
        }

        var cats = SelectedCategories().ToList();
        SetBusy(true, "Limpandoâ€¦");
        _cleanResults.Clear();
        CleanBtn.IsEnabled = false;
        SummaryFiles.Text = SummarySize.Text = SummaryCategories.Text = "â€¦";

        _cts = new CancellationTokenSource();
        var progress = new Progress<CleanProgress>(p =>
        {
            ProgressLabel.Text  = $"Limpando: {p.CurrentItem}";
            ProgressDetail.Text = $"{p.Done}/{p.Total}";
        });

        try
        {
            var results = await _engine.CleanAsync(cats, progress, _cts.Token);
            foreach (var r in results.Select(r => new CleanResultVm(r, "Limpo")))
                _cleanResults.Add(r);

            long freed  = results.Sum(r => r.BytesFound);
            int  deleted = results.Sum(r => r.FilesFound);
            _sessionFreed += freed;

            SummaryFiles.Text      = deleted.ToString("N0");
            SummarySize.Text       = CleanEngine.FormatSize(freed);
            SummaryCategories.Text = results.Count(r => r.FilesFound > 0).ToString();

            ProgressLabel.Text  = $"Limpeza concluÃ­da â€” {CleanEngine.FormatSize(freed)} liberados!";
            ProgressDetail.Text = "";
            StatusText.Text     = $"Limpeza: {deleted} arquivos excluÃ­dos, {CleanEngine.FormatSize(freed)} liberados";
            TotalFreedText.Text = $"Total liberado nesta sessÃ£o: {CleanEngine.FormatSize(_sessionFreed)}";

            // Salva no histÃ³rico
            var catNames = string.Join(", ", cats.Select(c =>
                CleanEngine.AllTargets.FirstOrDefault(t => t.Category == c)?.Label ?? c.ToString()));
            CleanEngine.AppendHistory(new CleanHistoryEntry(
                Guid.NewGuid().ToString("N")[..8],
                DateTime.Now, freed, deleted, catNames));
            RefreshHistory();
        }
        catch (OperationCanceledException)
        {
            ProgressLabel.Text = "Limpeza cancelada.";
        }
        catch (Exception ex)
        {
            WpfMsgBox.Show($"Erro durante limpeza:\n{ex.Message}", "ZÃ© Faxina");
        }
        finally { SetBusy(false, null); }
    }

    private void StopClean_OnClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // â”€â”€ Registro â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async void ScanRegistry_OnClick(object sender, RoutedEventArgs e)
    {
        _regIssues.Clear();
        RegProgressBar.Visibility = Visibility.Visible;
        RegResultLabel.Text = "";
        FixRegBtn.IsEnabled = false;

        var progress = new Progress<string>(msg => RegProgressLabel.Text = msg);
        _cts = new CancellationTokenSource();

        try
        {
            var issues = await ToolsEngine.ScanRegistryAsync(progress, _cts.Token);
            foreach (var i in issues)
                _regIssues.Add(new RegIssueVm(i));

            RegResultLabel.Text = issues.Count == 0
                ? "âœ… Nenhum problema encontrado!"
                : $"âš  {issues.Count} problema(s) encontrado(s)";
            FixRegBtn.IsEnabled = issues.Count > 0;
        }
        catch (Exception ex)
        {
            RegResultLabel.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            RegProgressBar.Visibility = Visibility.Collapsed;
            RegProgressLabel.Text = "";
        }
    }

    private void FixRegistry_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = RegistryIssueList.SelectedItems.Cast<RegIssueVm>().ToList();
        if (selected.Count == 0)
        {
            WpfMsgBox.Show("Selecione ao menos um item para corrigir.", "ZÃ© Faxina");
            return;
        }
        var r = WpfMsgBox.Show(
            $"Isso irÃ¡ remover {selected.Count} entrada(s) do registro.\nEssa aÃ§Ã£o nÃ£o pode ser desfeita facilmente.\n\nDeseja continuar?",
            "ZÃ© Faxina â€” Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        foreach (var vm in selected)
        {
            ToolsEngine.FixRegistryIssue(vm.Issue);
            _regIssues.Remove(vm);
        }
        RegResultLabel.Text = $"âœ… {selected.Count} entradas corrigidas.";
        FixRegBtn.IsEnabled = _regIssues.Count > 0;
    }

    // â”€â”€ Ferramentas â€“ InicializaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RefreshStartup_OnClick(object sender, RoutedEventArgs e) => LoadStartup();

    private void LoadStartup()
    {
        _startupItems.Clear();
        foreach (var e in ToolsEngine.GetStartupEntries())
            _startupItems.Add(new StartupVm(e));
        StatusText.Text = $"{_startupItems.Count} entradas de inicializaÃ§Ã£o encontradas.";
    }

    private void DisableStartup_OnClick(object sender, RoutedEventArgs e)
    {
        if (StartupList.SelectedItem is not StartupVm vm) return;
        ToolsEngine.DisableStartupEntry(vm.Entry);
        LoadStartup();
    }

    private void EnableStartup_OnClick(object sender, RoutedEventArgs e)
    {
        if (StartupList.SelectedItem is not StartupVm vm) return;
        ToolsEngine.EnableStartupEntry(vm.Entry);
        LoadStartup();
    }

    // â”€â”€ Ferramentas â€“ Desinstalar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private List<ProgramVm> _allPrograms = [];

    private void RefreshPrograms_OnClick(object sender, RoutedEventArgs e) => LoadPrograms();

    private void LoadPrograms()
    {
        _allPrograms = ToolsEngine.GetInstalledPrograms().Select(p => new ProgramVm(p)).ToList();
        ApplyProgramFilter();
        ProgramCountLabel.Text = $"{_allPrograms.Count} programas instalados";
    }

    private void UninstallSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyProgramFilter();

    private void ApplyProgramFilter()
    {
        var q = UninstallSearch.Text.Trim().ToLowerInvariant();
        _programFilter.Clear();
        foreach (var p in _allPrograms.Where(p => string.IsNullOrEmpty(q) || p.Name.ToLowerInvariant().Contains(q)))
            _programFilter.Add(p);
    }

    private void Uninstall_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProgramList.SelectedItem is not ProgramVm vm) return;
        var r = WpfMsgBox.Show(
            $"Deseja desinstalar:\n{vm.Name} {vm.Version}?",
            "ZÃ© Faxina", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            ToolsEngine.UninstallProgram(vm.Program);
    }

    // â”€â”€ HistÃ³rico â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void RefreshHistory()
    {
        _historyItems.Clear();
        foreach (var h in CleanEngine.LoadHistory())
            _historyItems.Add(new HistoryVm(h));
    }

    private void ClearHistory_OnClick(object sender, RoutedEventArgs e)
    {
        CleanEngine.ClearHistory();
        _historyItems.Clear();
        StatusText.Text = "HistÃ³rico limpo.";
    }

    // â”€â”€ NavegaÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        PageClean.Visibility    = Visibility.Collapsed;
        PageRegistry.Visibility = Visibility.Collapsed;
        PageTools.Visibility    = Visibility.Collapsed;
        PageHistory.Visibility  = Visibility.Collapsed;
        PageOptions.Visibility  = Visibility.Collapsed;

        if (sender == NavClean)    { PageClean.Visibility    = Visibility.Visible; }
        if (sender == NavRegistry) { PageRegistry.Visibility = Visibility.Visible; }
        if (sender == NavTools)    { PageTools.Visibility    = Visibility.Visible; LoadStartupOnce(); }
        if (sender == NavHistory)  { PageHistory.Visibility  = Visibility.Visible; RefreshHistory(); }
        if (sender == NavOptions)  { PageOptions.Visibility  = Visibility.Visible; }
    }

    private bool _startupLoaded;
    private void LoadStartupOnce()
    {
        if (_startupLoaded) return;
        _startupLoaded = true;
        LoadStartup();
        LoadPrograms();
    }

    private void ToolTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ToolPageStartup.Visibility  = Visibility.Collapsed;
        ToolPageUninstall.Visibility = Visibility.Collapsed;
        if (sender == ToolTabStartup)  ToolPageStartup.Visibility  = Visibility.Visible;
        if (sender == ToolTabUninstall) ToolPageUninstall.Visibility = Visibility.Visible;
    }

    // â”€â”€ UtilitÃ¡rios de UI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void SetBusy(bool busy, string? label)
    {
        AnalyzeBtn.IsEnabled       = !busy;
        StopCleanBtn.Visibility    = busy ? Visibility.Visible : Visibility.Collapsed;
        CleanProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (label is not null) ProgressLabel.Text = label;
    }
}

// â”€â”€â”€ ViewModels â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

internal sealed class CleanResultVm
{
    public CleanResultVm(CleanResult r, string status)
    {
        Label      = r.Label;
        BytesFound = r.BytesFound;
        FilesFound = r.FilesFound;
        Group      = CleanEngine.AllTargets.FirstOrDefault(t => t.Category == r.Category)?.Group ?? "";
        FilesText  = r.FilesFound.ToString("N0");
        SizeText   = CleanEngine.FormatSize(r.BytesFound);
        Status     = r.Error is not null ? $"Erro: {r.Error[..Math.Min(r.Error.Length, 30)]}" : status;
    }
    public string Label      { get; }
    public string Group      { get; }
    public long   BytesFound { get; }
    public int    FilesFound { get; }
    public string FilesText  { get; }
    public string SizeText   { get; }
    public string Status     { get; }
}

internal sealed class RegIssueVm
{
    public RegIssueVm(RegistryIssue i) { Issue = i; }
    public RegistryIssue Issue       { get; }
    public string Key                => Issue.Key;
    public string ValueName          => Issue.ValueName;
    public string Description        => Issue.Description;
    public string TypeLabel          => Issue.Type switch
    {
        RegistryIssueType.MissingFileRef     => "Arquivo ausente",
        RegistryIssueType.InvalidStartup     => "InicializaÃ§Ã£o invÃ¡lida",
        RegistryIssueType.OrphanedUninstall  => "Desinstalador Ã³rfÃ£o",
        RegistryIssueType.InvalidFont        => "Fonte invÃ¡lida",
        _                                    => Issue.Type.ToString(),
    };
}

internal sealed class StartupVm
{
    public StartupVm(StartupEntry e) { Entry = e; }
    public StartupEntry Entry  { get; }
    public string Name         => Entry.Name;
    public string Command      => Entry.Command;
    public string Location     => Entry.Location;
    public string StateLabel   => Entry.Enabled ? "Ativado" : "Desativado";
}

internal sealed class ProgramVm
{
    public ProgramVm(InstalledProgram p) { Program = p; }
    public InstalledProgram Program { get; }
    public string Name         => Program.Name;
    public string Version      => Program.Version;
    public string Publisher    => Program.Publisher;
    public string InstallDate  => Program.InstallDate;
    public string SizeText     => Program.SizeBytes > 0 ? CleanEngine.FormatSize(Program.SizeBytes) : "â€”";
}

internal sealed class HistoryVm
{
    public HistoryVm(CleanHistoryEntry h) { Entry = h; }
    public CleanHistoryEntry Entry { get; }
    public string DateText    => Entry.Date.ToString("dd/MM/yyyy  HH:mm");
    public int    FilesDeleted => Entry.FilesDeleted;
    public string FreedText   => CleanEngine.FormatSize(Entry.BytesFreed);
    public string Categories  => Entry.Categories;
}

