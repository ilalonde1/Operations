#nullable enable
using System.Globalization;

namespace Kor.Operations.PMTools;

internal static class HistoricalAnalyticsTooltipText
{
    public static string FeePerHourComparison(double observedPortfolioMedian)
        => "Fee ÷ production hours (Eng + Draft). The gross billing rate. " +
           $"Compared against the observed portfolio median: {observedPortfolioMedian.ToString("C0", CultureInfo.CurrentCulture)}/hr.";
}
