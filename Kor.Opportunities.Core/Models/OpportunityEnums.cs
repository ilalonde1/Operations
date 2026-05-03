#nullable enable
namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Pursuit-lifecycle status for an <see cref="Opportunity"/>.
/// Backed by <c>opportunities.Opportunities.Status int</c>; values are stable on disk.
/// </summary>
public enum OpportunityStatus
{
    Identified = 1,
    Reviewing = 2,
    Qualified = 3,
    Pursuing = 4,
    ProposalSubmitted = 5,
    Won = 6,
    Lost = 7,
    NoBid = 8,
    Withdrawn = 9,
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
