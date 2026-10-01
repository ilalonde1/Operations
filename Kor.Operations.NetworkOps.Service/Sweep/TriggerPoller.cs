#nullable enable
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.NetworkOps.Service.Sweep;

// "Run a health check on this PC now" from the Command Center: the page inserts a row into
// NetworkOps.JobTriggers; this claims it within seconds, runs it through the same dispatch path
// as the schedule (so it is recorded as a run like any other), and writes the result back for the
// page to show. A trigger left Running by a service that died is requeued at startup.
internal sealed class TriggerPoller(NetworkOpsStore store, HealthSweeper sweeper, JobDispatcher dispatcher, IServiceProvider services, ILogger<TriggerPoller> log)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var requeued = await store.RequeueAbandonedTriggersAsync(Environment.MachineName, ct);
            if (requeued > 0) log.LogWarning("Requeued {Count} trigger(s) left running by a previous start", requeued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Could not requeue abandoned triggers"); }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            NetworkOpsStore.ClaimedTrigger? t;
            try { t = await store.ClaimTriggerAsync(Environment.MachineName, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Trigger poll failed"); continue; }
            if (t is null) continue;

            log.LogInformation("Trigger {Id}: {Job} {Device} requested by {By}", t.TriggerId, t.JobName, t.DeviceName ?? "(fleet)", t.RequestedBy);
            // A health check of named PCs has its own path; any other trigger runs that catalog job whole
            // (so every scheduled job can also be run on demand, and a new job needs no change here).
            Func<CancellationToken, Task<string>>? work = t.JobName == HealthSweepJob.JobName
                ? c => sweeper.SweepAsync(t.DeviceName is null ? null : [t.DeviceName], c)
                : t.JobName == RackSweepJob.JobName && t.DeviceName is not null
                ? c => services.GetRequiredService<RackSweepJob>().SweepAsync(t.DeviceName, c)
                // On demand only, never scheduled: the fleet is not changed unless someone asks.
                : t.JobName == Agents.AgentRollout.JobName
                ? c => services.GetRequiredService<Agents.AgentRollout>().RunAsync(t.RequestedBy, c)
                : SchedulingCatalog.All.FirstOrDefault(s => s.Name.Equals(t.JobName, StringComparison.OrdinalIgnoreCase)) is { } entry
                    ? ((INetworkOpsJob)services.GetRequiredService(entry.JobType)).RunAsync
                    : null;
            if (work is null)
            {
                await store.CompleteTriggerAsync(t.TriggerId, false, $"unknown job '{t.JobName}'");
                continue;
            }
            // Same dispatch path as the schedule: the run is recorded like any other.
            var (success, result) = await dispatcher.RunAsync(t.JobName, work, ct);
            await store.CompleteTriggerAsync(t.TriggerId, success, result);
        }
    }
}
