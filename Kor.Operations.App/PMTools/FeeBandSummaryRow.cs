#nullable enable
namespace Kor.Operations.PMTools
{
    internal sealed class FeeBandSummaryRow
    {
        public string Band { get; init; } = "";
        public int ProjectCount { get; init; }
        public double TotalFee { get; init; }
        public double AvgFeePerHr { get; init; }
        public double AvgNetFeePerHr { get; init; }
        public double WeightedEngPct { get; init; }
        public double WeightedBillablePct { get; init; }
        public double AvgSubPct { get; init; }
        public double AvgOverheadRatio { get; init; }
        public double TotalArOutstanding { get; init; }
    }
}
