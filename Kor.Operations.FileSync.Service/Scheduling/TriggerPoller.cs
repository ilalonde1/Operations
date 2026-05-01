#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal sealed class TriggerPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

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
        _logger.LogInformation("TriggerPoller started. Host={Host} Interval={Interval}s.", hostName, PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PollOnceAsync(hostName, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task PollOnceAsync(string hostName, CancellationToken ct)
    {
        try
        {
            var trigger = await _store.ClaimNextPendingTriggerAsync(hostName, ct).ConfigureAwait(false);
            if (trigger is null)
                return;

            _logger.LogInformation(
                "Claimed trigger {TriggerId} for job '{Job}' requested by {By}.",
                trigger.TriggerId,
                trigger.JobName,
                trigger.RequestedBy);

            var config = await _store.GetJobAsync(trigger.JobName, ct).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "TriggerPoller iteration failed.");
        }
    }
}
