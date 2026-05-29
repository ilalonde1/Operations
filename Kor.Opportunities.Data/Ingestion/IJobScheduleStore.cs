#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Ingestion;

public interface IJobScheduleStore
{
    Task UpsertScheduleAsync(string jobName, string? cron, bool enabled, CancellationToken ct);

    Task<IReadOnlyList<JobScheduleRow>> ListWithLastRunAsync(CancellationToken ct);
}
