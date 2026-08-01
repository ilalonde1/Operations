#nullable enable
// BdHoningIntelBackfill — one-shot backfill for BD-Audit-2026-06-09 M3.
//
// Project-side intel extraction only happens at ingest time
// (SqlMajorProjectEnrichmentTrackingStore.RecordAttemptAsync), and the
// IntelExtractionCatchUpJob sweeps ONLY the org-side CanonicalOrgEnrichment
// table. Every ProjectBriefHoning / PrimeConsultantResearch row ingested
// BEFORE ProjectBriefHoningExtractor existed (1,876 + 181 rows) is
// therefore archived-only: its keyPeople/actions/signals/risks never
// reached the IntelProject* tables.
//
// This tool replays extraction for those rows through the exact ingest
// pipeline pieces (ProjectBriefHoningExtractor + ProjectIntelPersistence-
// Service). PersistAsync dedupes on NaturalKey, so re-runs update rather
// than duplicate. The shape-tolerant honing extractor is also used for
// PrimeConsultantResearch rows — their first-pass-like layout (root
// signals/actions/keyPeople) reads through its root-fallback path.
//
// Dry-run by default; pass --commit to write. Active MPIs only.
using System.Text.Json;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

var commit = args.Contains("--commit", StringComparer.OrdinalIgnoreCase);

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("BdHoningIntelBackfill");

var extractor = new ProjectBriefHoningExtractor();
var persistence = new ProjectIntelPersistenceService(cs, loggerFactory.CreateLogger<ProjectIntelPersistenceService>());

var rows = new List<(long EnrichmentId, long MpiId, string Provider, string Json, DateTimeOffset RefreshedAt)>();
await using (var con = new SqlConnection(cs))
{
    await con.OpenAsync().ConfigureAwait(false);
    await using var cmd = new SqlCommand(@"
SELECT e.Id, e.MajorProjectsInventoryId, e.ProviderName, e.ResultJson,
       ISNULL(e.LastRefreshAtUtc, e.CreatedAtUtc) AS RefreshedAtUtc
FROM opportunities.MajorProjectEnrichment e
JOIN opportunities.MajorProjectsInventory m ON m.Id = e.MajorProjectsInventoryId
WHERE e.ProviderName IN (N'ProjectBriefHoning', N'PrimeConsultantResearch')
  AND e.Status = N'ok'
  AND e.ResultJson IS NOT NULL
  AND m.RetiredAtUtc IS NULL
ORDER BY e.Id;", con) { CommandTimeout = 120 };
    await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
        rows.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetDateTimeOffset(4)));
    }
}

log.LogInformation("Backfill candidates: {Count} ({Mode})", rows.Count, commit ? "COMMIT" : "dry-run");

var persisted = 0; var empty = 0; var failed = 0;
long projects = 0, signals = 0, actions = 0, risks = 0, keyPeople = 0;

foreach (var row in rows)
{
    try
    {
        using var doc = JsonDocument.Parse(row.Json);
        var ctx = new ProjectIntelExtractionContext(row.MpiId, row.EnrichmentId, row.Provider, doc, row.RefreshedAt);
        var extracted = extractor.Extract(ctx);
        var total = extracted.Projects.Count + extracted.Signals.Count + extracted.Actions.Count
                    + extracted.Risks.Count + extracted.KeyPeople.Count;
        if (total == 0)
        {
            empty++;
            continue;
        }

        projects += extracted.Projects.Count;
        signals += extracted.Signals.Count;
        actions += extracted.Actions.Count;
        risks += extracted.Risks.Count;
        keyPeople += extracted.KeyPeople.Count;

        if (commit)
        {
            await persistence.PersistAsync(extracted, row.Provider, row.EnrichmentId, row.RefreshedAt, CancellationToken.None)
                .ConfigureAwait(false);
        }

        persisted++;
    }
    catch (Exception ex)
    {
        failed++;
        log.LogWarning(ex, "Enrichment {Id} (MPI {Mpi}, {Provider}) failed.", row.EnrichmentId, row.MpiId, row.Provider);
    }
}

log.LogInformation(
    "Done. rowsWithIntel={Persisted} emptyExtract={Empty} failed={Failed} | drafts: projects={P} signals={S} actions={A} risks={R} keyPeople={K}{Note}",
    persisted, empty, failed, projects, signals, actions, risks, keyPeople,
    commit ? "" : "  (dry-run — nothing written; re-run with --commit)");
return failed > 0 ? 1 : 0;
