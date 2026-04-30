#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.Financials
{
    /// <summary>True → "Actual", False → "Forecast". Used for the revenue forecast monthly table.</summary>
    internal sealed class BoolToActualLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? "Actual" : "Forecast";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
