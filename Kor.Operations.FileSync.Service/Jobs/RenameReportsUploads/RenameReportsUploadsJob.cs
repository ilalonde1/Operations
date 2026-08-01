#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;

// Cron shim. Cadence: every night @ 23:30 PT, mirroring the PS1
// Scheduled Task on KOR-APP01.
[DisallowConcurrentExecution]
internal sealed class RenameReportsUploadsJob : IJob
{
    private readonly IControlPlaneStore _store;
    private readonly JobDispatcher _dispatcher;
    private readonly ILogger<RenameReportsUploadsJob> _logger;

    public RenameReportsUploadsJob(
        IControlPlaneStore store,
        JobDispatcher dispatcher,
        ILogger<RenameReportsUploadsJob> logger)
    {
        _store = store;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var config = await _store.GetJobAsync(RenameReportsUploadsRunner.Name, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Cron fired for '{Job}' but no row exists in FileSync.Jobs.", RenameReportsUploadsRunner.Name);
            return;
        }

        if (!config.Enabled)
        {
            _logger.LogInformation("Cron fired for '{Job}' but Enabled=0; skipping.", RenameReportsUploadsRunner.Name);
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
