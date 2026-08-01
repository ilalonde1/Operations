#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.AwardPrograms;

public interface IAwardProgramStore
{
    Task<DateTimeOffset?> GetLastCatalogRefreshUtcAsync(CancellationToken ct);

    Task<int> UpsertAsync(IReadOnlyList<AwardProgramUpsert> programs, CancellationToken ct);

    Task<IReadOnlyList<AwardProgramRow>> ListUpcomingAsync(int take, CancellationToken ct);
}
