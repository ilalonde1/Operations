#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Inverts a bool. Used for "enable when not busy" button states on
/// <c>ScoringProfileWindow</c> — XAML's BindingExpression doesn't have a
/// negation operator, so a 6-line converter is the cleanest path.
/// </summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
