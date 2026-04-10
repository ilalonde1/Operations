#nullable enable
namespace Kor.Operations.PMTools
{
    internal sealed class ConstructionTypeSummaryRow
    {
        public string ConstructionType { get; init; } = "";
        public int ProjectCount { get; init; }
        public double TotalFee { get; init; }
        public double AvgFeePerHr { get; init; }
        public double AvgNetFeePerHr { get; init; }
        public double WeightedEngPct { get; init; }
        public double AvgSubPct { get; init; }
        public double TotalArOutstanding { get; init; }
        public double MedianFeePerHr { get; init; }
        public double BudgetAccuracyPct { get; init; }
        public int ClosedProjectCount { get; init; }
    }
}
