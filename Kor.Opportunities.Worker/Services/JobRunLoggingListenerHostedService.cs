#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace Kor.Opportunities.Worker.Services;

public sealed class JobRunLoggingListenerHostedService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobRunStore _store;
    private readonly ILogger<JobRunLoggingListener> _listenerLogger;
    private readonly ILogger<JobRunLoggingListenerHostedService> _logger;

    public JobRunLoggingListenerHostedService(
        ISchedulerFactory schedulerFactory,
        IJobRunStore store,
        ILogger<JobRunLoggingListener> listenerLogger,
        ILogger<JobRunLoggingListenerHostedService> logger)
    {
        _schedulerFactory = schedulerFactory;
        _store = store;
        _listenerLogger = listenerLogger;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
            scheduler.ListenerManager.AddJobListener(
                new JobRunLoggingListener(_store, _listenerLogger),
                EverythingMatcher<JobKey>.AllJobs());
            _logger.LogInformation("Registered global Quartz job-run logging listener.");
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register global Quartz job-run logging listener.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
            scheduler.ListenerManager.RemoveJobListener(JobRunLoggingListener.ListenerName);
        }
        catch (System.Exception ex)
        {
            _logger.LogDebug(ex, "Failed to unregister global Quartz job-run logging listener.");
        }
    }
}
