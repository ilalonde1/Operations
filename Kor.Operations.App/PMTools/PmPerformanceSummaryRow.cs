#nullable enable
using System;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Aggregated row for the PM Performance Summary view.
    /// One instance per project manager, computed from filtered HistoricalProjectRows.
    /// </summary>
    internal sealed class PmPerformanceSummaryRow
    {
        public string Pm { get; init; } = "";
        public int ProjectCount { get; init; }
        public double TotalFee { get; init; }
        public double TotalFeeBilled { get; init; }
        public double TotalUnpostedFeeBilled { get; init; }
        public double TotalFeeBilledWithUnposted => TotalFeeBilled + TotalUnpostedFeeBilled;
        public double WeightedPctBilled => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? TotalFeeBilled / TotalFee : 0;
        public double WeightedPctBilledWithUnposted => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? TotalFeeBilledWithUnposted / TotalFee : 0;
        public bool   HasUnpostedBilling => TotalUnpostedFeeBilled > 0.004;

        public double TotalEngHrs { get; init; }
        public double TotalDraftHrs { get; init; }
        public double TotalAllHrs { get; init; }
        public double EngPct => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalEngHrs / (TotalEngHrs + TotalDraftHrs) : 0;
        public double DraftPct => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalDraftHrs / (TotalEngHrs + TotalDraftHrs) : 0;

        /// <summary>Total fee ÷ total production hours (eng + draft).</summary>
        public double AvgFeePerHr => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalFee / (TotalEngHrs + TotalDraftHrs) : 0;

        public double TotalSubCost { get; init; }
        public double SubPctOfFee => Math.Abs(TotalFee) > AnalyticsThresholds.RoundingDollarFloor ? TotalSubCost / TotalFee : 0;

        public double TotalArOutstanding { get; init; }
        public double TotalAr90Plus { get; init; }

        public double AvgEngDelta { get; init; }
        public double AvgDraftDelta { get; init; }

        // ── Client Retention ──
        public int UniqueClients { get; init; }
        public int RepeatClients { get; init; }
        public double RepeatRate => UniqueClients > 0 ? (double)RepeatClients / UniqueClients : 0;

        // ── Billing Velocity ──
        /// <summary>Average months from project open to first revenue across PM's projects.</summary>
        public double AvgMonthsToFirstBill { get; init; }
        /// <summary>% of total fee billed within first 6 months of project opening.</summary>
        public double PctBilledWithin6Months { get; init; }

        // ── Subconsultant Dependency ──
        /// <summary>Fee per internal production hour after subtracting sub costs from fee.</summary>
        public double InternalFeePerHr => (TotalEngHrs + TotalDraftHrs) > 0 ? (TotalFee - TotalSubCost) / (TotalEngHrs + TotalDraftHrs) : 0;

        // ── Performance Score (0-100) ──

        /// <summary>% of projects NOT over budget (eng hours &lt;= estimated × OverBudgetFactor). Weight: 30%.</summary>
        public double DeliveryHealthScore { get; set; }
        /// <summary>Estimation accuracy percentile rank — lower |delta| = higher score. Weight: 30%.</summary>
        public double EstimationAccuracyScore { get; set; } = 50;
        /// <summary>Fee/Hr percentile rank vs peers. Weight: 20%.</summary>
        public double RevenueEfficiencyScore { get; set; } = 50;
        /// <summary>% of AR NOT 90+ days overdue. Weight: 20%.</summary>
        public double ArManagementScore { get; set; } = 100;
        /// <summary>Weighted composite: Delivery 30% + Estimation 30% + Revenue 20% + AR 20%.</summary>
        public double PerformanceScore { get; set; }

        public string PerformanceGrade => PerformanceScore switch
        {
            >= 93 => "A+", >= 87 => "A", >= 83 => "A-",
            >= 77 => "B+", >= 73 => "B", >= 70 => "B-",
            >= 67 => "C+", >= 63 => "C", >= 60 => "C-",
            >= 57 => "D+", >= 53 => "D", >= 50 => "D-",
            _ => "F"
        };

        public int GradeSortKey => PerformanceScore switch
        {
            >= 93 => 13, >= 87 => 12, >= 83 => 11,
            >= 77 => 10, >= 73 => 9, >= 70 => 8,
            >= 67 => 7, >= 63 => 6, >= 60 => 5,
            >= 57 => 4, >= 53 => 3, >= 50 => 2,
            _ => 1
        };

        public string PerformanceColor => PerformanceScore switch
        {
            >= 83 => "#16A34A",
            >= 70 => "#2563EB",
            >= 60 => "#D97706",
            >= 50 => "#EA580C",
            _ => "#DC2626"
        };
    }
}
