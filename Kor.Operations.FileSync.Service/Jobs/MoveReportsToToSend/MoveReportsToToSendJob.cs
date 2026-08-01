#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;

// Cron shim. Cadence: 5th of every month @ 08:00 PT, mirroring the
// "Move Reports From EOR To Server" Scheduled Task on KOR-APP01.
[DisallowConcurrentExecution]
internal sealed class MoveReportsToToSendJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<MoveReportsToToSendJob> _logger;

    public MoveReportsToToSendJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<MoveReportsToToSendJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(MoveReportsToToSendRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", MoveReportsToToSendRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", MoveReportsToToSendRunner.Name);
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
