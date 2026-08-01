#nullable enable
using AppFin = Kor.Operations.Financials;

namespace Kor.Operations.Tests.Financials.Executive;

internal static class SyntheticSnapshot
{
    public static AppFin.FinancialsSnapshot WithProjects(
        params (string Wbs1, string Name, double TotalFee, double FeeBilled, double EngHrs, double EngBudget, string Org)[] projects)
        => WithProjects(usdToCadRate: 1.36, projects);

    public static AppFin.FinancialsSnapshot WithProjects(
        double usdToCadRate,
        params (string Wbs1, string Name, double TotalFee, double FeeBilled, double EngHrs, double EngBudget, string Org)[] projects)
    {
        var rows = projects
            .Select(p => new AppFin.FinancialsProjectRow
            {
                Wbs1 = p.Wbs1,
                Name = p.Name,
                Pm = "PM",
                Fee = p.TotalFee,
                FeeBilled = p.FeeBilled,
                EngHrs = p.EngHrs,
                EngBudget = p.EngBudget,
                Org = p.Org,
                PercentBilled = p.TotalFee > 0.004 ? p.FeeBilled / p.TotalFee : 0.0
            })
            .ToList();

        return new AppFin.FinancialsSnapshot
        {
            RefreshedAt = new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero),
            Rows = rows,
            UsdToCadRate = usdToCadRate,
            Headline = AppFin.FinancialsHeadlineCalculator.Compute(rows, usdToCadRate)
        };
    }

    public static AppFin.FinancialsSnapshot Empty()
        => new()
        {
            RefreshedAt = new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero),
            Rows = new List<AppFin.FinancialsProjectRow>(),
            UsdToCadRate = 1.36,
            Headline = new AppFin.FinancialsHeadlineKpis()
        };
}
