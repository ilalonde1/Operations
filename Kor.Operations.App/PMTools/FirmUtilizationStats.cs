#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Firm-wide billable utilization computed from ALL tkDetail hours (no WBS1 filter).
    /// Billable = LaborCode NOT IN (70, 80), matching Staff Utilization definition.
    /// </summary>
    internal sealed class FirmUtilizationStats
    {
        public double TotalHrs { get; init; }
        public double BillableHrs { get; init; }
        public double BillablePct { get; init; }
        public Dictionary<int, (double Total, double Billable)> ByYear { get; init; } = new();
    }
}
