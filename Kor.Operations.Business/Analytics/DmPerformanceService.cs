#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools;

public static class DmPerformanceService
{
    public static List<PmPerformanceSummaryRow> Build(IReadOnlyList<HistoricalProjectRow> visible)
    {
        var groups = visible
            .Where(r => !string.IsNullOrWhiteSpace(r.DraftingManager))
            .GroupBy(r => r.DraftingManager, StringComparer.OrdinalIgnoreCase)
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
                var (avgMonthsToFirst, pctIn6) = PmPerformanceService.ComputeBillingVelocity(rows);

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
}
