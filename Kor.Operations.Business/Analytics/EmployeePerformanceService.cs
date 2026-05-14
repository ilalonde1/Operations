#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools;

public static class EmployeePerformanceService
{
    public static List<EmployeeSummaryRow> Build(
        IReadOnlyList<HistoricalProjectRow> visible,
        IReadOnlyList<EmployeeProjectHours> employeeProjectHours,
        IReadOnlyCollection<string> excludedEmployeeIds)
    {
        var projectLookup = new Dictionary<string, HistoricalProjectRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in visible)
            projectLookup.TryAdd(r.Wbs1, r);

        // Group ALL employee hours (including overhead/admin projects) for total hours,
        // but only count hours from visible (billable) projects for billable metrics.
        var groups = employeeProjectHours
            .GroupBy(ep => ep.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var allEntries = g.ToList();
                var billableEntries = allEntries.Where(e => projectLookup.ContainsKey(e.Wbs1)).ToList();

                // Primary construction type — the type where this employee spent the most hours
                var primaryType = billableEntries
                    .Where(e => projectLookup.TryGetValue(e.Wbs1, out _))
                    .GroupBy(e => (projectLookup[e.Wbs1].ConstructionType ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(tg => !string.IsNullOrWhiteSpace(tg.Key))
                    .OrderByDescending(tg => tg.Sum(x => x.EngHrs + x.DraftHrs))
                    .Select(tg => tg.Key)
                    .FirstOrDefault() ?? "";

                // Production hours from billable projects only
                var engHrs = billableEntries.Sum(e => e.EngHrs);
                var draftHrs = billableEntries.Sum(e => e.DraftHrs);
                var billableHrs = billableEntries.Sum(e => e.TotalHrs);

                // Total hours from ALL projects (including overhead, vacation, etc.)
                var totalAllHrs = allEntries.Sum(e => e.TotalHrs);

                var attributedFee = 0.0;
                var projectFees = new List<double>();
                foreach (var entry in billableEntries)
                {
                    if (projectLookup.TryGetValue(entry.Wbs1, out var proj) && proj.TotalEngDraft > 0)
                    {
                        var entryProd = entry.EngHrs + entry.DraftHrs;
                        var share = entryProd / proj.TotalEngDraft;
                        attributedFee += proj.TotalFee * share;
                        projectFees.Add(proj.TotalFee);
                    }
                }

                // Project Health: % of hours on projects that are NOT over-budget
                var healthyHrs = 0.0;
                var totalProjHrs = 0.0;
                foreach (var entry in billableEntries)
                {
                    if (projectLookup.TryGetValue(entry.Wbs1, out var proj))
                    {
                        var entryHrs = entry.EngHrs + entry.DraftHrs;
                        totalProjHrs += entryHrs;
                        // Project is "healthy" if actual eng hours haven't exceeded estimated budget
                        // (or if there's no estimate to compare against)
                        var isHealthy = proj.EstEngBudget <= 0 || proj.EngHrs <= proj.EstEngBudget * AnalyticsThresholds.OverBudgetFactor;
                        if (isHealthy) healthyHrs += entryHrs;
                    }
                }

                return new EmployeeSummaryRow
                {
                    EmployeeId = g.Key,
                    EmployeeName = allEntries.First().EmployeeName,
                    ProjectCount = billableEntries.Select(e => e.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    TotalEngHrs = engHrs,
                    TotalDraftHrs = draftHrs,
                    TotalBillableHrs = billableHrs,
                    TotalAllHrs = totalAllHrs,
                    AttributedFee = attributedFee,
                    AvgProjectFee = projectFees.Count > 0 ? projectFees.Average() : 0,
                    PrimaryRole = (engHrs == 0 && draftHrs == 0) ? "Inspector"
                        : engHrs >= draftHrs ? "Engineering" : "Drafting",
                    PrimaryConstructionType = primaryType,
                    HireDate = allEntries.Select(e => e.HireDate).FirstOrDefault(d => d.HasValue),
                    // Raw scores — efficiency normalized in second pass
                    BillableRateScore = Math.Min(100, (totalAllHrs > 0 ? billableHrs / totalAllHrs : 0) * 100),
                    ProjectHealthScore = totalProjHrs > 0 ? (healthyHrs / totalProjHrs) * 100 : 100,
                };
            })
            .Where(r => r.TotalAllHrs > 0 && r.ProjectCount > 0
                && !excludedEmployeeIds.Contains(r.EmployeeId))
            .OrderByDescending(r => r.AttributedFee)
            .ToList();

        PerformanceScoring.ScoreEmployeesSecondPass(groups, employeeProjectHours, wbs1 => projectLookup.ContainsKey(wbs1));
        return groups;
    }
}
