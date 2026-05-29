#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Worker.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services;

public sealed class JobScheduleRegistryHostedService : IHostedService
{
    private readonly IJobScheduleStore _store;
    private readonly IConfiguration _configuration;
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<JobScheduleRegistryHostedService> _logger;

    public JobScheduleRegistryHostedService(
        IJobScheduleStore store,
        IConfiguration configuration,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<JobScheduleRegistryHostedService> logger)
    {
        _store = store;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = 0;
            var opt = _options.Value;
            foreach (var job in ScheduledJobDefinitions.All)
            {
                var cron = _configuration[job.CronConfigKey] ?? job.DefaultCron;
                await _store.UpsertScheduleAsync(
                    job.JobName,
                    cron,
                    job.Enabled(opt),
                    cancellationToken).ConfigureAwait(false);
                count++;
            }

            _logger.LogInformation("Registered {Count} Quartz job schedules.", count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register Quartz job schedules.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
