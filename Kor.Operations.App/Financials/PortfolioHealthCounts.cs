#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.Financials
{
    /// <summary>
    /// The one definition of the portfolio trend's three buckets, shared by the Financials screen and the morning
    /// composer: Healthy = High Confidence + Watch, Watch = At Risk, Critical = Critical.
    /// </summary>
    public sealed record PortfolioHealthCounts(int Healthy, int Watch, int Critical, int Total)
    {
        public static PortfolioHealthCounts From(IEnumerable<UtilizationRow> rows) => FromLevels(rows.Select(r => r.ConfidenceLevel));

        public static PortfolioHealthCounts FromLevels(IEnumerable<DeliveryConfidenceLevel> levels)
        {
            int healthy = 0, watch = 0, critical = 0, total = 0;
            foreach (var level in levels)
            {
                total++;
                switch (level)
                {
                    case DeliveryConfidenceLevel.Critical: critical++; break;
                    case DeliveryConfidenceLevel.AtRisk: watch++; break;
                    default: healthy++; break;
                }
            }
            return new PortfolioHealthCounts(healthy, watch, critical, total);
        }
    }
}
