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
        _logger.LogInformation(
            "Dispatching '{Job}' run {RunId} via {Runner} (mode={Mode}, source={Source}).",
            config.JobName,
            runId,
            runner.GetType().Name,
            config.Mode,
            triggerSource);

        // ---- Phase 1: run the job ------------------------------------------
        // Cancellation-during-run is the ONLY path where the trigger should
        // stay Claimed for startup recovery to requeue, because the job
        // either didn't fire or only partially fired its side effects.
        // Post-run cancellation must NOT requeue -- the side effects already
        // happened and a re-fire would be a double-send / double-move bug.
        JobRunResult? result = null;
        Exception? runnerThrow = null;
        bool shutdownDuringRun = false;
        try
        {
            result = await runner.RunAsync(config, triggerSource, args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            shutdownDuringRun = true;
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
            runnerThrow = ex;
        }

        // ---- Phase 2: terminal-state writes --------------------------------
        // CancellationToken.None on every write here. Shutdown firing between
        // phase 1 and phase 2 (or mid-phase 2) MUST NOT flip a completed run
        // back to a cancellation-failed state -- that would defeat the
        // trigger-completion below and recovery would re-fire a job whose
        // side effects already happened.
        if (runnerThrow is not null)
        {
            _logger.LogError(runnerThrow, "Job '{Job}' (run {RunId}) threw.", config.JobName, runId);
            await SafeRecordFailureAsync(runId, runnerThrow).ConfigureAwait(false);
            await SafeAlertAsync(config, runId, triggerSource, triggeredBy, runnerThrow).ConfigureAwait(false);
        }
        else if (result is not null)
        {
            if (result.Success)
            {
                await SafeRecordSuccessAsync(runId, result.Summary).ConfigureAwait(false);
            }
            else
            {
                var failure = new InvalidOperationException(result.Summary);
                await SafeRecordFailureAsync(runId, failure).ConfigureAwait(false);
                await SafeAlertAsync(config, runId, triggerSource, triggeredBy, failure).ConfigureAwait(false);
            }
        }

        // ---- Phase 3: mark trigger Completed -------------------------------
        // Skip ONLY when shutdown happened during runner.RunAsync above.
        if (triggerId.HasValue && !shutdownDuringRun)
        {
            try
            {
                await _store.MarkTriggerCompletedAsync(triggerId.Value, runId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception markEx)
            {
                _logger.LogWarning(markEx, "Could not mark trigger {TriggerId} Completed.", triggerId.Value);
            }
        }

        return runId;
    }

    // Best-effort terminal writes. Each swallows its own exceptions because by
    // the time we're here the run has either succeeded or the failure is
    // already known -- a SQL hiccup recording the outcome must not throw past
    // the dispatcher into the caller's loop.
    private async Task SafeRecordSuccessAsync(long runId, string summary)
    {
        try { await _store.RecordRunSuccessAsync(runId, summary, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not record success for run {RunId}.", runId); }
    }

    private async Task SafeRecordFailureAsync(long runId, Exception failure)
    {
        try { await _store.RecordRunFailureAsync(runId, failure, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not record failure for run {RunId}.", runId); }
    }

    private async Task SafeAlertAsync(JobConfig config, long runId, string triggerSource, string? triggeredBy, Exception failure)
    {
        // Bound the alert send so we don't hold the dispatcher (and SCM stop
        // grace) for the SDK's default ~100s HTTP timeout if Graph is slow.
        using var alertCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await TryAlertFailureAsync(config, runId, triggerSource, triggeredBy, failure, alertCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure alert for '{Job}' run {RunId} could not be sent.", config.JobName, runId);
        }
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
