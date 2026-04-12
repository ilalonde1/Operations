#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Read-only row for the Historical Project Analytics grid.
    /// One instance per WBS1 project loaded from Deltek.
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
        public double FeeBilled { get; init; }
        public double PercentBilled => Fee > 0 ? FeeBilled / Fee : 0;

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
        public double SubPctOfFee => Fee > 0 ? SubCost / Fee : 0;

        // ── A/R Aging ──
        public double ArTotal { get; init; }
        public double ArCurrent { get; init; }
        public double Ar31To60 { get; init; }
        public double Ar61To90 { get; init; }
        public double Ar90Plus { get; init; }

        /// <summary>Fee ÷ production hours (eng + draft only).</summary>
        public double FeePerHr => TotalEngDraft > 0 ? Fee / TotalEngDraft : 0;

        // ── Net fee (fee minus subconsultant costs) ──
        public double NetFee => Fee - SubCost;
        public double NetFeePerHr => TotalEngDraft > 0 ? NetFee / TotalEngDraft : 0;

        // ── Duration ──
        public double? DurationMonths => (OpenDate.HasValue && CloseDate.HasValue)
            ? Math.Max(0, (CloseDate.Value - OpenDate.Value).TotalDays / 30.44)
            : (OpenDate.HasValue ? (DateTime.Today - OpenDate.Value).TotalDays / 30.44 : (double?)null);
        public string DurationDisplay => DurationMonths.HasValue
            ? $"{DurationMonths.Value:N0} mo"
            : "—";
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
