#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Ingestion;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

internal sealed class EnabledTriggerListener : ITriggerListener
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private readonly ILogger<EnabledTriggerListener> _logger;
    private readonly IJobScheduleStore _store;
    private readonly ConcurrentDictionary<string, (bool Enabled, DateTimeOffset CachedAtUtc)> _cache = new(StringComparer.Ordinal);

    public EnabledTriggerListener(
        ILogger<EnabledTriggerListener> logger,
        IJobScheduleStore store)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Name => nameof(EnabledTriggerListener);

    public Task TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<bool> VetoJobExecution(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var jobName = trigger.JobKey.Name;
        if (jobName == nameof(JobTriggerPollerJob))
        {
            return false;
        }

        // Quartz.JobDataMap.GetBooleanValue throws KeyNotFoundException when the
        // key is absent — which is the common case for scheduled fires. Guard
        // with ContainsKey so we don't throw out of the listener (which Quartz
        // converts to "Trigger and Job will NOT be fired", silently breaking
        // the scheduler).
        if (trigger.JobDataMap.ContainsKey("manualTrigger")
            && trigger.JobDataMap.GetBooleanValue("manualTrigger"))
        {
            return false;
        }

        if (trigger.Key.Name.StartsWith("MT_", StringComparison.Ordinal))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var (enabled, cachedAt) = _cache.GetOrAdd(jobName, _ => (true, DateTimeOffset.MinValue));
        if (cachedAt < now - CacheTtl)
        {
            try
            {
                var rows = await _store.ListWithLastRunAsync(cancellationToken).ConfigureAwait(false);
                var refreshedAt = DateTimeOffset.UtcNow;
                foreach (var row in rows)
                {
                    _cache[row.JobName] = (row.Enabled, refreshedAt);
                }

                if (_cache.TryGetValue(jobName, out var refreshed))
                {
                    enabled = refreshed.Enabled;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Listener}: failed to read JobSchedules enabled flags; allowing {JobName} to run.",
                    nameof(EnabledTriggerListener),
                    jobName);
                return false;
            }
        }

        if (!enabled)
        {
            _logger.LogInformation(
                "{Listener}: vetoed scheduled fire of '{JobName}' because it is disabled in JobSchedules.",
                nameof(EnabledTriggerListener),
                jobName);
            return true;
        }

        return false;
    }

    public Task TriggerMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
