#nullable enable
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>Result of scoring one <see cref="Opportunity"/>.</summary>
public readonly record struct OpportunityScore(decimal Score, RelevanceTier Tier);

/// <summary>
/// Pure-function scorer. Implementations must not have side effects and must
/// be safe to call from any thread.
/// </summary>
public interface IOpportunityScoringService
{
    /// <summary>Compute the relevance score and tier for the given opportunity
    /// against the currently-cached <see cref="ScoringOptions"/>.</summary>
    OpportunityScore Score(Opportunity opportunity);
}
