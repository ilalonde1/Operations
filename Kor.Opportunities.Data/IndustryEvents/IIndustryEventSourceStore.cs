#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.IndustryEvents;

public interface IIndustryEventSourceStore
{
    /// <summary>
    /// Guarded INSERT keyed on <c>CalendarUrl</c>. Never overwrites an existing
    /// row, so an operator can disable or retune a source in the database and
    /// the next bootstrap will respect it.
    /// </summary>
    Task<IndustryEventSourceRow> EnsureAsync(IndustryEventSourceSeed seed, CancellationToken ct);

    Task<IReadOnlyList<IndustryEventSourceRow>> ListActiveAsync(CancellationToken ct);

    Task<IReadOnlyList<IndustryEventSourceRow>> ListAllAsync(CancellationToken ct);

    Task UpdateHeartbeatAsync(long sourceId, int? eventCount, string? errorMessage, CancellationToken ct);
}
