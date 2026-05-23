#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IBuildingPermitStore
{
    Task<IReadOnlyList<PermitSourceRow>> ListActiveSourcesAsync(CancellationToken ct);

    /// <summary>Upsert by (PermitSourceId, ExternalId). Returns the row Id.</summary>
    Task<long> UpsertAsync(BuildingPermitUpsert permit, CancellationToken ct);

    Task SetOwnerCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct);
    Task SetApplicantCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct);
    Task SetContractorCanonicalAsync(long permitId, long? canonicalOrgId, CancellationToken ct);

    Task UpdateSourceHeartbeatAsync(long sourceId, string? errorMessage, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);
    Task<int> CountBySourceAsync(long sourceId, CancellationToken ct);
}
