#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Returns green if the project metric is better than the portfolio average, red if worse.
    /// ConverterParameter: "higher" = higher is better; "lower" = lower is better.
    /// values[0] = project value, values[1] = portfolio average.
    /// </summary>
    public sealed class ComparisonBrushConverter : IMultiValueConverter
    {
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
        private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

        static ComparisonBrushConverter()
        {
            Green.Freeze();
            Red.Freeze();
            Gray.Freeze();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double actual || values[1] is not double avg || avg == 0 || actual == 0)
                return Gray;
            var higherIsBetter = (parameter as string) == "higher";
            var isBetter = higherIsBetter ? actual >= avg : actual <= avg;
            return isBetter ? Green : Red;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
