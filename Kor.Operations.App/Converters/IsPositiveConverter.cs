#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.Converters
{
    public sealed class IsPositiveConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                int i => i > 0,
                long l => l > 0,
                float f => f > 0,
                double d => d > 0,
                decimal m => m > 0,
                _ => false,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
