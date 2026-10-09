using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Core.Scoring;

/// <summary>Transient evidence, not a column on Opportunities. Description is from
/// the newest observation (highest Id); Kind uses the shared source-kind authority.</summary>
public sealed record OpportunityScoringContext(OpportunityKind Kind, string? Description);
