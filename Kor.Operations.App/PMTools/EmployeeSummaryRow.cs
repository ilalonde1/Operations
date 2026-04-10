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
        public double BillablePct => TotalAllHrs > 0
            ? TotalBillableHrs / TotalAllHrs : 0;

        /// <summary>Fee attributed proportionally based on employee's share of each project's production hours.</summary>
        public double AttributedFee { get; init; }
        /// <summary>Attributed fee ÷ production hours (eng + draft).</summary>
        public double FeePerHr => (TotalEngHrs + TotalDraftHrs) > 0
            ? AttributedFee / (TotalEngHrs + TotalDraftHrs) : 0;

        public double AvgProjectFee { get; init; }
        public string PrimaryRole { get; init; } = "";
    }
}
