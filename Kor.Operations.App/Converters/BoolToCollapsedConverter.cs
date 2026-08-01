#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kor.Operations.Converters
{
    /// <summary>
    /// Inverse of BooleanToVisibilityConverter: true → Collapsed, false →
    /// Visible. Round 52 uses it to hide deep-dive grid columns while the
    /// Workload Meeting window is in Meeting Mode.
    /// </summary>
    public sealed class BoolToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
