#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
public sealed class DataRetirementJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<DataRetirementJob> _logger;

    public DataRetirementJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<DataRetirementJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.DataRetirementEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(DataRetirementJob),
                nameof(opt.DataRetirementEnabled));
            return;
        }

        var staleOppDays = Math.Max(1, opt.StaleOppDays);
        var staleProjectMonths = Math.Max(1, opt.StaleProjectMonths);
        var sw = Stopwatch.StartNew();
        var ct = context.CancellationToken;

        await using var cn = new SqlConnection(opt.OpportunitiesDb);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var oppsExpired = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.Opportunities
SET Status         = 7,
    WonLostOutcome = 3,
    OutcomeReason  = N'Auto-expired: submission deadline passed (no bid).',
    OutcomeAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedAtUtc   = SYSDATETIMEOFFSET()
WHERE Status = 1
  AND SubmissionDeadlineUtc IS NOT NULL
  AND SubmissionDeadlineUtc < SYSDATETIMEOFFSET();", ct).ConfigureAwait(false);

        var oppsAgedOut = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.Opportunities
SET Status         = 7,
    WonLostOutcome = 3,
    OutcomeReason  = @reason,
    OutcomeAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedAtUtc   = SYSDATETIMEOFFSET()
WHERE Status = 1
  AND SubmissionDeadlineUtc IS NULL
  AND UpdatedAtUtc < DATEADD(day, -@staleOppDays, SYSDATETIMEOFFSET());", ct, cmd =>
        {
            cmd.Parameters.Add("@staleOppDays", System.Data.SqlDbType.Int).Value = staleOppDays;
            cmd.Parameters.Add("@reason", System.Data.SqlDbType.NVarChar, 500).Value =
                $"Auto-expired: no longer listed (not re-observed in {staleOppDays} days).";
        }).ConfigureAwait(false);

        var projectsRetired = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = LEFT(N'Stage: ' + COALESCE(Stage, N'(blank)'), 200),
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE RetiredAtUtc IS NULL
  AND Stage IS NOT NULL
  AND (
        Stage LIKE '%complet%' OR Stage LIKE 'construction%' OR Stage LIKE '%under construction%'
     OR Stage LIKE '%construction started%' OR Stage LIKE '%in construction%'
     OR Stage LIKE '%construction phase%' OR Stage LIKE '%in-service%' OR Stage LIKE '%in service%'
     OR Stage LIKE '%operating%' OR Stage LIKE '%occupancy%' OR Stage LIKE '%built%'
     OR Stage LIKE '%in progress%' OR Stage LIKE '%underway%' OR Stage LIKE '%demolition%'
     OR Stage LIKE '%cancel%'
      );", ct).ConfigureAwait(false);

        var projectsStale = await ExecuteScalarIntAsync(cn, @"
SELECT COUNT_BIG(1)
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND LastVerifiedAtUtc < DATEADD(month, -@staleProjectMonths, SYSDATETIMEOFFSET());", ct, cmd =>
        {
            cmd.Parameters.Add("@staleProjectMonths", System.Data.SqlDbType.Int).Value = staleProjectMonths;
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Data retirement completed: opps expired={OppsExpired}; opps aged-out={OppsAgedOut}; projects retired={ProjectsRetired}; projects stale={ProjectsStale}; elapsedMs={ElapsedMs}.",
            oppsExpired,
            oppsAgedOut,
            projectsRetired,
            projectsStale,
            sw.ElapsedMilliseconds);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection cn,
        string sql,
        CancellationToken ct,
        Action<SqlCommand>? bind = null)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> ExecuteScalarIntAsync(
        SqlConnection cn,
        string sql,
        CancellationToken ct,
        Action<SqlCommand>? bind = null)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }
}
