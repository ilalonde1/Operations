#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Ingestion;

public interface IJobRunStore
{
    Task<long> StartRunAsync(string jobName, CancellationToken ct);

    Task CompleteRunAsync(long runId, bool success, string? summary, string? error, CancellationToken ct);

    Task<IReadOnlyList<JobRunRow>> ListRecentAsync(int take, CancellationToken ct);
}
