#nullable enable
using System;
using System.Collections.Generic;
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
        var opt = _accessor.GetCurrent();

        // Compose the searchable text. Includes everything an ingestion source
        // is expected to populate; null fields fall through harmlessly.
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

        // Hard-reject paths short-circuit before any other math runs.
        if (opt.MinimumValueThresholdCad > 0m
            && opportunity.EstimatedValue.HasValue
            && opportunity.EstimatedValue.Value < opt.MinimumValueThresholdCad)
        {
            return new OpportunityScore(opt.HardRejectScore, RelevanceTier.HardReject);
        }

        if (HasAnyMatch(text, opt.HardRejectCountryTerms))
        {
            return new OpportunityScore(opt.HardRejectScore, RelevanceTier.HardReject);
        }

        decimal score = 0m;
        score += SumWeightedMatches(text, opt.PositiveTermWeights);
        score -= SumWeightedMatches(text, opt.NegativeTermWeights);
        score += SumWeightedMatches(text, opt.RegionTermWeights);

        // Deadline feasibility. Only applied when a deadline is set, and only
        // for non-terminal pursuits — a deadline that has already passed
        // shouldn't keep penalising a Won/Lost row.
        if (opportunity.SubmissionDeadlineUtc.HasValue
            && !IsTerminalStatus(opportunity.Status))
        {
            var daysLeft = (opportunity.SubmissionDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalDays;
            if (daysLeft > 0 && daysLeft < opt.DeadlineWarningWindowDays)
            {
                score -= opt.DeadlineCrunchPenalty;
            }
            else if (daysLeft >= opt.DeadlineWarningWindowDays)
            {
                score += opt.DeadlineFeasibleBonus;
            }
        }

        // Deltek-driven bonuses. RepeatDeveloperBonus is the base - applied
        // whenever an Opportunity is Deltek-linked. The other three (PriorWork,
        // Recommend, LifetimeFee) stack on top, gated on HasAnyHistory so a
        // brand-new Clendor entry without KOR work doesn't grab the full stack.
        if (!string.IsNullOrWhiteSpace(opportunity.DeltekClientId))
        {
            score += opt.RepeatDeveloperBonus;

            var facts = _factsAccessor.GetFacts(opportunity.DeltekClientId);
            if (facts is { HasAnyHistory: true })
            {
                if (facts.PriorWork) score += opt.PriorWorkBonus;
                if (facts.Recommend) score += opt.RecommendBonus;

                // LifetimeFee threshold: 0 disables the threshold (any history wins).
                if (opt.LifetimeFeeBonus != 0m
                    && (opt.LifetimeFeeBonusThresholdCad <= 0m
                        || facts.LifetimeFee >= opt.LifetimeFeeBonusThresholdCad))
                {
                    score += opt.LifetimeFeeBonus;
                }
            }
        }

        score = Math.Clamp(score, opt.MinScore, opt.MaxScore);
        score = decimal.Round(score, 4, MidpointRounding.AwayFromZero);

        var tier = score >= opt.TierHighThreshold ? RelevanceTier.High
            : score >= opt.TierMediumThreshold ? RelevanceTier.Medium
            : score >= opt.TierLowThreshold ? RelevanceTier.Low
            : RelevanceTier.HardReject;

        return new OpportunityScore(score, tier);
    }

    private static bool IsTerminalStatus(OpportunityStatus s) =>
        s is OpportunityStatus.Won or OpportunityStatus.Lost;

    private static decimal SumWeightedMatches(string text, IReadOnlyDictionary<string, decimal> weights)
    {
        decimal total = 0m;
        foreach (var kv in weights)
        {
            if (text.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                total += kv.Value;
            }
        }

        return total;
    }

    private static bool HasAnyMatch(string text, IReadOnlyList<string> terms)
    {
        for (var i = 0; i < terms.Count; i++)
        {
            if (text.Contains(terms[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
