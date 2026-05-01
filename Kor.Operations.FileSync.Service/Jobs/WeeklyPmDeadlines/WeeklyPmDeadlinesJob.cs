#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

// Cron shim. Quartz fires this on its schedule; we look up the current job
// config (so a Shadow/Live flip in the DB takes effect on the next run) and
// hand it to JobDispatcher. Disabling Enabled=0 in FileSync.Jobs makes us
// skip without producing a JobRuns row.
[DisallowConcurrentExecution]
internal sealed class WeeklyPmDeadlinesJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<WeeklyPmDeadlinesJob> _logger;

    public WeeklyPmDeadlinesJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<WeeklyPmDeadlinesJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(WeeklyPmDeadlinesRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", WeeklyPmDeadlinesRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", WeeklyPmDeadlinesRunner.Name);
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
