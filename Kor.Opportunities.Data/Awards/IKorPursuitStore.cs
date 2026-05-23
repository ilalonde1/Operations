#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IKorPursuitStore
{
    Task<long> AddAsync(KorPursuitCreate p, CancellationToken ct);
    Task<KorPursuitRow?> GetAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListRecentAsync(int top, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListByBuyerCanonicalAsync(long canonicalOrgId, CancellationToken ct);
    Task<IReadOnlyList<KorPursuitRow>> ListByLostToCanonicalAsync(long canonicalOrgId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, int>> GetStageCountsAsync(CancellationToken ct);
}
