#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Awards;

public interface IOpportunityInterestedFirmStore
{
    /// <summary>
    /// Idempotent upsert. Keys on (OpportunityId, RawFirmName). On match,
    /// bumps LastSeenAtUtc + updates ResolvedCanonicalOrgId / ResolvedKind /
    /// Notes if currently NULL.
    /// </summary>
    Task UpsertAsync(
        long opportunityId,
        string rawFirmName,
        long? resolvedCanonicalOrgId,
        string? resolvedKind,
        string sourcePortal,
        string? sourcePostingUrl,
        DateTimeOffset? expressedAtUtc,
        string? notes,
        string? rawJson,
        CancellationToken ct);

    /// <summary>
    /// Returns all interested-firm rows for a given opportunity, ordered by
    /// most recent expressed-interest first.
    /// </summary>
    Task<IReadOnlyList<OpportunityInterestedFirm>> ListByOpportunityAsync(long opportunityId, CancellationToken ct);

    /// <summary>
    /// Returns all expressed-interest rows pointing at a given canonical
    /// (architect, competitor, GC, etc.) for surfacing on the org dossier.
    /// </summary>
    Task<IReadOnlyList<OpportunityInterestedFirm>> ListByCanonicalOrgAsync(long canonicalOrgId, int limit, CancellationToken ct);

    /// <summary>
    /// Backfill helper: re-resolve canonical org id for a previously-saved
    /// row when the resolver has more data than the first attempt could see.
    /// </summary>
    Task<int> RepointCanonicalAsync(long interestedFirmId, long resolvedCanonicalOrgId, string? resolvedKind, CancellationToken ct);
}
