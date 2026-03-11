namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Portfolio-level count metric, parameterized by a target health bucket.
    /// This is included to let project detail pages show the broader portfolio context.
    /// </summary>
    public sealed class PortfolioHealthCountsMetric : ICfoMetric
    {
        public enum Bucket
        {
            Healthy = 0,
            Watch = 1,
            Critical = 2
        }

        public Bucket TargetBucket { get; }

        public PortfolioHealthCountsMetric(Bucket targetBucket)
        {
            TargetBucket = targetBucket;
        }

        public string Name => TargetBucket switch
        {
            Bucket.Healthy => "Portfolio Healthy Projects (Count)",
            Bucket.Watch => "Portfolio Watch Projects (Count)",
            Bucket.Critical => "Portfolio Critical Projects (Count)",
            _ => "Portfolio Projects (Count)"
        };

        public string Description =>
            "WHAT:\n" +
            "Portfolio-level count of projects in a given health bucket.\n\n" +
            "WHY IT MATTERS:\n" +
            "Gives executives immediate context for whether this project is part of a broader systemic trend.\n\n" +
            "HOW IT IS CALCULATED:\n" +
            "Reads the current portfolio snapshot counts (Healthy/Watch/Critical) when available.";

        public string Formula => TargetBucket switch
        {
            Bucket.Healthy => "Count = PortfolioHealthyCount",
            Bucket.Watch => "Count = PortfolioWatchCount",
            Bucket.Critical => "Count = PortfolioCriticalCount",
            _ => "Count = (not defined)"
        };

        public decimal ComputeValue(ProjectData data)
        {
            data ??= ProjectData.Create();
            var c = data.PortfolioCounts;
            if (c == null)
                return 0m;

            return TargetBucket switch
            {
                Bucket.Healthy => c.Healthy,
                Bucket.Watch => c.Watch,
                Bucket.Critical => c.Critical,
                _ => 0m
            };
        }
    }
}
