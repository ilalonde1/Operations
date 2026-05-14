#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools;

/// <summary>
/// One period's revenue and billing data for a project.
/// Loaded from PRSummaryMain grouped by WBS1 + Period.
/// </summary>
public sealed record PeriodRevenue(string Period, double Revenue, double Billed);

/// <summary>
/// Firm-wide billable utilization computed from ALL tkDetail hours (no WBS1 filter).
/// Billable = LaborCode NOT IN (70, 80), matching Staff Utilization definition.
/// </summary>
public sealed class FirmUtilizationStats
{
    public double TotalHrs { get; init; }
    public double BillableHrs { get; init; }
    public double BillablePct { get; init; }
    public Dictionary<int, (double Total, double Billable)> ByYear { get; init; } = new();
}

/// <summary>
/// Raw employee-project hour data from tkDetail.
/// One row per employee per project.
/// </summary>
public sealed class EmployeeProjectHours
{
    public string EmployeeId { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string Wbs1 { get; init; } = "";
    public double EngHrs { get; init; }
    public double DraftHrs { get; init; }
    public double BillableHrs { get; init; }
    public double TotalHrs { get; init; }
    public DateTime? HireDate { get; init; }
}

public sealed class QuarterlyEmployeeHours
{
    public int Year { get; init; }
    public int Quarter { get; init; }
    public string EmployeeId { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string Wbs1 { get; init; } = "";
    public double EngHrs { get; init; }
    public double DraftHrs { get; init; }
    public double BillableHrs { get; init; }
    public double TotalHrs { get; init; }
}

/// <summary>Per-employee weekly billable and total hours from tkDetail for the last 12 weeks.</summary>
public sealed class EmployeeWeeklyHours
{
    public string EmployeeId { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public DateTime WeekStart { get; init; }
    public double BillableHrs { get; init; }
    public double TotalHrs { get; init; }
}

/// <summary>Per-employee billing rate, raw/effective cost rate, and Partner-imputation flag.</summary>
public sealed class EmployeeRate
{
    public string EmployeeId { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public double BillingRate { get; init; }
    public double CostRate { get; init; }
    public bool IsPartner { get; init; }
    public double EffectiveCostRate { get; init; }
}

public sealed class HistoricalPeerProject
{
    public string Wbs1 { get; init; } = "";
    public double Fee { get; init; }
    public string Phase { get; init; } = "";
    public string ConstructionType { get; init; } = "";
    public string ProjectCategory { get; init; } = "";
    public double EngHrs { get; init; }
    public double DraftHrs { get; init; }
}

public delegate (double EngHrs, double DraftHrs, int PeerCount) HistoricalPeerBudgetEstimator(
    double fee,
    string? phase,
    string? constructionType,
    string? projectCategory,
    IReadOnlyList<HistoricalPeerProject> allPeers,
    string excludeWbs1);

/// <summary>
/// Historical project row. Primarily immutable data loaded from Deltek; TotalCost is computed and
/// attached by the ViewModel after LoadAsync using per-employee billable hours and cost rates.
/// </summary>
public sealed class HistoricalProjectRow
{
    public string Wbs1 { get; init; } = "";
    public string Name { get; init; } = "";
    public string Pm { get; init; } = "";
    public string DraftingManager { get; init; } = "";
    public string Phase { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? OpenDate { get; init; }
    public DateTime? CloseDate { get; init; }
    public string ConstructionType { get; init; } = "";
    public string ProjectCategory { get; init; } = "";
    public string DraftingType { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string Org { get; init; } = "";

    public double Fee { get; init; }
    public double HourlyRevenue { get; init; }
    public double TotalFee => Fee + HourlyRevenue;
    public double FeeBilled { get; init; }
    public double UnpostedFeeBilled { get; init; }
    public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
    public double PercentBilled => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? FeeBilled / TotalFee : 0;
    public double PercentBilledWithUnposted => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? FeeBilledWithUnposted / TotalFee : 0;
    public bool HasUnpostedBilling => UnpostedFeeBilled > 0.004;

    public double EngHrs { get; init; }
    public double DraftHrs { get; init; }
    public double TotalEngDraft => EngHrs + DraftHrs;
    public double EngPct => Math.Abs(TotalEngDraft) > AnalyticsThresholds.RoundingDollarFloor ? EngHrs / TotalEngDraft : 0;
    public double DraftPct => Math.Abs(TotalEngDraft) > AnalyticsThresholds.RoundingDollarFloor ? DraftHrs / TotalEngDraft : 0;

    public int TotalInspections { get; init; }
    public int LastMonthInspections { get; init; }

    public double InspHrs { get; init; }
    public double DocPrepHrs { get; init; }
    public double GenHrs { get; init; }
    public double AdminHrs { get; init; }
    public double NonBillHrs { get; init; }
    public double TotalAllHrs { get; init; }
    public double BillableHrs { get; init; }
    public double BillablePct => Math.Abs(TotalAllHrs) > AnalyticsThresholds.RoundingDollarFloor ? BillableHrs / TotalAllHrs : 0;
    public double OverheadRatio => Math.Abs(TotalAllHrs) > AnalyticsThresholds.RoundingDollarFloor
        ? (AdminHrs + NonBillHrs) / TotalAllHrs : 0;

    public double SubCost { get; init; }
    public double SubPctOfFee => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? SubCost / TotalFee : 0;
    public double TotalCost { get; set; }
    public double Margin => FeeBilled - TotalCost;
    public double MarginPct => Math.Abs(FeeBilled) > AnalyticsThresholds.RoundingDollarFloor ? Margin / FeeBilled : 0;
    public bool IsMarginNegative => Margin < 0;

    public double ArTotal { get; init; }
    public double ArCurrent { get; init; }
    public double Ar31To60 { get; init; }
    public double Ar61To90 { get; init; }
    public double Ar90Plus { get; init; }

    public double FeePerHr => Math.Abs(TotalEngDraft) > AnalyticsThresholds.RoundingDollarFloor ? TotalFee / TotalEngDraft : 0;
    public double NetFee => TotalFee - SubCost;
    public double NetFeePerHr => Math.Abs(TotalEngDraft) > AnalyticsThresholds.RoundingDollarFloor ? NetFee / TotalEngDraft : 0;

    public double? DurationMonths => (OpenDate.HasValue && CloseDate.HasValue)
        ? Math.Max(0, (CloseDate.Value - OpenDate.Value).TotalDays / 30.44)
        : (OpenDate.HasValue ? (DateTime.Today - OpenDate.Value).TotalDays / 30.44 : (double?)null);
    public string DurationDisplay => DurationMonths.HasValue
        ? $"{DurationMonths.Value:N0} mo"
        : "\u2014";
    public double FeePerMonth => Math.Abs(DurationMonths ?? 0) > AnalyticsThresholds.RoundingDollarFloor ? TotalFee / DurationMonths!.Value : 0;
    public int? OpenYear => OpenDate?.Year;

    public double EstEngBudget { get; set; }
    public double EstDraftBudget { get; set; }
    public int BudgetPeerCount { get; set; }

    public double EngBudgetDelta => EstEngBudget > 0 ? EstEngBudget - EngHrs : 0;
    public double DraftBudgetDelta => EstDraftBudget > 0 ? EstDraftBudget - DraftHrs : 0;

    public List<PeriodRevenue>? RevenueTimeline { get; set; }
}
