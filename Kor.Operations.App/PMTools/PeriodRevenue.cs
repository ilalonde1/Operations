#nullable enable
namespace Kor.Operations.PMTools
{
    /// <summary>
    /// One period's revenue and billing data for a project.
    /// Loaded from PRSummaryMain grouped by WBS1 + Period.
    /// </summary>
    internal sealed record PeriodRevenue(string Period, double Revenue, double Billed);
}
