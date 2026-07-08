#nullable enable
namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// One possible-duplicate match surfaced by the manual-entry guard
/// (<see cref="IOpportunityStore.FindPossibleDuplicatesAsync"/>). Carries just
/// enough for the user to decide "same thing — open it" vs "genuinely new —
/// save anyway".
/// </summary>
public sealed record OpportunityDuplicateCandidate(
    string OpportunityKey,
    string Name,
    string BuyerName,
    int Status,
    string? City,
    bool HasPursuit,
    bool SameBuyer,
    double NameScore,
    DuplicateConfidence Confidence);
