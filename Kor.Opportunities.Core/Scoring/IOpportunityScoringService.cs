#nullable enable
using System.Collections.Generic;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>Result of scoring one <see cref="Opportunity"/>.</summary>
public readonly record struct OpportunityScore(decimal Score, RelevanceTier Tier);

/// <summary>One contributing factor in a score explanation. Label is a
/// short user-facing string ("Region: Vancouver", "Repeat developer",
/// "Deadline crunch (&lt; 14 days)"). Delta is the signed contribution
/// in the same units as <see cref="OpportunityScore.Score"/>.</summary>
public sealed record ScoreFactor(string Label, decimal Delta)
{
    public bool IsPositive => Delta >= 0m;
    public string DeltaDisplay => (Delta >= 0m ? "+" : "") + Delta.ToString("0.##");
}

/// <summary>Full audit-trail of a single scoring run. Factors are pre-sorted
/// by absolute impact descending so the UI can render them top-down without
/// post-processing. When <see cref="HardRejected"/> is true, Factors holds
/// exactly one entry describing the reject reason.</summary>
public sealed record OpportunityScoreExplanation(
    decimal Score,
    RelevanceTier Tier,
    IReadOnlyList<ScoreFactor> Factors,
    bool HardRejected);

/// <summary>
/// Pure-function scorer. Implementations must not have side effects and must
/// be safe to call from any thread.
/// </summary>
public interface IOpportunityScoringService
{
    /// <summary>Compute the relevance score and tier for the given opportunity
    /// against the currently-cached <see cref="ScoringOptions"/>.</summary>
    OpportunityScore Score(Opportunity opportunity);

    OpportunityScoreExplanation Explain(Opportunity opportunity);
}
