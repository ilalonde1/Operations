#nullable enable
namespace Kor.Operations.FileSync.Service.ControlPlane;

internal interface IControlPlaneStore
{
    Task<bool> PingAsync(CancellationToken ct);

    Task WriteHeartbeatAsync(
        string hostName,
        DateTimeOffset startedAt,
        string mode,
        string? version,
        int jobsRegistered,
        int? watcherGen,
        CancellationToken ct);

    Task<JobConfig?> GetJobAsync(string jobName, CancellationToken ct);

    Task<PendingTrigger?> ClaimNextPendingTriggerAsync(string claimingHost, CancellationToken ct);

    Task<long> RecordRunStartAsync(
        string jobName,
        string mode,
        string triggerSource,
        string? triggeredBy,
        string? version,
        CancellationToken ct);

    Task RecordRunSuccessAsync(long runId, string summary, CancellationToken ct);

    Task RecordRunFailureAsync(long runId, Exception ex, CancellationToken ct);

    Task MarkTriggerCompletedAsync(long triggerId, long runId, CancellationToken ct);
}
