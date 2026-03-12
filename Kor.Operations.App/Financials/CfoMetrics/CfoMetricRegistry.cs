#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.Financials.CfoMetrics
{
    /// <summary>
    /// Central registry for CFO metrics.
    /// Keeps metric instantiation in one place and provides name-based lookup for UI.
    /// </summary>
    public sealed class CfoMetricRegistry
    {
        private readonly List<ICfoMetric> _all;
        private readonly Dictionary<string, ICfoMetric> _byName;

        public CfoMetricRegistry()
        {
            _all = new List<ICfoMetric>
            {
                new DeliveryConfidenceMetric(),
                new PercentHoursSpentMetric(),
                new BudgetBurnRateMetric(),
                new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Healthy),
                new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Watch),
                new PortfolioHealthCountsMetric(PortfolioHealthCountsMetric.Bucket.Critical),
            };

            _byName = _all
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ICfoMetric> GetAllMetrics() => _all;

        public ICfoMetric? TryGetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return _byName.TryGetValue(name.Trim(), out var m) ? m : null;
        }
    }
}
