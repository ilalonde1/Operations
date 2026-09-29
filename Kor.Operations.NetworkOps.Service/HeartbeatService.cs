#nullable enable
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service;

// The service's pulse in NetworkOps.ServiceHeartbeat. The dead-man watcher reads this from
// ANOTHER machine: the thing that reports a dead service must not be the service (doctrine D10).
internal sealed class HeartbeatService(NetworkOpsStore store, IOptions<NetworkOpsOptions> options, ILogger<HeartbeatService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.HeartbeatSeconds)));
        do
        {
            try { await store.BeatAsync(Environment.MachineName, started, JobDispatcher.Version, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Heartbeat write failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}
