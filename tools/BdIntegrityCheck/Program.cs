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
//   --out  report directory   (default: ./integrity-reports)

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

var outDir = ReadArg(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "integrity-reports");
Directory.CreateDirectory(outDir);

// Severity: Error counts toward the non-zero exit (structural breakage that
// strands data); Warn is hygiene/staleness drift worth seeing but not fatal.
//
// CALIBRATION (2026-06-26): structural Error checks test for references to a
// *missing* (hard-deleted) parent — that is the true dangling-reference bug and
// should always be 0. References to a *retired* (soft, row present) parent are a
// separate, high-volume, mostly-expected staleness signal (read paths filter
// RetiredAtUtc), so they are reported as Warn, not Error, to avoid crying wolf.
var invariants = new (string Key, string Desc, bool Error, string CountSql, string SampleSql)[]
{
    ("org_enrichment_on_missing_org",
     "CanonicalOrgEnrichment rows whose org id has NO row at all (hard-deleted parent — a true dangling reference)",
     true,
     @"SELECT COUNT(*) FROM opportunities.CanonicalOrgEnrichment e
       WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = e.CanonicalOrgId);",
     @"SELECT TOP 5 e.Id, e.CanonicalOrgId, e.ProviderName FROM opportunities.CanonicalOrgEnrichment e
       WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = e.CanonicalOrgId);"),

    ("affiliation_on_missing_org",
     "Active IntelPersonAffiliation rows whose org id has NO row at all (hard-deleted parent)",
     true,
     @"SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
       WHERE a.RetiredAtUtc IS NULL
         AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = a.CanonicalOrgId);",
     @"SELECT TOP 5 a.Id, a.IntelPersonId, a.CanonicalOrgId FROM opportunities.IntelPersonAffiliation a
       WHERE a.RetiredAtUtc IS NULL
         AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = a.CanonicalOrgId);"),

    ("mpi_org_fk_on_missing_org",
     "Active MajorProjectsInventory rows with an Architect/GC/StructuralEngineer/Proponent FK whose org id has NO row",
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
      OR (m.ProponentCanonicalOrgId          IS NOT NULL AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId)));"),

    ("active_mpi_fk_on_retired_org",
     "Active MPI rows whose org FK points to a RETIRED (present) org — stale link, should re-resolve to a survivor",
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
      OR EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.ProponentCanonicalOrgId          AND co.RetiredAtUtc IS NOT NULL));"),

    ("active_affiliation_on_retired_org",
     "Active IntelPersonAffiliation rows pointing to a RETIRED (present) org — stale, should re-point to the survivor",
     false,
     @"SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
       WHERE a.RetiredAtUtc IS NULL
         AND EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NOT NULL);",
     @"SELECT TOP 5 a.Id, a.IntelPersonId, a.CanonicalOrgId FROM opportunities.IntelPersonAffiliation a
       WHERE a.RetiredAtUtc IS NULL
         AND EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NOT NULL);"),

    ("org_merge_from_still_active",
     "CanonicalOrgMerge ledger rows whose MergedFrom id is still a LIVE org (a live id marked merged-away = inconsistent)",
     true,
     @"SELECT COUNT(*) FROM opportunities.CanonicalOrgMerge m
       WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedFromCanonicalOrgId AND co.RetiredAtUtc IS NULL);",
     @"SELECT TOP 5 m.MergedFromCanonicalOrgId, m.MergedIntoCanonicalOrgId, m.Reason FROM opportunities.CanonicalOrgMerge m
       WHERE EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedFromCanonicalOrgId AND co.RetiredAtUtc IS NULL);"),

    ("org_merge_dead_survivor",
     "CanonicalOrgMerge ledger rows whose MergedInto survivor is missing or retired (chain not collapsed to a live org)",
     true,
     @"SELECT COUNT(*) FROM opportunities.CanonicalOrgMerge m
       WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL);",
     @"SELECT TOP 5 m.MergedFromCanonicalOrgId, m.MergedIntoCanonicalOrgId, m.Reason FROM opportunities.CanonicalOrgMerge m
       WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL);"),

    ("person_zero_markers",
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
         AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL);"),

    ("person_ambiguous_name_clusters",
     "Normalized names with >1 ACTIVE IntelPerson. NOTE: name-only, so includes legitimately distinct same-name people — NOT a blind-merge list; needs name+org+signal dedup",
     false,
     @"SELECT COUNT(*) FROM (
         SELECT NormalizedName FROM opportunities.IntelPerson
         WHERE RetiredAtUtc IS NULL AND NormalizedName IS NOT NULL AND NormalizedName <> N''
         GROUP BY NormalizedName HAVING COUNT(*) > 1) x;",
     @"SELECT TOP 5 NormalizedName, COUNT(*) AS ActiveRows FROM opportunities.IntelPerson
       WHERE RetiredAtUtc IS NULL AND NormalizedName IS NOT NULL AND NormalizedName <> N''
       GROUP BY NormalizedName HAVING COUNT(*) > 1 ORDER BY COUNT(*) DESC;"),

    ("org_multi_entity_active_name",
     "Active target-kind orgs with slash/semicolon/plus in DisplayName. NOTE: heuristic — legit firm names ('X, Planning + Interiors') match too; review before acting, never auto-reject",
     false,
     @"SELECT COUNT(*) FROM opportunities.CanonicalOrg co
       WHERE co.RetiredAtUtc IS NULL
         AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
         AND (co.DisplayName LIKE N'%/%' OR co.DisplayName LIKE N'%;%' OR co.DisplayName LIKE N'% + %');",
     @"SELECT TOP 5 co.Id, co.DisplayName, co.Kind FROM opportunities.CanonicalOrg co
       WHERE co.RetiredAtUtc IS NULL
         AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
         AND (co.DisplayName LIKE N'%/%' OR co.DisplayName LIKE N'%;%' OR co.DisplayName LIKE N'% + %');"),
};

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
