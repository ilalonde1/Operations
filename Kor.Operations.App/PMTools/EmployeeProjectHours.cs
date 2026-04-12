#nullable enable
namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Raw employee-project hour data from tkDetail.
    /// One row per employee per project.
    /// </summary>
    internal sealed class EmployeeProjectHours
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

    internal sealed class QuarterlyEmployeeHours
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
}
