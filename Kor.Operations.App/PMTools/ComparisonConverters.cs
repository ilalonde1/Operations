#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.PMTools
{
    /// <summary>Returns true if values[0] &lt; values[1] and values[0] &gt; 0.</summary>
    internal sealed class LessThanConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is double a && values[1] is double b && b > 0)
                return a > 0 && a < b;
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Returns true if values[0] &gt; values[1].</summary>
    internal sealed class GreaterThanConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is double a && values[1] is double b)
                return a > b;
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
