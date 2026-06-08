using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Escritorio.Shared;
using Escritorio.Shared.Formats;
using Microsoft.Win32;

namespace Planilson;

public partial class MainWindow : Window
{
    // Dimensoes padrao das celulas da grade
    private const double ColWidth = 64;
    private const double RowHeight = 20;
    // Quantas colunas/linhas extras manter alem da borda visivel
    private const int ColBuffer = 5;
    private const int RowBuffer = 10;
    // Limites praticos da grade
    private const int MaxCols = 1024;
    private const int MaxRows = 100_000;

    private readonly DataTable _table = new();
    private string _currentColumn = "A";
    private int _currentRow = 1;

    // ScrollViewer interno do DataGrid — usado para detectar a posicao do scroll
    private ScrollViewer? _scrollViewer;
    // Evita reentrada durante expansao
    private bool _expanding;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ----------------------------------------------------------------
    // Inicialização — após o layout estar montado
    // ----------------------------------------------------------------

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Calcula quantas colunas/linhas cabem na janela inicial
        var visibleCols = (int)Math.Ceiling(Grid.ActualWidth / ColWidth) + ColBuffer;
        var visibleRows = (int)Math.Ceiling(Grid.ActualHeight / RowHeight) + RowBuffer;

        BuildSheet(Math.Max(visibleCols, 26), Math.Max(visibleRows, 50));

        // Encontra o ScrollViewer interno do DataGrid para monitorar o scroll
        _scrollViewer = FindScrollViewer(Grid);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += ScrollViewer_OnScrollChanged;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv)
            {
                return sv;
            }
            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    // ----------------------------------------------------------------
    // Expansão automática ao rolar
    // ----------------------------------------------------------------

    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_expanding || _scrollViewer is null)
        {
            return;
        }

        _expanding = true;
        try
        {
            ExpandIfNeeded();
        }
        finally
        {
            _expanding = false;
        }
    }

    private void ExpandIfNeeded()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        // ---- Colunas ----
        // Largura visivel + posicao de scroll horizontal = borda direita em pixels
        var rightEdge = _scrollViewer.HorizontalOffset + _scrollViewer.ViewportWidth;
        var totalColWidth = _table.Columns.Count * ColWidth;

        if (totalColWidth - rightEdge < ColBuffer * ColWidth && _table.Columns.Count < MaxCols)
        {
            var toAdd = ColBuffer * 2;
            for (var i = 0; i < toAdd && _table.Columns.Count < MaxCols; i++)
            {
                _table.Columns.Add(GetColumnLabel(_table.Columns.Count), typeof(string));
            }
            // Força reconstrução das colunas do DataGrid (AutoGenerateColumns reage ao DataTable)
            Grid.ItemsSource = null;
            Grid.ItemsSource = _table.DefaultView;
        }

        // ---- Linhas ----
        var bottomEdge = _scrollViewer.VerticalOffset + _scrollViewer.ViewportHeight;
        var totalRowHeight = _table.Rows.Count * RowHeight;

        if (totalRowHeight - bottomEdge < RowBuffer * RowHeight && _table.Rows.Count < MaxRows)
        {
            var toAdd = RowBuffer * 3;
            for (var i = 0; i < toAdd && _table.Rows.Count < MaxRows; i++)
            {
                _table.Rows.Add(new object[_table.Columns.Count]);
            }
        }
    }

    // ----------------------------------------------------------------
    // Criação/recriação da grade
    // ----------------------------------------------------------------

    private void BuildSheet(int columns, int rows)
    {
        _table.Clear();
        _table.Columns.Clear();

        for (var c = 0; c < columns; c++)
        {
            _table.Columns.Add(GetColumnLabel(c), typeof(string));
        }

        for (var r = 0; r < rows; r++)
        {
            _table.Rows.Add(new object[columns]);
        }

        Grid.ItemsSource = _table.DefaultView;
        _currentColumn = "A";
        _currentRow = 1;
        CellNameText.Text = "A1";
        FormulaBox.Text = string.Empty;
        StatusText.Text = "Pronto";
    }

    // ----------------------------------------------------------------
    // Labels de coluna (A, B, …, Z, AA, AB, …)
    // ----------------------------------------------------------------

    private static string GetColumnLabel(int index)
    {
        var value = index + 1;
        var label = string.Empty;
        while (value > 0)
        {
            var rem = (value - 1) % 26;
            label = (char)('A' + rem) + label;
            value = (value - 1) / 26;
        }
        return label;
    }

    // ----------------------------------------------------------------
    // Eventos da grade
    // ----------------------------------------------------------------

    private void Grid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void Grid_OnSelectedCellsChanged(object? sender, SelectedCellsChangedEventArgs e)
    {
        var cell = Grid.SelectedCells.FirstOrDefault();
        if (!cell.IsValid || cell.Item is not DataRowView rowView || cell.Column?.Header is null)
        {
            return;
        }

        _currentColumn = cell.Column.Header.ToString() ?? "A";
        _currentRow = _table.Rows.IndexOf(rowView.Row) + 1;
        CellNameText.Text = $"{_currentColumn}{_currentRow}";
        FormulaBox.Text = rowView[_currentColumn]?.ToString() ?? string.Empty;

        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        double sum = 0;
        var count = 0;
        foreach (var cell in Grid.SelectedCells)
        {
            if (cell.Item is DataRowView rv && cell.Column?.Header is not null)
            {
                var raw = rv[cell.Column.Header.ToString()!]?.ToString();
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                {
                    sum += n;
                    count++;
                }
            }
        }

        SumText.Text = count > 0
            ? $"Soma: {sum.ToString("0.##", CultureInfo.InvariantCulture)}    Média: {(sum / count).ToString("0.##", CultureInfo.InvariantCulture)}    Contagem: {count}"
            : "Soma: 0";
    }

    private void Grid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditingElement is TextBox box && e.Row.Item is DataRowView && e.Column?.Header is not null)
        {
            if (FormulaEngine.IsFormula(box.Text))
            {
                box.Text = FormulaEngine.Evaluate(box.Text, ResolveCell);
            }

            Dispatcher.BeginInvoke(new Action(UpdateSelectionSummary));
        }
    }

    private string? ResolveCell(string column, int row)
    {
        var rowIndex = row - 1;
        if (rowIndex < 0 || rowIndex >= _table.Rows.Count || !_table.Columns.Contains(column))
        {
            return "0";
        }
        return _table.Rows[rowIndex][column]?.ToString();
    }

    private void ApplyFormula_OnClick(object sender, RoutedEventArgs e) => ApplyFormula();

    private void FormulaBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFormula();
        }
    }

    private void ApplyFormula()
    {
        if (!_table.Columns.Contains(_currentColumn))
        {
            return;
        }

        var input = FormulaBox.Text;
        var rowIndex = _currentRow - 1;
        if (rowIndex < 0 || rowIndex >= _table.Rows.Count)
        {
            return;
        }

        _table.Rows[rowIndex][_currentColumn] =
            FormulaEngine.IsFormula(input) ? FormulaEngine.Evaluate(input, ResolveCell) : input;

        Grid.Items.Refresh();
        StatusText.Text = $"Célula {_currentColumn}{_currentRow} atualizada.";
        UpdateSelectionSummary();
    }

    // ----------------------------------------------------------------
    // Arquivo
    // ----------------------------------------------------------------

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var visibleCols = (int)Math.Ceiling(Grid.ActualWidth / ColWidth) + ColBuffer;
        var visibleRows = (int)Math.Ceiling(Grid.ActualHeight / RowHeight) + RowBuffer;
        BuildSheet(Math.Max(visibleCols, 26), Math.Max(visibleRows, 50));

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollToTop();
            _scrollViewer.ScrollToLeftEnd();
        }
    }

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Abrir planilha", Filter = SpreadsheetFormat.OpenFilter };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var parsed = SpreadsheetFormat.Load(dialog.FileName);
            if (parsed.Count == 0)
            {
                New_OnClick(sender, e);
                StatusText.Text = $"Aberto (vazio): {dialog.FileName}";
                return;
            }

            var maxColumns = Math.Max(parsed.Max(c => c.Count), 1);
            var visibleCols = (int)Math.Ceiling(Grid.ActualWidth / ColWidth) + ColBuffer;
            var visibleRows = (int)Math.Ceiling(Grid.ActualHeight / RowHeight) + RowBuffer;
            BuildSheet(Math.Max(maxColumns, Math.Max(visibleCols, 26)),
                       Math.Max(parsed.Count + RowBuffer, Math.Max(visibleRows, 50)));

            for (var r = 0; r < parsed.Count; r++)
            {
                for (var c = 0; c < parsed[r].Count && c < _table.Columns.Count; c++)
                {
                    _table.Rows[r][c] = parsed[r][c];
                }
            }

            Grid.Items.Refresh();
            StatusText.Text = $"Aberto: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o arquivo.\n\n{ex.Message}", "Planílson",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar planilha",
            Filter = SpreadsheetFormat.SaveFilter,
            FileName = "planilha"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SpreadsheetFormat.Save(_table, dialog.FileName);
            StatusText.Text = $"Salvo: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar o arquivo.\n\n{ex.Message}", "Planílson",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ----------------------------------------------------------------
    // Células
    // ----------------------------------------------------------------

    private void AddRow_OnClick(object sender, RoutedEventArgs e)
    {
        _table.Rows.Add(new object[_table.Columns.Count]);
        StatusText.Text = "Linha adicionada.";
    }

    private void AddColumn_OnClick(object sender, RoutedEventArgs e)
    {
        _table.Columns.Add(GetColumnLabel(_table.Columns.Count), typeof(string));
        Grid.ItemsSource = null;
        Grid.ItemsSource = _table.DefaultView;
        StatusText.Text = "Coluna adicionada.";
    }

    private void DeleteRow_OnClick(object sender, RoutedEventArgs e)
    {
        var rows = Grid.SelectedCells
            .Where(c => c.Item is DataRowView)
            .Select(c => ((DataRowView)c.Item).Row)
            .Distinct()
            .ToList();

        if (rows.Count == 0 || _table.Rows.Count - rows.Count < 1)
        {
            StatusText.Text = "Selecione ao menos uma linha (mantendo uma na planilha).";
            return;
        }

        foreach (var row in rows)
        {
            _table.Rows.Remove(row);
        }
        StatusText.Text = $"{rows.Count} linha(s) excluída(s).";
    }

    private void DeleteColumn_OnClick(object sender, RoutedEventArgs e)
    {
        var columns = Grid.SelectedCells
            .Where(c => c.Column?.Header is not null)
            .Select(c => c.Column!.Header.ToString()!)
            .Distinct()
            .ToList();

        if (columns.Count == 0 || _table.Columns.Count - columns.Count < 1)
        {
            StatusText.Text = "Selecione ao menos uma coluna (mantendo uma na planilha).";
            return;
        }

        foreach (var column in columns.Where(c => _table.Columns.Contains(c)))
        {
            _table.Columns.Remove(column);
        }

        Grid.ItemsSource = null;
        Grid.ItemsSource = _table.DefaultView;
        StatusText.Text = $"{columns.Count} coluna(s) excluída(s).";
    }

    private void ClearCell_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var cell in Grid.SelectedCells)
        {
            if (cell.Item is DataRowView rv && cell.Column?.Header is not null)
            {
                rv[cell.Column.Header.ToString()!] = string.Empty;
            }
        }
        Grid.Items.Refresh();
        StatusText.Text = "Células limpas.";
    }

    // ----------------------------------------------------------------
    // Área de transferência
    // ----------------------------------------------------------------

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        var value = FirstSelectedValue();
        if (value is not null)
        {
            Clipboard.SetText(value);
            StatusText.Text = "Conteúdo copiado.";
        }
    }

    private void Cut_OnClick(object sender, RoutedEventArgs e)
    {
        var cell = Grid.SelectedCells.FirstOrDefault();
        if (cell.IsValid && cell.Item is DataRowView rv && cell.Column?.Header is { } header)
        {
            Clipboard.SetText(rv[header.ToString()!]?.ToString() ?? string.Empty);
            rv[header.ToString()!] = string.Empty;
            Grid.Items.Refresh();
            StatusText.Text = "Conteúdo recortado.";
        }
    }

    private void Paste_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText() || !_table.Columns.Contains(_currentColumn))
        {
            return;
        }
        var rowIndex = _currentRow - 1;
        if (rowIndex < 0 || rowIndex >= _table.Rows.Count)
        {
            return;
        }
        _table.Rows[rowIndex][_currentColumn] = Clipboard.GetText();
        Grid.Items.Refresh();
        StatusText.Text = "Conteúdo colado.";
    }

    private string? FirstSelectedValue()
    {
        var cell = Grid.SelectedCells.FirstOrDefault();
        if (cell.IsValid && cell.Item is DataRowView rv && cell.Column?.Header is { } header)
        {
            return rv[header.ToString()!]?.ToString() ?? string.Empty;
        }
        return null;
    }

    // ----------------------------------------------------------------
    // Formato de número
    // ----------------------------------------------------------------

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private void FormatCurrency_OnClick(object sender, RoutedEventArgs e) => ApplyNumberFormat(v => v.ToString("C2", PtBr));
    private void FormatPercent_OnClick(object sender, RoutedEventArgs e) => ApplyNumberFormat(v => v.ToString("0.##%", PtBr));
    private void FormatThousands_OnClick(object sender, RoutedEventArgs e) => ApplyNumberFormat(v => v.ToString("#,##0.##", PtBr));
    private void FormatGeneral_OnClick(object sender, RoutedEventArgs e) => ApplyNumberFormat(v => v.ToString(CultureInfo.InvariantCulture));

    private void ApplyNumberFormat(Func<double, string> formatter)
    {
        var applied = 0;
        foreach (var cell in Grid.SelectedCells)
        {
            if (cell.Item is DataRowView rv && cell.Column?.Header is { } header)
            {
                var raw = rv[header.ToString()!]?.ToString() ?? string.Empty;
                if (TryParseCell(raw, out var value))
                {
                    rv[header.ToString()!] = formatter(value);
                    applied++;
                }
            }
        }
        Grid.Items.Refresh();
        StatusText.Text = applied > 0 ? $"Formato aplicado a {applied} célula(s)." : "Nenhuma célula numérica selecionada.";
    }

    private static bool TryParseCell(string raw, out double value)
    {
        var cleaned = raw.Replace("R$", string.Empty).Replace("%", string.Empty).Trim();
        return double.TryParse(cleaned, NumberStyles.Any, PtBr, out value)
               || double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    // ----------------------------------------------------------------
    // Alinhamento
    // ----------------------------------------------------------------

    private void AlignLeft_OnClick(object sender, RoutedEventArgs e) => SetColumnAlignment(HorizontalAlignment.Left);
    private void AlignCenter_OnClick(object sender, RoutedEventArgs e) => SetColumnAlignment(HorizontalAlignment.Center);
    private void AlignRight_OnClick(object sender, RoutedEventArgs e) => SetColumnAlignment(HorizontalAlignment.Right);

    private void SetColumnAlignment(HorizontalAlignment alignment)
    {
        var columns = Grid.SelectedCells.Select(c => c.Column).Distinct().OfType<DataGridTextColumn>().ToList();
        if (columns.Count == 0)
        {
            StatusText.Text = "Selecione uma célula para alinhar a coluna.";
            return;
        }

        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(HorizontalAlignmentProperty, alignment));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0)));

        foreach (var column in columns)
        {
            column.ElementStyle = style;
        }
        StatusText.Text = "Alinhamento aplicado à coluna.";
    }

    // ----------------------------------------------------------------
    // Funções
    // ----------------------------------------------------------------

    private void FuncSum_OnClick(object sender, RoutedEventArgs e) => GenerateFunction("SOMA");
    private void FuncAverage_OnClick(object sender, RoutedEventArgs e) => GenerateFunction("MÉDIA");
    private void FuncMax_OnClick(object sender, RoutedEventArgs e) => GenerateFunction("MÁXIMO");
    private void FuncMin_OnClick(object sender, RoutedEventArgs e) => GenerateFunction("MÍNIMO");
    private void FuncCount_OnClick(object sender, RoutedEventArgs e) => GenerateFunction("CONT");

    private void GenerateFunction(string function)
    {
        var cells = Grid.SelectedCells
            .Where(c => c.Column?.Header is not null && c.Item is DataRowView)
            .Select(c => (Col: c.Column!.Header.ToString()!, Row: _table.Rows.IndexOf(((DataRowView)c.Item).Row) + 1))
            .ToList();

        if (cells.Count == 0)
        {
            StatusText.Text = "Selecione as células de origem.";
            return;
        }

        var minCol = cells.Min(c => GetColumnIndex(c.Col));
        var maxCol = cells.Max(c => GetColumnIndex(c.Col));
        var minRow = cells.Min(c => c.Row);
        var maxRow = cells.Max(c => c.Row);

        var reference = minCol == maxCol && minRow == maxRow
            ? $"{GetColumnLabel(minCol)}{minRow}"
            : $"{GetColumnLabel(minCol)}{minRow}:{GetColumnLabel(maxCol)}{maxRow}";

        FormulaBox.Text = $"={function}({reference})";
        StatusText.Text = "Fórmula gerada. Selecione a célula de destino e clique em Aplicar.";
    }

    private static int GetColumnIndex(string label)
    {
        var index = 0;
        foreach (var c in label.ToUpperInvariant())
        {
            index = index * 26 + (c - 'A' + 1);
        }
        return index - 1;
    }

    // ----------------------------------------------------------------
    // Ordenação
    // ----------------------------------------------------------------

    private void SortAscending_OnClick(object sender, RoutedEventArgs e) => Sort(ascending: true);
    private void SortDescending_OnClick(object sender, RoutedEventArgs e) => Sort(ascending: false);

    private void Sort(bool ascending)
    {
        var column = Grid.SelectedCells.FirstOrDefault().Column?.Header?.ToString() ?? _currentColumn;
        if (!_table.Columns.Contains(column))
        {
            return;
        }
        _table.DefaultView.Sort = $"[{column}] {(ascending ? "ASC" : "DESC")}";
        StatusText.Text = $"Classificado pela coluna {column} ({(ascending ? "crescente" : "decrescente")}).";
    }
}

