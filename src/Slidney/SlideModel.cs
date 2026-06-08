using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Slidney;

public sealed class SlideModel : INotifyPropertyChanged
{
    private string _title = "Título";
    private string _content = "Conteúdo do slide";
    private string _background = "#C43E1C";
    private double _titleFontSize = 40;
    private double _contentFontSize = 22;
    private bool _bold;
    private string _foreground = "#FFFFFF";
    private TextAlignment _contentAlignment = TextAlignment.Left;
    private int _layout;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Content
    {
        get => _content;
        set => SetField(ref _content, value);
    }

    public string Background
    {
        get => _background;
        set
        {
            if (SetField(ref _background, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public double TitleFontSize
    {
        get => _titleFontSize;
        set => SetField(ref _titleFontSize, value);
    }

    public double ContentFontSize
    {
        get => _contentFontSize;
        set => SetField(ref _contentFontSize, value);
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            if (SetField(ref _bold, value))
            {
                OnPropertyChanged(nameof(ContentFontWeight));
            }
        }
    }

    public string Foreground
    {
        get => _foreground;
        set
        {
            if (SetField(ref _foreground, value))
            {
                OnPropertyChanged(nameof(ForegroundBrush));
            }
        }
    }

    public TextAlignment ContentAlignment
    {
        get => _contentAlignment;
        set => SetField(ref _contentAlignment, value);
    }

    public int Layout
    {
        get => _layout;
        set
        {
            if (SetField(ref _layout, value))
            {
                OnPropertyChanged(nameof(TitleVisibility));
                OnPropertyChanged(nameof(ContentVisibility));
            }
        }
    }

    public FontWeight ContentFontWeight => _bold ? FontWeights.Bold : FontWeights.Normal;

    public Visibility TitleVisibility => _layout == 2 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ContentVisibility => _layout == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush ForegroundBrush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_foreground));
            }
            catch
            {
                return Brushes.White;
            }
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(_background);
                var darker = Color.FromRgb(
                    (byte)(color.R * 0.7),
                    (byte)(color.G * 0.7),
                    (byte)(color.B * 0.7));
                return new LinearGradientBrush(color, darker, 135);
            }
            catch
            {
                return Brushes.SteelBlue;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
