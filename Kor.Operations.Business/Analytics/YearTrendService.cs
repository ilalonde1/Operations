#nullable enable

namespace Kor.Operations.PMTools;

public static class YearTrendService
{
    public static List<YearTrendRow> Build(
        IReadOnlyList<HistoricalProjectRow> visible,
        FirmUtilizationStats? firmUtilization = null)
    {
        return visible
            .Where(r => r.OpenYear.HasValue)
            .GroupBy(r => r.OpenYear!.Value)
            .Select(g =>
            {
                var rows = g.ToList();
                var totalEng = rows.Sum(r => r.EngHrs);
                var totalDraft = rows.Sum(r => r.DraftHrs);
                var totalProd = totalEng + totalDraft;
                var totalAll = rows.Sum(r => r.TotalAllHrs);
                var totalFee = rows.Sum(r => r.TotalFee);
                var overheadHrs = rows.Sum(r => r.AdminHrs + r.NonBillHrs);
                return new YearTrendRow
                {
                    Year = g.Key,
                    ProjectCount = rows.Count,
                    TotalFee = totalFee,
                    AvgFee = rows.Count > 0 ? totalFee / rows.Count : 0,
                    AvgFeePerHr = totalProd > 0 ? totalFee / totalProd : 0,
                    AvgNetFeePerHr = totalProd > 0 ? (totalFee - rows.Sum(r => r.SubCost)) / totalProd : 0,
                    WeightedEngPct = totalProd > 0 ? totalEng / totalProd : 0,
                    WeightedBillablePct = totalAll > 0 ? rows.Sum(r => r.BillableHrs) / totalAll : 0,
                    AvgSubPct = totalFee > 0 ? rows.Sum(r => r.SubCost) / totalFee : 0,
                    WeightedOverheadRatio = totalAll > 0 ? overheadHrs / totalAll : 0,
                    TotalArOutstanding = rows.Sum(r => r.ArTotal),
                    FirmBillablePct = firmUtilization?.ByYear.TryGetValue(g.Key, out var u) == true && u.Total > 0
                        ? u.Billable / u.Total : 0,
                };
            })
            .OrderByDescending(r => r.Year)
            .ToList();
    }
}
