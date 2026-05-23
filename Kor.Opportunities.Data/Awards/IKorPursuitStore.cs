#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IKorPursuitStore
{
    Task<long> AddAsync(KorPursuitCreate p, CancellationToken ct);
    /// <summary>
    /// Insert if (ExternalSource, ExternalSourceKey) is new; otherwise update the
    /// existing row's fields in place. Idempotent, call from import jobs.
    /// Returns the row Id.
    /// </summary>
    Task<long> UpsertByExternalKeyAsync(KorPursuitCreate p, CancellationToken ct);
    Task<int> CountByExternalSourceAsync(string externalSource, CancellationToken ct);
    Task<KorPursuitRow?> GetAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListRecentAsync(int top, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListByBuyerCanonicalAsync(long canonicalOrgId, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListByLostToCanonicalAsync(long canonicalOrgId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, int>> GetStageCountsAsync(CancellationToken ct);
}
