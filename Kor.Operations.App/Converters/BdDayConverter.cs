#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using Kor.Operations.App.BusinessDevelopment;

namespace Kor.Operations.Converters;

public sealed class BdDayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DateTimeOffset date => BdDateDisplay.Day(date),
            DateTime date => BdDateDisplay.Day(date),
            _ => "",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
