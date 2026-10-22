#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using Kor.Operations.App.BusinessDevelopment;

namespace Kor.Operations.Converters;

/// <summary>A stored UPN or login as the person's name ("ilalonde@korstructural.com" reads "Ian Lalonde").</summary>
public sealed class StaffNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => StaffDisplay.Short(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
