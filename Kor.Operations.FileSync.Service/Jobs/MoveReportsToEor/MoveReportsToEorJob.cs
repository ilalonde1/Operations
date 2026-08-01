#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;

// Cron shim. Same shape as ConcreteTestReportsJob: looks up the current
// FileSync.Jobs row (so Mode/Enabled flips take effect on the next fire)
// and hands off to JobDispatcher. PS1 cadence: 1st of month @ 00:00 PT.
[DisallowConcurrentExecution]
internal sealed class MoveReportsToEorJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<MoveReportsToEorJob> _logger;

    public MoveReportsToEorJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<MoveReportsToEorJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(MoveReportsToEorRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", MoveReportsToEorRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", MoveReportsToEorRunner.Name);
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
