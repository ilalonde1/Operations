// BdIntegrityCheck — data-integrity invariant suite for KorOpportunitiesDb.
//
// Reactive whack-a-mole on BD data bugs ends here: instead of discovering the
// next "gremlin" three weeks later inside a drain, this asserts the invariants
// that have actually broken before and makes any drift LOUD and early. Run it
// nightly (Worker/Quartz) or on demand. Each invariant is a COUNT + a small
// sample; Error-severity violations set a non-zero exit code so it can gate a
// pipeline. Every check is isolated in try/catch — one bad query never aborts
// the whole report.
//
// Usage: BdIntegrityCheck [--db <connstr>] [--out <dir>]
//   --db   override connection (default: env KOR_OPPORTUNITIES_OPPORTUNITIESDB)
//   --out  report directory   (default: <app dir>/integrity-reports)

using System.Text;
using Microsoft.Data.SqlClient;

static string? ReadArg(string[] a, string name)
{
    for (var i = 0; i < a.Length - 1; i++)
        if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase))
            return a[i + 1];
    return null;
}

var cs = ReadArg(args, "--db") ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("Missing connection string. Pass --db or set KOR_OPPORTUNITIES_OPPORTUNITIESDB.");
    return 2;
}

// Default under the app (bin) dir, which is git-ignored — running from the repo
// root must not drop untracked report files there.
var outDir = ReadArg(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "integrity-reports");
Directory.CreateDirectory(outDir);

var invariants = new List<(string Key, string Desc, bool Error, string CountSql, string SampleSql)>();

// ── Table-driven org-reference checks ────────────────────────────────────────
// Every table carrying a CanonicalOrgId FK. An ACTIVE row (RetiredAtUtc IS NULL
// where the table has that column) referencing a MISSING (hard-deleted) org is a
// true dangling reference = Error and must be 0. Referencing a RETIRED (present)
// org is a stale link = Warn (high-volume, mostly-expected; read paths filter
// RetiredAtUtc). Driven off the live FK set so new tables are easy to add.
var orgLinkTables = new (string Table, bool HasRetired)[]
{
    ("IntelPersonAffiliation", true),
    ("IntelSignal", true),
    ("IntelAction", true),
    ("IntelNarrative", true),
    ("IntelRisk", true),
    ("IntelWork", true),
    ("IntelProjectKeyPerson", true),
    ("CanonicalOrgEnrichment", false),
    ("NewsArticleOrgMention", false),
    ("OrgAlias", false),
    ("BdResearchTriggers", false),
};
foreach (var (tbl, hasRet) in orgLinkTables)
{
    var active = hasRet ? "x.RetiredAtUtc IS NULL AND " : "";
    invariants.Add(($"{tbl}_on_missing_org",
        $"Active {tbl} rows whose CanonicalOrgId has NO row (hard-deleted parent — dangling reference)",
        true,
        $@"SELECT COUNT(*) FROM opportunities.{tbl} x
           WHERE {active}x.CanonicalOrgId IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.CanonicalOrgId);",
        $@"SELECT TOP 5 x.CanonicalOrgId, COUNT(*) AS Rows FROM opportunities.{tbl} x
           WHERE {active}x.CanonicalOrgId IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.CanonicalOrgId)
           GROUP BY x.CanonicalOrgId;"));
    invariants.Add(($"{tbl}_on_retired_org",
        $"Active {tbl} rows whose CanonicalOrgId is RETIRED (present) — stale link, should re-point to a survivor",
        false,
        $@"SELECT COUNT(*) FROM opportunities.{tbl} x
           WHERE {active}EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.CanonicalOrgId AND co.RetiredAtUtc IS NOT NULL);",
        $@"SELECT TOP 5 x.CanonicalOrgId, COUNT(*) AS Rows FROM opportunities.{tbl} x
           WHERE {active}EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.CanonicalOrgId AND co.RetiredAtUtc IS NOT NULL)
           GROUP BY x.CanonicalOrgId;"));
}

// ── MPI org FKs (four columns on one row — kept hand-written) ─────────────────
invariants.Add(("mpi_org_fk_on_missing_org",
    "Active MajorProjectsInventory rows with an Architect/GC/StructuralEngineer/Proponent FK whose org has NO row",
    true,
    @"SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
      WHERE m.RetiredAtUtc IS NULL AND (
        (m.ArchitectCanonicalOrgId          IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ArchitectCanonicalOrgId))
     OR (m.GeneralContractorCanonicalOrgId  IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.GeneralContractorCanonicalOrgId))
     OR (m.StructuralEngineerCanonicalOrgId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.StructuralEngineerCanonicalOrgId))
     OR (m.ProponentCanonicalOrgId          IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId)));",
    @"SELECT TOP 5 m.Id, m.ProjectStatus FROM opportunities.MajorProjectsInventory m
      WHERE m.RetiredAtUtc IS NULL AND (
        (m.ArchitectCanonicalOrgId          IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ArchitectCanonicalOrgId))
     OR (m.GeneralContractorCanonicalOrgId  IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.GeneralContractorCanonicalOrgId))
     OR (m.StructuralEngineerCanonicalOrgId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.StructuralEngineerCanonicalOrgId))
     OR (m.ProponentCanonicalOrgId          IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId)));"));
invariants.Add(("active_mpi_fk_on_retired_org",
    "Active MPI rows whose org FK points to a RETIRED (present) org — stale link",
    false,
    @"SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m
      WHERE m.RetiredAtUtc IS NULL AND (
        EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ArchitectCanonicalOrgId          AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.GeneralContractorCanonicalOrgId  AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.StructuralEngineerCanonicalOrgId AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId          AND co.RetiredAtUtc IS NOT NULL));",
    @"SELECT TOP 5 m.Id, m.ProjectStatus FROM opportunities.MajorProjectsInventory m
      WHERE m.RetiredAtUtc IS NULL AND (
        EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ArchitectCanonicalOrgId          AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.GeneralContractorCanonicalOrgId  AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.StructuralEngineerCanonicalOrgId AND co.RetiredAtUtc IS NOT NULL)
     OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId          AND co.RetiredAtUtc IS NOT NULL));"));

// ── Merge-ledger consistency ─────────────────────────────────────────────────
invariants.Add(("org_merge_from_still_active",
    "CanonicalOrgMerge rows whose MergedFrom id is still a LIVE org (a live id marked merged-away = inconsistent)",
    true,
    @"SELECT COUNT(*) FROM opportunities.CanonicalOrgMerge m
      WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedFromCanonicalOrgId AND co.RetiredAtUtc IS NULL);",
    @"SELECT TOP 5 m.MergedFromCanonicalOrgId, m.MergedIntoCanonicalOrgId, m.Reason FROM opportunities.CanonicalOrgMerge m
      WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedFromCanonicalOrgId AND co.RetiredAtUtc IS NULL);"));
invariants.Add(("org_merge_dead_survivor",
    "CanonicalOrgMerge rows whose MergedInto survivor is missing or retired (chain not collapsed to a live org)",
    true,
    @"SELECT COUNT(*) FROM opportunities.CanonicalOrgMerge m
      WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL);",
    @"SELECT TOP 5 m.MergedFromCanonicalOrgId, m.MergedIntoCanonicalOrgId, m.Reason FROM opportunities.CanonicalOrgMerge m
      WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL);"));

// ── Hygiene (Warn) ───────────────────────────────────────────────────────────
invariants.Add(("person_zero_markers",
    "Active IntelPerson rows with no email, no LinkedIn, no phone, and no active affiliation (prune/regen candidates)",
    false,
    @"SELECT COUNT(*) FROM opportunities.IntelPerson p
      WHERE p.RetiredAtUtc IS NULL
        AND (p.Email IS NULL OR LTRIM(RTRIM(p.Email)) = N'')
        AND (p.LinkedinUrl IS NULL OR LTRIM(RTRIM(p.LinkedinUrl)) = N'')
        AND (p.Phone IS NULL OR LTRIM(RTRIM(p.Phone)) = N'')
        AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL);",
    @"SELECT TOP 5 p.Id, p.DisplayName FROM opportunities.IntelPerson p
      WHERE p.RetiredAtUtc IS NULL
        AND (p.Email IS NULL OR LTRIM(RTRIM(p.Email)) = N'')
        AND (p.LinkedinUrl IS NULL OR LTRIM(RTRIM(p.LinkedinUrl)) = N'')
        AND (p.Phone IS NULL OR LTRIM(RTRIM(p.Phone)) = N'')
        AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL);"));
invariants.Add(("person_ambiguous_name_clusters",
    "Normalized names with >1 ACTIVE IntelPerson. NOTE: name-only, so includes legitimately distinct same-name people — NOT a blind-merge list; needs name+org+signal dedup",
    false,
    @"SELECT COUNT(*) FROM (
        SELECT NormalizedName FROM opportunities.IntelPerson
        WHERE RetiredAtUtc IS NULL AND NormalizedName IS NOT NULL AND NormalizedName <> N''
        GROUP BY NormalizedName HAVING COUNT(*) > 1) x;",
    @"SELECT TOP 5 NormalizedName, COUNT(*) AS ActiveRows FROM opportunities.IntelPerson
      WHERE RetiredAtUtc IS NULL AND NormalizedName IS NOT NULL AND NormalizedName <> N''
      GROUP BY NormalizedName HAVING COUNT(*) > 1 ORDER BY COUNT(*) DESC;"));
invariants.Add(("org_multi_entity_active_name",
    "Active target-kind orgs with slash/semicolon/plus in DisplayName. NOTE: heuristic — legit firm names ('X, Planning + Interiors') match too; review before acting, never auto-reject",
    false,
    @"SELECT COUNT(*) FROM opportunities.CanonicalOrg co
      WHERE co.RetiredAtUtc IS NULL
        AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
        AND (co.DisplayName LIKE N'%/%' OR co.DisplayName LIKE N'%;%' OR co.DisplayName LIKE N'% + %');",
    @"SELECT TOP 5 co.Id, co.DisplayName, co.Kind FROM opportunities.CanonicalOrg co
      WHERE co.RetiredAtUtc IS NULL
        AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
        AND (co.DisplayName LIKE N'%/%' OR co.DisplayName LIKE N'%;%' OR co.DisplayName LIKE N'% + %');"));

var sb = new StringBuilder();
void Emit(string line) { Console.WriteLine(line); sb.AppendLine(line); }

Emit($"BdIntegrityCheck — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
Emit(new string('=', 72));

await using var con = new SqlConnection(cs);
await con.OpenAsync().ConfigureAwait(false);

var errorViolations = 0;
var warnViolations = 0;
var checkFailures = 0;

foreach (var inv in invariants)
{
    try
    {
        await using var countCmd = new SqlCommand(inv.CountSql, con) { CommandTimeout = 180 };
        var raw = await countCmd.ExecuteScalarAsync().ConfigureAwait(false);
        var count = raw is null || raw is DBNull ? 0L : Convert.ToInt64(raw);
        var sev = inv.Error ? "ERROR" : "WARN ";
        var status = count == 0 ? "OK   " : sev;
        Emit($"[{status}] {inv.Key}: {count}");

        if (count > 0)
        {
            if (inv.Error) errorViolations++; else warnViolations++;
            Emit($"         {inv.Desc}");
            await using var sampleCmd = new SqlCommand(inv.SampleSql, con) { CommandTimeout = 180 };
            await using var r = await sampleCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await r.ReadAsync().ConfigureAwait(false))
            {
                var cols = new List<string>();
                for (var i = 0; i < r.FieldCount; i++)
                    cols.Add($"{r.GetName(i)}={(r.IsDBNull(i) ? "null" : r.GetValue(i))}");
                Emit("         · " + string.Join(", ", cols));
            }
        }
    }
    catch (Exception ex)
    {
        checkFailures++;
        Emit($"[FAIL ] {inv.Key}: check could not run — {ex.GetType().Name}: {ex.Message}");
    }
}

Emit(new string('=', 72));
Emit($"Errors: {errorViolations}   Warnings: {warnViolations}   CheckFailures: {checkFailures}");

var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var reportPath = Path.Combine(outDir, $"integrity-report-{stamp}.txt");
await File.WriteAllTextAsync(reportPath, sb.ToString()).ConfigureAwait(false);
Console.WriteLine($"Report written: {reportPath}");

// Non-zero exit only on structural (Error) violations or a check that failed to
// run — Warn drift is reported but does not fail the pipeline.
return (errorViolations > 0 || checkFailures > 0) ? 1 : 0;
