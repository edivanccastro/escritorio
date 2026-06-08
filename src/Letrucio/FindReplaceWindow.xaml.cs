using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Letrucio;

public partial class FindReplaceWindow : Window
{
    private readonly RichTextBox _editor;
    private TextPointer? _searchPosition;

    public FindReplaceWindow(RichTextBox editor)
    {
        InitializeComponent();
        _editor = editor;
    }

    public void SetMode(bool replaceMode)
    {
        ReplaceRow.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButton.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ReplaceAllButton.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        Title = replaceMode ? "Substituir" : "Localizar";
        FindBox.Focus();
    }

    private StringComparison Comparison =>
        MatchCaseBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private void FindNext_OnClick(object sender, RoutedEventArgs e)
    {
        var query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var start = _searchPosition ?? _editor.Document.ContentStart;
        var match = FindFrom(start, query);

        if (match is null && _searchPosition is not null)
        {
            match = FindFrom(_editor.Document.ContentStart, query);
            StatusLabel.Text = match is not null ? "Pesquisa reiniciada do comeÃ§o." : string.Empty;
        }

        if (match is null)
        {
            StatusLabel.Text = "Texto nÃ£o encontrado.";
            _searchPosition = null;
            return;
        }

        _editor.Selection.Select(match.Start, match.End);
        _editor.Focus();
        _searchPosition = match.End;
        if (string.IsNullOrEmpty(StatusLabel.Text) || StatusLabel.Text == "Texto nÃ£o encontrado.")
        {
            StatusLabel.Text = "OcorrÃªncia localizada.";
        }
    }

    private void Replace_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.Selection.IsEmpty &&
            string.Equals(_editor.Selection.Text, FindBox.Text, Comparison))
        {
            _editor.Selection.Text = ReplaceBox.Text;
            _searchPosition = _editor.Selection.End;
        }
        FindNext_OnClick(sender, e);
    }

    private void ReplaceAll_OnClick(object sender, RoutedEventArgs e)
    {
        var query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var count = 0;
        var position = _editor.Document.ContentStart;
        while (FindFrom(position, query) is { } match)
        {
            match.Text = ReplaceBox.Text;
            position = match.End;
            count++;
            if (count > 5000)
            {
                break;
            }
        }

        _searchPosition = null;
        StatusLabel.Text = $"{count} substituiÃ§Ã£o(Ãµes) realizada(s).";
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private TextRange? FindFrom(TextPointer start, string query)
    {
        var current = start;
        while (current is not null)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = current.GetTextInRun(LogicalDirection.Forward);
                var index = runText.IndexOf(query, Comparison);
                if (index >= 0)
                {
                    var matchStart = current.GetPositionAtOffset(index);
                    var matchEnd = matchStart?.GetPositionAtOffset(query.Length);
                    if (matchStart is not null && matchEnd is not null)
                    {
                        return new TextRange(matchStart, matchEnd);
                    }
                }
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }
}

