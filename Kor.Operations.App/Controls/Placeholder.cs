#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Kor.Operations.Controls;

/// <summary>
/// Attached placeholder text for any <see cref="TextBox"/>: a grey hint drawn while the
/// box is empty and unfocused, so a row of boxes says what each one is for without a
/// hover. Before 2026-10-13 the CRM's add-contact and log-activity rows were seven and
/// six unlabelled boxes whose meaning lived only in tooltips ("Clarity is King").
///
/// Usage: <c>controls:Placeholder.Text="Email"</c>. Draws in an adorner, so the theme's
/// TextBox template is untouched.
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.Loaded -= Refresh;
        box.TextChanged -= Refresh;
        box.GotKeyboardFocus -= Refresh;
        box.LostKeyboardFocus -= Refresh;

        if (e.NewValue is string text && text.Length > 0)
        {
            box.Loaded += Refresh;
            box.TextChanged += Refresh;
            box.GotKeyboardFocus += Refresh;
            box.LostKeyboardFocus += Refresh;
            if (box.IsLoaded)
            {
                Refresh(box, EventArgs.Empty);
            }
        }
        else
        {
            Remove(box);
        }
    }

    private static void Refresh(object? sender, EventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (string.IsNullOrEmpty(box.Text) && !box.IsKeyboardFocusWithin)
        {
            Show(box);
        }
        else
        {
            Remove(box);
        }
    }

    private static void Show(TextBox box)
    {
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer is null || Find(layer, box) is not null)
        {
            return;
        }

        layer.Add(new PlaceholderAdorner(box, GetText(box) ?? string.Empty));
    }

    private static void Remove(TextBox box)
    {
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer is null)
        {
            return;
        }

        var existing = Find(layer, box);
        if (existing is not null)
        {
            layer.Remove(existing);
        }
    }

    private static PlaceholderAdorner? Find(AdornerLayer layer, TextBox box)
    {
        var adorners = layer.GetAdorners(box);
        if (adorners is null)
        {
            return null;
        }

        foreach (var adorner in adorners)
        {
            if (adorner is PlaceholderAdorner p)
            {
                return p;
            }
        }

        return null;
    }

    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly string _text;

        public PlaceholderAdorner(TextBox box, string text) : base(box)
        {
            _text = text;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var box = (TextBox)AdornedElement;
            var formatted = new FormattedText(
                _text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(box.FontFamily, FontStyles.Italic, FontWeights.Normal, box.FontStretch),
                box.FontSize,
                Brushes.Gray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var x = box.BorderThickness.Left + box.Padding.Left + 4;
            var y = Math.Max(0, (box.ActualHeight - formatted.Height) / 2);
            drawingContext.DrawText(formatted, new Point(x, y));
        }
    }
}
