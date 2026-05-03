#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// Read/write access to <c>opportunities.Opportunities</c>. RowVersion is the
/// concurrency token on every update path; stale tokens raise
/// <see cref="OpportunityConcurrencyException"/>.
/// </summary>
public interface IOpportunityStore
{
    /// <summary>List all opportunities ordered by <c>UpdatedAtUtc</c> DESC.</summary>
    Task<IReadOnlyList<Opportunity>> ListAsync(CancellationToken ct);

    /// <summary>Returns null if no row matches.</summary>
    Task<Opportunity?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Returns null if no row matches.</summary>
    Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct);

    /// <summary>
    /// Inserts a new row. The supplied <see cref="Opportunity.Id"/> is ignored
    /// (IDENTITY column). Returns the row as persisted, with the assigned Id,
    /// CreatedAt/UpdatedAt timestamps, and RowVersion populated.
    /// </summary>
    Task<Opportunity> InsertAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct);

    /// <summary>
    /// Updates an existing row. The supplied <see cref="Opportunity.RowVersion"/>
    /// must match the row currently in the database; if it doesn't, throws
    /// <see cref="OpportunityConcurrencyException"/>. Returns the row as
    /// persisted with the bumped RowVersion.
    /// </summary>
    Task<Opportunity> UpdateAsync(Opportunity opportunity, string actorDisplay, CancellationToken ct);

    /// <summary>
    /// Atomic status transition. Updates <c>Status</c> + the matching
    /// <c>...SinceUtc</c> milestone column in a single UPDATE so the lifecycle
    /// timeline never falls out of sync. Optimistic-concurrency on RowVersion;
    /// throws <see cref="OpportunityConcurrencyException"/> on stale token.
    /// Returns the row as persisted with the bumped RowVersion.
    /// </summary>
    Task<Opportunity> ChangeStatusAsync(
        long id,
        OpportunityStatus newStatus,
        byte[] expectedRowVersion,
        string actorDisplay,
        CancellationToken ct);
}
