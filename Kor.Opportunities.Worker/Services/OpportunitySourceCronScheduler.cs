#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services;

/// <summary>
/// Generic per-source cron: every <c>CronTickIntervalSeconds</c> (default 300s
/// = 5 min), queues a Pending IngestionTrigger row for any enabled
/// <c>OpportunitySources</c> whose last run was longer ago than its
/// <c>CrawlDelaySeconds</c> value, AND has no Pending/InProgress trigger
/// already in flight. The existing IngestionTriggerPollerBackgroundService
/// picks up the new triggers on its next ~30s cycle.
///
/// This replaces the need for per-source Quartz job classes
/// (CanadaBuysIngestionJob, etc.) for ALL the post-Phase-1 sources we've added
/// (Bonfire RSS, B&amp;T Awards, BC Bid Awards, BC Bid Unverified, APC...).
/// The original Quartz jobs still run on their own schedules and are
/// unaffected.
/// </summary>
internal sealed class OpportunitySourceCronScheduler : BackgroundService
{
    private const int MinPeriodSeconds = 30;
    private const int DefaultPeriodSeconds = 300;

    private readonly OpportunitiesWorkerOptions _options;
    private readonly ILogger<OpportunitySourceCronScheduler> _logger;

    public OpportunitySourceCronScheduler(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<OpportunitySourceCronScheduler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _options.OpportunitiesDb
            ?? throw new InvalidOperationException(
                "OpportunitiesDb connection string missing — OpportunitySourceCronScheduler cannot run.");

        var periodSeconds = Math.Max(
            MinPeriodSeconds,
            _options.CronTickIntervalSeconds <= 0
                ? DefaultPeriodSeconds
                : _options.CronTickIntervalSeconds);
        var period = TimeSpan.FromSeconds(periodSeconds);

        _logger.LogInformation(
            "OpportunitySourceCronScheduler starting; tick every {Period}s.",
            periodSeconds);

        // Brief startup delay so we don't queue 30 triggers in the first second
        // after a service restart while other startup work is settling.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var queued = await QueueDueTriggersAsync(connectionString, stoppingToken).ConfigureAwait(false);
                if (queued > 0)
                {
                    _logger.LogInformation(
                        "OpportunitySourceCronScheduler queued {Queued} due trigger(s).",
                        queued);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpportunitySourceCronScheduler tick failed.");
            }

            try
            {
                await Task.Delay(period, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Single-statement insert: queues a trigger for every enabled source
    /// whose last successful run is older than its CrawlDelaySeconds value
    /// (or has never been run), provided no Pending/InProgress trigger is
    /// already queued for that source.
    ///
    /// We intentionally skip the well-known sources that still have their own
    /// Quartz jobs (CanadaBuys / CanadaBuysNew / SamGov / GraphEmail) — those
    /// run on their own cron schedules and we don't want to double-trigger
    /// them.
    /// </summary>
    private async Task<int> QueueDueTriggersAsync(string connectionString, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), s.Id, 'Pending', SYSDATETIMEOFFSET(), 'cron'
FROM opportunities.OpportunitySources s
WHERE s.IsEnabled = 1
  AND s.CrawlDelaySeconds > 0
  -- Quartz-managed sources have their own schedules; don't double-trigger.
  AND s.Name NOT IN ('CanadaBuys', 'CanadaBuysNew', 'SamGov', 'BdAlerts')
  -- No pending or in-flight trigger already queued.
  AND NOT EXISTS (
      SELECT 1 FROM opportunities.IngestionTriggers t
      WHERE t.OpportunitySourceId = s.Id
        AND t.Status IN ('Pending', 'InProgress')
  )
  -- Has never run OR last completed run is older than crawl window.
  AND NOT EXISTS (
      SELECT 1 FROM opportunities.IngestionTriggers t
      INNER JOIN opportunities.IngestionRuns r ON r.Id = t.IngestionRunId
      WHERE t.OpportunitySourceId = s.Id
        AND r.StartedAtUtc > DATEADD(SECOND, -s.CrawlDelaySeconds, SYSDATETIMEOFFSET())
  );
SELECT @@ROWCOUNT;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        var rows = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return rows is null ? 0 : Convert.ToInt32(rows);
    }
}
