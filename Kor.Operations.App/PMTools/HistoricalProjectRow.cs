#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Historical project row. Primarily immutable data loaded from Deltek; TotalCost is computed and
    /// attached by the ViewModel after LoadAsync using per-employee billable hours and cost rates.
    /// </summary>
    internal sealed class HistoricalProjectRow
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

        public double Fee { get; init; }
        public double HourlyRevenue { get; init; }
        public double TotalFee => Fee + HourlyRevenue;
        public double FeeBilled { get; init; }
        public double UnpostedFeeBilled { get; init; }
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double PercentBilled => TotalFee > 0 ? FeeBilled / TotalFee : 0;
        public double PercentBilledWithUnposted => TotalFee > 0 ? FeeBilledWithUnposted / TotalFee : 0;
        /// <summary>
        /// True when there are open AR invoices in periods Deltek hasn't yet closed
        /// (or has only partially closed) for this WBS1. Drives italic/amber rendering
        /// of the Billed cell and the AR-divergence badge.
        /// </summary>
        public bool HasUnpostedBilling => UnpostedFeeBilled > 0.004;

        // ── Production hours (eng + draft) ──
        public double EngHrs { get; init; }
        public double DraftHrs { get; init; }
        public double TotalEngDraft => EngHrs + DraftHrs;
        public double EngPct => TotalEngDraft > 0 ? EngHrs / TotalEngDraft : 0;
        public double DraftPct => TotalEngDraft > 0 ? DraftHrs / TotalEngDraft : 0;

        // ── Inspections ──
        public int TotalInspections { get; init; }
        public int LastMonthInspections { get; init; }

        // ── Full labor code breakdown (Checking merged into Engineering) ──
        public double InspHrs { get; init; }
        public double DocPrepHrs { get; init; }
        public double GenHrs { get; init; }
        public double AdminHrs { get; init; }
        public double NonBillHrs { get; init; }
        public double TotalAllHrs { get; init; }
        public double BillableHrs { get; init; }
        public double BillablePct => TotalAllHrs > 0 ? BillableHrs / TotalAllHrs : 0;
        /// <summary>Non-billable hours (Admin + NonBillable) as ratio of total. Only codes 70+80.</summary>
        public double OverheadRatio => TotalAllHrs > 0
            ? (AdminHrs + NonBillHrs) / TotalAllHrs : 0;

        // ── Subconsultant costs ──
        public double SubCost { get; init; }
        public double SubPctOfFee => TotalFee > 0 ? SubCost / TotalFee : 0;
        /// <summary>
        /// Total employee-hour cost for this project: sum of each employee's hours on the
        /// project multiplied by their EffectiveCostRate (imputed for Partners, raw for
        /// everyone else). Populated by the ViewModel after LoadAsync; not init-only so
        /// the ViewModel can attach the value post-load.
        /// </summary>
        public double TotalCost { get; set; }
        /// <summary>Fee billed minus total employee-hour cost.</summary>
        public double Margin => FeeBilled - TotalCost;
        /// <summary>Margin as share of fee billed. Zero when FeeBilled is zero.</summary>
        public double MarginPct => FeeBilled > 0 ? Margin / FeeBilled : 0;
        /// <summary>True when the computed Margin is negative. Used by the UI for red coloring.</summary>
        public bool IsMarginNegative => Margin < 0;

        // ── A/R Aging ──
        public double ArTotal { get; init; }
        public double ArCurrent { get; init; }
        public double Ar31To60 { get; init; }
        public double Ar61To90 { get; init; }
        public double Ar90Plus { get; init; }

        /// <summary>Fee ÷ production hours (eng + draft only).</summary>
        public double FeePerHr => TotalEngDraft > 0 ? TotalFee / TotalEngDraft : 0;

        // ── Net fee (fee minus subconsultant costs) ──
        public double NetFee => TotalFee - SubCost;
        public double NetFeePerHr => TotalEngDraft > 0 ? NetFee / TotalEngDraft : 0;

        // ── Duration ──
        public double? DurationMonths => (OpenDate.HasValue && CloseDate.HasValue)
            ? Math.Max(0, (CloseDate.Value - OpenDate.Value).TotalDays / 30.44)
            : (OpenDate.HasValue ? (DateTime.Today - OpenDate.Value).TotalDays / 30.44 : (double?)null);
        public string DurationDisplay => DurationMonths.HasValue
            ? $"{DurationMonths.Value:N0} mo"
            : "—";
        public double FeePerMonth => (DurationMonths ?? 0) > 0 ? TotalFee / DurationMonths!.Value : 0;
        public int? OpenYear => OpenDate?.Year;

        // ── Budget estimation (peer-based with formula fallback) ──
        /// <summary>Estimated engineering hours — peer median if 3+ peers, else formula.</summary>
        public double EstEngBudget { get; set; }
        /// <summary>Estimated drafting hours — peer median if 3+ peers, else formula.</summary>
        public double EstDraftBudget { get; set; }
        /// <summary>Number of peer projects used for budget estimate (0 = formula fallback).</summary>
        public int BudgetPeerCount { get; set; }

        /// <summary>Estimated − actual: positive = under, negative = over.</summary>
        public double EngBudgetDelta => EstEngBudget > 0 ? EstEngBudget - EngHrs : 0;
        /// <summary>Estimated − actual: positive = under, negative = over.</summary>
        public double DraftBudgetDelta => EstDraftBudget > 0 ? EstDraftBudget - DraftHrs : 0;

        // ── Revenue timeline (loaded separately, attached after main query) ──
        public List<PeriodRevenue>? RevenueTimeline { get; set; }
    }
}
