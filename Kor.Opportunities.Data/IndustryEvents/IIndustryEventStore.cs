#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.IndustryEvents;

public interface IIndustryEventStore
{
    Task UpsertAsync(IndustryEventRecord record, CancellationToken ct);

    Task<IReadOnlyList<IndustryEventRow>> ListUpcomingAsync(CancellationToken ct);
}
