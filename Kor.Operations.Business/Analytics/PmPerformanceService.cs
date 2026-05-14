#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools;

public static class PmPerformanceService
{
    public static List<PmPerformanceSummaryRow> Build(IReadOnlyList<HistoricalProjectRow> visible)
    {
        var groups = visible
            .Where(r => !string.IsNullOrWhiteSpace(r.Pm))
            .GroupBy(r => r.Pm, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToList();
                var comparable = rows.Where(r => r.EstEngBudget > 0 && r.EngHrs > 0).ToList();
                var totalAr = rows.Sum(r => r.ArTotal);
                var ar90 = rows.Sum(r => r.Ar90Plus);
                var healthyCount = rows.Count(r => r.EstEngBudget <= 0 || r.EngHrs <= r.EstEngBudget * AnalyticsThresholds.OverBudgetFactor);
                var clientGroups = rows.Where(r => !string.IsNullOrWhiteSpace(r.ClientId)).GroupBy(r => r.ClientId, StringComparer.OrdinalIgnoreCase).ToList();
                var uniqueClients = clientGroups.Count;
                var repeatClients = clientGroups.Count(cg => cg.Count() >= 2);
                var (avgMonthsToFirst, pctIn6) = ComputeBillingVelocity(rows);

                return new PmPerformanceSummaryRow
                {
                    Pm = g.Key,
                    ProjectCount = rows.Count,
                    TotalFee = rows.Sum(r => r.TotalFee),
                    TotalFeeBilled = rows.Sum(r => r.FeeBilled),
                    TotalUnpostedFeeBilled = rows.Sum(r => r.UnpostedFeeBilled),
                    TotalEngHrs = rows.Sum(r => r.EngHrs),
                    TotalDraftHrs = rows.Sum(r => r.DraftHrs),
                    TotalAllHrs = rows.Sum(r => r.TotalAllHrs),
                    TotalSubCost = rows.Sum(r => r.SubCost),
                    TotalArOutstanding = totalAr,
                    TotalAr90Plus = ar90,
                    AvgEngDelta = comparable.Count > 0 ? comparable.Average(r => r.EngBudgetDelta) : 0,
                    AvgDraftDelta = comparable.Count > 0 ? comparable.Average(r => r.DraftBudgetDelta) : 0,
                    DeliveryHealthScore = rows.Count > 0 ? (double)healthyCount / rows.Count * 100 : 100,
                    ArManagementScore = totalAr > 0 ? Math.Max(0, (1.0 - ar90 / totalAr) * 100) : 100,
                    UniqueClients = uniqueClients,
                    RepeatClients = repeatClients,
                    AvgMonthsToFirstBill = avgMonthsToFirst,
                    PctBilledWithin6Months = pctIn6,
                };
            })
            .OrderByDescending(r => r.TotalFee)
            .ToList();

        PerformanceScoring.ScorePmDmGroups(groups);
        return groups;
    }

    internal static (double AvgMonthsToFirst, double PctIn6) ComputeBillingVelocity(List<HistoricalProjectRow> rows)
    {
        var monthsToFirst = new List<double>();
        var totalFee = 0.0;
        var billedIn6 = 0.0;

        foreach (var r in rows)
        {
            if (!r.OpenDate.HasValue || r.RevenueTimeline == null || r.RevenueTimeline.Count == 0 || r.TotalFee <= 0)
                continue;

            var openYear = r.OpenDate.Value.Year;
            var openMonth = r.OpenDate.Value.Month;
            var sixMonthCutoff = r.OpenDate.Value.AddMonths(6);

            var firstRevPeriod = r.RevenueTimeline
                .Where(p => p.Revenue > 0 && p.Period.Length >= 6)
                .OrderBy(p => p.Period)
                .FirstOrDefault();

            if (firstRevPeriod != null && int.TryParse(firstRevPeriod.Period[..4], out var pYr) && int.TryParse(firstRevPeriod.Period[4..6], out var pMo))
            {
                var months = (pYr - openYear) * 12 + (pMo - openMonth);
                if (months >= 0) monthsToFirst.Add(months);
            }

            totalFee += r.TotalFee;
            var openPeriod = $"{openYear}{openMonth:D2}";
            var cutoffPeriod = $"{sixMonthCutoff.Year}{sixMonthCutoff.Month:D2}";
            billedIn6 += r.RevenueTimeline
                .Where(p => p.Revenue > 0
                            && string.CompareOrdinal(p.Period, openPeriod) >= 0
                            && string.CompareOrdinal(p.Period, cutoffPeriod) <= 0)
                .Sum(p => p.Revenue);
        }

        var avgMonths = monthsToFirst.Count > 0 ? monthsToFirst.Average() : 0;
        var pctIn6 = Math.Abs(totalFee) > AnalyticsThresholds.RoundingDollarFloor ? billedIn6 / totalFee : 0;
        return (avgMonths, pctIn6);
    }
}
