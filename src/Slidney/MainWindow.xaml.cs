using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Escritorio.Shared;
using Escritorio.Shared.Formats;
using Microsoft.Win32;

namespace Slidney;

public partial class MainWindow : Window
{
    private static readonly string[] Palette =
    {
        "#C43E1C", "#1F3864", "#217346", "#5B2C6F", "#2E2E2E", "#0B5394", "#A6792B"
    };

    private static readonly string[] TextColors =
    {
        "#FFFFFF", "#000000", "#FFD966", "#FFC000", "#A9D18E", "#9DC3E6", "#F4B183", "#D9D9D9"
    };

    private readonly ObservableCollection<SlideModel> _slides = new();
    private bool _isLoading;
    private int _paletteIndex;

    public MainWindow()
    {
        InitializeComponent();
        SlidesList.ItemsSource = _slides;
        BuildThemeGrid();
        BuildTextColorGrid();
        _slides.Add(new SlideModel { Title = "Bem-vindo", Content = "Sua primeira apresentação no Slidney." });
        SlidesList.SelectedIndex = 0;
        UpdateStatus();
    }

    private void BuildThemeGrid()
    {
        foreach (var hex in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex
            };
            var captured = hex;
            swatch.Click += (_, _) =>
            {
                if (Current is not null)
                {
                    Current.Background = captured;
                    StatusText.Text = "Tema do slide aplicado.";
                }
            };
            ThemeGrid.Children.Add(swatch);
        }
    }

    private void BuildTextColorGrid()
    {
        foreach (var hex in TextColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex
            };
            var captured = hex;
            swatch.Click += (_, _) =>
            {
                if (Current is not null)
                {
                    Current.Foreground = captured;
                }
                TextColorPopup.IsOpen = false;
            };
            TextColorGrid.Children.Add(swatch);
        }
    }

    private SlideModel? Current => SlidesList.SelectedItem as SlideModel;

    private void SlidesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Current is null)
        {
            return;
        }

        _isLoading = true;
        SlideTitleBox.Text = Current.Title;
        SlideContentBox.Text = Current.Content;
        BoldToggle.IsChecked = Current.Bold;
        _isLoading = false;
        UpdateStatus();
    }

    private void SlideField_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || Current is null)
        {
            return;
        }

        Current.Title = SlideTitleBox.Text;
        Current.Content = SlideContentBox.Text;
    }

    private void AddSlide_OnClick(object sender, RoutedEventArgs e)
    {
        var slide = new SlideModel
        {
            Title = $"Slide {_slides.Count + 1}",
            Content = "Clique para editar o conteúdo.",
            Background = Palette[_slides.Count % Palette.Length]
        };
        _slides.Add(slide);
        SlidesList.SelectedItem = slide;
        StatusText.Text = "Slide adicionado.";
        UpdateStatus();
    }

    private void DuplicateSlide_OnClick(object sender, RoutedEventArgs e)
    {
        if (Current is null)
        {
            return;
        }

        var clone = new SlideModel
        {
            Title = $"{Current.Title} (cópia)",
            Content = Current.Content,
            Background = Current.Background
        };
        _slides.Insert(SlidesList.SelectedIndex + 1, clone);
        SlidesList.SelectedItem = clone;
        StatusText.Text = "Slide duplicado.";
        UpdateStatus();
    }

    private void RemoveSlide_OnClick(object sender, RoutedEventArgs e)
    {
        if (_slides.Count <= 1 || Current is null)
        {
            StatusText.Text = "A apresentação precisa de pelo menos um slide.";
            return;
        }

        var index = SlidesList.SelectedIndex;
        _slides.Remove(Current);
        SlidesList.SelectedIndex = Math.Max(0, index - 1);
        StatusText.Text = "Slide removido.";
        UpdateStatus();
    }

    private void SlideColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (Current is null)
        {
            return;
        }

        _paletteIndex = (_paletteIndex + 1) % Palette.Length;
        Current.Background = Palette[_paletteIndex];
        StatusText.Text = "Cor do slide alterada.";
    }

    private void MoveUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SlidesList.SelectedIndex;
        if (index > 0)
        {
            _slides.Move(index, index - 1);
            SlidesList.SelectedIndex = index - 1;
            StatusText.Text = "Slide movido para cima.";
            UpdateStatus();
        }
    }

    private void MoveDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SlidesList.SelectedIndex;
        if (index >= 0 && index < _slides.Count - 1)
        {
            _slides.Move(index, index + 1);
            SlidesList.SelectedIndex = index + 1;
            StatusText.Text = "Slide movido para baixo.";
            UpdateStatus();
        }
    }

    private void LayoutBoth_OnClick(object sender, RoutedEventArgs e) => SetLayout(0);
    private void LayoutTitle_OnClick(object sender, RoutedEventArgs e) => SetLayout(1);
    private void LayoutBlank_OnClick(object sender, RoutedEventArgs e) => SetLayout(2);

    private void SetLayout(int layout)
    {
        if (Current is not null)
        {
            Current.Layout = layout;
            StatusText.Text = "Layout do slide alterado.";
        }
    }

    private void Bold_OnClick(object sender, RoutedEventArgs e)
    {
        if (Current is not null)
        {
            Current.Bold = BoldToggle.IsChecked == true;
        }
    }

    private void GrowFont_OnClick(object sender, RoutedEventArgs e)
    {
        if (Current is not null)
        {
            Current.ContentFontSize = Math.Min(96, Current.ContentFontSize + 2);
            Current.TitleFontSize = Math.Min(120, Current.TitleFontSize + 2);
        }
    }

    private void ShrinkFont_OnClick(object sender, RoutedEventArgs e)
    {
        if (Current is not null)
        {
            Current.ContentFontSize = Math.Max(8, Current.ContentFontSize - 2);
            Current.TitleFontSize = Math.Max(12, Current.TitleFontSize - 2);
        }
    }

    private void TextColor_OnClick(object sender, RoutedEventArgs e) => TextColorPopup.IsOpen = true;

    private void AlignLeft_OnClick(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Left);
    private void AlignCenter_OnClick(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Center);
    private void AlignRight_OnClick(object sender, RoutedEventArgs e) => SetAlignment(TextAlignment.Right);

    private void SetAlignment(TextAlignment alignment)
    {
        if (Current is not null)
        {
            Current.ContentAlignment = alignment;
            StatusText.Text = "Alinhamento aplicado.";
        }
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        _slides.Clear();
        _slides.Add(new SlideModel { Title = "Bem-vindo", Content = "Nova apresentação." });
        SlidesList.SelectedIndex = 0;
        StatusText.Text = "Nova apresentação.";
        UpdateStatus();
    }

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Abrir apresentação", Filter = PresentationFormat.OpenFilter };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var slides = PresentationFormat.Load(dialog.FileName);
            if (slides.Count > 0)
            {
                _slides.Clear();
                for (var i = 0; i < slides.Count; i++)
                {
                    _slides.Add(new SlideModel
                    {
                        Title = slides[i].Title,
                        Content = slides[i].Content,
                        Background = string.IsNullOrWhiteSpace(slides[i].Background)
                            ? Palette[i % Palette.Length]
                            : slides[i].Background
                    });
                }
                SlidesList.SelectedIndex = 0;
                StatusText.Text = $"Aberto: {dialog.FileName}";
                UpdateStatus();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o arquivo.\n{ex.Message}", "Slidney",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar apresentação",
            Filter = PresentationFormat.SaveFilter,
            FileName = "apresentacao"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var slides = _slides.Select(s => new SlideData
            {
                Title = s.Title,
                Content = s.Content,
                Background = s.Background
            }).ToList();

            PresentationFormat.Save(slides, dialog.FileName);
            StatusText.Text = $"Salvo: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar o arquivo.\n{ex.Message}", "Slidney",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Present_OnClick(object sender, RoutedEventArgs e) =>
        StartPresentation(Math.Max(0, SlidesList.SelectedIndex));

    private void PresentFromStart_OnClick(object sender, RoutedEventArgs e) => StartPresentation(0);

    private void StartPresentation(int startIndex)
    {
        if (_slides.Count == 0)
        {
            return;
        }

        var window = new PresentationWindow(_slides, startIndex) { Owner = this };
        window.ShowDialog();
        StatusText.Text = "Apresentação encerrada.";
    }

    private void UpdateStatus()
    {
        var index = SlidesList.SelectedIndex + 1;
        SlideCountText.Text = $"Slide {Math.Max(index, 1)} de {_slides.Count}";
    }
}
