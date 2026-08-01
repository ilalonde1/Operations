#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

public sealed class JobRunLoggingListener : IJobListener
{
    public const string ListenerName = nameof(JobRunLoggingListener);

    private const string RunIdKey = "JobRunId";

    private readonly IJobRunStore _store;
    private readonly ILogger<JobRunLoggingListener> _logger;

    public JobRunLoggingListener(IJobRunStore store, ILogger<JobRunLoggingListener> logger)
    {
        _store = store;
        _logger = logger;
    }

    public string Name => ListenerName;

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var jobName = context.JobDetail.JobType.Name;
            var runId = await _store.StartRunAsync(jobName, cancellationToken).ConfigureAwait(false);
            context.Put(RunIdKey, runId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to start job-run log for {JobName}.",
                context.JobDetail.JobType.Name);
        }
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (context.Get(RunIdKey) is not long runId)
            {
                return;
            }

            await _store.CompleteRunAsync(
                runId,
                jobException is null,
                context.Result?.ToString(),
                jobException?.Message,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to complete job-run log for {JobName}.",
                context.JobDetail.JobType.Name);
        }
    }
}
