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
    /// <summary>List active opportunities ordered by <c>UpdatedAtUtc</c> DESC.</summary>
    Task<IReadOnlyList<Opportunity>> ListAsync(CancellationToken ct, int maxRows = 5000, bool includeClosed = true, bool includeNonPrime = true);

    /// <summary>Returns null if no row matches.</summary>
    Task<Opportunity?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Returns null if no row matches.</summary>
    Task<Opportunity?> GetByKeyAsync(string opportunityKey, CancellationToken ct);

    /// <summary>
    /// The opportunity's already-resolved buyer canonical-org id, or null when
    /// the buyer hasn't been resolved (or the row is gone). Lean single-column
    /// read — BuyerCanonicalOrgId is deliberately NOT on the domain model /
    /// AllColumns (a model widening would ripple MapReader ordinals across
    /// every consumer); the Bazaar buyer-dossier affordance (plan 1.4) looks it
    /// up on demand instead.
    /// </summary>
    Task<long?> GetBuyerCanonicalOrgIdAsync(long opportunityId, CancellationToken ct);

    /// <summary>
    /// Manual-entry duplicate guard (2026-07-07): finds active opportunities
    /// likely to be the SAME real-world RFP as a proposed manual entry, ranked
    /// most-likely-first. Buyer matching resolves <paramref name="buyerName"/>
    /// to a canonical org (read-only) so "City of Vancouver" and "Vancouver
    /// (City of)" match; title matching uses <see cref="OpportunityDuplicateScorer"/>.
    /// Never mutates. Returns at most <paramref name="take"/> candidates whose
    /// confidence is Medium or High. Excludes the opportunity with
    /// <paramref name="excludeKey"/> (the row being edited, if any).
    /// </summary>
    Task<IReadOnlyList<OpportunityDuplicateCandidate>> FindPossibleDuplicatesAsync(
        string buyerName,
        string name,
        string? city,
        string? excludeKey,
        int take,
        CancellationToken ct);

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
