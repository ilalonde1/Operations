#nullable enable

using System;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Jobs;

[DisallowConcurrentExecution]
public sealed class IntelExtractionCatchUpJob : IJob
{
    private const int CommandTimeoutSeconds = 120;

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly IntelExtractorRegistry _registry;
    private readonly IntelPersistenceService _persistence;
    private readonly ILogger<IntelExtractionCatchUpJob> _logger;

    public IntelExtractionCatchUpJob(
        IOptions<OpportunitiesWorkerOptions> options,
        IntelExtractorRegistry registry,
        IntelPersistenceService persistence,
        ILogger<IntelExtractionCatchUpJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context?.CancellationToken ?? CancellationToken.None;
        var sw = Stopwatch.StartNew();
        var processed = 0;
        var persisted = 0;
        var skipped = 0;
        var empty = 0;
        var errors = 0;

        await using var con = new SqlConnection(_options.Value.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);

        // Sweep window widened to 7 days (R81): the original 36h cap was a
        // performance assumption; in practice the table is well-indexed and a
        // weekly sweep catches longer outages without measurable cost. The
        // chokepoint in SqlEnrichmentTrackingStore handles the live case;
        // this is the safety net.
        var sql = @"
SELECT e.Id,
       e.CanonicalOrgId,
       e.ProviderName,
       e.ResultJson,
       ISNULL(e.LastRefreshAtUtc, e.CreatedAtUtc) AS RefreshedAtUtc
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.CanonicalOrg co ON co.Id = e.CanonicalOrgId
WHERE e.Status = N'ok'
  AND e.ResultJson IS NOT NULL
  AND " + IntelNoiseProviders.SqlNotInClause("e.ProviderName") + @"
  AND e.UpdatedAtUtc >= DATEADD(DAY, -7, sysdatetimeoffset())
  AND co.RetiredAtUtc IS NULL
ORDER BY e.Id;";
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new EnrichmentRow(
                Id: reader.GetInt64(0),
                CanonicalOrgId: reader.GetInt64(1),
                ProviderName: reader.GetString(2),
                ResultJson: reader.GetString(3),
                RefreshedAtUtc: reader.GetDateTimeOffset(4));

            processed++;
            try
            {
                var status = await ProcessRowAsync(row, ct).ConfigureAwait(false);
                switch (status)
                {
                    case RowStatus.Persisted:
                        persisted++;
                        break;
                    case RowStatus.SkippedNoExtractor:
                        skipped++;
                        break;
                    case RowStatus.Empty:
                        empty++;
                        break;
                    case RowStatus.Error:
                        errors++;
                        break;
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(
                    ex,
                    "{Job}: failed processing enrichment {EnrichmentId} provider {ProviderName} org {CanonicalOrgId}.",
                    nameof(IntelExtractionCatchUpJob),
                    row.Id,
                    row.ProviderName,
                    row.CanonicalOrgId);
            }
        }

        sw.Stop();

        // R81: surface the live-ingest counters from the chokepoint store
        // (SqlEnrichmentTrackingStore) so cumulative live extracted /
        // missing-extractor counts get logged alongside this run's per-row
        // counts. Drift surfaces nightly via the structured log.
        var (liveExtracted, liveMissing) = Kor.Opportunities.Data.Awards.SqlEnrichmentTrackingStore.GetIntelCounters();
        _logger.LogInformation(
            "{Job}: completed. processed={Processed}; persisted={Persisted}; skippedNoExtractor={Skipped}; empty={Empty}; errors={Errors}; liveExtractedCumulative={LiveExtracted}; liveMissingExtractorCumulative={LiveMissing}; elapsedMs={ElapsedMs}.",
            nameof(IntelExtractionCatchUpJob),
            processed,
            persisted,
            skipped,
            empty,
            errors,
            liveExtracted,
            liveMissing,
            sw.ElapsedMilliseconds);
    }

    private async Task<RowStatus> ProcessRowAsync(EnrichmentRow row, CancellationToken ct)
    {
        var extractor = _registry.Resolve(row.ProviderName);
        if (extractor.ProviderName == "*" || extractor is DefaultIntelExtractor)
        {
            return RowStatus.SkippedNoExtractor;
        }

        using var doc = JsonDocument.Parse(row.ResultJson);
        var ctx = new IntelExtractionContext(
            row.CanonicalOrgId,
            row.Id,
            row.ProviderName,
            doc,
            row.RefreshedAtUtc);

        var drafts = extractor.Extract(ctx);
        if (DraftCount(drafts) == 0)
        {
            return RowStatus.Empty;
        }

        await _persistence.PersistAsync(drafts, ctx, ct).ConfigureAwait(false);
        return RowStatus.Persisted;
    }

    private static int DraftCount(ExtractedIntel drafts) =>
        drafts.People.Count
        + drafts.Affiliations.Count
        + drafts.Signals.Count
        + drafts.Actions.Count
        + drafts.Works.Count
        + drafts.Risks.Count
        + drafts.Narratives.Count;

    private enum RowStatus
    {
        Persisted,
        SkippedNoExtractor,
        Empty,
        Error,
    }

    private sealed record EnrichmentRow(
        long Id,
        long CanonicalOrgId,
        string ProviderName,
        string ResultJson,
        DateTimeOffset RefreshedAtUtc);
}
