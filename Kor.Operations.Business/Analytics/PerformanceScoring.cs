#nullable enable

namespace Kor.Operations.PMTools;

public static class PerformanceScoring
{
    public static void ScorePmDmGroups(List<PmPerformanceSummaryRow> groups)
    {
        if (groups.Count == 0) return;

        // Estimation Accuracy: percentile rank of |AvgEngDelta| — LOWER absolute delta = HIGHER score
        var absDeltas = groups.Where(r => Math.Abs(r.AvgEngDelta) > 0).Select(r => Math.Abs(r.AvgEngDelta)).OrderBy(v => v).ToList();
        var nDelta = absDeltas.Count;

        // Revenue Efficiency: percentile rank of AvgFeePerHr — HIGHER = better
        var feePerHrs = groups.Where(r => r.AvgFeePerHr > 0).Select(r => r.AvgFeePerHr).OrderBy(v => v).ToList();
        var nFee = feePerHrs.Count;

        foreach (var row in groups)
        {
            // Estimation Accuracy — inverted percentile (lower delta = higher rank)
            if (nDelta > 1 && Math.Abs(row.AvgEngDelta) > 0)
            {
                var absDelta = Math.Abs(row.AvgEngDelta);
                var above = absDeltas.Count(v => v > absDelta);
                row.EstimationAccuracyScore = Math.Min(100, ((double)above / (nDelta - 1)) * 100);
            }

            // Revenue Efficiency — standard percentile
            if (nFee > 1 && row.AvgFeePerHr > 0)
            {
                var below = feePerHrs.Count(v => v < row.AvgFeePerHr);
                row.RevenueEfficiencyScore = Math.Min(100, ((double)below / (nFee - 1)) * 100);
            }

            // Composite: Delivery 30% + Estimation 30% + Revenue 20% + AR 20%
            row.PerformanceScore = Math.Round(
                row.DeliveryHealthScore * 0.30 +
                row.EstimationAccuracyScore * 0.30 +
                row.RevenueEfficiencyScore * 0.20 +
                row.ArManagementScore * 0.20, 0);
        }
    }

    public static void ScoreEmployeesSecondPass(
        List<EmployeeSummaryRow> groups,
        IReadOnlyList<EmployeeProjectHours> employeeProjectHours,
        Func<string, bool> projectIsKnown)
    {
        if (groups.Count > 0)
        {
            var feePerHrs = groups.Where(r => r.FeePerHr > 0).Select(r => r.FeePerHr).OrderBy(v => v).ToList();
            var n = feePerHrs.Count;
            foreach (var row in groups)
            {
                if (n > 1 && row.FeePerHr > 0)
                {
                    // Percentile rank: (values below this) / (n - 1) gives 0-100 range
                    // Lowest = 0, highest = 100, properly distributed
                    var below = feePerHrs.Count(v => v < row.FeePerHr);
                    row.EfficiencyScore = Math.Min(100, ((double)below / (n - 1)) * 100);
                }
                else if (n == 1 && row.FeePerHr > 0)
                {
                    // Only one employee with hours — default to median
                    row.EfficiencyScore = 50;
                }
                // else: no FeePerHr data — EfficiencyScore stays at 50 (default)

                // Composite: Billable Rate 30% + Efficiency 40% + Project Health 30%
                row.ProductivityScore = Math.Round(
                    row.BillableRateScore * 0.30 +
                    row.EfficiencyScore * 0.40 +
                    row.ProjectHealthScore * 0.30, 0);

                // Consistency: coefficient of variation of hours across projects (lower = steadier workload)
                var hrsPerProject = employeeProjectHours
                    .Where(e => e.EmployeeId.Equals(row.EmployeeId, StringComparison.OrdinalIgnoreCase)
                        && projectIsKnown(e.Wbs1) && (e.EngHrs + e.DraftHrs) > 0)
                    .Select(e => e.EngHrs + e.DraftHrs)
                    .ToList();
                if (hrsPerProject.Count >= 3)
                {
                    var mean = hrsPerProject.Average();
                    var stdDev = Math.Sqrt(hrsPerProject.Sum(h => (h - mean) * (h - mean)) / hrsPerProject.Count);
                    row.ConsistencyScore = mean > 0 ? stdDev / mean : 0;
                }

                // Peer comparison: compare Fee/Hr against employees with the same primary construction type
                if (row.FeePerHr > 0 && !string.IsNullOrWhiteSpace(row.PrimaryConstructionType))
                {
                    var peerGroup = groups
                        .Where(p => p != row
                            && p.FeePerHr > 0
                            && p.PrimaryConstructionType.Equals(row.PrimaryConstructionType, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.FeePerHr)
                        .ToList();

                    if (peerGroup.Count >= 2)
                    {
                        var median = Median(peerGroup);
                        row.PeerGroupMedianFeePerHr = median;
                        row.VsPeerPct = median > 0 ? (row.FeePerHr / median) * 100 : 0;
                        row.PeerCount = peerGroup.Count;
                    }
                }
            }
        }
    }

    public static void ScoreEmployeesBackfillPass(List<EmployeeSummaryRow> groups)
    {
        var feePerHrs = groups.Where(r => r!.FeePerHr > 0).Select(r => r!.FeePerHr).OrderBy(v => v).ToList();
        var n = feePerHrs.Count;
        foreach (var row in groups)
        {
            if (n > 1 && row!.FeePerHr > 0)
            {
                var below = feePerHrs.Count(v => v < row.FeePerHr);
                row.EfficiencyScore = Math.Min(100, ((double)below / (n - 1)) * 100);
            }
            row!.ProductivityScore = Math.Round(
                row.BillableRateScore * 0.30 + row.EfficiencyScore * 0.40 + row.ProjectHealthScore * 0.30, 0);
        }
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        var n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
