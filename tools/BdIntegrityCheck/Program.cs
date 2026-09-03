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

// ── Entity identity: is one org row one real-world company? ──────────────────
// Added 2026-09-03 after the Continuum incident: canonical 74300 held BOTH a
// Denver mixed-use developer AND a Victoria BC architecture practice, because
// NormalizeAggressiveKey strips "partners/llc/architecture/inc" so both names
// collapsed to "continuum", --merge-dba supplied the post-DBA key, and
// ChooseSurvivor preferred Developer (KindRank 2) over Architect (3). Nothing
// errored. A FirmNarrative refresh then rewrote the row for the Denver firm and
// destroyed the Victoria text in place. These checks would have failed on that
// row the day the merge happened, and they fail on every instance at once.

// Severity WARN, deliberately: the first live run returned 91, and inspection
// showed a large legitimate share — one institution with many campus domains
// (University of California: ucla/ucsb/ucsd/ucop), one body with several of its
// own domains (Alberta Health Services: ahs.ca + albertahealthservices.ca +
// gov.ab.ca), and member associations whose contacts sit at member organisations
// (Alberta Municipalities: calgary.ca + edmonton.ca). As an ERROR this would gate
// the pipeline red forever and be ignored inside a week. It is a REVIEW WORKLIST.
// The high-signal subset is its intersection with org_dba_merge_discipline_conflict.
invariants.Add(("org_conflated_people_domains",
    "REVIEW WORKLIST (not a pass/fail): active orgs whose active people span 2+ distinct corporate email domains — the two-companies-in-one-row signature. Known-legitimate matches: multi-campus institutions, bodies with several own domains, and member associations whose contacts sit at member orgs. Cross it with org_dba_merge_discipline_conflict for the likely-conflated ones",
    false,
    @"WITH d AS (
        SELECT a.CanonicalOrgId,
               LOWER(SUBSTRING(p.Email, CHARINDEX('@', p.Email) + 1, 200)) AS Domain
        FROM opportunities.IntelPersonAffiliation a
        JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
        JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
        WHERE a.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NULL
          AND p.Email IS NOT NULL AND CHARINDEX('@', p.Email) > 1
          AND LOWER(SUBSTRING(p.Email, CHARINDEX('@', p.Email) + 1, 200)) NOT IN
              (N'gmail.com', N'hotmail.com', N'outlook.com', N'yahoo.com', N'yahoo.ca',
               N'live.com', N'icloud.com', N'shaw.ca', N'telus.net', N'me.com'))
      SELECT COUNT(*) FROM (
        SELECT CanonicalOrgId FROM d GROUP BY CanonicalOrgId HAVING COUNT(DISTINCT Domain) > 1) x;",
    @"WITH d AS (
        SELECT a.CanonicalOrgId, co.DisplayName,
               LOWER(SUBSTRING(p.Email, CHARINDEX('@', p.Email) + 1, 200)) AS Domain
        FROM opportunities.IntelPersonAffiliation a
        JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
        JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
        WHERE a.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NULL
          AND p.Email IS NOT NULL AND CHARINDEX('@', p.Email) > 1
          AND LOWER(SUBSTRING(p.Email, CHARINDEX('@', p.Email) + 1, 200)) NOT IN
              (N'gmail.com', N'hotmail.com', N'outlook.com', N'yahoo.com', N'yahoo.ca',
               N'live.com', N'icloud.com', N'shaw.ca', N'telus.net', N'me.com'))
      SELECT TOP 10 CanonicalOrgId, MIN(DisplayName) AS DisplayName,
             COUNT(DISTINCT Domain) AS Domains,
             STRING_AGG(CONVERT(nvarchar(max), Domain), N' | ') WITHIN GROUP (ORDER BY Domain) AS DomainList
      FROM (SELECT DISTINCT CanonicalOrgId, DisplayName, Domain FROM d) u
      GROUP BY CanonicalOrgId HAVING COUNT(DISTINCT Domain) > 1
      ORDER BY COUNT(DISTINCT Domain) DESC, CanonicalOrgId;"));

invariants.Add(("org_dba_merge_discipline_conflict",
    "Active orgs that absorbed a 'X DBA: Y' alias via DedupeMerge where the alias names a DISCIPLINE the survivor's Kind contradicts (architecture alias on a Developer row = Continuum's exact shape)",
    false,
    @"SELECT COUNT(DISTINCT co.Id) FROM opportunities.CanonicalOrg co
      JOIN opportunities.OrgAlias oa ON oa.CanonicalOrgId = co.Id AND oa.Source = N'DedupeMerge'
      WHERE co.RetiredAtUtc IS NULL
        AND ( (oa.RawName LIKE N'%architect%'   AND co.Kind IN (N'Developer', N'GC'))
           OR (oa.RawName LIKE N'%construction%' AND co.Kind = N'Architect')
           OR (oa.RawName LIKE N'%contracting%'  AND co.Kind = N'Architect')
           OR (oa.RawName LIKE N'%engineering%'  AND co.Kind IN (N'Developer', N'Architect')) );",
    @"SELECT TOP 10 co.Id, co.Kind, co.DisplayName, oa.RawName AS ConflictingAlias
      FROM opportunities.CanonicalOrg co
      JOIN opportunities.OrgAlias oa ON oa.CanonicalOrgId = co.Id AND oa.Source = N'DedupeMerge'
      WHERE co.RetiredAtUtc IS NULL
        AND ( (oa.RawName LIKE N'%architect%'   AND co.Kind IN (N'Developer', N'GC'))
           OR (oa.RawName LIKE N'%construction%' AND co.Kind = N'Architect')
           OR (oa.RawName LIKE N'%contracting%'  AND co.Kind = N'Architect')
           OR (oa.RawName LIKE N'%engineering%'  AND co.Kind IN (N'Developer', N'Architect')) )
      ORDER BY co.Id;"));

invariants.Add(("org_narrative_without_website_anchor",
    "EXPOSURE GAUGE, not a defect: active orgs carrying an active narrative but NO Website. A FirmNarrative refresh on these is anchored only by DisplayName, which is how Continuum was rewritten for the wrong company. Expect this to fall as websites are backfilled",
    false,
    @"SELECT COUNT(*) FROM opportunities.CanonicalOrg co
      WHERE co.RetiredAtUtc IS NULL
        AND (co.Website IS NULL OR LTRIM(RTRIM(co.Website)) = N'')
        AND EXISTS (SELECT 1 FROM opportunities.IntelNarrative n
                    WHERE n.CanonicalOrgId = co.Id AND n.RetiredAtUtc IS NULL);",
    @"SELECT TOP 10 co.Id, co.Kind, co.DisplayName,
             (SELECT COUNT(*) FROM opportunities.IntelNarrative n
              WHERE n.CanonicalOrgId = co.Id AND n.RetiredAtUtc IS NULL) AS ActiveNarratives
      FROM opportunities.CanonicalOrg co
      WHERE co.RetiredAtUtc IS NULL
        AND (co.Website IS NULL OR LTRIM(RTRIM(co.Website)) = N'')
        AND EXISTS (SELECT 1 FROM opportunities.IntelNarrative n
                    WHERE n.CanonicalOrgId = co.Id AND n.RetiredAtUtc IS NULL)
      ORDER BY ActiveNarratives DESC, co.Id;"));

// ── Source freshness: is a feed still actually producing? ────────────────────
// Added 2026-09-03. BC Stats DISCONTINUED the Major Projects Inventory (last
// issue Q3 2025, page removed 30 June 2026). BcMajorProjectsInventoryJob kept
// running weekly and kept recording Success=true, because it successfully
// downloaded the last surviving CSV — a frozen Q1-2025 snapshot. Nobody noticed
// for two months. Same shape as BidsTenders_Surrey: 102 runs, all "successful",
// zero rows ever.
//
// The lesson, and the check: SUCCESS MEANS "THE FETCH WORKED", NOT "THE DATA IS
// CURRENT." A source that runs clean and inserts nothing is the signature of a
// publisher that has gone away, and with 109 enabled sources any of them can do
// this silently at any time.

invariants.Add(("source_went_silent",
    "A feed that USED to insert rows has inserted NOTHING in 30 days despite running successfully at least 4 times — the dead-publisher signature (BC MPI 2026, BidsTenders_Surrey). Success means the fetch worked, not that the data is current. Check the upstream source before assuming the code broke",
    false,
    @"WITH runs AS (
        SELECT r.ProviderName,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) AND r.Success = 1 THEN 1 ELSE 0 END) AS RecentOkRuns,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) THEN ISNULL(r.InsertedCount,0) ELSE 0 END) AS RecentInserts,
               SUM(ISNULL(r.InsertedCount,0)) AS LifetimeInserts
        FROM opportunities.IngestionRuns r
        GROUP BY r.ProviderName)
      SELECT COUNT(*) FROM runs
      WHERE RecentOkRuns >= 4 AND RecentInserts = 0 AND LifetimeInserts > 0;",
    @"WITH runs AS (
        SELECT r.ProviderName,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) AND r.Success = 1 THEN 1 ELSE 0 END) AS RecentOkRuns,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) THEN ISNULL(r.InsertedCount,0) ELSE 0 END) AS RecentInserts,
               SUM(ISNULL(r.InsertedCount,0)) AS LifetimeInserts,
               MAX(r.StartedAtUtc) AS LastRun
        FROM opportunities.IngestionRuns r
        GROUP BY r.ProviderName)
      SELECT TOP 15 ProviderName, RecentOkRuns, LifetimeInserts,
             CONVERT(varchar(10), LastRun, 23) AS LastRun
      FROM runs
      WHERE RecentOkRuns >= 4 AND RecentInserts = 0 AND LifetimeInserts > 0
      ORDER BY LifetimeInserts DESC;"));

// ⚠ InsertedCount = 0 is NOT by itself a dead feed. Probing the first version of
// this check on 2026-09-03 found three genuinely different situations hiding
// under one number, so it is split. Reporting them as one would have been the
// broad-name-over-a-narrow-check mistake.
//   Bonfire_VCH          359 runs · 0 inserted · 0 duplicate · 0 skipped  -> nothing ever arrived
//   Bonfire_IslandHealth 359 runs · 0 inserted · 182 dup · 282 skipped    -> 464 items arrived, none taken
//   Bonfire_AHS          358 runs · 0 inserted · 192 dup · 2,316 skipped  -> items arrive every run, all filtered

invariants.Add(("source_never_delivered_anything",
    "An enabled source that has run 5+ times and has never seen a SINGLE item — nothing inserted, nothing duplicate, nothing skipped. The feed is genuinely empty or the URL is wrong. Verify the endpoint returns items at all before assuming the publisher is quiet",
    false,
    @"WITH runs AS (
        SELECT r.ProviderName, COUNT(*) AS Runs,
               SUM(ISNULL(r.InsertedCount,0) + ISNULL(r.DuplicateCount,0) + ISNULL(r.SkippedCount,0)) AS AnyItems
        FROM opportunities.IngestionRuns r GROUP BY r.ProviderName)
      SELECT COUNT(*) FROM runs WHERE Runs >= 5 AND AnyItems = 0;",
    @"WITH runs AS (
        SELECT r.ProviderName, COUNT(*) AS Runs, MAX(r.StartedAtUtc) AS LastRun,
               SUM(ISNULL(r.InsertedCount,0) + ISNULL(r.DuplicateCount,0) + ISNULL(r.SkippedCount,0)) AS AnyItems
        FROM opportunities.IngestionRuns r GROUP BY r.ProviderName)
      SELECT TOP 20 ProviderName, Runs, CONVERT(varchar(10), LastRun, 23) AS LastRun
      FROM runs WHERE Runs >= 5 AND AnyItems = 0 ORDER BY Runs DESC;"));

// ⚠ Verified 2026-09-03 before trusting this check's own wording: for the sources
// examined the gate is RIGHT, not broken. Island Health's rejects are "Island
// Health Taxi Services" and "Mobile Food Services", both dropped for "no
// building/structural/design signal" — correct behaviour. Estate-wide that reason
// accounts for 10,046 rejects. So a source appearing here usually means the buyer
// simply does not tender construction through this channel, NOT that we are
// losing work. The finding to chase is the opposite one: where does that buyer
// put its construction work? For the health authorities the answer is the LMFM
// prequalified-consultant rosters and Infrastructure BC, not open Bonfire RSS.
invariants.Add(("source_everything_filtered_out",
    "A source where items ARRIVE but nothing is ever kept — 100% skipped or duplicate, zero inserts. Usually CORRECT (the buyer tenders non-construction through this channel). Treat as a prompt to ask where that buyer posts its construction work, not as a defect. Confirm against RelevanceGateRejects before acting",
    false,
    @"WITH runs AS (
        SELECT r.ProviderName, COUNT(*) AS Runs, SUM(ISNULL(r.InsertedCount,0)) AS Ins,
               SUM(ISNULL(r.DuplicateCount,0) + ISNULL(r.SkippedCount,0)) AS Filtered
        FROM opportunities.IngestionRuns r GROUP BY r.ProviderName)
      SELECT COUNT(*) FROM runs WHERE Runs >= 5 AND Ins = 0 AND Filtered > 0;",
    @"WITH runs AS (
        SELECT r.ProviderName, COUNT(*) AS Runs, SUM(ISNULL(r.InsertedCount,0)) AS Ins,
               SUM(ISNULL(r.DuplicateCount,0)) AS Dup, SUM(ISNULL(r.SkippedCount,0)) AS Skipped
        FROM opportunities.IngestionRuns r GROUP BY r.ProviderName)
      SELECT TOP 20 ProviderName, Runs, Dup, Skipped
      FROM runs WHERE Runs >= 5 AND Ins = 0 AND (Dup + Skipped) > 0
      ORDER BY (Dup + Skipped) DESC;"));

invariants.Add(("person_duplicate_active_affiliation",
    "Same person affiliated to the same org more than once, both active. Inflates how 'rich' an org looks, double-counts contacts in dossiers, and multiplies on every refresh. Found 2026-09-03 when a re-research after an org split added a second row for people who were already there",
    false,
    @"SELECT COUNT(*) FROM (
        SELECT IntelPersonId, CanonicalOrgId
        FROM opportunities.IntelPersonAffiliation
        WHERE RetiredAtUtc IS NULL
        GROUP BY IntelPersonId, CanonicalOrgId
        HAVING COUNT(*) > 1) x;",
    @"SELECT TOP 10 co.Id AS OrgId, co.DisplayName, p.DisplayName AS Person, COUNT(*) AS ActiveRows
      FROM opportunities.IntelPersonAffiliation a
      JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
      JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
      WHERE a.RetiredAtUtc IS NULL
      GROUP BY co.Id, co.DisplayName, p.DisplayName
      HAVING COUNT(*) > 1
      ORDER BY COUNT(*) DESC, co.Id;"));

invariants.Add(("org_thin_unsafe_redirect",
    "Thin orgs (no people) that a dossier whole-word-prefix redirect could resolve to a RICHER org with a DIFFERENT fuzzy key — the read-time identity substitution the write-time dedup gate would refuse. APPROXIMATION of SqlBriefDataStore.RedirectSafe; treat as a review worklist",
    false,
    @"WITH thin AS (
        SELECT co.Id, co.DisplayName, co.FuzzyNormalizedName
        FROM opportunities.CanonicalOrg co
        WHERE co.RetiredAtUtc IS NULL AND LEN(co.FuzzyNormalizedName) >= 6
          AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a
                          WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL))
      SELECT COUNT(*) FROM thin t
      WHERE EXISTS (
        SELECT 1 FROM opportunities.CanonicalOrg r
        WHERE r.RetiredAtUtc IS NULL AND r.Id <> t.Id
          AND r.FuzzyNormalizedName <> t.FuzzyNormalizedName
          AND LEN(r.DisplayName) > LEN(t.DisplayName)
          AND LEFT(LOWER(LTRIM(RTRIM(r.DisplayName))), LEN(LTRIM(RTRIM(t.DisplayName))) + 1)
              = LOWER(LTRIM(RTRIM(t.DisplayName))) + N' '
          AND EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a2
                      WHERE a2.CanonicalOrgId = r.Id AND a2.RetiredAtUtc IS NULL));",
    @"WITH thin AS (
        SELECT co.Id, co.DisplayName, co.FuzzyNormalizedName
        FROM opportunities.CanonicalOrg co
        WHERE co.RetiredAtUtc IS NULL AND LEN(co.FuzzyNormalizedName) >= 6
          AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a
                          WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL))
      SELECT TOP 10 t.Id AS ThinId, t.DisplayName AS ThinName,
             r.Id AS RicherId, r.DisplayName AS RicherName
      FROM thin t
      JOIN opportunities.CanonicalOrg r
        ON r.RetiredAtUtc IS NULL AND r.Id <> t.Id
       AND r.FuzzyNormalizedName <> t.FuzzyNormalizedName
       AND LEN(r.DisplayName) > LEN(t.DisplayName)
       AND LEFT(LOWER(LTRIM(RTRIM(r.DisplayName))), LEN(LTRIM(RTRIM(t.DisplayName))) + 1)
           = LOWER(LTRIM(RTRIM(t.DisplayName))) + N' '
      WHERE EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a2
                    WHERE a2.CanonicalOrgId = r.Id AND a2.RetiredAtUtc IS NULL)
      ORDER BY t.Id;"));

var sb = new StringBuilder();
void Emit(string line) { Console.WriteLine(line); sb.AppendLine(line); }

Emit($"BdIntegrityCheck — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
Emit(new string('=', 72));

// Repo rule 11: a harness states what it covers and what it does NOT, in its own
// summary — a broad name over a narrow check is worse than no check, because the
// next reader stops looking. This text is emitted into the same report the claims
// get written from, deliberately.
Emit("IDENTITY CHECKS — COVERAGE STATEMENT");
Emit("  COVERS: one-row-two-companies where the people carry differing corporate email");
Emit("          domains; DBA-merge survivors whose absorbed alias contradicts their Kind;");
Emit("          orgs whose narrative can be refreshed with no website anchor; thin orgs a");
Emit("          dossier prefix-redirect could resolve to a different richer entity.");
Emit("  DOES NOT COVER: whether any narrative's CONTENT is about the right company —");
Emit("          IntelNarrative is overwritten in place with no version history, so a wrong");
Emit("          refresh leaves no trace to compare against. It also cannot see a conflation");
Emit("          whose people share one domain, or have no email at all.");
Emit("  WOULD NOT HAVE CAUGHT: the Continuum narrative overwrite itself. It would have");
Emit("          flagged the CONFLATED ROW months earlier (org_dba_merge_discipline_conflict");
Emit("          fires on an 'architecture' alias sitting on a Developer), but once the");
Emit("          paragraph was replaced no check here can tell the new text is wrong.");
Emit("  ACCEPTANCE: verified 2026-09-03 that org_dba_merge_discipline_conflict returns");
Emit("          canonical 74300 (Continuum Partners, LLC) on both of its DedupeMerge");
Emit("          aliases. A check nobody has fired on a known instance is a hypothesis.");
Emit("  SEVERITY: all four identity checks are WARN worklists — they need human");
Emit("          judgement, and a permanently-red ERROR gets ignored inside a week,");
Emit("          which is worse than no check. Only the structural invariants above");
Emit("          (dangling org references, merge-ledger consistency) gate the exit code.");
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
