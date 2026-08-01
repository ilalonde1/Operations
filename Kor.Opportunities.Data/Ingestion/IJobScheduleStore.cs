#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Ingestion;

public interface IJobScheduleStore
{
    Task UpsertScheduleAsync(string jobName, string? cron, bool enabled, string? category, CancellationToken ct);

    Task<IReadOnlyList<JobScheduleRow>> ListWithLastRunAsync(CancellationToken ct);

    Task UpdateEnabledAsync(string jobName, bool enabled, CancellationToken ct);

    Task UpdateLastReportPathAsync(string jobName, string? reportPath, CancellationToken ct);

    Task UpdateNextFireAsync(string jobName, DateTimeOffset? nextFireAtUtc, CancellationToken ct);

    Task<long> EnqueueTriggerAsync(string jobName, string? requestedBy, CancellationToken ct);

    Task<IReadOnlyList<JobTriggerRequestRow>> ListPendingTriggersAsync(int top, CancellationToken ct);

    Task MarkTriggerFiredAsync(long requestId, CancellationToken ct);

    Task MarkTriggerFailedAsync(long requestId, string errorMessage, CancellationToken ct);

    Task<IReadOnlyList<JobRunRow>> GetRecentRunsAsync(string jobName, int top, CancellationToken ct);

    Task<IReadOnlyList<BareBdTargetRow>> GetBareTopTargetsAsync(int top, int minMpiRefs, CancellationToken ct);

    Task<DateTimeOffset?> GetMostRecentJobRunStartedAtUtcAsync(CancellationToken ct);
}

public sealed record JobTriggerRequestRow(
    long Id,
    string JobName,
    DateTimeOffset RequestedAtUtc,
    string? RequestedBy);
