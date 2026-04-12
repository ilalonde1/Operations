#nullable enable
namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Aggregated employee row for the Employee Summary view.
    /// One instance per employee, computed from EmployeeProjectHours + project-level data.
    /// </summary>
    internal sealed class EmployeeSummaryRow
    {
        public string EmployeeName { get; init; } = "";
        public string EmployeeId { get; init; } = "";
        public int ProjectCount { get; init; }

        public double TotalEngHrs { get; init; }
        public double TotalDraftHrs { get; init; }
        public double TotalBillableHrs { get; init; }
        public double TotalAllHrs { get; init; }

        public double EngPct => (TotalEngHrs + TotalDraftHrs) > 0
            ? TotalEngHrs / (TotalEngHrs + TotalDraftHrs) : 0;
        public double DraftPct => (TotalEngHrs + TotalDraftHrs) > 0
            ? TotalDraftHrs / (TotalEngHrs + TotalDraftHrs) : 0;
        public double BillablePct => TotalAllHrs > 0
            ? TotalBillableHrs / TotalAllHrs : 0;

        /// <summary>Fee attributed proportionally based on employee's share of each project's production hours.</summary>
        public double AttributedFee { get; init; }
        /// <summary>Attributed fee ÷ production hours (eng + draft).</summary>
        public double FeePerHr => (TotalEngHrs + TotalDraftHrs) > 0
            ? AttributedFee / (TotalEngHrs + TotalDraftHrs) : 0;

        public double AvgProjectFee { get; init; }
        public string PrimaryRole { get; init; } = "";
        public string PrimaryConstructionType { get; init; } = "";

        // ── Peer Comparison ──
        /// <summary>Median Fee/Hr of employees in the same primary construction type.</summary>
        public double PeerGroupMedianFeePerHr { get; set; }
        /// <summary>This employee's Fee/Hr as % of peer group median. &gt;100 = above peers.</summary>
        public double VsPeerPct { get; set; }
        /// <summary>Number of peer employees compared against.</summary>
        public int PeerCount { get; set; }

        // ── Productivity Score (0-100) ──
        /// <summary>Billable rate: % of total hours on real billable projects, normalized 0-100.</summary>
        public double BillableRateScore { get; set; }
        /// <summary>Fee/Hr relative to portfolio — normalized 0-100 using percentile rank. Default 50 (median) when can't rank.</summary>
        public double EfficiencyScore { get; set; } = 50;
        /// <summary>% of this person's hours on projects that are on-track (not over budget). 0-100.</summary>
        public double ProjectHealthScore { get; set; }
        /// <summary>Weighted composite: (BillableRate × 0.30) + (Efficiency × 0.40) + (Health × 0.30).</summary>
        public double ProductivityScore { get; set; }

        public string ProductivityGrade => ProductivityScore switch
        {
            >= 93 => "A+",
            >= 87 => "A",
            >= 83 => "A-",
            >= 77 => "B+",
            >= 73 => "B",
            >= 70 => "B-",
            >= 67 => "C+",
            >= 63 => "C",
            >= 60 => "C-",
            >= 57 => "D+",
            >= 53 => "D",
            >= 50 => "D-",
            _ => "F"
        };

        /// <summary>Numeric sort key for Grade column — higher = better. Maps A+=13 down to F=1.</summary>
        public int GradeSortKey => ProductivityScore switch
        {
            >= 93 => 13,
            >= 87 => 12,
            >= 83 => 11,
            >= 77 => 10,
            >= 73 => 9,
            >= 70 => 8,
            >= 67 => 7,
            >= 63 => 6,
            >= 60 => 5,
            >= 57 => 4,
            >= 53 => 3,
            >= 50 => 2,
            _ => 1
        };

        public string ProductivityColor => ProductivityScore switch
        {
            >= 83 => "#16A34A",  // green (A range)
            >= 70 => "#2563EB",  // blue (B range)
            >= 60 => "#D97706",  // amber (C range)
            >= 50 => "#EA580C",  // orange (D range)
            _ => "#DC2626"       // red (F)
        };
    }
}
