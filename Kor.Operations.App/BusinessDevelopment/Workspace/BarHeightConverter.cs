#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class BarHeightConverter : IMultiValueConverter
{
    public double MaxHeight { get; set; } = 120;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double v || values[1] is not double max)
        {
            return 0.0;
        }

        if (max <= 0 || v <= 0)
        {
            return 0.0;
        }

        return Math.Max(1.0, v / max * MaxHeight);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
