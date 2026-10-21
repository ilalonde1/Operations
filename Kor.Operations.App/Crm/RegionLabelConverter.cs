#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Kor.Operations.App.Crm;

/// <summary>
/// A region as people read it: the tracker's keys ("Vancouver/LowerMainland", "Okanagan-BcInterior", "VancouverIsland") with
/// their words spaced ("Vancouver / Lower Mainland", "Okanagan / BC Interior", "Vancouver Island"). Display only; the key is
/// still what filters.
/// </summary>
public sealed class RegionLabelConverter : IValueConverter
{
    public static readonly RegionLabelConverter Instance = new();

    public static string Label(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return "";
        var t = Regex.Replace(region, "(?<=[a-z])(?=[A-Z])", " ");
        t = Regex.Replace(t, @"\s*[/-]\s*", " / ");
        return Regex.Replace(t, @"\bBc\b", "BC");
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Label(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
