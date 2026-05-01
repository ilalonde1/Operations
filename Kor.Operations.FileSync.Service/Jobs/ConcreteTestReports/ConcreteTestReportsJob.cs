#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;

// Cron shim. Same shape as WeeklyPmDeadlinesJob: looks up the current
// FileSync.Jobs row (so Mode/Enabled flips take effect on the next fire)
// and hands off to JobDispatcher. PS1 cadence: 1st of month @ 00:30 PT.
[DisallowConcurrentExecution]
internal sealed class ConcreteTestReportsJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<ConcreteTestReportsJob> _logger;

    public ConcreteTestReportsJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<ConcreteTestReportsJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(ConcreteTestReportsRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", ConcreteTestReportsRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", ConcreteTestReportsRunner.Name);
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
