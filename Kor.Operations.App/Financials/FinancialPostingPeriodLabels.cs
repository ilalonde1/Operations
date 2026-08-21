#nullable enable
using System;
using System.Globalization;
using System.Windows;

namespace Kor.Operations.Financials;

internal static class FinancialPostingPeriodLabels
{
    public static string DeltekPostedThrough(DateTime? maxPostedPeriod, bool normalCloseLagAtTwoMonths = false)
    {
        var postedMonth = MonthStart(maxPostedPeriod);
        if (!postedMonth.HasValue) return string.Empty;

        var lag = MonthLag(postedMonth.Value);
        if (lag <= 1) return string.Empty;

        var postedLabel = postedMonth.Value.ToString("MMM yyyy", CultureInfo.CurrentCulture);
        return normalCloseLagAtTwoMonths && lag == 2
            ? $"Deltek posted through {postedLabel} — normal close lag."
            : $"Deltek posted through {postedLabel} — figures may reflect a {lag}-month posting lag.";
    }

    public static string GlPostedThrough(int? maxPostedPeriod)
    {
        var postedMonth = MonthStart(maxPostedPeriod);
        if (!postedMonth.HasValue) return string.Empty;

        var lag = MonthLag(postedMonth.Value);
        return lag <= 1
            ? string.Empty
            : $"GL posted through {postedMonth.Value.ToString("MMM yyyy", CultureInfo.CurrentCulture)} — months after that have no posted data yet.";
    }

    public static string BilledThrough(int? maxBilledPeriod)
    {
        var postedMonth = MonthStart(maxBilledPeriod);
        return postedMonth.HasValue
            ? $"Billed through {postedMonth.Value.ToString("MMM yyyy", CultureInfo.CurrentCulture)}."
            : string.Empty;
    }

    public static Visibility VisibleWhenPresent(string label)
        => string.IsNullOrWhiteSpace(label) ? Visibility.Collapsed : Visibility.Visible;

    private static DateTime? MonthStart(DateTime? period)
        => period.HasValue ? new DateTime(period.Value.Year, period.Value.Month, 1) : null;

    private static DateTime? MonthStart(int? yyyymm)
    {
        if (!yyyymm.HasValue) return null;
        var year = yyyymm.Value / 100;
        var month = yyyymm.Value % 100;
        return month >= 1 && month <= 12 && year is >= 1990 and <= 2100
            ? new DateTime(year, month, 1)
            : null;
    }

    private static int MonthLag(DateTime postedMonth)
    {
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return (currentMonth.Year - postedMonth.Year) * 12 + currentMonth.Month - postedMonth.Month;
    }
}
