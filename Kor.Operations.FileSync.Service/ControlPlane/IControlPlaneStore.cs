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

    Task<IReadOnlyDictionary<string, string?>> GetKnobsAsync(string jobName, CancellationToken ct);

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

    // Marks a Claimed trigger as Cancelled with a reason note. Used when the
    // poller successfully claims a row but cannot reach JobDispatcher (e.g.
    // GetJobAsync threw, runner registration missing) -- without this the
    // row would stay Claimed forever and the manual fire would never
    // surface a result in the Command Center.
    Task MarkTriggerCancelledAsync(long triggerId, string reason, CancellationToken ct);

    // Resets any FileSync.JobTriggers row stuck in 'Claimed' for longer than
    // staleAfter back to 'Pending'. Used on TriggerPoller startup and
    // periodically: covers the case where a previous service process died
    // mid-claim before either MarkTriggerCompleted or MarkTriggerCancelled.
    // Returns the number of rows reset.
    Task<int> RecoverStaleClaimsAsync(TimeSpan staleAfter, CancellationToken ct);
}
