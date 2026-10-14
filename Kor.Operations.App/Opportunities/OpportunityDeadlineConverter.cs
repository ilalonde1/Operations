#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.Opportunities;

/// <summary>Read-only deadline presentation for historical and competition grids.</summary>
public sealed class OpportunityDeadlineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => OpportunityDeadlineDisplay.Date(value is DateTimeOffset deadline ? deadline : null);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
