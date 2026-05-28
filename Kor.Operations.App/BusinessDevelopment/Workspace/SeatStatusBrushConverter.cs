#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class SeatStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value as string;
        return status?.Trim().ToLowerInvariant() switch
        {
            "open" or "rotating" => new SolidColorBrush(Color.FromRgb(220, 252, 231)),
            "locked" or "preferred" => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
            _ => new SolidColorBrush(Color.FromRgb(229, 231, 235)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
