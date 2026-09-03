#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kor.Operations.StandardDetails;

// A lightweight full-image viewer for a detail drawing: scroll to zoom, drag to pan, double-click or
// Esc to reset/close. Built entirely in code so it needs no XAML/resource plumbing — the preview art
// is often too small to read in the pane, so this is the "get in close" affordance.
internal sealed class DetailZoomWindow : Window
{
    private readonly Image _image;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScrollViewer _scroller;
    private Point _panStart;
    private double _hOffAtPanStart;
    private double _vOffAtPanStart;
    private bool _panning;

    public DetailZoomWindow(ImageSource source, string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Detail drawing" : title;
        Width = 1120;
        Height = 840;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x2E));

        _image = new Image { Source = source, Stretch = Stretch.None, LayoutTransform = _scale, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            Content = new Border { Padding = new Thickness(24), Child = _image, Background = Brushes.Transparent }
        };

        var hint = new TextBlock
        {
            Text = "Scroll to zoom  ·  drag to pan  ·  double-click to fit  ·  Esc to close",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xCD, 0xD3)),
            FontSize = 12,
            Margin = new Thickness(14, 8, 14, 8)
        };

        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);
        root.Children.Add(_scroller);
        Content = root;

        Loaded += (_, _) => FitToWindow();
        _scroller.PreviewMouseWheel += OnWheel;
        _image.MouseLeftButtonDown += OnPanStart;
        _image.MouseMove += OnPanMove;
        _image.MouseLeftButtonUp += OnPanEnd;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var next = Math.Clamp(_scale.ScaleX * factor, 0.1, 8.0);
        _scale.ScaleX = next;
        _scale.ScaleY = next;
    }

    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FitToWindow();
            e.Handled = true;
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(_scroller);
        _hOffAtPanStart = _scroller.HorizontalOffset;
        _vOffAtPanStart = _scroller.VerticalOffset;
        _image.CaptureMouse();
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(_scroller);
        _scroller.ScrollToHorizontalOffset(_hOffAtPanStart - (p.X - _panStart.X));
        _scroller.ScrollToVerticalOffset(_vOffAtPanStart - (p.Y - _panStart.Y));
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        _image.ReleaseMouseCapture();
    }

    private void FitToWindow()
    {
        if (_image.Source is not BitmapSource bs || bs.PixelWidth == 0 || bs.PixelHeight == 0) return;
        var availW = _scroller.ViewportWidth > 0 ? _scroller.ViewportWidth : Width - 60;
        var availH = _scroller.ViewportHeight > 0 ? _scroller.ViewportHeight : Height - 90;
        var s = Math.Min((availW - 48) / bs.PixelWidth, (availH - 48) / bs.PixelHeight);
        s = Math.Clamp(s, 0.1, 1.0);
        _scale.ScaleX = s;
        _scale.ScaleY = s;
    }
}
