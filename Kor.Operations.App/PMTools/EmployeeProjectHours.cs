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

    /// <summary>Per-employee weekly billable and total hours from tkDetail for the last 12 weeks.</summary>
    internal sealed class EmployeeWeeklyHours
    {
        public string EmployeeId { get; init; } = "";
        public string EmployeeName { get; init; } = "";
        public DateTime WeekStart { get; init; }
        public double BillableHrs { get; init; }
        public double TotalHrs { get; init; }
    }

    /// <summary>Per-employee billing rate, raw/effective cost rate, and Partner-imputation flag.</summary>
    internal sealed class EmployeeRate
    {
        public string EmployeeId { get; init; } = "";
        public string EmployeeName { get; init; } = "";
        public double BillingRate { get; init; }
        public double CostRate { get; init; }
        public bool IsPartner { get; init; }
        public double EffectiveCostRate { get; init; }
    }
}
