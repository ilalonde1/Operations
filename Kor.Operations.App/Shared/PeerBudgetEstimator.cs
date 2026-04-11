#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.Shared
{
    /// <summary>
    /// Peer-based budget estimation shared between FinancialsService and HistoricalAnalyticsService.
    /// Finds similar completed projects (fee ±30-50%, same construction type/phase/category)
    /// and uses their median eng/draft hours as the budget estimate.
    /// </summary>
    internal static class PeerBudgetEstimator
    {
        public sealed class PeerProject
        {
            public string Wbs1 { get; set; } = "";
            public double Fee { get; set; }
            public string Phase { get; set; } = "";
            public string ConstructionType { get; set; } = "";
            public string ProjectCategory { get; set; } = "";
            public double EngHrs { get; set; }
            public double DraftHrs { get; set; }
        }

        /// <summary>
        /// Find similar peers and return median eng/draft hours.
        /// Adaptive fee range (±30% then ±50%), tiered matching on type/category/phase.
        /// Returns (0,0,0) if fewer than 3 peers found.
        /// </summary>
        public static (double engHrs, double draftHrs, int peerCount) Estimate(
            double fee, string? phase, string? constructionType, string? projectCategory,
            List<PeerProject> allPeers, string excludeWbs1)
        {
            if (fee <= 0 || allPeers.Count == 0) return (0, 0, 0);

            var ph = (phase ?? "").Trim();
            var ct = (constructionType ?? "").Trim();
            var cat = (projectCategory ?? "").Trim();
            var hasPhase = !string.IsNullOrWhiteSpace(ph);
            var hasType = !string.IsNullOrWhiteSpace(ct);
            var hasCat = !string.IsNullOrWhiteSpace(cat);

            // Adaptive fee range: try ±30% first for tighter peers, widen to ±50% if needed
            List<PeerProject>? result = null;
            foreach (var pct in new[] { 0.30, 0.50 })
            {
                var feeMin = fee * (1.0 - pct);
                var feeMax = fee * (1.0 + pct);

                var candidates = allPeers
                    .Where(p => !p.Wbs1.Equals(excludeWbs1, StringComparison.OrdinalIgnoreCase)
                             && p.Fee >= feeMin && p.Fee <= feeMax)
                    .ToList();

                if (candidates.Count < 3) continue;

                // Tiered: type+cat+phase → type+phase → type+cat → type → phase → all
                var pools = new List<List<PeerProject>>();

                if (hasType && hasCat && hasPhase)
                    pools.Add(candidates.Where(p =>
                        p.ConstructionType.Equals(ct, StringComparison.OrdinalIgnoreCase)
                        && p.ProjectCategory.Equals(cat, StringComparison.OrdinalIgnoreCase)
                        && p.Phase.Equals(ph, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType && hasPhase)
                    pools.Add(candidates.Where(p =>
                        p.ConstructionType.Equals(ct, StringComparison.OrdinalIgnoreCase)
                        && p.Phase.Equals(ph, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType && hasCat)
                    pools.Add(candidates.Where(p =>
                        p.ConstructionType.Equals(ct, StringComparison.OrdinalIgnoreCase)
                        && p.ProjectCategory.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType)
                    pools.Add(candidates.Where(p =>
                        p.ConstructionType.Equals(ct, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasPhase)
                    pools.Add(candidates.Where(p =>
                        p.Phase.Equals(ph, StringComparison.OrdinalIgnoreCase)).ToList());

                pools.Add(candidates);

                var pool = pools.FirstOrDefault(p => p.Count >= 3);
                if (pool != null && pool.Count >= 3)
                {
                    result = pool;
                    break;
                }
            }

            if (result == null || result.Count < 3) return (0, 0, 0);

            // Top 8 by fee proximity
            var peers = result
                .OrderBy(p => Math.Abs(p.Fee - fee))
                .Take(8)
                .ToList();

            return (Median(peers.Select(p => p.EngHrs).ToList()),
                    Median(peers.Select(p => p.DraftHrs).ToList()),
                    peers.Count);
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }
    }
}
