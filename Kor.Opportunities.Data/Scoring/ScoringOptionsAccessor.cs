#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Core.Scoring;

namespace Kor.Opportunities.Data.Scoring;

/// <summary>
/// Caches the active <see cref="ScoringOptions"/> with a 10 s refresh window
/// so the scorer can be called per-row without hitting SQL. Falls back to
/// <see cref="ScoringOptions.KorDefaults"/> when no profile has been saved yet.
///
/// Thread-safety: reads are gated by a single lock (the cached instance is
/// never mutated; we hand out clones).
/// </summary>
public sealed class ScoringOptionsAccessor : IScoringOptionsAccessor
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly IScoringProfileStore _store;
    private readonly object _gate = new();
    private ScoringOptions _cached = ScoringOptions.KorDefaults();
    private DateTime _nextRefreshUtc = DateTime.MinValue;

    public ScoringOptionsAccessor(IScoringProfileStore store)
    {
        _store = store;
    }

    public ScoringOptions GetCurrent()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow < _nextRefreshUtc)
            {
                return Clone(_cached);
            }

            // Synchronous wait on purpose — this method is called from sync code
            // paths (scorer, list rendering) and the cache window keeps it cheap.
            // 10 s default; under SQL latency the worst case is one tick.
            ScoringOptions? loaded = null;
            try
            {
                loaded = _store.LoadAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Don't let a transient SQL error wipe the cache; keep serving the last known good value.
                _nextRefreshUtc = DateTime.UtcNow + RefreshInterval;
                return Clone(_cached);
            }

            _cached = Normalize(loaded ?? ScoringOptions.KorDefaults());
            _nextRefreshUtc = DateTime.UtcNow + RefreshInterval;
            return Clone(_cached);
        }
    }

    public ScoringOptions Update(ScoringOptions updated)
    {
        var normalized = Normalize(updated);

        _store.SaveAsync(normalized, System.Threading.CancellationToken.None).GetAwaiter().GetResult();

        lock (_gate)
        {
            _cached = normalized;
            _nextRefreshUtc = DateTime.UtcNow + RefreshInterval;
            return Clone(normalized);
        }
    }

    private static ScoringOptions Clone(ScoringOptions source) => new()
    {
        PositiveTermWeights = new Dictionary<string, decimal>(source.PositiveTermWeights, StringComparer.OrdinalIgnoreCase),
        NegativeTermWeights = new Dictionary<string, decimal>(source.NegativeTermWeights, StringComparer.OrdinalIgnoreCase),
        RegionTermWeights = new Dictionary<string, decimal>(source.RegionTermWeights, StringComparer.OrdinalIgnoreCase),
        HardRejectCountryTerms = source.HardRejectCountryTerms.ToArray(),
        MinScore = source.MinScore,
        MaxScore = source.MaxScore,
        HardRejectScore = source.HardRejectScore,
        RepeatDeveloperBonus = source.RepeatDeveloperBonus,
        PriorWorkBonus = source.PriorWorkBonus,
        RecommendBonus = source.RecommendBonus,
        LifetimeFeeBonus = source.LifetimeFeeBonus,
        LifetimeFeeBonusThresholdCad = source.LifetimeFeeBonusThresholdCad,
        DeadlineFeasibleBonus = source.DeadlineFeasibleBonus,
        DeadlineCrunchPenalty = source.DeadlineCrunchPenalty,
        DeadlineWarningWindowDays = source.DeadlineWarningWindowDays,
        MinimumValueThresholdCad = source.MinimumValueThresholdCad,
        TierHighThreshold = source.TierHighThreshold,
        TierMediumThreshold = source.TierMediumThreshold,
        TierLowThreshold = source.TierLowThreshold,
    };

    /// <summary>Trims keys, drops blanks, ensures Min &lt;= Max.</summary>
    private static ScoringOptions Normalize(ScoringOptions s)
    {
        var minScore = s.MinScore;
        var maxScore = s.MaxScore;
        if (maxScore < minScore)
        {
            (minScore, maxScore) = (maxScore, minScore);
        }

        return new ScoringOptions
        {
            PositiveTermWeights = NormalizeWeights(s.PositiveTermWeights),
            NegativeTermWeights = NormalizeWeights(s.NegativeTermWeights),
            RegionTermWeights = NormalizeWeights(s.RegionTermWeights),
            HardRejectCountryTerms = NormalizeTerms(s.HardRejectCountryTerms),
            MinScore = minScore,
            MaxScore = maxScore,
            HardRejectScore = s.HardRejectScore,
            RepeatDeveloperBonus = s.RepeatDeveloperBonus,
            PriorWorkBonus = s.PriorWorkBonus,
            RecommendBonus = s.RecommendBonus,
            LifetimeFeeBonus = s.LifetimeFeeBonus,
            LifetimeFeeBonusThresholdCad = s.LifetimeFeeBonusThresholdCad,
            DeadlineFeasibleBonus = s.DeadlineFeasibleBonus,
            DeadlineCrunchPenalty = s.DeadlineCrunchPenalty,
            DeadlineWarningWindowDays = s.DeadlineWarningWindowDays,
            MinimumValueThresholdCad = s.MinimumValueThresholdCad,
            TierHighThreshold = s.TierHighThreshold,
            TierMediumThreshold = s.TierMediumThreshold,
            TierLowThreshold = s.TierLowThreshold,
        };
    }

    private static Dictionary<string, decimal> NormalizeWeights(IReadOnlyDictionary<string, decimal>? source)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var kv in source)
        {
            var term = (kv.Key ?? string.Empty).Trim();
            if (term.Length > 0)
            {
                result[term] = kv.Value;
            }
        }

        return result;
    }

    private static string[] NormalizeTerms(IEnumerable<string>? source)
    {
        if (source is null)
        {
            return Array.Empty<string>();
        }

        return source
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
