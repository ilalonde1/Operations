#nullable enable

using System;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class JobTriggerPollerJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<JobTriggerPollerJob> _logger;
    private readonly IJobScheduleStore _store;

    public JobTriggerPollerJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<JobTriggerPollerJob> logger,
        IJobScheduleStore store)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.JobTriggerPollerEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration.", nameof(JobTriggerPollerJob));
            return;
        }

        var ct = context.CancellationToken;
        var firedCount = 0;
        var failedCount = 0;
        var pendingCount = 0;

        try
        {
            var batchSize = Math.Max(1, opt.JobTriggerPollerBatchSize);
            var pending = await _store.ListPendingTriggersAsync(batchSize, ct).ConfigureAwait(false);
            pendingCount = pending.Count;

            foreach (var req in pending)
            {
                ct.ThrowIfCancellationRequested();

                var jobKey = new JobKey(req.JobName);
                var exists = await context.Scheduler.CheckExists(jobKey, ct).ConfigureAwait(false);
                if (!exists)
                {
                    failedCount++;
                    var error = $"Job '{req.JobName}' is not registered with the scheduler.";
                    await _store.MarkTriggerFailedAsync(req.Id, error, ct).ConfigureAwait(false);
                    _logger.LogWarning(
                        "{Job}: triggerRequest {RequestId} failed because job {JobName} is not registered.",
                        nameof(JobTriggerPollerJob),
                        req.Id,
                        req.JobName);
                    continue;
                }

                try
                {
                    await context.Scheduler.TriggerJob(jobKey, ct).ConfigureAwait(false);
                    await _store.MarkTriggerFiredAsync(req.Id, ct).ConfigureAwait(false);
                    firedCount++;
                    _logger.LogInformation(
                        "{Job}: fired job {JobName} for triggerRequest {RequestId} requestedBy {RequestedBy}.",
                        nameof(JobTriggerPollerJob),
                        req.JobName,
                        req.Id,
                        req.RequestedBy);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    try
                    {
                        await _store.MarkTriggerFailedAsync(req.Id, ex.Message, ct).ConfigureAwait(false);
                    }
                    catch (Exception markEx)
                    {
                        _logger.LogWarning(
                            markEx,
                            "{Job}: failed to mark triggerRequest {RequestId} failed.",
                            nameof(JobTriggerPollerJob),
                            req.Id);
                    }

                    _logger.LogError(
                        ex,
                        "{Job}: failed to fire job {JobName} for triggerRequest {RequestId}.",
                        nameof(JobTriggerPollerJob),
                        req.JobName,
                        req.Id);
                }
            }

            context.Result = TruncateResult($"JobTriggerPoller: fired={firedCount}, failed={failedCount}, pending-now={pendingCount}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failedCount++;
            context.Result = TruncateResult($"JobTriggerPoller failed: {ex.GetType().Name}: {ex.Message}; fired={firedCount}, failed={failedCount}, pending-now={pendingCount}");
            _logger.LogError(ex, "{Job}: failed while polling trigger requests.", nameof(JobTriggerPollerJob));
        }
    }

    private static string TruncateResult(string result) =>
        result.Length <= 1000 ? result : result[..1000];
}
