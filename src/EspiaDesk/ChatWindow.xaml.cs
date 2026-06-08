using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace EspiaDesk;

public partial class ChatWindow : Window
{
    private readonly Func<string, Task> _sendAction;
    private readonly string _localName;

    public ChatWindow(Func<string, Task> sendAction, string localName)
    {
        InitializeComponent();
        _sendAction = sendAction;
        _localName  = localName;
    }

    public void AddMessage(string sender, string text, bool isLocal)
    {
        var align = isLocal ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var margin = isLocal
            ? new Thickness(40, 2, 4, 2)
            : new Thickness(4,  2, 40, 2);

        var bubble = new Border
        {
            CornerRadius        = new CornerRadius(8),
            Margin              = margin,
            Padding             = new Thickness(10, 6, 10, 6),
            Background          = isLocal
                ? new SolidColorBrush(Color.FromRgb(0x7B, 0x2D, 0x8B))
                : (Brush)FindResource("PanelBrush"),
            HorizontalAlignment = align,
        };

        var sp = new StackPanel();

        if (!isLocal)
        {
            sp.Children.Add(new TextBlock
            {
                Text       = sender,
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0x2D, 0x8B)),
                Margin     = new Thickness(0, 0, 0, 2)
            });
        }

        sp.Children.Add(new TextBlock
        {
            Text         = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = isLocal ? Brushes.White : (Brush)FindResource("ForegroundBrush"),
            FontSize     = 12,
        });

        sp.Children.Add(new TextBlock
        {
            Text                = DateTime.Now.ToString("HH:mm"),
            FontSize            = 9,
            Foreground          = new SolidColorBrush(
                isLocal ? Color.FromArgb(180, 255, 255, 255)
                        : Color.FromRgb(0x88, 0x88, 0x88)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 2, 0, 0)
        });

        bubble.Child = sp;
        MsgPanel.Children.Add(bubble);
        Scroll.ScrollToBottom();
    }

    private async void Send_Click(object s, RoutedEventArgs e) => await DoSend();

    private async void Input_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
        {
            e.Handled = true;
            await DoSend();
        }
    }

    private async Task DoSend()
    {
        var text = TxtInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        TxtInput.Clear();
        AddMessage(_localName, text, isLocal: true);
        try { await _sendAction(text); }
        catch { }
    }
}
