#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Kor.Operations.App.Crm;

/// <summary>
/// MultiValueConverter that returns true when values[0] (the bound region name
/// on a tab button) equals values[1] (the VM's <see cref="BdTrackingViewModel.SelectedRegion"/>).
/// Used by the region-tab style's DataTrigger to flip selected tab to the
/// Brand.Primary slate fill.
/// </summary>
public sealed class RegionEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2) return false;
        var tabValue = values[0] as string;
        var selected = values[1] as string;
        return string.Equals(tabValue, selected, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
