#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Kor.Operations.Financials
{
    /// <summary>Converts a double[] of monthly billings into a PointCollection for a Polyline sparkline.</summary>
    public sealed class SparklineConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double[] data || data.Length < 2)
                return new PointCollection();

            var max = data[0]; var min = data[0];
            foreach (var v in data) { if (v > max) max = v; if (v < min) min = v; }
            var range    = max - min;
            var flatLine = range < 0.001;   // all values equal → draw at vertical midpoint

            const double W = 76, H = 16;
            var pts = new PointCollection(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                var x = i * W / (data.Length - 1);
                var y = flatLine ? H / 2 : H - (data[i] - min) / range * H;
                pts.Add(new Point(x, y));
            }
            return pts;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Scales a 0–1 fraction to a pixel width using ConverterParameter as the max.</summary>
    public sealed class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double pct) return 0.0;
            var max = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var w) ? w : 60.0;
            return Math.Max(0.0, Math.Min(max, pct * max));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Converts a hex color string (e.g. "#FF16A34A") to a SolidColorBrush. Results are cached.</summary>
    public sealed class StringToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter _bc = new();
        private static readonly Dictionary<string, SolidColorBrush> _cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && s.Length > 0)
            {
                if (_cache.TryGetValue(s, out var cached)) return cached;
                try
                {
                    var brush = (SolidColorBrush)_bc.ConvertFromString(s)!;
                    brush.Freeze();
                    _cache[s] = brush;
                    return brush;
                }
                catch { }
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
