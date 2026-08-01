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
        var telemetryRetentionDays = Math.Max(7, opt.TelemetryRetentionDays);
        var sw = Stopwatch.StartNew();
        var ct = context.CancellationToken;

        await using var cn = new SqlConnection(opt.OpportunitiesDb);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        // Audit 2026-07-01 n5: stamp UpdatedBy so the audit trail shows the job
        // (not the last human) as the author of automated expiry, and never expire
        // an owned row — a just-grabbed opportunity is mid-transaction between
        // ownership and the Pursuing status flip for a moment.
        var oppsExpired = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.Opportunities
SET Status         = 7,
    WonLostOutcome = 3,
    OutcomeReason  = N'Auto-expired: submission deadline passed (no bid).',
    OutcomeAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedBy      = N'DataRetirementJob'
WHERE Status = 1
  AND OwnerStaffId IS NULL
  AND SubmissionDeadlineUtc IS NOT NULL
  AND SubmissionDeadlineUtc < SYSDATETIMEOFFSET();", ct).ConfigureAwait(false);

        var oppsAgedOut = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.Opportunities
SET Status         = 7,
    WonLostOutcome = 3,
    OutcomeReason  = @reason,
    OutcomeAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedAtUtc   = SYSDATETIMEOFFSET(),
    UpdatedBy      = N'DataRetirementJob'
WHERE Status = 1
  AND OwnerStaffId IS NULL
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
      )
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l WHERE l.MajorProjectsInventoryId = opportunities.MajorProjectsInventory.Id);", ct).ConfigureAwait(false);

        var projectsRetiredByCompletionYear = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = N'Completed: CompletionYear past',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE RetiredAtUtc IS NULL
  AND CompletionYear IS NOT NULL
  AND CompletionYear < YEAR(SYSDATETIMEOFFSET())
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l WHERE l.MajorProjectsInventoryId = opportunities.MajorProjectsInventory.Id);", ct).ConfigureAwait(false);

        var eventsRetired = await ExecuteNonQueryAsync(cn, @"
UPDATE opportunities.IndustryEvents
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = N'Event date passed',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE RetiredAtUtc IS NULL
  AND EndDate IS NOT NULL
  AND EndDate < CAST(SYSDATETIMEOFFSET() AS date);", ct).ConfigureAwait(false);

        var orgsArchived = 0;
        var orgsResurrected = 0;
        if (opt.LowValueOrgArchiveEnabled)
        {
            orgsArchived = await ExecuteNonQueryAsync(cn, @"
UPDATE o
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = N'Low-value auto-archive: isolated commodity vendor; resurrects on any future reference',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.CanonicalOrg o
WHERE o.RetiredAtUtc IS NULL
  AND o.Kind IN (N'Vendor', N'Subcontractor')
  AND o.ClendorClientId IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM opportunities.MajorProjectsInventory m
      WHERE m.RetiredAtUtc IS NULL
        AND (m.ProponentCanonicalOrgId = o.Id
          OR m.ArchitectCanonicalOrgId = o.Id
          OR m.StructuralEngineerCanonicalOrgId = o.Id
          OR m.GeneralContractorCanonicalOrgId = o.Id))
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.BuyerCanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.CanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelNarrative n WHERE n.CanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelWork w WHERE w.CanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelAction a WHERE a.CanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityInterestedFirms f WHERE f.ResolvedCanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.Opportunities op WHERE op.BuyerCanonicalOrgId = o.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention nm WHERE nm.CanonicalOrgId = o.Id);",
                ct,
                commandTimeoutSeconds: 300).ConfigureAwait(false);

            orgsResurrected = await ExecuteNonQueryAsync(cn, @"
UPDATE o
SET RetiredAtUtc = NULL,
    RetiredReason = NULL,
    UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.CanonicalOrg o
WHERE o.RetiredReason LIKE N'Low-value auto-archive%'
  AND (
       EXISTS (
           SELECT 1
           FROM opportunities.MajorProjectsInventory m
           WHERE m.RetiredAtUtc IS NULL
             AND (m.ProponentCanonicalOrgId = o.Id
               OR m.ArchitectCanonicalOrgId = o.Id
               OR m.StructuralEngineerCanonicalOrgId = o.Id
               OR m.GeneralContractorCanonicalOrgId = o.Id))
    OR EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.BuyerCanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.CanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.IntelNarrative n WHERE n.CanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.IntelWork w WHERE w.CanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.IntelAction a WHERE a.CanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.OpportunityInterestedFirms f WHERE f.ResolvedCanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.Opportunities op WHERE op.BuyerCanonicalOrgId = o.Id)
    OR EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention nm WHERE nm.CanonicalOrgId = o.Id));",
                ct,
                commandTimeoutSeconds: 300).ConfigureAwait(false);
        }

        var ingestionTriggersDeleted = await ExecuteNonQueryAsync(cn, @"
DELETE FROM opportunities.IngestionTriggers
WHERE Status IN ('Completed', 'Failed')
  AND RequestedAtUtc < DATEADD(day, -@days, SYSDATETIMEOFFSET());",
            ct,
            cmd => cmd.Parameters.Add("@days", System.Data.SqlDbType.Int).Value = telemetryRetentionDays,
            commandTimeoutSeconds: 300).ConfigureAwait(false);

        var ingestionRunsDeleted = await ExecuteNonQueryAsync(cn, @"
DELETE FROM opportunities.IngestionRuns
WHERE StartedAtUtc < DATEADD(day, -@days, SYSDATETIMEOFFSET());",
            ct,
            cmd => cmd.Parameters.Add("@days", System.Data.SqlDbType.Int).Value = telemetryRetentionDays,
            commandTimeoutSeconds: 300).ConfigureAwait(false);

        var jobRunsDeleted = await ExecuteNonQueryAsync(cn, @"
DELETE FROM opportunities.JobRuns
WHERE StartedAtUtc < DATEADD(day, -@days, SYSDATETIMEOFFSET());",
            ct,
            cmd => cmd.Parameters.Add("@days", System.Data.SqlDbType.Int).Value = telemetryRetentionDays,
            commandTimeoutSeconds: 300).ConfigureAwait(false);

        // Gate rejects need a longer horizon than run telemetry: the point of
        // the table is periodic (roughly monthly) false-negative review, so a
        // reject must survive several review cycles before it ages out.
        var gateRejectsDeleted = await ExecuteNonQueryAsync(cn, @"
IF OBJECT_ID(N'opportunities.RelevanceGateRejects', N'U') IS NOT NULL
DELETE FROM opportunities.RelevanceGateRejects
WHERE LastRejectedAtUtc < DATEADD(day, -120, SYSDATETIMEOFFSET());",
            ct,
            commandTimeoutSeconds: 300).ConfigureAwait(false);

        // Cascade-retire intel orphaned on a retired parent. Org/project retirement
        // (here, the dedup merge tool, manual cleanup) does not always cascade to
        // child intel, which leaves live actions/signals/people/narratives attached
        // to dead records — they surface in dossiers and Priority Actions. This
        // generic sweep retires any opportunities.Intel* row whose CanonicalOrg,
        // MajorProjectsInventory, or IntelPerson parent is retired, across EVERY such
        // table (present and future, by sys.columns), so the class self-heals instead of
        // re-accumulating. Idempotent; only touches rows whose parent is already
        // retired. (One-time backfill ran 2026-06-23; this keeps it from coming back.)
        var orphanIntelRetired = await ExecuteScalarIntAsync(cn, @"
DECLARE @s NVARCHAR(MAX) = N'DECLARE @n INT = 0;';
SELECT @s = @s + N'UPDATE x SET RetiredAtUtc = SYSDATETIMEOFFSET() FROM opportunities.' + QUOTENAME(t.name)
  + N' x JOIN opportunities.CanonicalOrg c ON c.Id = x.CanonicalOrgId WHERE x.RetiredAtUtc IS NULL AND c.RetiredAtUtc IS NOT NULL'
  -- Fix F13: never cascade-retire intel belonging to the buyer of a LIVE
  -- pursuit (Stage 1/3) — a retired-but-still-referenced buyer org is itself
  -- the anomaly to surface, not a reason to strip the pursuit''s intel.
  + N' AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements ce WHERE ce.Stage IN (1, 3) AND ce.BuyerCanonicalOrgId = c.Id); SET @n += @@ROWCOUNT;' + CHAR(10)
FROM sys.tables t
WHERE SCHEMA_NAME(t.schema_id) = N'opportunities' AND t.name LIKE N'Intel%'
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'CanonicalOrgId')
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'RetiredAtUtc');
SELECT @s = @s + N'UPDATE x SET RetiredAtUtc = SYSDATETIMEOFFSET() FROM opportunities.' + QUOTENAME(t.name)
  + N' x JOIN opportunities.MajorProjectsInventory m ON m.Id = x.MajorProjectsInventoryId WHERE x.RetiredAtUtc IS NULL AND m.RetiredAtUtc IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l WHERE l.MajorProjectsInventoryId = m.Id); SET @n += @@ROWCOUNT;' + CHAR(10)
FROM sys.tables t
WHERE SCHEMA_NAME(t.schema_id) = N'opportunities'
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'MajorProjectsInventoryId')
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'RetiredAtUtc');
SELECT @s = @s + N'UPDATE x SET RetiredAtUtc = SYSDATETIMEOFFSET() FROM opportunities.' + QUOTENAME(t.name)
  + N' x JOIN opportunities.IntelPerson p ON p.Id = x.IntelPersonId WHERE x.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NOT NULL; SET @n += @@ROWCOUNT;' + CHAR(10)
FROM sys.tables t
WHERE SCHEMA_NAME(t.schema_id) = N'opportunities'
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'IntelPersonId')
  AND EXISTS (SELECT 1 FROM sys.columns col WHERE col.object_id = t.object_id AND col.name = N'RetiredAtUtc');
SET @s = @s + N'SELECT @n;';
EXEC sys.sp_executesql @s;", ct).ConfigureAwait(false);

        var projectsStale = await ExecuteScalarIntAsync(cn, @"
SELECT COUNT_BIG(1)
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND LastVerifiedAtUtc < DATEADD(month, -@staleProjectMonths, SYSDATETIMEOFFSET());", ct, cmd =>
        {
            cmd.Parameters.Add("@staleProjectMonths", System.Data.SqlDbType.Int).Value = staleProjectMonths;
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Data retirement completed: opps expired={OppsExpired}; opps aged-out={OppsAgedOut}; projects retired={ProjectsRetired}; projects retired by completion year={ProjectsRetiredByCompletionYear}; events retired={EventsRetired}; orgs archived={OrgsArchived}; orgs resurrected={OrgsResurrected}; ingestion triggers deleted={IngestionTriggersDeleted}; ingestion runs deleted={IngestionRunsDeleted}; job runs deleted={JobRunsDeleted}; gate rejects deleted={GateRejectsDeleted}; orphan intel retired={OrphanIntelRetired}; projects stale={ProjectsStale}; elapsedMs={ElapsedMs}.",
            oppsExpired,
            oppsAgedOut,
            projectsRetired,
            projectsRetiredByCompletionYear,
            eventsRetired,
            orgsArchived,
            orgsResurrected,
            ingestionTriggersDeleted,
            ingestionRunsDeleted,
            jobRunsDeleted,
            gateRejectsDeleted,
            orphanIntelRetired,
            projectsStale,
            sw.ElapsedMilliseconds);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection cn,
        string sql,
        CancellationToken ct,
        Action<SqlCommand>? bind = null,
        int commandTimeoutSeconds = 120)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = commandTimeoutSeconds;
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

