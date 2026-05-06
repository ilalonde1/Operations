#nullable enable
using System;

namespace Kor.Operations.Financials
{
    public sealed class PortfolioTrendPoint
    {
        public DateTime SnapshotDate { get; }
        public string DateLabel { get; }
        public int HealthyCount { get; }
        public int WatchCount { get; }
        public int CriticalCount { get; }
        public int TotalProjects { get; }

        public string HealthyTooltip => $"{HealthyCount} projects - Healthy";
        public string WatchTooltip => $"{WatchCount} projects - Watch";
        public string CriticalTooltip => $"{CriticalCount} projects - Critical";

        public PortfolioTrendPoint(DateTime snapshotDate, int healthy, int watch, int critical, int total)
        {
            SnapshotDate = snapshotDate.Date;
            DateLabel = snapshotDate.ToString("MM-dd");
            HealthyCount = healthy;
            WatchCount = watch;
            CriticalCount = critical;
            TotalProjects = total;
        }
    }
}
