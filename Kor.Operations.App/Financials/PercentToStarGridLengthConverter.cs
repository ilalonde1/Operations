#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kor.Operations.Financials
{
    public sealed class PercentToStarGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return new GridLength(Math.Max(0.0, d), GridUnitType.Star);

            if (value is float f)
                return new GridLength(Math.Max(0.0, f), GridUnitType.Star);

            if (value is int i)
                return new GridLength(Math.Max(0.0, i), GridUnitType.Star);

            return new GridLength(0.0, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
