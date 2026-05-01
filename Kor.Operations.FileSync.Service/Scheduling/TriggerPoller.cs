#nullable enable
using System.Reflection;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal sealed class TriggerPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IControlPlaneStore _store;
    private readonly IJobRunner _runner;
    private readonly ILogger<TriggerPoller> _logger;

    public TriggerPoller(IControlPlaneStore store, IJobRunner runner, ILogger<TriggerPoller> logger)
    {
        _store = store;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = Environment.MachineName;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        _logger.LogInformation("TriggerPoller started. Host={Host} Interval={Interval}s.", hostName, PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PollOnceAsync(hostName, version, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task PollOnceAsync(string hostName, string version, CancellationToken ct)
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

            await DispatchAsync(trigger, version, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TriggerPoller iteration failed.");
        }
    }

    private async Task DispatchAsync(PendingTrigger trigger, string version, CancellationToken ct)
    {
        var config = await _store.GetJobAsync(trigger.JobName, ct).ConfigureAwait(false);
        if (config is null)
        {
            _logger.LogWarning("Trigger {TriggerId} references unknown job '{Job}'; marking trigger completed with a stub run.", trigger.TriggerId, trigger.JobName);
            config = new JobConfig(trigger.JobName, "Shadow", null, false);
        }

        var runId = await _store.RecordRunStartAsync(
            jobName: config.JobName,
            mode: config.Mode,
            triggerSource: "Manual",
            triggeredBy: trigger.RequestedBy,
            version: version,
            ct: ct).ConfigureAwait(false);

        try
        {
            var result = await _runner.RunAsync(config, "Manual", trigger.Args, ct).ConfigureAwait(false);
            if (result.Success)
                await _store.RecordRunSuccessAsync(runId, result.Summary, ct).ConfigureAwait(false);
            else
                await _store.RecordRunFailureAsync(runId, new InvalidOperationException(result.Summary), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job '{Job}' (run {RunId}) threw.", config.JobName, runId);
            await _store.RecordRunFailureAsync(runId, ex, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _store.MarkTriggerCompletedAsync(trigger.TriggerId, runId, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
