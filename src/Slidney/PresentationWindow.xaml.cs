using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Slidney;

public partial class PresentationWindow : Window
{
    private readonly ObservableCollection<SlideModel> _slides;
    private int _index;

    public PresentationWindow(ObservableCollection<SlideModel> slides, int startIndex)
    {
        InitializeComponent();
        _slides = slides;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, slides.Count - 1));
        Render();
    }

    private void Render()
    {
        if (_slides.Count == 0)
        {
            return;
        }

        var slide = _slides[_index];
        TitleText.Text = slide.Title;
        ContentText.Text = slide.Content;
        SlideSurface.Background = slide.BackgroundBrush;
        PagerText.Text = $"{_index + 1} / {_slides.Count}";
    }

    private void Next()
    {
        if (_index < _slides.Count - 1)
        {
            _index++;
            Render();
        }
    }

    private void Previous()
    {
        if (_index > 0)
        {
            _index--;
            Render();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.PageDown:
                Next();
                break;
            case Key.Left:
            case Key.Up:
            case Key.PageUp:
                Previous();
                break;
        }
    }

    private void Window_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e) => Close();
}
