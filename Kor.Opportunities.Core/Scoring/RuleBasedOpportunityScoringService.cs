#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// Pure-rules implementation of <see cref="IOpportunityScoringService"/>.
/// Mirrors the Contract Radar skeleton (RuleBasedJobScoringService) but with
/// every tunable in <see cref="ScoringOptions"/> — no in-source term arrays
/// (plan §16 risk #3).
/// </summary>
public sealed class RuleBasedOpportunityScoringService : IOpportunityScoringService
{
    private readonly IScoringOptionsAccessor _accessor;
    private readonly IDeltekClientFactsAccessor _factsAccessor;

    public RuleBasedOpportunityScoringService(
        IScoringOptionsAccessor accessor,
        IDeltekClientFactsAccessor factsAccessor)
    {
        _accessor = accessor;
        _factsAccessor = factsAccessor ?? throw new ArgumentNullException(nameof(factsAccessor));
    }

    public OpportunityScore Score(Opportunity opportunity)
    {
        var ex = Explain(opportunity);
        return new OpportunityScore(ex.Score, ex.Tier);
    }

    public OpportunityScoreExplanation Explain(Opportunity opportunity)
    {
        var opt = _accessor.GetCurrent();

        var text = string.Join(' ', new[]
        {
            opportunity.Name,
            opportunity.BuyerName,
            opportunity.ProjectAddress ?? string.Empty,
            opportunity.ProjectCity ?? string.Empty,
            opportunity.ProjectProvince ?? string.Empty,
            opportunity.ConstructionType ?? string.Empty,
            opportunity.ProjectCategory ?? string.Empty,
            opportunity.OutcomeReason ?? string.Empty,
        });

        // Hard-reject paths short-circuit. Single factor explaining the reject.
        if (opt.MinimumValueThresholdCad > 0m
            && opportunity.EstimatedValue.HasValue
            && opportunity.EstimatedValue.Value < opt.MinimumValueThresholdCad)
        {
            var f = new ScoreFactor(
                $"Hard reject: value {opportunity.EstimatedValue.Value:N0} {opportunity.EstimatedValueCurrency} below {opt.MinimumValueThresholdCad:N0} CAD minimum",
                opt.HardRejectScore);
            return new OpportunityScoreExplanation(
                opt.HardRejectScore,
                RelevanceTier.HardReject,
                new[] { f },
                HardRejected: true);
        }

        var hardCountry = FirstMatch(text, opt.HardRejectCountryTerms);
        if (hardCountry is not null)
        {
            var f = new ScoreFactor($"Hard reject: country term '{hardCountry}'", opt.HardRejectScore);
            return new OpportunityScoreExplanation(
                opt.HardRejectScore,
                RelevanceTier.HardReject,
                new[] { f },
                HardRejected: true);
        }

        var factors = new List<ScoreFactor>();

        AppendWeightedMatches(text, opt.PositiveTermWeights, prefix: "Match: ", sign: +1, factors);
        AppendWeightedMatches(text, opt.NegativeTermWeights, prefix: "Negative: ", sign: -1, factors);
        AppendWeightedMatches(text, opt.RegionTermWeights, prefix: "Region: ", sign: +1, factors);

        if (opportunity.SubmissionDeadlineUtc.HasValue && !IsTerminalStatus(opportunity.Status))
        {
            var daysLeft = (opportunity.SubmissionDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalDays;
            if (daysLeft > 0 && daysLeft < opt.DeadlineWarningWindowDays && opt.DeadlineCrunchPenalty != 0m)
            {
                factors.Add(new ScoreFactor(
                    $"Deadline crunch (< {opt.DeadlineWarningWindowDays} days)",
                    -opt.DeadlineCrunchPenalty));
            }
            else if (daysLeft >= opt.DeadlineWarningWindowDays && opt.DeadlineFeasibleBonus != 0m)
            {
                factors.Add(new ScoreFactor(
                    $"Deadline feasible (>= {opt.DeadlineWarningWindowDays} days)",
                    opt.DeadlineFeasibleBonus));
            }
        }

        if (!string.IsNullOrWhiteSpace(opportunity.DeltekClientId))
        {
            if (opt.RepeatDeveloperBonus != 0m)
            {
                factors.Add(new ScoreFactor("Deltek-linked (repeat developer)", opt.RepeatDeveloperBonus));
            }

            var facts = _factsAccessor.GetFacts(opportunity.DeltekClientId);
            if (facts is { HasAnyHistory: true })
            {
                if (facts.PriorWork && opt.PriorWorkBonus != 0m)
                {
                    factors.Add(new ScoreFactor("Prior work in Deltek", opt.PriorWorkBonus));
                }
                if (facts.Recommend && opt.RecommendBonus != 0m)
                {
                    factors.Add(new ScoreFactor("Recommended client", opt.RecommendBonus));
                }
                if (opt.LifetimeFeeBonus != 0m
                    && (opt.LifetimeFeeBonusThresholdCad <= 0m
                        || facts.LifetimeFee >= opt.LifetimeFeeBonusThresholdCad))
                {
                    factors.Add(new ScoreFactor(
                        $"Lifetime fees >= {opt.LifetimeFeeBonusThresholdCad:N0} CAD",
                        opt.LifetimeFeeBonus));
                }
            }
        }

        var raw = factors.Sum(f => f.Delta);
        var clamped = Math.Clamp(raw, opt.MinScore, opt.MaxScore);
        var score = decimal.Round(clamped, 4, MidpointRounding.AwayFromZero);

        var tier = score >= opt.TierHighThreshold ? RelevanceTier.High
            : score >= opt.TierMediumThreshold ? RelevanceTier.Medium
            : score >= opt.TierLowThreshold ? RelevanceTier.Low
            : RelevanceTier.HardReject;

        var sorted = factors
            .OrderByDescending(f => Math.Abs(f.Delta))
            .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OpportunityScoreExplanation(score, tier, sorted, HardRejected: false);
    }

    private static void AppendWeightedMatches(
        string text,
        IReadOnlyDictionary<string, decimal> weights,
        string prefix,
        int sign,
        List<ScoreFactor> sink)
    {
        foreach (var kv in weights)
        {
            if (kv.Value == 0m) continue;
            if (text.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                // Region weights already carry signs; positive/negative buckets need
                // sign applied. The caller passes +1 for positive/region and -1 for
                // negative so the sum still matches Score()'s original arithmetic.
                sink.Add(new ScoreFactor(prefix + kv.Key, sign * kv.Value));
            }
        }
    }

    private static string? FirstMatch(string text, IReadOnlyList<string> terms)
    {
        for (var i = 0; i < terms.Count; i++)
        {
            if (text.Contains(terms[i], StringComparison.OrdinalIgnoreCase))
            {
                return terms[i];
            }
        }

        return null;
    }

    private static bool IsTerminalStatus(OpportunityStatus s) =>
        s is OpportunityStatus.Won or OpportunityStatus.Lost;
}
