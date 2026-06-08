using System.Windows;

namespace Letrucio;

public partial class InsertTableWindow : Window
{
    public int Rows { get; private set; } = 3;
    public int Columns { get; private set; } = 3;

    public InsertTableWindow()
    {
        InitializeComponent();
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        Columns = ParseClamp(ColumnsBox.Text, 1, 20, 3);
        Rows = ParseClamp(RowsBox.Text, 1, 100, 3);
        DialogResult = true;
    }

    private static int ParseClamp(string text, int min, int max, int fallback)
    {
        if (!int.TryParse(text, out var value))
        {
            value = fallback;
        }
        return Math.Clamp(value, min, max);
    }
}

