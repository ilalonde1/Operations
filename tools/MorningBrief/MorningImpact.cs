using System.Globalization;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Opportunities.Data.Crm;

namespace Kor.MorningBrief;

/// <summary>
/// The impact scoreboard (Ian, 18 October 2026): what changed in dollars since the baseline. History (invoiced, collected, AR,
/// DSO per closed month) comes from Deltek through ImpactHistory; the day's readings come from the engine's own figures (the AR
/// Outstanding KPI's firmwide aging rows, the snapshot's risk statuses) and the CRM, and are recorded once a day in
/// opportunities.ImpactSnapshot. Pure; no store reads.
/// </summary>
public static class MorningImpact
{
    /// <summary>The day the scoreboard starts: the evening Ian asked for it.</summary>
    public static readonly DateOnly Baseline = new(2026, 10, 18);

    public static readonly IReadOnlyList<(string Metric, string Label, bool Money, bool LowerIsBetter)> Shown = new[]
    {
        ("ArTotal", "AR owed to KOR", true, true),
        ("ArOver60", "AR over 60 days", true, true),
        ("ArOver90", "AR over 90 days", true, true),
        ("ArInactive", "AR on projects outside the active list", true, true),
        ("CriticalProjects", "Critical projects", false, true),
        ("OverBudgetProjects", "Projects over hours budget", false, true),
        ("CrmWonCount", "Pursuits won (CRM running total)", false, false),
        ("CrmWonFee", "Fees won (CRM running total)", true, false),
        ("CrmLostCount", "Pursuits lost (CRM running total)", false, true),
        ("ActionsClosedTotal", "BD actions closed (running total)", false, false),
        ("MeetingsHeldTotal", "Meetings held (running total)", false, false),
    };

    /// <summary>The day's readings from the engine's figures. AR rows are the AR Outstanding KPI's firmwide aging rows.</summary>
    public static Dictionary<string, double> EngineReadings(IReadOnlyList<KpiArOutstandingRow> arRows, IReadOnlyCollection<UtilizationRow> active)
    {
        var activeWbs = active.Select(u => u.Wbs1.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["ArTotal"] = arRows.Sum(r => r.Total),
            ["ArOver60"] = arRows.Sum(r => r.Aged61To90 + r.Aged90Plus),
            ["ArOver90"] = arRows.Sum(r => r.Aged90Plus),
            ["ArInactive"] = arRows.Where(r => !activeWbs.Contains((r.Wbs1 ?? "").Trim())).Sum(r => r.Total),
            ["CriticalProjects"] = PortfolioHealthCounts.From(active.ToArray()).Critical,
            ["OverBudgetProjects"] = active.Count(u => string.Equals(u.RiskStatus, "Over budget", StringComparison.OrdinalIgnoreCase)),
        };
    }

    /// <summary>The prefix of a reading taken as-is from an Executive Summary KPI: "Exec." + the title's letters and digits.</summary>
    public const string ExecPrefix = "Exec.";

    /// <summary>"Net Income (T12mo)" → "Exec.NetIncomeT12mo"; "AR > 60 Days" → "Exec.AR60Days".</summary>
    public static string ExecKey(string title) => ExecPrefix + new string(title.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// A KPI's headline as the engine printed it, as a number: "$3,086,200" → 3086200, "2.49" → 2.49, "160 days" → 160,
    /// "70.8%" → 70.8, "($12,000)" or "−$12,000" → -12000. Anything else ("Data unavailable", a sentence) is not a reading.
    /// </summary>
    public static bool TryParseKpiValue(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        var negative = t.StartsWith('(') && t.EndsWith(')') || t.StartsWith('-') || t.StartsWith('−');
        t = t.Trim('(', ')', '-', '−', ' ').Replace("$", "").Replace(",", "").Replace("%", "");
        if (t.EndsWith(" days", StringComparison.OrdinalIgnoreCase)) t = t[..^5];
        if (!double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (negative) value = -value;
        return true;
    }

    /// <summary>
    /// The operating plan's daily readings (migration 372). The Financials engine is the source of every firm figure: each
    /// Executive Summary KPI whose headline is a number is recorded as-is under ExecKey(title) (cash, AR, DSO, net multiplier,
    /// labour margin, net income, utilization…), and the money lost on losing projects is the engine's own per-project
    /// ProfitDollars (fee billed less direct labour, subconsultants and allocated overhead), summed over active projects that
    /// have booked labour. Nothing here re-derives a figure the engine produces. Also: active projects, projects billing slower
    /// than they burn (the alert's rows), hours past budget (the KPI's rows), and average monthly invoicing over the last six
    /// closed months and the six before (ImpactHistory, the scoreboard's own Deltek history). A reading the engine did not
    /// supply is left out, never recorded as zero.
    /// </summary>
    public static Dictionary<string, double> PlanReadings(ExecutiveSummaryResult result, IReadOnlyCollection<UtilizationRow> active,
        IReadOnlyList<ImpactMonth> months, int? billingLag)
    {
        var readings = new Dictionary<string, double>(StringComparer.Ordinal) { ["ActiveProjects"] = active.Count };
        foreach (var kpi in result.Kpis)
            if (TryParseKpiValue(kpi.ValueText, out var v)) readings[ExecKey(kpi.Title)] = v;
        if (billingLag is { } lag) readings["BillingLagProjects"] = lag;
        if (result.Kpis.FirstOrDefault(k => k.Title == "Projects Over Budget")?.ProjectDrilldownRows is { } over)
            readings["OverBudgetHours"] = over.Where(p => p.OverByHours > 0).Sum(p => p.OverByHours);
        var costed = active.Select(u => u.Project).Where(p => p.TotalDirectLaborCost > 0).ToList();
        if (costed.Count > 0)
        {
            var losing = costed.Where(p => p.ProfitDollars < 0).ToList();
            readings["LosingProjects"] = losing.Count;
            readings["LosingProjectsLoss"] = -losing.Sum(p => p.ProfitDollars);
            readings["ProjectsBelowMultiplier2"] = costed.Count(p => p.MultiplierTier == "Critical");
            // Billing to date lags labour on a job in progress, so it reads as losing until it bills. Projects with 90% or
            // more of their fee billed (posted or not) are essentially done: their loss is money actually given away.
            var done = losing.Where(p => p.PercentBilledWithUnposted >= 0.9).ToList();
            readings["LosingDoneProjects"] = done.Count;
            readings["LosingDoneProjectsLoss"] = -done.Sum(p => p.ProfitDollars);
            // The same money by fee type, from the engine's own split of TotalFee into the contract Fee and HourlyRevenue (T&M
            // extras): a fixed-fee loss is a fee or scope problem, an hourly loss is a rate or write-off problem.
            foreach (var g in done.GroupBy(FeeType))
            {
                readings["LosingDone" + g.Key] = g.Count();
                readings["LosingDone" + g.Key + "Loss"] = -g.Sum(p => p.ProfitDollars);
            }
            foreach (var type in FeeTypes.Where(t => !readings.ContainsKey("LosingDone" + t)))
            {
                readings["LosingDone" + type] = 0;
                readings["LosingDone" + type + "Loss"] = 0;
            }
        }
        var year = months.TakeLast(12).ToList();
        if (year.Count == 12)
        {
            readings["InvoicedMonthAvg6"] = year.Skip(6).Average(m => m.Invoiced);
            readings["InvoicedMonthAvgPrior6"] = year.Take(6).Average(m => m.Invoiced);
        }
        return readings;
    }

    static readonly string[] FeeTypes = ["FixedFee", "Hourly", "Mixed"];

    /// <summary>A project's fee type from the engine's split: contract fee only, hourly revenue only, or both.</summary>
    public static string FeeType(FinancialsProjectRow p)
    {
        var fixedFee = Math.Abs(p.Fee) > AnalyticsThresholds.RoundingDollarFloor;
        var hourly = Math.Abs(p.HourlyRevenue) > AnalyticsThresholds.RoundingDollarFloor;
        return fixedFee && hourly ? "Mixed" : hourly ? "Hourly" : "FixedFee";
    }

    public static MorningFact Fact(IReadOnlyList<ImpactMonth> months, IReadOnlyList<ImpactReading> readings, string? note = null)
    {
        var lines = new List<string>();
        var days = readings.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count > 0)
        {
            var first = days[0];
            var last = days[^1];
            lines.Add(Inv($"Readings from {first:yyyy-MM-dd} (baseline) to {last:yyyy-MM-dd}, {days.Count} days recorded."));
            foreach (var (metric, label, money, lower) in Shown)
            {
                var start = readings.FirstOrDefault(r => r.Date == first && r.Metric == metric);
                var now = readings.LastOrDefault(r => r.Metric == metric);
                if (start is null || now is null) continue;
                var change = now.Value - start.Value;
                var verdict = Math.Abs(change) < 0.5 ? "no change" : (change < 0) == lower ? "better" : "worse";
                lines.Add($"{label}: baseline {Show(start.Value, money)}, now {Show(now.Value, money)} ({Signed(change, money)}, {verdict}).");
            }
        }
        if (months.Count > 0)
        {
            var recent = months.TakeLast(12).ToList();
            lines.Add("Collected by month, oldest first (cash received, GST included; invoiced excludes GST, so do not subtract one from the other): "
                + string.Join("; ", recent.Select(m => $"{Label(m.Period)} {Show(m.Collected, true)}")));
            lines.Add("Invoiced by month, oldest first: " + string.Join("; ", recent.Select(m => $"{Label(m.Period)} {Show(m.Invoiced, true)}")));
            lines.Add("AR and DSO at month end: " + string.Join("; ", recent.Where(m => m.Dso > 0)
                .Select(m => Inv($"{Label(m.Period)} {Show(m.ArEnd, true)} / {m.Dso:0} days"))));
            var yearInvoiced = recent.Sum(m => m.Invoiced);
            if (recent.Count == 12 && yearInvoiced > 0)
                lines.Add(Inv($"Each 30 days off DSO frees about {Show(yearInvoiced / 365.0 * 30.0, true)} of cash (12 months invoiced {Show(yearInvoiced, true)} / 365 × 30)."));
        }
        return new MorningFact("Impact scoreboard (baseline " + Baseline.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ")",
            lines, lines.Count, lines.Count, note);
    }

    public static string Label(int period) => new DateTime(period / 100, period % 100, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private static string Show(double v, bool money) => money
        ? "$" + Math.Round(v).ToString("N0", CultureInfo.InvariantCulture)
        : Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);

    private static string Signed(double v, bool money) => (v >= 0 ? "+" : "−") + Show(Math.Abs(v), money);

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);
}
