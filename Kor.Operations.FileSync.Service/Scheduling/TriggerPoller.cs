#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal sealed class TriggerPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoverySweepInterval = TimeSpan.FromMinutes(5);

    // Heartbeat-staleness cutoff for periodic recovery. Worker writes a
    // heartbeat every HeartbeatSeconds (default 60). Five minutes is ~5x
    // the default tick -- generous enough to avoid false-positives from a
    // brief SQL hiccup, tight enough to detect a hung Worker quickly.
    // Crucially this is a check on the heartbeat row, NOT on ClaimedAt:
    // a live host running a long batch job is never recovered.
    private static readonly TimeSpan HeartbeatStaleCutoff = TimeSpan.FromMinutes(5);

    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<TriggerPoller> _logger;

    public TriggerPoller(IControlPlaneStore store, JobDispatcher dispatcher, ILogger<TriggerPoller> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = Environment.MachineName;
        var processStartedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("TriggerPoller started. Host={Host} Interval={Interval}s.", hostName, PollInterval.TotalSeconds);

        // Startup: anything THIS host claimed before the current process began
        // is, by construction, an orphan. Safe to requeue without consulting
        // heartbeats (our own heartbeat is fresh because Worker just started).
        await TryRecoverOwnPriorClaimsAsync(hostName, processStartedAt, stoppingToken).ConfigureAwait(false);
        var lastSweepAt = DateTimeOffset.UtcNow;

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PollOnceAsync(hostName, stoppingToken).ConfigureAwait(false);

                if (DateTimeOffset.UtcNow - lastSweepAt >= RecoverySweepInterval)
                {
                    await TryRecoverDeadHostClaimsAsync(stoppingToken).ConfigureAwait(false);
                    lastSweepAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task PollOnceAsync(string hostName, CancellationToken ct)
    {
        PendingTrigger? trigger = null;
        try
        {
            trigger = await _store.ClaimNextPendingTriggerAsync(hostName, ct).ConfigureAwait(false);
            if (trigger is null)
                return;

            _logger.LogInformation(
                "Claimed trigger {TriggerId} for job '{Job}' requested by {By}.",
                trigger.TriggerId,
                trigger.JobName,
                trigger.RequestedBy);

            // Once we have a Claimed row, the only way it stops being Claimed
            // is via JobDispatcher's finally (MarkTriggerCompleted) OR an
            // explicit Cancelled mark below. Without that branch the row would
            // sit Claimed forever and the next poll would skip it.
            JobConfig? config;
            try
            {
                config = await _store.GetJobAsync(trigger.JobName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetJobAsync failed for trigger {TriggerId} ('{Job}'); marking trigger Cancelled.", trigger.TriggerId, trigger.JobName);
                await SafeMarkCancelledAsync(trigger.TriggerId, $"GetJobAsync failed: {ex.GetType().Name}").ConfigureAwait(false);
                return;
            }

            if (config is null)
            {
                _logger.LogWarning(
                    "Trigger {TriggerId} references unknown job '{Job}'; running NoOp shim so the trigger row closes cleanly.",
                    trigger.TriggerId,
                    trigger.JobName);
                config = new JobConfig(trigger.JobName, "Shadow", null, false);
            }

            await _dispatcher.DispatchAsync(
                config: config,
                triggerSource: "Manual",
                triggeredBy: trigger.RequestedBy,
                args: trigger.Args,
                triggerId: trigger.TriggerId,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service is stopping; let the stale-claim sweep on next startup
            // (or this process's recovery on next startup) put the row back.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TriggerPoller iteration failed.");
            // Defensive: if we somehow have a Claimed row and a non-cancellation
            // exception escaped (e.g. the dispatcher's own finally couldn't run),
            // mark Cancelled so we don't strand the row.
            if (trigger is not null)
                await SafeMarkCancelledAsync(trigger.TriggerId, $"Poll iteration threw: {ex.GetType().Name}").ConfigureAwait(false);
        }
    }

    private async Task SafeMarkCancelledAsync(long triggerId, string reason)
    {
        try
        {
            // Use a fresh CancellationToken: the caller may already be cancelling and
            // we still want this terminal-state flip to land.
            await _store.MarkTriggerCancelledAsync(triggerId, reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort MarkTriggerCancelled({TriggerId}) failed.", triggerId);
        }
    }

    private async Task TryRecoverOwnPriorClaimsAsync(string hostName, DateTimeOffset processStartedAt, CancellationToken ct)
    {
        try
        {
            var dropped = await _store.RecoverOwnPriorClaimsAsync(hostName, processStartedAt, ct).ConfigureAwait(false);
            if (dropped > 0)
                _logger.LogWarning("Startup: recovered {Count} orphaned Claimed trigger(s) from a previous {Host} process.", dropped, hostName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup own-prior-claim recovery failed.");
        }
    }

    private async Task TryRecoverDeadHostClaimsAsync(CancellationToken ct)
    {
        try
        {
            var dropped = await _store.RecoverDeadHostClaimsAsync(HeartbeatStaleCutoff, ct).ConfigureAwait(false);
            if (dropped > 0)
                _logger.LogWarning("Recovered {Count} Claimed trigger(s) from host(s) with no heartbeat in {Cutoff}.", dropped, HeartbeatStaleCutoff);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dead-host claim recovery sweep failed.");
        }
    }
}
