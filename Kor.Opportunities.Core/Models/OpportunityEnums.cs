#nullable enable
namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Pursuit-lifecycle status for an <see cref="Opportunity"/>.
/// Backed by <c>opportunities.Opportunities.Status int</c>; values are stable on disk.
/// Collapsed 2026-05-15 from 9 to 5: Reviewing/Qualified folded into New;
/// NoBid/Withdrawn folded into Lost (distinction survives in <see cref="WonLostOutcome"/>).
/// </summary>
public enum OpportunityStatus
{
    New = 1,        // was Identified pre-2026-05-15; value preserved.
    Pursuing = 4,
    Submitted = 5,  // was ProposalSubmitted pre-2026-05-15; value preserved.
    Won = 6,
    Lost = 7,
}

/// <summary>
/// Buyer type for filtering and scoring. 0 = Unknown by convention.
/// </summary>
public enum BuyerType
{
    Unknown = 0,
    Municipal = 1,
    Provincial = 2,
    Federal = 3,
    Private = 4,
    InstitutionalEducation = 5,
    InstitutionalHealthcare = 6,
    NonProfit = 7,
    Other = 99,
}

/// <summary>
/// Discipline classification. 0 = Unknown by convention.
/// </summary>
public enum OpportunityDiscipline
{
    Unknown = 0,
    Structural = 1,
    Inspections = 2,
    Mixed = 3,
    OutOfScope = 99,
}

/// <summary>
/// Relevance tier from the rules-based scorer. Persisted; values stable on disk.
/// HardReject is distinct from Low so the UI can hide vs. de-emphasize.
/// </summary>
public enum RelevanceTier
{
    HardReject = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Outcome of a pursuit. NULL on the row until the opportunity reaches a terminal status.
/// </summary>
public enum WonLostOutcome
{
    Won = 1,
    Lost = 2,
    NoBid = 3,
    Withdrawn = 4,
}
