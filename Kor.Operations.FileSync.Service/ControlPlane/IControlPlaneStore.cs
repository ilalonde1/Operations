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

    // Startup recovery: any row this host claimed strictly before
    // notClaimedAfter is, by definition, an orphan from a previous incarnation
    // (this process couldn't have started a dispatch before it began running).
    // Returns the number of rows reset to Pending.
    Task<int> RecoverOwnPriorClaimsAsync(string hostName, DateTimeOffset notClaimedAfter, CancellationToken ct);

    // Periodic recovery: only requeues claims whose ClaimedByHost has no
    // recent ServiceHeartbeat row -- i.e., the claiming service is verifiably
    // dead. Live hosts running long batch jobs are NOT touched, even if their
    // claim is older than the heartbeat cutoff. Returns the number of rows
    // reset to Pending.
    Task<int> RecoverDeadHostClaimsAsync(TimeSpan heartbeatStaleAfter, CancellationToken ct);
}
