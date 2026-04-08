#nullable enable
namespace Kor.Operations.PMTools
{
    internal sealed class YearTrendRow
    {
        public int Year { get; init; }
        public int ProjectCount { get; init; }
        public double TotalFee { get; init; }
        public double AvgFee { get; init; }
        public double AvgFeePerHr { get; init; }
        public double AvgNetFeePerHr { get; init; }
        public double WeightedEngPct { get; init; }
        public double WeightedBillablePct { get; init; }
        public double AvgSubPct { get; init; }
        public double WeightedOverheadRatio { get; init; }
        public double TotalArOutstanding { get; init; }
        /// <summary>Firm-wide billable % for this year (from ALL tkDetail, not just filtered projects).</summary>
        public double FirmBillablePct { get; init; }
    }
}
