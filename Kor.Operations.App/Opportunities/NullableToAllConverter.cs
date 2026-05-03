#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Renders a nullable filter value as either the string "All" (when null)
/// or the value's own ToString() (otherwise). Used by the Opportunities
/// filter-ribbon ComboBoxes so users see "All" instead of an empty row.
/// </summary>
public sealed class NullableToAllConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? "All" : value.ToString() ?? "All";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
