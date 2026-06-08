using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Escritorio.Shared.Formats;
using Microsoft.Win32;

namespace Letrucio;

public partial class MainWindow : Window
{
    private bool _isSyncing;
    private bool _formatPainterArmed;
    private readonly Dictionary<DependencyProperty, object> _painterFormat = new();

    private static readonly Color[] ColorPalette =
    {
        Colors.Black, (Color)ColorConverter.ConvertFromString("#404040"), (Color)ColorConverter.ConvertFromString("#7F7F7F"),
        (Color)ColorConverter.ConvertFromString("#BFBFBF"), Colors.White, (Color)ColorConverter.ConvertFromString("#C00000"),
        Colors.Red, (Color)ColorConverter.ConvertFromString("#FFC000"), Colors.Yellow, (Color)ColorConverter.ConvertFromString("#92D050"),
        (Color)ColorConverter.ConvertFromString("#00B050"), (Color)ColorConverter.ConvertFromString("#00B0F0"),
        (Color)ColorConverter.ConvertFromString("#0070C0"), (Color)ColorConverter.ConvertFromString("#002060"),
        (Color)ColorConverter.ConvertFromString("#7030A0"), (Color)ColorConverter.ConvertFromString("#A52A2A"),
        Colors.DarkOrange, (Color)ColorConverter.ConvertFromString("#1F3864"), (Color)ColorConverter.ConvertFromString("#548235"),
        (Color)ColorConverter.ConvertFromString("#BF8F00"),
    };

    private static readonly Color[] HighlightPalette =
    {
        Colors.Yellow, (Color)ColorConverter.ConvertFromString("#00FF00"), Colors.Cyan, Colors.Magenta,
        (Color)ColorConverter.ConvertFromString("#FF9900"), (Color)ColorConverter.ConvertFromString("#FF66CC"),
        (Color)ColorConverter.ConvertFromString("#C0C0C0"), (Color)ColorConverter.ConvertFromString("#99CCFF"),
    };

    private static readonly string[] Symbols =
    {
        "©", "®", "™", "§", "¶", "•", "·", "…", "—", "–",
        "€", "£", "¢", "¥", "°", "±", "×", "÷", "≈", "≠",
        "≤", "≥", "√", "∞", "µ", "α", "β", "π", "Ω", "Δ",
        "→", "←", "↑", "↓", "✓", "✗", "★", "☆", "♥", "♦",
        "“", "”", "‘", "’", "«", "»", "¿", "¡", "ª", "º",
    };

    private static readonly Color[] PageColors =
    {
        Colors.White, (Color)ColorConverter.ConvertFromString("#FFF8E7"),
        (Color)ColorConverter.ConvertFromString("#EAF3FB"), (Color)ColorConverter.ConvertFromString("#F0F7F0"),
        (Color)ColorConverter.ConvertFromString("#FBEFF0"), (Color)ColorConverter.ConvertFromString("#2B2B2B"),
    };

    private int _pageColorIndex;

    public MainWindow()
    {
        InitializeComponent();
        PopulateFontControls();
        BuildColorGrid(FontColorGrid, ColorPalette, FontColorChosen);
        BuildColorGrid(HighlightGrid, HighlightPalette, HighlightChosen);
        BuildSymbolGrid();
        Editor.Document = new FlowDocument(
            new Paragraph(new Run("Bem-vindo ao Letrúcio. Comece a escrever o seu documento aqui.")));
        UpdateWordCount();
    }

    private void PopulateFontControls()
    {
        var families = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(name => name)
            .ToList();
        FontFamilyBox.ItemsSource = families;
        FontFamilyBox.SelectedItem = "Calibri";

        FontSizeBox.ItemsSource = new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72 };
        FontSizeBox.SelectedItem = 14;
    }

    private void BuildColorGrid(System.Windows.Controls.Panel panel, IEnumerable<Color> colors, Action<Color> onPick)
    {
        foreach (var color in colors)
        {
            var swatch = new Button
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = color.ToString()
            };
            var captured = color;
            swatch.Click += (_, _) => onPick(captured);
            panel.Children.Add(swatch);
        }
    }

    private void BuildSymbolGrid()
    {
        foreach (var symbol in Symbols)
        {
            var button = new Button
            {
                Content = symbol,
                Width = 30,
                Height = 30,
                Margin = new Thickness(1),
                FontSize = 15,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = symbol;
            button.Click += (_, _) =>
            {
                Editor.CaretPosition.InsertTextInRun(captured);
                SymbolPopup.IsOpen = false;
                Editor.Focus();
            };
            SymbolGrid.Children.Add(button);
        }
    }

    // ---------- Arquivo ----------

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        Editor.Document = new FlowDocument(new Paragraph(new Run(string.Empty)));
        StatusText.Text = "Novo documento criado.";
        UpdateWordCount();
    }

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir documento",
            Filter = TextDocumentFormat.OpenFilter
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Editor.Document = TextDocumentFormat.Load(dialog.FileName);
            StatusText.Text = $"Aberto: {dialog.FileName}";
            UpdateWordCount();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o arquivo.\n\n{ex.Message}", "Letrúcio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => SaveDocument();

    private void SaveAs_OnClick(object sender, RoutedEventArgs e) => SaveDocument();

    private void SaveDocument()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar documento",
            Filter = TextDocumentFormat.SaveFilter,
            FileName = "documento"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            TextDocumentFormat.Save(Editor.Document, dialog.FileName);
            StatusText.Text = $"Salvo: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar o arquivo.\n\n{ex.Message}", "Letrúcio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Print_OnClick(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        var document = Editor.Document;
        var originalWidth = document.PageWidth;
        document.PageWidth = printDialog.PrintableAreaWidth;
        IDocumentPaginatorSource paginator = document;
        printDialog.PrintDocument(paginator.DocumentPaginator, "Documento Letrúcio");
        document.PageWidth = originalWidth;
        StatusText.Text = "Documento enviado para impressão.";
    }

    private void Undo_OnClick(object sender, RoutedEventArgs e) => Editor.Undo();

    private void Redo_OnClick(object sender, RoutedEventArgs e) => Editor.Redo();

    // ---------- Fonte ----------

    private void Bold_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;
        EditingCommands.ToggleBold.Execute(null, Editor);
        Editor.Focus();
    }

    private void Italic_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;
        EditingCommands.ToggleItalic.Execute(null, Editor);
        Editor.Focus();
    }

    private void Underline_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;
        EditingCommands.ToggleUnderline.Execute(null, Editor);
        Editor.Focus();
    }

    private void Strikethrough_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSyncing) return;

        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var hasStrike = current is not null &&
                        current.Any(d => d.Location == TextDecorationLocation.Strikethrough);

        var updated = new TextDecorationCollection();
        if (current is not null)
        {
            foreach (var d in current.Where(d => d.Location != TextDecorationLocation.Strikethrough))
            {
                updated.Add(d);
            }
        }
        if (!hasStrike)
        {
            updated.Add(TextDecorations.Strikethrough[0]);
        }

        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, updated);
        Editor.Focus();
    }

    private void Subscript_OnClick(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);

    private void Superscript_OnClick(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);

    private void ToggleBaseline(BaselineAlignment target)
    {
        if (_isSyncing) return;
        var current = Editor.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
        var next = current is BaselineAlignment b && b == target ? BaselineAlignment.Baseline : target;
        Editor.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, next);
        Editor.Focus();
    }

    private void GrowFont_OnClick(object sender, RoutedEventArgs e)
    {
        EditingCommands.IncreaseFontSize.Execute(null, Editor);
        Editor.Focus();
    }

    private void ShrinkFont_OnClick(object sender, RoutedEventArgs e)
    {
        EditingCommands.DecreaseFontSize.Execute(null, Editor);
        Editor.Focus();
    }

    private void FontColor_OnClick(object sender, RoutedEventArgs e) => FontColorPopup.IsOpen = true;

    private void FontColorChosen(Color color)
    {
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
        FontColorPopup.IsOpen = false;
        Editor.Focus();
    }

    private void Highlight_OnClick(object sender, RoutedEventArgs e) => HighlightPopup.IsOpen = true;

    private void HighlightChosen(Color color)
    {
        Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(color));
        HighlightPopup.IsOpen = false;
        Editor.Focus();
    }

    private void HighlightNone_OnClick(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        HighlightPopup.IsOpen = false;
        Editor.Focus();
    }

    private void ClearFormatting_OnClick(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Selection;
        selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Black);
        selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
        selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
        selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Calibri"));
        StatusText.Text = "Formatação limpa.";
        Editor.Focus();
    }

    private void ChangeCase_OnClick(object sender, RoutedEventArgs e) => CasePopup.IsOpen = true;

    private void CaseUpper_OnClick(object sender, RoutedEventArgs e) => ApplyCase(t => t.ToUpper(CultureInfo.CurrentCulture));

    private void CaseLower_OnClick(object sender, RoutedEventArgs e) => ApplyCase(t => t.ToLower(CultureInfo.CurrentCulture));

    private void CaseTitle_OnClick(object sender, RoutedEventArgs e) =>
        ApplyCase(t => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t.ToLower(CultureInfo.CurrentCulture)));

    private void ApplyCase(Func<string, string> transform)
    {
        CasePopup.IsOpen = false;
        if (Editor.Selection.IsEmpty)
        {
            StatusText.Text = "Selecione um texto para alterar a caixa.";
            return;
        }
        Editor.Selection.Text = transform(Editor.Selection.Text);
        Editor.Focus();
    }

    // ---------- Parágrafo ----------

    private void LineSpacing_OnClick(object sender, RoutedEventArgs e) => SpacingPopup.IsOpen = true;

    private void SpacingOption_OnClick(object sender, RoutedEventArgs e)
    {
        SpacingPopup.IsOpen = false;
        if (sender is not Button { Tag: string tag } ||
            !double.TryParse(tag, NumberStyles.Any, CultureInfo.InvariantCulture, out var multiplier))
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(Block.LineHeightProperty, 18.0 * multiplier);
        Editor.Selection.ApplyPropertyValue(Block.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        StatusText.Text = $"Espaçamento entre linhas: {multiplier:0.##}.";
        Editor.Focus();
    }

    // ---------- Estilos ----------

    private void StyleNormal_OnClick(object sender, RoutedEventArgs e) =>
        ApplyStyle(14, FontWeights.Normal, Brushes.Black);

    private void StyleHeading1_OnClick(object sender, RoutedEventArgs e) =>
        ApplyStyle(26, FontWeights.Bold, AccentBrush());

    private void StyleHeading2_OnClick(object sender, RoutedEventArgs e) =>
        ApplyStyle(20, FontWeights.SemiBold, AccentBrush());

    private void ApplyStyle(double size, FontWeight weight, Brush foreground)
    {
        var selection = Editor.Selection;
        selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        selection.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
        selection.ApplyPropertyValue(TextElement.ForegroundProperty, foreground);
        Editor.Focus();
    }

    private Brush AccentBrush() =>
        TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;

    // ---------- Edição ----------

    private void Find_OnClick(object sender, RoutedEventArgs e) => OpenFindReplace(false);

    private void Replace_OnClick(object sender, RoutedEventArgs e) => OpenFindReplace(true);

    private FindReplaceWindow? _findWindow;

    private void OpenFindReplace(bool replaceMode)
    {
        if (_findWindow is null || !_findWindow.IsLoaded)
        {
            _findWindow = new FindReplaceWindow(Editor) { Owner = this };
            _findWindow.Closed += (_, _) => _findWindow = null;
        }
        _findWindow.SetMode(replaceMode);
        _findWindow.Show();
        _findWindow.Activate();
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
        Editor.Focus();
    }

    // ---------- Inserir ----------

    private void InsertTable_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertTableWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0) };
        for (var c = 0; c < dialog.Columns; c++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        for (var r = 0; r < dialog.Rows; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < dialog.Columns; c++)
            {
                var cell = new TableCell(new Paragraph(new Run(string.Empty)))
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);

        var caretPara = Editor.CaretPosition.Paragraph;
        if (caretPara is not null)
        {
            Editor.Document.Blocks.InsertAfter(caretPara, table);
        }
        else
        {
            Editor.Document.Blocks.Add(table);
        }

        StatusText.Text = $"Tabela {dialog.Rows}x{dialog.Columns} inserida.";
        Editor.Focus();
    }

    private void InsertImage_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Inserir imagem",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var bitmap = new BitmapImage(new Uri(dialog.FileName));
        var image = new Image { Source = bitmap, Width = Math.Min(400, bitmap.PixelWidth), Stretch = Stretch.Uniform };
        var container = new InlineUIContainer(image, Editor.CaretPosition);
        Editor.CaretPosition = container.ElementEnd;
        StatusText.Text = "Imagem inserida.";
    }

    private void Symbol_OnClick(object sender, RoutedEventArgs e) => SymbolPopup.IsOpen = true;

    private void InsertDate_OnClick(object sender, RoutedEventArgs e)
    {
        Editor.CaretPosition.InsertTextInRun(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
        StatusText.Text = "Data inserida.";
        UpdateWordCount();
    }

    private void InsertPageBreak_OnClick(object sender, RoutedEventArgs e)
    {
        var caretPara = Editor.CaretPosition.Paragraph;
        var newParagraph = new Paragraph(new Run(string.Empty)) { BreakPageBefore = true };
        if (caretPara is not null)
        {
            Editor.Document.Blocks.InsertAfter(caretPara, newParagraph);
        }
        else
        {
            Editor.Document.Blocks.Add(newParagraph);
        }
        Editor.CaretPosition = newParagraph.ContentStart;
        StatusText.Text = "Quebra de página inserida.";
        Editor.Focus();
    }

    // ---------- Layout ----------

    private void PageColor_OnClick(object sender, RoutedEventArgs e)
    {
        _pageColorIndex = (_pageColorIndex + 1) % PageColors.Length;
        var color = PageColors[_pageColorIndex];
        PageBorder.Background = new SolidColorBrush(color);
        Editor.Foreground = color.R + color.G + color.B < 240 ? Brushes.White : Brushes.Black;
        StatusText.Text = "Cor da página alterada.";
    }

    // ---------- Revisão ----------

    private void WordCount_OnClick(object sender, RoutedEventArgs e)
    {
        var count = CountWords();
        MessageBox.Show(this, $"O documento contém {count} palavra(s).", "Contar Palavras",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SpellCheck_OnClick(object sender, RoutedEventArgs e)
    {
        Editor.SpellCheck.IsEnabled = SpellToggle.IsChecked == true;
        StatusText.Text = Editor.SpellCheck.IsEnabled
            ? "Verificação ortográfica ativada."
            : "Verificação ortográfica desativada.";
    }

    // ---------- Exibir / Zoom ----------

    private void ZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomTransform is null)
        {
            return;
        }
        var scale = e.NewValue / 100.0;
        ZoomTransform.ScaleX = scale;
        ZoomTransform.ScaleY = scale;
        ZoomText.Text = $"{e.NewValue:0}%";
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) =>
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 10);

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) =>
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 10);

    private void ZoomReset_OnClick(object sender, RoutedEventArgs e) => ZoomSlider.Value = 100;

    // ---------- Pincel de Formatação ----------

    private void FormatPainter_OnClick(object sender, RoutedEventArgs e)
    {
        if (FormatPainterToggle.IsChecked == true)
        {
            _painterFormat.Clear();
            foreach (var property in new DependencyProperty[]
                     {
                         TextElement.FontFamilyProperty, TextElement.FontSizeProperty,
                         TextElement.FontWeightProperty, TextElement.FontStyleProperty,
                         TextElement.ForegroundProperty, TextElement.BackgroundProperty,
                         Inline.TextDecorationsProperty, Inline.BaselineAlignmentProperty
                     })
            {
                var value = Editor.Selection.GetPropertyValue(property);
                if (value != DependencyProperty.UnsetValue)
                {
                    _painterFormat[property] = value;
                }
            }
            _formatPainterArmed = true;
            StatusText.Text = "Pincel de formatação: selecione o texto de destino.";
        }
        else
        {
            _formatPainterArmed = false;
        }
    }

    // ---------- Sincronização de estado ----------

    private void Editor_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_formatPainterArmed && !Editor.Selection.IsEmpty && _painterFormat.Count > 0)
        {
            foreach (var (property, value) in _painterFormat)
            {
                Editor.Selection.ApplyPropertyValue(property, value);
            }
            _formatPainterArmed = false;
            FormatPainterToggle.IsChecked = false;
            StatusText.Text = "Formatação aplicada.";
        }

        SyncFormattingState();
        UpdateWordCount();
    }

    private void SyncFormattingState()
    {
        _isSyncing = true;
        try
        {
            BoldToggle.IsChecked = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight
                                   && weight == FontWeights.Bold;
            ItalicToggle.IsChecked = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style
                                     && style == FontStyles.Italic;

            var decorations = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            UnderlineToggle.IsChecked = decorations is not null &&
                                        decorations.Any(d => d.Location == TextDecorationLocation.Underline);
            StrikeToggle.IsChecked = decorations is not null &&
                                     decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

            var baseline = Editor.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
            SubToggle.IsChecked = baseline is BaselineAlignment.Subscript;
            SupToggle.IsChecked = baseline is BaselineAlignment.Superscript;

            if (Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family)
            {
                FontFamilyBox.SelectedItem = family.Source;
            }

            if (Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size)
            {
                FontSizeBox.Text = size.ToString("0");
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void FontFamilyBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing || FontFamilyBox.SelectedItem is not string family)
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(family));
        Editor.Focus();
    }

    private void FontSizeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing || FontSizeBox.SelectedItem is null)
        {
            return;
        }

        if (double.TryParse(FontSizeBox.SelectedItem.ToString(), out var size))
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            Editor.Focus();
        }
    }

    private int CountWords()
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private void UpdateWordCount()
    {
        WordCountText.Text = $"Palavras: {CountWords()}";
    }
}

