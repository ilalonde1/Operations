#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.KorMapSync;

// Cron shim for the public project map feed. The job row remains the source
// of truth for Enabled/Mode; Quartz only supplies the daily wake-up.
[DisallowConcurrentExecution]
internal sealed class KorMapSyncJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<KorMapSyncJob> _logger;

    public KorMapSyncJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<KorMapSyncJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(KorMapSyncRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", KorMapSyncRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", KorMapSyncRunner.Name);
            return;
        }

        await _dispatcher.DispatchAsync(
            config: config,
            triggerSource: "Cron",
            triggeredBy: "Quartz",
            args: null,
            triggerId: null,
            ct: ct).ConfigureAwait(false);
    }
}
