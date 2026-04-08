#nullable enable
namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Aggregated row for the PM Performance Summary view.
    /// One instance per project manager, computed from filtered HistoricalProjectRows.
    /// </summary>
    internal sealed class PmPerformanceSummaryRow
    {
        public string Pm { get; init; } = "";
        public int ProjectCount { get; init; }
        public double TotalFee { get; init; }
        public double TotalFeeBilled { get; init; }
        public double WeightedPctBilled => TotalFee > 0 ? TotalFeeBilled / TotalFee : 0;

        public double TotalEngHrs { get; init; }
        public double TotalDraftHrs { get; init; }
        public double TotalAllHrs { get; init; }
        public double EngPct => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalEngHrs / (TotalEngHrs + TotalDraftHrs) : 0;
        public double DraftPct => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalDraftHrs / (TotalEngHrs + TotalDraftHrs) : 0;

        /// <summary>Total fee ÷ total production hours (eng + draft).</summary>
        public double AvgFeePerHr => (TotalEngHrs + TotalDraftHrs) > 0 ? TotalFee / (TotalEngHrs + TotalDraftHrs) : 0;

        public double TotalSubCost { get; init; }
        public double SubPctOfFee => TotalFee > 0 ? TotalSubCost / TotalFee : 0;

        public double TotalArOutstanding { get; init; }
        public double TotalAr90Plus { get; init; }

        public double AvgEngDelta { get; init; }
        public double AvgDraftDelta { get; init; }
    }
}
