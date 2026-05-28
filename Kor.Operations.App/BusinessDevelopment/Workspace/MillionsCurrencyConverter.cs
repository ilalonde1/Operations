#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class MillionsCurrencyConverter : IValueConverter
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double millions)
        {
            return string.Empty;
        }

        return string.Format(CanadianCulture, "{0:C0}", (decimal)millions * 1_000_000m);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
