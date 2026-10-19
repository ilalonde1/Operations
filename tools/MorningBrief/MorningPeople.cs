using System.Globalization;
using Kor.Operations.Compensation;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;

namespace Kor.MorningBrief;

/// <summary>
/// The COO's people view from measures the app already computes: billable hours per person (EmployeeAnalyticsService weeks),
/// billed $ per person over twelve months (CompensationService, the Compensation screen's figure) and each PM's book of
/// work (the Financials snapshot rows). Pure formatting; no store reads.
/// </summary>
public static class MorningPeople
{
    /// <summary>The standard week the Staff Utilization screen measures workload against.</summary>
    public const double StandardWeekHours = 37.5;

    public static MorningFact BillableHours(IReadOnlyList<EmployeeWeeklyHours> rows, DateTime lastCompleteWeek)
    {
        var last = lastCompleteWeek.Date;
        var first = last.AddDays(-7 * 11);
        var recentFrom = last.AddDays(-7 * 3);
        var people = rows.Where(r => r.WeekStart.Date >= first && r.WeekStart.Date <= last)
            .GroupBy(r => r.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var week = g.Where(r => r.WeekStart.Date == last).ToArray();
                var recent = g.Where(r => r.WeekStart.Date >= recentFrom).ToArray();
                var prior = g.Where(r => r.WeekStart.Date < recentFrom).ToArray();
                return new
                {
                    Name = g.First().EmployeeName, Id = g.Key,
                    WeekBillable = week.Sum(r => r.BillableHrs), WeekTotal = week.Sum(r => r.TotalHrs),
                    Billable = g.Sum(r => r.BillableHrs), Total = g.Sum(r => r.TotalHrs),
                    Weeks = g.Select(r => r.WeekStart.Date).Distinct().Count(),
                    RecentShare = Share(recent.Sum(r => r.BillableHrs), recent.Sum(r => r.TotalHrs)),
                    PriorShare = Share(prior.Sum(r => r.BillableHrs), prior.Sum(r => r.TotalHrs)),
                };
            })
            .OrderByDescending(p => p.Billable).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();

        var lines = new List<string>
        {
            $"Twelve weeks {first:yyyy-MM-dd} to week of {last:yyyy-MM-dd}. Billable share = billable / recorded hours; workload = average recorded hours a week / {N1(StandardWeekHours)} h (the Staff Utilization standard). A person with fewer than 12 weeks of hours was away or joined mid-window.",
        };
        lines.AddRange(people.Select(p =>
            $"{p.Name} ({p.Id}): last week {N1(p.WeekBillable)} of {N1(p.WeekTotal)} h billable; 12 weeks {N0(p.Billable)} billable of {N0(p.Total)} recorded ({Pct(Share(p.Billable, p.Total))}), "
            + $"{N1(p.Billable / 12)} billable h/week, workload {Pct(p.Total / 12 / StandardWeekHours)}; weeks with hours {p.Weeks} of 12."));
        var totalBillable = people.Sum(p => p.Billable);
        var totalRecorded = people.Sum(p => p.Total);
        lines.Add($"Everyone: {N0(totalBillable)} billable of {N0(totalRecorded)} recorded hours ({Pct(Share(totalBillable, totalRecorded))}).");

        // Two shapes a COO acts on: a billable share that has collapsed, and a sustained load well past the standard week.
        var situations = new List<MorningSituation>();
        foreach (var p in people)
        {
            if (p.PriorShare is >= .6 && p.RecentShare is { } r && r <= p.PriorShare - .25)
                situations.Add(new MorningSituation("billable drop",
                    $"A staff member's billable share fell from {Pct(p.PriorShare)} to {Pct(r)} over the last four weeks.",
                    $"{p.Name}: billable share {Pct(p.PriorShare)} → {Pct(r)} (last four weeks)", Array.Empty<string>()));
            if (p.Weeks >= 8 && p.Total / p.Weeks > 45)
                situations.Add(new MorningSituation("sustained overload",
                    FormattableString.Invariant($"A staff member has averaged {p.Total / p.Weeks:0} recorded hours a week for {p.Weeks} weeks, well over the {StandardWeekHours} hour standard."),
                    $"{p.Name}: {N1(p.Total / p.Weeks)} h/week over {p.Weeks} weeks", Array.Empty<string>()));
        }
        return new MorningFact($"Billable hours per person, last 12 weeks ({people.Length} people with hours)", lines, people.Length, people.Length)
            { Situations = situations };
    }

    public static MorningFact Billed(IReadOnlyList<EmployeeCompensationRow> rows)
    {
        var ordered = rows.OrderByDescending(r => r.AnnualizedRevenueActual).ThenBy(r => r.FullName, StringComparer.Ordinal).ToArray();
        var lines = new List<string>
        {
            "Billed $ = hourly revenue plus fixed-fee revenue allocated by each person's billed share, trailing 12 months: the Compensation screen's figure. Partners carry the imputed cost rate.",
        };
        lines.AddRange(ordered.Select(r =>
            $"{r.FullName}{(r.IsPartner ? " (partner)" : "")}: billed ${N0(r.AnnualizedRevenueActual)}; {N0(r.AnnualizedBillableHoursActual)} billable of {N0(r.AnnualizedHoursActual)} recorded h ({Pct(r.UtilizationActual)}); "
            + $"{(r.AnnualizedBillableHoursActual > 0 ? "$" + N0(r.AnnualizedRevenueActual / r.AnnualizedBillableHoursActual) : "no")} per billable h; overtime {N0(r.OvertimeHoursActual)} h."));
        lines.Add($"Everyone: billed ${N0(ordered.Sum(r => r.AnnualizedRevenueActual))}; overtime {N0(ordered.Sum(r => r.OvertimeHoursActual))} h.");
        return new MorningFact($"Billed $ per person, trailing 12 months ({ordered.Length} active staff)", lines, ordered.Length, ordered.Length);
    }

    public static MorningFact PmWorkload(IReadOnlyList<UtilizationRow> rows)
    {
        var pms = rows.Where(r => !string.IsNullOrWhiteSpace(r.Pm)).GroupBy(r => r.Pm.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Pm = g.Key, Projects = g.Count(),
                Fee = g.Sum(r => r.Project.TotalFee),
                Unbilled = g.Sum(r => Math.Max(0, r.Project.TotalFee - r.Project.FeeBilledWithUnposted)),
                Hours = g.Sum(r => r.EngHours), Budget = g.Sum(r => r.EngBudget),
                Critical = g.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.Critical),
                AtRisk = g.Count(r => r.ConfidenceLevel == DeliveryConfidenceLevel.AtRisk),
                Over = g.Count(r => r.RiskStatus == "Over budget"),
            })
            .OrderByDescending(p => p.Unbilled).ThenBy(p => p.Pm, StringComparer.Ordinal).ToArray();
        var lines = new List<string> { "Each PM's active projects from the Financials snapshot. Unbilled = fee (incl. hourly) not yet billed, unposted included; hours are engineering hours against the peer-estimated budget." };
        lines.AddRange(pms.Select(p =>
            $"{p.Pm}: {p.Projects} projects; fee ${N0(p.Fee)}, unbilled ${N0(p.Unbilled)}; engineering {N0(p.Hours)} of {N0(p.Budget)} budgeted h; "
            + $"{p.Critical} critical, {p.AtRisk} at risk, {p.Over} over budget."));
        return new MorningFact($"PM workload ({pms.Length} PMs, {rows.Count} active projects)", lines, pms.Length, pms.Length);
    }

    private static double? Share(double billable, double total) => total > 0 ? billable / total : null;
    // Invariant "P0" prints "80 %"; the brief writes "80%".
    private static string Pct(double? value) => value is { } v ? (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "unknown";
    private static string N0(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string N1(double value) => value.ToString("N1", CultureInfo.InvariantCulture);
}
