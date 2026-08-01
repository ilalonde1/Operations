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
        // The fallback to MarkTriggerCancelled is what stops a transient SQL
        // hiccup here from leaving the row Claimed -- recovery would then
        // requeue a job whose side effects already happened (double-fire).
        if (triggerId.HasValue && !shutdownDuringRun)
        {
            await SafeMarkTriggerCompletedAsync(triggerId.Value, runId).ConfigureAwait(false);
        }

        return runId;
    }

    // Terminal writes are durability-critical: a swallowed SQL failure here
    // can leave JobRuns rows stuck Running forever or (for trigger completion)
    // leave the row Claimed so RecoverOwnPriorClaimsAsync requeues it on
    // restart. Each retries within a bounded total budget; the trigger path
    // additionally falls back to MarkTriggerCancelled to guarantee a terminal
    // status that recovery will not requeue.
    //
    // Budget caps matter: SqlControlPlaneStore uses CommandTimeout=15s, so an
    // unbounded 4-attempt loop could spend 60s+ blocking shutdown. We give
    // each terminal write its own non-shutdown budget CTS so the worst-case
    // dispatcher hold-time on shutdown is bounded:
    //   SafeRecord*       : 10s budget
    //   SafeMarkCompleted : 10s primary + 5s fallback = 15s
    //   SafeAlert         : 30s (already enforced separately, see below)
    // Worst-case post-runner cost on shutdown <= 10 + 15 + 30 = 55s, safely
    // under SCM's 125s default stop grace.
    private const int TerminalWriteAttempts = 4;
    private static readonly TimeSpan RecordRunBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MarkCompletedBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MarkCancelledBudget = TimeSpan.FromSeconds(5);

    private async Task SafeRecordSuccessAsync(long runId, string summary)
    {
        var ok = await TryWithRetryAsync(
            ct => _store.RecordRunSuccessAsync(runId, summary, ct),
            opName: $"RecordRunSuccess(run={runId})",
            totalBudget: RecordRunBudget).ConfigureAwait(false);

        if (!ok)
        {
            _logger.LogError(
                "RecordRunSuccess for run {RunId} failed after {Attempts} attempts (budget {BudgetSec}s); row may be stuck Running. Manual intervention required.",
                runId, TerminalWriteAttempts, RecordRunBudget.TotalSeconds);
        }
    }

    private async Task SafeRecordFailureAsync(long runId, Exception failure)
    {
        var ok = await TryWithRetryAsync(
            ct => _store.RecordRunFailureAsync(runId, failure, ct),
            opName: $"RecordRunFailure(run={runId})",
            totalBudget: RecordRunBudget).ConfigureAwait(false);

        if (!ok)
        {
            _logger.LogError(
                failure,
                "RecordRunFailure for run {RunId} failed after {Attempts} attempts (budget {BudgetSec}s); row may be stuck Running. Underlying failure attached.",
                runId, TerminalWriteAttempts, RecordRunBudget.TotalSeconds);
        }
    }

    private async Task SafeMarkTriggerCompletedAsync(long triggerId, long runId)
    {
        var ok = await TryWithRetryAsync(
            ct => _store.MarkTriggerCompletedAsync(triggerId, runId, ct),
            opName: $"MarkTriggerCompleted(trigger={triggerId},run={runId})",
            totalBudget: MarkCompletedBudget).ConfigureAwait(false);

        if (ok) return;

        // Last-ditch fallback: drive the row to a terminal state so recovery
        // does not requeue a job whose side effects already happened. The
        // 'reason' is log-only -- JobTriggers has no Notes column, so we log
        // it explicitly here to give operators a paper trail (the SQL UPDATE
        // ignores the reason; the JobRuns row carries the real outcome).
        var reason = $"MarkTriggerCompleted failed within {MarkCompletedBudget.TotalSeconds}s budget; run={runId} already finished.";
        _logger.LogWarning(
            "Falling back to MarkTriggerCancelled for trigger {TriggerId} (run={RunId}). Reason (log-only, not persisted on JobTriggers): {Reason}",
            triggerId, runId, reason);

        var cancelOk = await TryWithRetryAsync(
            ct => _store.MarkTriggerCancelledAsync(triggerId, reason, ct),
            opName: $"MarkTriggerCancelled-fallback(trigger={triggerId})",
            totalBudget: MarkCancelledBudget).ConfigureAwait(false);

        if (!cancelOk)
        {
            _logger.LogError(
                "Could not terminalize trigger {TriggerId} (run={RunId}) within fallback budget {BudgetSec}s; row remains Claimed. Recovery WILL requeue this trigger on next restart -- manual intervention required to avoid double-fire.",
                triggerId, runId, MarkCancelledBudget.TotalSeconds);
        }
        else
        {
            _logger.LogWarning(
                "Trigger {TriggerId} terminalized as Cancelled (fallback); run {RunId} JobRuns row carries the real outcome.",
                triggerId, runId);
        }
    }

    private async Task<bool> TryWithRetryAsync(Func<CancellationToken, Task> op, string opName, TimeSpan totalBudget)
    {
        // One CTS shared across all attempts -- this is what bounds the worst
        // case so SQL CommandTimeout (15s) cannot stack into 60s+ on shutdown.
        // The budget is independent of host shutdown ct so terminal writes
        // still get to retry while the host is winding down.
        using var budgetCts = new CancellationTokenSource(totalBudget);

        for (int attempt = 1; attempt <= TerminalWriteAttempts; attempt++)
        {
            if (budgetCts.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "{Op} budget {BudgetMs}ms exhausted before attempt {Attempt}; giving up.",
                    opName, totalBudget.TotalMilliseconds, attempt);
                return false;
            }

            try
            {
                await op(budgetCts.Token).ConfigureAwait(false);
                if (attempt > 1)
                    _logger.LogInformation("{Op} succeeded on attempt {Attempt}.", opName, attempt);
                return true;
            }
            catch (OperationCanceledException) when (budgetCts.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "{Op} cancelled by budget {BudgetMs}ms on attempt {Attempt}.",
                    opName, totalBudget.TotalMilliseconds, attempt);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt == TerminalWriteAttempts)
                {
                    _logger.LogWarning(ex, "{Op} failed on final attempt {Attempt}.", opName, attempt);
                    return false;
                }

                // Pre-attempt sleep between retries: 250ms, 500ms, 1000ms.
                // Three sleeps for four attempts; cumulative ~1.75s of delay
                // plus per-attempt SQL CommandTimeout, all capped by the
                // shared budget CTS above.
                var delay = TimeSpan.FromMilliseconds(250 * (1 << (attempt - 1)));
                _logger.LogWarning(ex, "{Op} attempt {Attempt} failed; retrying in {Delay}ms.", opName, attempt, delay.TotalMilliseconds);
                try
                {
                    await Task.Delay(delay, budgetCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (budgetCts.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "{Op} budget {BudgetMs}ms exhausted while sleeping before attempt {Next}; giving up.",
                        opName, totalBudget.TotalMilliseconds, attempt + 1);
                    return false;
                }
            }
        }

        return false;
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
