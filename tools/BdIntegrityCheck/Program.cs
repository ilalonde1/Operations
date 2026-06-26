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

// ── Dynamic org-reference checks (catalog-driven, self-maintaining) ───────────
// Enumerate EVERY column in the opportunities schema whose name ends in
// 'CanonicalOrgId' — the codebase convention for an org reference, whether it is
// FK-constrained or not, and regardless of role prefix (Buyer/Bidder/Target/
// Architect/...). For each: an ACTIVE row (RetiredAtUtc IS NULL where present)
// pointing at a MISSING org is a dangling reference (Error, must be 0); pointing
// at a RETIRED (present) org is a stale link (Warn). CanonicalOrgMerge is excluded
// — its MergedFrom/Into columns intentionally reference dead/retired ids and have
// their own consistency checks below. Any new org-link column is covered for free.
var orgLinkCols = new List<(string Table, string Col, bool HasRetired)>();
{
    await using var metaCmd = new SqlCommand(@"
SELECT t.name AS TableName, c.name AS ColName,
       CAST(MAX(CASE WHEN rc.name = 'RetiredAtUtc' THEN 1 ELSE 0 END) AS int) AS HasRetired
FROM sys.tables t
JOIN sys.columns c  ON c.object_id = t.object_id
LEFT JOIN sys.columns rc ON rc.object_id = t.object_id AND rc.name = 'RetiredAtUtc'
WHERE t.schema_id = SCHEMA_ID('opportunities')
  AND c.name LIKE '%CanonicalOrgId'
  AND t.name <> 'CanonicalOrgMerge'
  AND t.name <> 'CanonicalOrg'
GROUP BY t.name, c.name
ORDER BY t.name, c.name;", con) { CommandTimeout = 60 };
    await using var mr = await metaCmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await mr.ReadAsync().ConfigureAwait(false))
        orgLinkCols.Add((mr.GetString(0), mr.GetString(1), mr.GetInt32(2) == 1));
}
foreach (var (tbl, col, hasRet) in orgLinkCols)
{
    var active = hasRet ? "x.RetiredAtUtc IS NULL AND " : "";
    invariants.Insert(0, ($"{tbl}.{col}_on_missing_org",
        $"Active {tbl}.{col} rows whose org has NO row (hard-deleted parent — dangling reference)",
        true,
        $@"SELECT COUNT(*) FROM opportunities.{tbl} x
           WHERE {active}x.{col} IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.{col});",
        $@"SELECT TOP 5 x.{col} AS OrgId, COUNT(*) AS Rows FROM opportunities.{tbl} x
           WHERE {active}x.{col} IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.{col})
           GROUP BY x.{col};"));
    invariants.Add(($"{tbl}.{col}_on_retired_org",
        $"Active {tbl}.{col} rows pointing at a RETIRED (present) org — stale link, should re-point to a survivor",
        false,
        $@"SELECT COUNT(*) FROM opportunities.{tbl} x
           WHERE {active}EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.{col} AND co.RetiredAtUtc IS NOT NULL);",
        $@"SELECT TOP 5 x.{col} AS OrgId, COUNT(*) AS Rows FROM opportunities.{tbl} x
           WHERE {active}EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = x.{col} AND co.RetiredAtUtc IS NOT NULL)
           GROUP BY x.{col};"));
}

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
