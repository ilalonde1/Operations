#nullable enable
using System;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// Raised when an UPDATE or status-change against
/// <c>opportunities.Opportunities</c> matches no rows because the supplied
/// <c>RowVersion</c> is stale. Caller should re-fetch the canonical row,
/// re-apply the user's change, and retry — never auto-overwrite.
/// </summary>
public sealed class OpportunityConcurrencyException : Exception
{
    public OpportunityConcurrencyException(long opportunityId)
        : base($"Opportunity Id={opportunityId} was updated by another user (or the row no longer exists). Reload and try again.")
    {
        OpportunityId = opportunityId;
    }

    public long OpportunityId { get; }
}
