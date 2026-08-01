#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace Kor.Opportunities.Worker.Services;

internal sealed class JobScheduleNextFireUpdaterHostedService : BackgroundService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobScheduleStore _store;
    private readonly ILogger<JobScheduleNextFireUpdaterHostedService> _logger;

    public JobScheduleNextFireUpdaterHostedService(
        ISchedulerFactory schedulerFactory,
        IJobScheduleStore store,
        ILogger<JobScheduleNextFireUpdaterHostedService> logger)
    {
        _schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler(stoppingToken).ConfigureAwait(false);
                var keys = await scheduler.GetJobKeys(
                    GroupMatcher<JobKey>.AnyGroup(),
                    stoppingToken).ConfigureAwait(false);

                foreach (var key in keys)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var triggers = await scheduler.GetTriggersOfJob(key, stoppingToken).ConfigureAwait(false);
                    DateTimeOffset? earliest = null;
                    foreach (var trigger in triggers)
                    {
                        var next = trigger.GetFireTimeAfter(DateTimeOffset.UtcNow);
                        if (next is null)
                        {
                            continue;
                        }

                        if (earliest is null || next < earliest)
                        {
                            earliest = next;
                        }
                    }

                    try
                    {
                        await _store.UpdateNextFireAsync(key.Name, earliest, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception storeEx)
                    {
                        _logger.LogWarning(
                            storeEx,
                            "{Service}: failed to update next-fire for {JobName}.",
                            nameof(JobScheduleNextFireUpdaterHostedService),
                            key.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Service}: pass failed; will retry in 60s.",
                    nameof(JobScheduleNextFireUpdaterHostedService));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
