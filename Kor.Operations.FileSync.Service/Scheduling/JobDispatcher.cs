#nullable enable
using System.Reflection;
using Kor.Operations.FileSync.Service.Alerting;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Scheduling;

// Single dispatch path used by both the manual TriggerPoller and the
// Quartz cron shim. Records run-start, picks the runner, records
// success/failure, and (if a triggerId is supplied) marks the manual
// trigger Completed. Keeping this in one place is what guarantees
// every run shows up in FileSync.JobRuns regardless of how it fired.
internal sealed class JobDispatcher
{
    private static readonly string ServiceVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private readonly IControlPlaneStore _store;
    private readonly JobRunnerRegistry _runners;
    private readonly IAlertNotifier _alerter;
    private readonly ILogger<JobDispatcher> _logger;

    public JobDispatcher(
        IControlPlaneStore store,
        JobRunnerRegistry runners,
        IAlertNotifier alerter,
        ILogger<JobDispatcher> logger)
    {
        _store = store;
        _runners = runners;
        _alerter = alerter;
        _logger = logger;
    }

    public async Task<long> DispatchAsync(
        JobConfig config,
        string triggerSource,
        string? triggeredBy,
        string? args,
        long? triggerId,
        CancellationToken ct)
    {
        var runId = await _store.RecordRunStartAsync(
            jobName: config.JobName,
            mode: config.Mode,
            triggerSource: triggerSource,
            triggeredBy: triggeredBy,
            version: ServiceVersion,
            ct: ct).ConfigureAwait(false);

        var runner = _runners.Resolve(config.JobName);
        // When this is set the finally below SKIPS marking the trigger Completed,
        // so a manual trigger mid-cancelled by host shutdown is left Claimed for
        // TriggerPoller's startup recovery to requeue. Without this, deploy-time
        // cancellation would silently consume the trigger and a fresh process
        // would never re-fire it.
        bool shutdownCancellation = false;
        try
        {
            _logger.LogInformation(
                "Dispatching '{Job}' run {RunId} via {Runner} (mode={Mode}, source={Source}).",
                config.JobName,
                runId,
                runner.GetType().Name,
                config.Mode,
                triggerSource);

            var result = await runner.RunAsync(config, triggerSource, args, ct).ConfigureAwait(false);
            if (result.Success)
            {
                await _store.RecordRunSuccessAsync(runId, result.Summary, ct).ConfigureAwait(false);
            }
            else
            {
                var failure = new InvalidOperationException(result.Summary);
                await _store.RecordRunFailureAsync(runId, failure, ct).ConfigureAwait(false);
                await TryAlertFailureAsync(config, runId, triggerSource, triggeredBy, failure, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown / deploy. Record the JobRun as failed-on-cancellation
            // (so the row doesn't sit Running forever) but do NOT alert (operators
            // don't want a deploy-cancellation email per claimed trigger), and do
            // NOT mark the trigger Completed (so TriggerPoller's startup recovery
            // resets it to Pending and the next process picks it back up).
            shutdownCancellation = true;
            _logger.LogWarning("Job '{Job}' (run {RunId}) cancelled by host shutdown.", config.JobName, runId);
            try
            {
                var cancellation = new OperationCanceledException($"Cancelled by host shutdown for {config.JobName}.");
                await _store.RecordRunFailureAsync(runId, cancellation, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception recordEx)
            {
                _logger.LogWarning(recordEx, "Could not record cancellation for run {RunId}.", runId);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job '{Job}' (run {RunId}) threw.", config.JobName, runId);
            await _store.RecordRunFailureAsync(runId, ex, CancellationToken.None).ConfigureAwait(false);
            await TryAlertFailureAsync(config, runId, triggerSource, triggeredBy, ex, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (triggerId.HasValue && !shutdownCancellation)
            {
                await _store.MarkTriggerCompletedAsync(triggerId.Value, runId, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return runId;
    }

    // Best-effort: alert send must never poison the dispatch path. If the
    // mail call throws (network, Graph 5xx, throttle), we log and move on --
    // the run is already recorded as Failed in JobRuns either way.
    private async Task TryAlertFailureAsync(
        JobConfig config,
        long runId,
        string triggerSource,
        string? triggeredBy,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            var subject = $"[FileSync] '{config.JobName}' run {runId} FAILED on {Environment.MachineName}";
            var body = string.Join(
                Environment.NewLine,
                $"Job:       {config.JobName}",
                $"Run:       {runId}",
                $"Host:      {Environment.MachineName}",
                $"Mode:      {config.Mode}",
                $"Trigger:   {triggerSource} ({triggeredBy ?? "(none)"})",
                $"Time:      {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
                string.Empty,
                $"Error:     {ex.GetType().Name}: {ex.Message}",
                string.Empty,
                "Stack:",
                ex.ToString(),
                string.Empty,
                "Logs on host: %ProgramData%\\KorOperations\\FileSync\\logs");

            await _alerter.SendAlertAsync(subject, body, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown cancellation -- propagate so the dispatch path can wind
            // down cleanly. The OCE catch in DispatchAsync handles run-state.
            throw;
        }
        catch (Exception alertEx)
        {
            _logger.LogWarning(alertEx, "Failure alert for '{Job}' run {RunId} could not be sent.", config.JobName, runId);
        }
    }
}
