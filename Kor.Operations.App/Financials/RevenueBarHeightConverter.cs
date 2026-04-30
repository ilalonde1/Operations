#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// Multi-value converter that scales a revenue value to a bar height in pixels.
    /// Inputs: [0] = month revenue (double), [1] = chart max value (double).
    /// Output: pixel height (double) clamped to [1, MaxHeight] when value > 0, else 0.
    /// </summary>
    internal sealed class RevenueBarHeightConverter : IMultiValueConverter
    {
        public double MaxHeight { get; set; } = 180;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return 0.0;
            if (values[0] is not double revenue || values[1] is not double max) return 0.0;
            if (max <= 0 || revenue <= 0) return 0.0;
            var ratio = revenue / max;
            return Math.Max(1.0, ratio * MaxHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
