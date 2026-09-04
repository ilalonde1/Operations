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
using System.Text.RegularExpressions;
using Kor.Opportunities.Data.Awards;
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
// The ledger exists so that anything still holding a LOSER id can be forwarded to
// a live survivor. A row is therefore only broken when a forward is still
// possible — i.e. the loser id could still turn up — and the survivor is gone.
//
// 2026-09-04: this fired on 230932 -> 230931 where BOTH orgs had been deleted and
// nothing anywhere referenced either (alias, awards, bids, affiliations, facts and
// enrichment all zero). Nothing can ever ask to be forwarded from an id that no
// longer exists and that no row holds, so the pair is inert history, not a defect.
// The condition below says that out loud rather than leaving a standing ERROR
// that everyone learns to scroll past.
invariants.Add(("org_merge_dead_survivor",
    "CanonicalOrgMerge rows whose MergedInto survivor is missing or retired WHILE the loser id still exists — a forward that can be asked for and cannot be answered. Pairs where the loser is also gone are inert history and excluded",
    true,
    @"SELECT COUNT(*) FROM opportunities.CanonicalOrgMerge m
      WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL)
        AND EXISTS     (SELECT 1 FROM opportunities.CanonicalOrg lo WHERE lo.Id = m.MergedFromCanonicalOrgId);",
    @"SELECT TOP 5 m.MergedFromCanonicalOrgId, m.MergedIntoCanonicalOrgId, m.Reason FROM opportunities.CanonicalOrgMerge m
      WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg co WHERE co.Id = m.MergedIntoCanonicalOrgId AND co.RetiredAtUtc IS NULL)
        AND EXISTS     (SELECT 1 FROM opportunities.CanonicalOrg lo WHERE lo.Id = m.MergedFromCanonicalOrgId);"));

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
        WHERE r.EndedAtUtc IS NOT NULL   -- an IN-FLIGHT run looks identical to a
                                         -- failed one (Success stays 0 until the
                                         -- row is finalised). Counting them cost
                                         -- a real mistake on 2026-09-03: a
                                         -- 9-minute GovCanada run was read at 7
                                         -- minutes, declared failed, and its
                                         -- sibling cancelled. It had inserted 295.
        GROUP BY r.ProviderName)
      -- LifetimeInserts >= 5, not > 0: a source with a single insert in its whole
      -- life never 'worked' in the sense this check claims, and letting it through
      -- makes the description a lie (Bonfire_Saanich, 352 runs, 1 insert ever).
      SELECT COUNT(*) FROM runs
      WHERE RecentOkRuns >= 4 AND RecentInserts = 0 AND LifetimeInserts >= 5;",
    @"WITH runs AS (
        SELECT r.ProviderName,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) AND r.Success = 1 THEN 1 ELSE 0 END) AS RecentOkRuns,
               SUM(CASE WHEN r.StartedAtUtc > DATEADD(day,-30,sysdatetimeoffset()) THEN ISNULL(r.InsertedCount,0) ELSE 0 END) AS RecentInserts,
               SUM(ISNULL(r.InsertedCount,0)) AS LifetimeInserts,
               MAX(r.StartedAtUtc) AS LastRun
        FROM opportunities.IngestionRuns r
        WHERE r.EndedAtUtc IS NOT NULL   -- an IN-FLIGHT run looks identical to a
                                         -- failed one (Success stays 0 until the
                                         -- row is finalised). Counting them cost
                                         -- a real mistake on 2026-09-03: a
                                         -- 9-minute GovCanada run was read at 7
                                         -- minutes, declared failed, and its
                                         -- sibling cancelled. It had inserted 295.
        GROUP BY r.ProviderName)
      SELECT TOP 15 ProviderName, RecentOkRuns, LifetimeInserts,
             CONVERT(varchar(10), LastRun, 23) AS LastRun
      FROM runs
      WHERE RecentOkRuns >= 4 AND RecentInserts = 0 AND LifetimeInserts >= 5
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

// ── Adversarial review of the ArcGIS adapter, 2026-09-04 ─────────────────────
// Codex raised OpportunityKey prefix collision as a latent risk. It is not
// latent. IngestionService.ComposeOpportunityKey prefixes the key with the first
// 8 ALPHANUMERIC characters of the source name, and 54 sources reduce to
// BIDSTEND, plus most Bonfire tenants to BONFIRE?. Measured live the same day:
// BIDSTEND-26-067 "Audio Visual System Replacement and Upgrade" is ONE row
// carrying observations from BOTH BidsTenders_MapleRidge AND BidsTenders_Coquitlam
// — two municipalities' tender #26-067 merged into one opportunity.
//
// The key algorithm cannot simply be changed: every OpportunityKey on record is
// built from it, so a new algorithm re-keys the whole corpus. These two checks
// make the exposure and the damage countable in the meantime.
invariants.Add(("source_key_prefix_collision",
    "Two or more sources whose names share their first 8 alphanumeric characters, which is the whole of the OpportunityKey source prefix. Any two such sources that ever emit the same external reference produce ONE opportunity row for two different things. Name a new source so its first 8 alphanumeric characters are unique",
    false,
    @"WITH p AS (
        SELECT Id, Name,
               UPPER(LEFT(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name,'_',''),'-',''),' ',''),'.',''),'(',''),')',''), 8)) AS Prefix
        FROM opportunities.OpportunitySources
        WHERE IsEnabled = 1)
      SELECT ISNULL(SUM(N), 0) FROM (
        SELECT COUNT(*) AS N FROM p GROUP BY Prefix HAVING COUNT(*) > 1) x;",
    @"WITH p AS (
        SELECT Id, Name,
               UPPER(LEFT(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Name,'_',''),'-',''),' ',''),'.',''),'(',''),')',''), 8)) AS Prefix
        FROM opportunities.OpportunitySources
        WHERE IsEnabled = 1)
      SELECT TOP 20 Prefix, COUNT(*) AS Sources
      FROM p GROUP BY Prefix HAVING COUNT(*) > 1 ORDER BY COUNT(*) DESC;"));

invariants.Add(("opportunity_merged_across_sibling_sources",
    "ONE opportunity row observed by two DIFFERENT tenants of the same provider AT DIFFERENT URLs — two municipalities' tenders collapsed into one row. The distinct-URL condition is what separates this from legitimate cross-posting: CivicInfoBC_All and CivicInfoBC_Construction see the SAME listing at the SAME url and are correct; BidsTenders_Coquitlam .../Detail/60 and BidsTenders_MapleRidge .../Detail/7 are two different procurements. This is the OpportunityKey prefix collision actually firing",
    true,
    @"WITH fam AS (
        SELECT ob.OpportunityId,
               LEFT(s.Name, CHARINDEX('_', s.Name + '_') - 1) AS Family,
               ob.OpportunitySourceId, ob.Url
        FROM opportunities.OpportunityObservations ob
        JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
        WHERE s.Name LIKE '%[_]%')
      SELECT COUNT(*) FROM (
        SELECT OpportunityId FROM fam
        GROUP BY OpportunityId, Family
        HAVING COUNT(DISTINCT OpportunitySourceId) > 1 AND COUNT(DISTINCT Url) > 1) x;",
    @"WITH fam AS (
        SELECT ob.OpportunityId,
               LEFT(s.Name, CHARINDEX('_', s.Name + '_') - 1) AS Family,
               ob.OpportunitySourceId, ob.Url
        FROM opportunities.OpportunityObservations ob
        JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
        WHERE s.Name LIKE '%[_]%'),
      bad AS (
        SELECT OpportunityId FROM fam
        GROUP BY OpportunityId, Family
        HAVING COUNT(DISTINCT OpportunitySourceId) > 1 AND COUNT(DISTINCT Url) > 1)
      SELECT TOP 20 LEFT(o.OpportunityKey, 40) AS OppKey, LEFT(o.Name, 50) AS Name,
             STRING_AGG(CAST(s.Name AS nvarchar(max)), ' + ') AS Sources
      FROM bad b
      JOIN opportunities.Opportunities o ON o.Id = b.OpportunityId
      JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
      JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
      GROUP BY o.OpportunityKey, o.Name;"));

// Codex also found that source_everything_filtered_out only fires on ZERO
// LIFETIME inserts, so a feed that delivered once and is now rejecting
// everything slips between the checks. This is the time-windowed version: it
// HAS delivered before, items still arrive, and nothing has been kept in 30 days.
invariants.Add(("source_insert_rate_collapsed",
    "A feed that has delivered before, is still returning items, and has kept NOTHING in 30 days. The lifetime-zero checks cannot see this shape — it is the regression version of source_everything_filtered_out. Confirm against RelevanceGateRejects: the gate is often right, and the useful question is whether the buyer moved its construction work elsewhere",
    false,
    @"WITH lifetime AS (
        SELECT ProviderName, SUM(ISNULL(InsertedCount,0)) AS EverInserted
        FROM opportunities.IngestionRuns GROUP BY ProviderName),
      recent AS (
        SELECT ProviderName, COUNT(*) AS Runs,
               SUM(ISNULL(InsertedCount,0)) AS Ins,
               SUM(ISNULL(DuplicateCount,0) + ISNULL(SkippedCount,0)) AS Filtered
        FROM opportunities.IngestionRuns
        WHERE EndedAtUtc IS NOT NULL AND StartedAtUtc >= DATEADD(DAY, -30, SYSDATETIMEOFFSET())
        GROUP BY ProviderName)
      SELECT COUNT(*) FROM recent r JOIN lifetime l ON l.ProviderName = r.ProviderName
      WHERE l.EverInserted > 0 AND r.Runs >= 4 AND r.Ins = 0 AND r.Filtered > 0;",
    @"WITH lifetime AS (
        SELECT ProviderName, SUM(ISNULL(InsertedCount,0)) AS EverInserted
        FROM opportunities.IngestionRuns GROUP BY ProviderName),
      recent AS (
        SELECT ProviderName, COUNT(*) AS Runs,
               SUM(ISNULL(InsertedCount,0)) AS Ins,
               SUM(ISNULL(DuplicateCount,0) + ISNULL(SkippedCount,0)) AS Filtered
        FROM opportunities.IngestionRuns
        WHERE EndedAtUtc IS NOT NULL AND StartedAtUtc >= DATEADD(DAY, -30, SYSDATETIMEOFFSET())
        GROUP BY ProviderName)
      SELECT TOP 20 r.ProviderName, r.Runs, r.Filtered AS FilteredLast30d, l.EverInserted
      FROM recent r JOIN lifetime l ON l.ProviderName = r.ProviderName
      WHERE l.EverInserted > 0 AND r.Runs >= 4 AND r.Ins = 0 AND r.Filtered > 0
      ORDER BY r.Filtered DESC;"));

// ── The duplicate class the name-based checks cannot see (2026-09-04) ────────
// org_multi_entity_active_name finds duplicates by NAME. RJC was a duplicate by
// DOMAIN with DIFFERENT names — "RJC Engineers" against "Read Jones
// Christoffersen Ltd.", an initialism versus the words it stands for. No check
// was watching that shape, and it had split KOR's most formidable BC competitor
// almost exactly in half: 37 people / 40 awards on one row, 20 / 31 on the other.
//
// Second instance of the class (Perkins & Will was five fragments on 2026-09-03),
// so per the repo's rule 11 this is the check that finds them all rather than a
// third one-off merge.
//
// ⚠ IT IS A CANDIDATE LIST, NOT A DEFECT COUNT. Sharing a domain is legitimate
// for joint ventures ("EllisDon Kinetic, A Joint Venture"), for deliberate
// regional entities ("Stantec (Sacramento)" vs "Stantec Inc.") and for every
// ministry under www2.gov.bc.ca. Ordering by how much intel is split puts the
// ones worth merging at the top; each still needs a human look, and merging goes
// through BdCanonicalDedup --pairs with an allowlist entry recording the reason.
invariants.Add(("org_same_domain_different_names",
    "Live canonical orgs sharing a WebsiteDomain under different names — one real firm split across several rows, so every dossier, brief and search sees only part of what we hold. Ordered by split intel. CANDIDATES, not confirmed duplicates: JVs, regional entities and government ministries legitimately share a domain",
    false,
    @"SELECT ISNULL(SUM(N), 0) FROM (
        SELECT COUNT(*) AS N
        FROM opportunities.CanonicalOrg
        WHERE RetiredAtUtc IS NULL AND WebsiteDomain IS NOT NULL AND LTRIM(RTRIM(WebsiteDomain)) <> ''
        GROUP BY WebsiteDomain HAVING COUNT(*) > 1) x;",
    @"WITH o AS (
        SELECT co.Id, co.WebsiteDomain, co.DisplayName, co.KorProjectsCount, co.ClendorClientId,
               (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = co.Id) AS People,
               (SELECT COUNT(*) FROM opportunities.OpportunityAwards aw WHERE aw.AwardedToCanonicalOrgId = co.Id) AS Awards
        FROM opportunities.CanonicalOrg co
        WHERE co.RetiredAtUtc IS NULL AND co.WebsiteDomain IS NOT NULL AND LTRIM(RTRIM(co.WebsiteDomain)) <> '')
      SELECT TOP 20 WebsiteDomain, COUNT(*) AS Rows_, SUM(People) AS People, SUM(Awards) AS Awards,
             SUM(KorProjectsCount) AS KorJobs,
             MAX(CASE WHEN ClendorClientId IS NOT NULL THEN 'DELTEK' ELSE '' END) AS Client
      FROM o
      WHERE WebsiteDomain IN (SELECT WebsiteDomain FROM o GROUP BY WebsiteDomain HAVING COUNT(*) > 1)
      GROUP BY WebsiteDomain
      ORDER BY SUM(People) + SUM(Awards) DESC;"));

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

// ── Duplicate classes that need the platform's own normalizers (2026-09-04) ──
// Whole-database duplicate sweep, docs/BD-Duplicate-Sweep-Prompt-2026-09-04.md.
// The SQL invariants above can group by a STORED key. They cannot ask "would the
// resolver's own normalizer have grouped these?", and a SQL re-implementation of
// NormalizeForFuzzyMatch / NormalizeAggressiveKey / ExtractWebsiteDomain would be
// a third same-company heuristic, which Kor.Opportunities.Data/CLAUDE.md forbids.
// So this section loads the live org table ONCE (one query, ~10k rows) and runs
// the real code over it. Every check writes its FULL population to
// <out>/<key>-<stamp>.csv: the report shows a sample, the file is the population,
// the count line is the claim (repo rule 12). Nothing here writes to the database.
//
// The same-domain tiers were sized on 2026-09-04 BEFORE this was written, so they
// are measured, not guessed: 326 groups = U1 87 (every member a public-sector
// Kind) + U2 7 (public suffix, mixed kinds) + R1 68 (mixed public/commercial)
// + R2 11 (a JV-shaped member) + S1 85 (every name carries the domain's brand)
// + S2 35 (some names do) + R3 33 (none do — the RJC/initialism shape).
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var liveOrgs = new List<LiveOrg>();
try
{
    await using var orgCmd = new SqlCommand(@"
SELECT co.Id, co.Kind, co.DisplayName, co.FuzzyNormalizedName, co.WebsiteDomain, co.Website,
       co.ClendorClientId, co.KorProjectsCount,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL),
       (SELECT COUNT(*) FROM opportunities.OpportunityAwards aw WHERE aw.AwardedToCanonicalOrgId = co.Id),
       (SELECT COUNT(*) FROM opportunities.IntelNarrative n WHERE n.CanonicalOrgId = co.Id AND n.RetiredAtUtc IS NULL)
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
ORDER BY co.Id;", con) { CommandTimeout = 180 };
    await using var orgReader = await orgCmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await orgReader.ReadAsync().ConfigureAwait(false))
    {
        liveOrgs.Add(new LiveOrg(
            Id: orgReader.GetInt64(0),
            Kind: orgReader.GetString(1),
            DisplayName: orgReader.GetString(2),
            StoredFuzzy: orgReader.IsDBNull(3) ? "" : orgReader.GetString(3),
            Domain: orgReader.IsDBNull(4) ? "" : orgReader.GetString(4).Trim().ToLowerInvariant(),
            Website: orgReader.IsDBNull(5) ? "" : orgReader.GetString(5).Trim(),
            Clendor: orgReader.IsDBNull(6) ? null : orgReader.GetString(6),
            KorJobs: orgReader.GetInt32(7),
            People: orgReader.GetInt32(8),
            Awards: orgReader.GetInt32(9),
            Narratives: orgReader.GetInt32(10)));
    }
}
catch (Exception ex)
{
    checkFailures++;
    Emit($"[FAIL ] duplicate-class section: live org load failed — {ex.GetType().Name}: {ex.Message}");
}

if (liveOrgs.Count > 0)
{
    Emit(new string('-', 72));
    Emit($"DUPLICATE CLASSES — computed with the platform's own normalizers over {liveOrgs.Count} live orgs");

    // One check, same shape as the SQL invariants, plus the whole population on disk.
    void Worklist(string key, string desc, bool warn, IReadOnlyList<string[]> rows, string[] header, int groups, int orgs, int sample = 10)
    {
        var status = groups == 0 ? "OK   " : warn ? "WARN " : "INFO ";
        Emit($"[{status}] {key}: {groups}" + (orgs > 0 ? $" groups / {orgs} orgs" : ""));
        if (groups == 0) return;
        if (warn) warnViolations++;
        Emit($"         {desc}");
        foreach (var row in rows.Take(sample))
            Emit("         · " + string.Join(", ", header.Zip(row, (h, v) => $"{h}={v}")));
        var path = Path.Combine(outDir, $"{key}-{stamp}.csv");
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", header));
        foreach (var row in rows) csv.AppendLine(string.Join(",", row.Select(DupRules.CsvCell)));
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Emit($"         full population ({rows.Count} rows): {path}");
    }

    // 1. Control characters in the name. NormalizedName strips spaces but not
    //    CR/LF/TAB, so "Ministry of Forests" + LF + "Branch" and the one-line
    //    spelling are two rows the strict key cannot see as one. 9 of the 15
    //    groups in the 2026-09-04 BdCanonicalDedup dry run were this shape.
    {
        var rows = liveOrgs
            .Where(o => o.DisplayName.Any(c => c == '\r' || c == '\n' || c == '\t'))
            .Select(o => new[] { o.Id.ToString(), o.Kind, DupRules.ShowControls(o.DisplayName) })
            .ToList();
        Worklist("org_name_control_chars",
            "Live org DisplayName carrying CR, LF or TAB. The computed NormalizedName strips spaces but not these, so the row can never match its one-line twin and every intake mints another. Fix = strip at intake, then UPDATE the stored names; a merge is only needed where the twin already exists",
            true, rows, new[] { "Id", "Kind", "DisplayName" }, rows.Count, 0);
    }

    // 2. Stored fuzzy key disagrees with the normalizer. Empty is the hand-insert
    //    case CLAUDE.md warns about; stale is the same defect from the other side
    //    (the normalizer or the name moved on, the key did not). Either way
    //    FindByFuzzyNormalizedName cannot see the row and the next intake mints a
    //    twin. BdCanonicalDedup --backfill-fuzzy-key rewrites every key.
    {
        var rows = liveOrgs
            .Select(o => (o, expected: CanonicalOrgResolver.NormalizeForFuzzyMatch(o.DisplayName)))
            .Where(t => !string.Equals(t.o.StoredFuzzy, t.expected, StringComparison.Ordinal))
            .Select(t => new[] { t.o.Id.ToString(), t.o.Kind, t.o.DisplayName, t.o.StoredFuzzy, t.expected })
            .ToList();
        Worklist("org_fuzzy_key_stale",
            "Live orgs whose stored FuzzyNormalizedName is not what NormalizeForFuzzyMatch(DisplayName) returns today (empty counts). The write-time gate is blind to these rows, so the next reference to the same firm creates a duplicate. Fix = BdCanonicalDedup --backfill-fuzzy-key, then re-run this report: a backfilled key that now collides with another live row is a duplicate, not a repair",
            true, rows, new[] { "Id", "Kind", "DisplayName", "StoredFuzzy", "ExpectedFuzzy" }, rows.Count, 0);
    }

    // 3. Website anchor malformed. The domain is the one anchor the identity gate
    //    and the same-domain check can use; a row whose anchor is the string
    //    'null', or has a Website but no domain, is invisible to both.
    {
        var rows = new List<string[]>();
        foreach (var o in liveOrgs)
        {
            string? reason = null;
            if (o.Domain == "null")
                reason = "WebsiteDomain is the literal string 'null'";
            else if (o.Website.Length > 0 && o.Domain.Length == 0)
                reason = "Website set but WebsiteDomain empty (half-anchored)";
            else if (o.Website.Length == 0 && o.Domain.Length > 0)
                reason = "WebsiteDomain set but Website empty";
            else if (o.Website.Length > 0)
            {
                var expected = CanonicalOrgResolver.ExtractWebsiteDomain(o.Website);
                if (!string.Equals(expected, o.Domain, StringComparison.OrdinalIgnoreCase))
                    reason = $"WebsiteDomain is not ExtractWebsiteDomain(Website) = '{expected}'";
            }
            if (reason is not null)
                rows.Add(new[] { o.Id.ToString(), o.Kind, o.DisplayName, o.Website, o.Domain, reason });
        }
        Worklist("org_website_anchor_malformed",
            "Live orgs whose website anchor cannot do its job: the literal string 'null', a Website with no WebsiteDomain, a domain with no Website, or a domain that is not ExtractWebsiteDomain(Website). These rows are invisible to the domain resolver, to ResearchIdentityGate and to the same-domain check. Fix = one UPDATE per reason; no merge involved",
            true, rows, new[] { "Id", "Kind", "DisplayName", "Website", "WebsiteDomain", "Reason" }, rows.Count, 0);
    }

    // 4. The write-time gate's own key, recomputed. Two live rows with the same
    //    NormalizeForFuzzyMatch(DisplayName) are a duplicate the gate would have
    //    refused to mint had the key been stored — so every group here is also a
    //    stale/empty-key story (check 2) or a race. Sense Engineering 20284/927808
    //    was this shape on 2026-09-04.
    {
        var groups = liveOrgs
            .Select(o => (o, key: CanonicalOrgResolver.NormalizeForFuzzyMatch(o.DisplayName)))
            .Where(t => t.key.Length >= 6)
            .GroupBy(t => t.key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(t => t.o).ToList())
            .OrderByDescending(g => g.Sum(o => o.Intel))
            .ToList();
        var rows = DupRules.LoserSurvivorRows(groups, g => CanonicalOrgResolver.NormalizeForFuzzyMatch(g[0].DisplayName));
        Worklist("org_fuzzy_key_collision",
            "Live orgs that share NormalizeForFuzzyMatch(DisplayName) — the write-time gate's own key — under different Ids. The gate would have attached, not created, had the stored key matched; these are duplicates the platform itself can prove. Survivor suggested by frozen Kind, Clendor anchor, then intel; merge via BdCanonicalDedup --pairs",
            true, rows, DupRules.LoserSurvivorHeader, groups.Count, groups.Sum(g => g.Count));
    }

    // 5. The ampersand blind spot, as a DIFFERENTIAL. NormalizeForFuzzyMatch folds
    //    " & " (spaced) to " and " but NormalizeName then strips a bare "&", so
    //    "Perkins&Will" -> perkinswill and "Perkins and Will" -> perkinsandwill:
    //    different keys, one firm, and 271546 was minted that way. Run the same
    //    normalizer with every "&" and " + " folded to " and " first, and report
    //    ONLY the groups the current key cannot already see (check 4 has those).
    {
        var groups = liveOrgs
            .Select(o => (o, key: CanonicalOrgResolver.NormalizeForFuzzyMatch(DupRules.FoldAmpersand(o.DisplayName))))
            .Where(t => t.key.Length >= 6)
            .GroupBy(t => t.key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Where(g => g.Select(t => CanonicalOrgResolver.NormalizeForFuzzyMatch(t.o.DisplayName))
                         .Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Select(t => t.o).ToList())
            .OrderByDescending(g => g.Sum(o => o.Intel))
            .ToList();
        var rows = DupRules.LoserSurvivorRows(groups, g => CanonicalOrgResolver.NormalizeForFuzzyMatch(DupRules.FoldAmpersand(g[0].DisplayName)));
        Worklist("org_ampersand_fold_collision",
            "Live orgs whose names are one firm spelled with '&', ' + ' or ' and ' and which the current fuzzy key keeps apart (the key strips a bare '&' instead of folding it). Differential: same normalizer, ampersand folded first, minus the groups check 4 already sees. Fixing the normalizer moves these into check 4; until then they are merge candidates for --pairs with an allowlist reason",
            true, rows, DupRules.LoserSurvivorHeader, groups.Count, groups.Sum(g => g.Count));
    }

    // 6. What BdCanonicalDedup --commit would do TODAY. Its default mode groups by
    //    NormalizeAggressiveKey with no similarity gate (the fuzzy gate only guards
    //    --pairs), and on 2026-09-04 its dry run proposed re-merging 927758
    //    Continuum Architecture into 74300 Continuum Partners — the conflation split
    //    by hand the day before. Each group is classified so the reader can see
    //    which are honest twins and which are the Continuum shape (cross-Kind, only
    //    the stripped suffix words in common).
    {
        var groups = liveOrgs
            .Select(o => (o, key: CanonicalOrgResolver.NormalizeAggressiveKey(o.DisplayName)))
            .Where(t => t.key.Length >= 4)
            .GroupBy(t => t.key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => (key: g.Key, members: g.Select(t => t.o).OrderBy(o => o.Id).ToList()))
            .ToList();
        var rows = new List<string[]>();
        foreach (var (key, members) in groups)
        {
            var shape = members.Any(o => o.DisplayName.Any(c => c == '\r' || c == '\n' || c == '\t')) ? "control-char twin"
                : members.Select(o => CanonicalOrgResolver.NormalizeForFuzzyMatch(o.DisplayName)).Distinct(StringComparer.Ordinal).Count() == 1 ? "fuzzy twin (check 4)"
                : "aggressive-only: differs by a stripped word (inc/ltd/co/architects/partners/group)";
            var crossKind = members.Select(o => o.Kind).Distinct(StringComparer.Ordinal).Count() > 1 ? "CROSS-KIND" : "";
            foreach (var o in members)
                rows.Add(new[] { key, shape, crossKind, o.Id.ToString(), o.Kind, DupRules.ShowControls(o.DisplayName), o.Domain, o.Intel.ToString() });
        }
        Worklist("org_aggressive_key_collision",
            "Live orgs BdCanonicalDedup's DEFAULT mode would merge on --commit, grouped by NormalizeAggressiveKey, which strips inc/ltd/co/architects/partners/group and every non-alphanumeric. That path has NO similarity gate, so an 'aggressive-only CROSS-KIND' row here is the Continuum shape and must not be committed without a per-pair review. Never run --commit without --pairs while this list has a cross-kind row",
            true, rows, new[] { "AggressiveKey", "Shape", "CrossKind", "Id", "Kind", "DisplayName", "WebsiteDomain", "Intel" }, groups.Count, groups.Sum(g => g.members.Count), sample: 12);
    }

    // 7. Same domain, three ways. org_same_domain_different_names above counts all
    //    845 rows in one number, and a sweep that treats them as one class destroys
    //    the government hierarchy on its first run: canada.ca is 32 real departments.
    //    So the groups are tiered by a rule that can be argued with, not a TLD list:
    //    U = umbrella (every member a public-sector Kind, or a public suffix) — NOT
    //    a defect; S1 = shell (no public member, no JV-shaped name, every name
    //    carries the domain's brand label) — merge candidates; everything else is
    //    a review row with its tier saying why.
    {
        var domainGroups = liveOrgs
            .Where(o => o.Domain.Length > 0 && o.Domain != "null")
            .GroupBy(o => o.Domain, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => (domain: g.Key, members: g.OrderBy(o => o.Id).ToList()))
            .Select(g => (g.domain, g.members, tier: DupRules.DomainTier(g.members)))
            .OrderByDescending(g => g.members.Sum(o => o.Intel))
            .ToList();

        var umbrella = domainGroups.Where(g => g.tier is "U1" or "U2").ToList();
        var shell = domainGroups.Where(g => g.tier == "S1").ToList();
        var review = domainGroups.Where(g => g.tier is "R1a" or "R1b" or "R2" or "R3" or "S2").ToList();

        var uRows = umbrella
            .Select(g => new[] { g.domain, g.tier, g.members.Count.ToString(), string.Join(" | ", g.members.Select(o => $"{o.Id} {o.DisplayName}")) })
            .ToList();
        Worklist("org_same_domain_umbrella",
            "NOT A DEFECT — counted so the exclusion is visible. Same-domain groups where every member is a public-sector Kind (Buyer/Government) or the domain is a public suffix: departments, ministries, campuses. Correct as they stand; excluded from the shell list below. A commercial firm mis-Kinded as Buyer hides here",
            false, uRows, new[] { "Domain", "Tier", "Members", "Orgs" }, umbrella.Count, umbrella.Sum(g => g.members.Count), sample: 5);

        var sRows = new List<string[]>();
        foreach (var g in shell)
        {
            var s = DupRules.SuggestSurvivor(g.members);
            foreach (var l in g.members.Where(o => o.Id != s.Id))
                sRows.Add(new[] { l.Id.ToString(), s.Id.ToString(), g.domain, l.DisplayName, l.Kind, l.Intel.ToString(), s.DisplayName, s.Kind, s.Intel.ToString(), s.Clendor is null ? "" : "DELTEK" });
        }
        Worklist("org_same_domain_shell_brand_match",
            "One firm held as several rows: same WebsiteDomain, no public-sector member, no JV-shaped name, and EVERY member's name carries the domain's brand label (stantec.com -> 'stantec'). Regional and legal-entity variants of one company. Survivor suggested by frozen Kind, then Clendor anchor, then intel (people+awards+narratives+KOR jobs), then lowest Id; the dedup tool upgrades the survivor's Kind itself. Still reviewed per group before --pairs",
            true, sRows, new[] { "LoserId", "SurvivorId", "Domain", "LoserName", "LoserKind", "LoserIntel", "SurvivorName", "SurvivorKind", "SurvivorIntel", "SurvivorClient" }, shell.Count, shell.Sum(g => g.members.Count));

        var rRows = new List<string[]>();
        foreach (var g in review)
            foreach (var o in g.members)
                rRows.Add(new[] { g.domain, g.tier, DupRules.TierWhy(g.tier), o.Id.ToString(), o.Kind, o.DisplayName, o.Intel.ToString() });
        Worklist("org_same_domain_shell_review",
            "Same-domain groups that are neither umbrella nor brand-matched shells, each with the reason it needs a human: R1a a commercial row shares a name word with the public body on its domain (a mis-Kinded twin); R1b it shares none (a wrong anchor or a subsidiary — not a merge); R2 has a JV-shaped member (EllisDon Kinetic is not EllisDon); S2 only some names carry the brand (RJC Engineers vs Read Jones Christoffersen — the initialism shape, usually a true duplicate); R3 no name carries the brand (renamed brands, holding companies, parked domains)",
            true, rRows, new[] { "Domain", "Tier", "Why", "Id", "Kind", "DisplayName", "Intel" }, review.Count, review.Sum(g => g.members.Count));
    }

    // 9. The wrong anchor. A commercial org whose WebsiteDomain belongs to a public
    //    body: a public suffix, or a domain a Buyer/Government row also holds, with
    //    no shared name word and no brand match to excuse it. Not a duplicate — a
    //    developer stamped with the municipality whose project page named it. It
    //    matters more than a duplicate: ResearchIdentityGate now REFUSES research
    //    whose website disagrees with the one on file, so these rows will defend
    //    the wrong anchor until it is cleared. Singletons are included; check 7
    //    only sees a domain when two rows share it.
    {
        var publicByDomain = liveOrgs
            .Where(o => DupRules.IsPublicKind(o.Kind) && o.Domain.Length > 0 && o.Domain != "null")
            .GroupBy(o => o.Domain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(o => o.DisplayName).ToList(), StringComparer.Ordinal);
        var rows = new List<string[]>();
        foreach (var o in liveOrgs.Where(o => !DupRules.IsPublicKind(o.Kind) && o.Domain.Length > 0 && o.Domain != "null"))
        {
            var onPublicSuffix = DupRules.IsPublicSuffix(o.Domain);
            publicByDomain.TryGetValue(o.Domain, out var publicNames);
            if (!onPublicSuffix && publicNames is null) continue;
            if (publicNames is not null && publicNames.Any(n => DupRules.SharesSignificantWord(n, o.DisplayName))) continue; // R1a, check 7's
            var label = DupRules.BrandLabel(o.Domain);
            if (label.Length >= 3 && DupRules.Alnum(o.DisplayName).Contains(label, StringComparison.Ordinal)) continue;
            rows.Add(new[]
            {
                o.Id.ToString(), o.Kind, o.DisplayName, o.Domain,
                onPublicSuffix ? "public suffix" : "held by " + string.Join(" | ", publicNames!),
                o.Intel.ToString(),
            });
        }
        rows = rows.OrderByDescending(r => int.Parse(r[5])).ToList();
        Worklist("org_commercial_on_public_domain",
            "Commercial-Kind orgs whose WebsiteDomain is a public body's — a public suffix, or a domain a Buyer/Government row also holds — sharing no name word with that body and not carrying the domain's brand. Not duplicates. Mostly wrong anchors (a developer stamped with the municipality whose project page mentioned it: Highland Valley Copper on golden.ca); some are a body's own development arm on its parent's domain (Petroglyph on snuneymuxw.ca), which is correct and stays. Fix for a wrong one = clear the domain (one UPDATE) and let the next research write the right one back; while it stands, ResearchIdentityGate defends the wrong website",
            true, rows, new[] { "Id", "Kind", "DisplayName", "WebsiteDomain", "WhyPublic", "Intel" }, rows.Count, 0);
    }

    // 8. Whole-word name prefix, same Kind, commercial only: "Stantec" against
    //    "Stantec Consulting Ltd.", "Perkins&Will" against "Perkins&Will Calgary".
    //    The general form of org_thin_unsafe_redirect, which only looks at thin rows.
    //    Two different domains on the pair is treated as proof of two companies and
    //    the pair is dropped; one shared domain is already in check 7.
    {
        var rows = new List<string[]>();
        var byFirstToken = liveOrgs
            .Where(o => !DupRules.IsPublicKind(o.Kind))
            .GroupBy(o => DupRules.FirstToken(o.DisplayName), StringComparer.Ordinal)
            .Where(g => g.Key.Length >= 3 && g.Count() > 1);
        foreach (var g in byFirstToken)
        {
            var members = g.ToList();
            foreach (var a in members)
            {
                var aName = a.DisplayName.Trim();
                if (DupRules.Alnum(aName).Length < 6) continue;
                foreach (var b in members)
                {
                    if (a.Id == b.Id || a.Kind != b.Kind) continue;
                    if (!b.DisplayName.Trim().StartsWith(aName + " ", StringComparison.OrdinalIgnoreCase)) continue;
                    if (a.Domain.Length > 0 && b.Domain.Length > 0) continue; // same domain: check 7; different: two firms
                    if (DupRules.IsCompositeName(b.DisplayName)) continue;
                    rows.Add(new[] { a.Id.ToString(), b.Id.ToString(), a.Kind, aName, b.DisplayName.Trim(), a.Intel.ToString(), b.Intel.ToString(), a.Domain, b.Domain });
                }
            }
        }
        rows = rows.OrderByDescending(r => int.Parse(r[5]) + int.Parse(r[6])).ToList();
        Worklist("org_name_prefix_same_kind",
            "CANDIDATES, low confidence: a live commercial org whose whole name is a whole-word prefix of another live org of the same Kind, where at most one of the pair carries a domain. Catches suffix and region variants the fuzzy key keeps apart; also catches 'Vulcan' vs 'Vulcan Real Estate'. A pair is only a duplicate if a person confirms it — anchor the shorter row with a website first and let check 7 decide",
            true, rows, new[] { "ShortId", "LongId", "Kind", "ShortName", "LongName", "ShortIntel", "LongIntel", "ShortDomain", "LongDomain" }, rows.Count, 0);
    }

    Emit("DUPLICATE CLASSES — COVERAGE STATEMENT");
    Emit("  COVERS: live orgs only. Names that differ by control characters; stored fuzzy keys");
    Emit("          that disagree with the normalizer; malformed website anchors; rows the");
    Emit("          write-time key already proves duplicate; rows one ampersand fold away from");
    Emit("          that; everything the dedup tool's default --commit would merge; same-domain");
    Emit("          groups tiered umbrella / shell / review; same-Kind whole-word name prefixes.");
    Emit("  DOES NOT COVER: retired rows and the retired-to-live links the merge ledger owns;");
    Emit("          affiliation duplicates (person_duplicate_active_affiliation above, 2+ ACTIVE");
    Emit("          rows only — a retired predecessor is churn, not a duplicate); typos, and");
    Emit("          initialisms or renames on rows with NO domain (74% of the table), which no");
    Emit("          key can see; whether two same-domain rows are subsidiaries a BD user wants");
    Emit("          kept apart (Ledcor Construction vs Ledcor Properties) — the shell tier says");
    Emit("          'one company', a person says whether that is the right granularity.");
    Emit("  WOULD NOT CATCH: RJC before its merge — rjc.ca lands in S2 (review), not S1, because");
    Emit("          'readjoneschristoffersen' does not contain 'rjc'. The initialism shape is");
    Emit("          reported, not decided. Nor a conflated row whose members share one domain.");
    Emit("  ACCEPTANCE: org_aggressive_key_collision must list key 'continuum' with 74300 and");
    Emit("          927758 as aggressive-only CROSS-KIND; org_same_domain_shell_brand_match must");
    Emit("          list stantec.com. The third acceptance instance, org_fuzzy_key_collision on");
    Emit("          Sense Engineering 20284/927808, was FIXED on 2026-09-04 (merged into 927808,");
    Emit("          and the 16 hand-written keys behind it repaired), so that check is expected");
    Emit("          to be empty now — a green org_fuzzy_key_collision no longer proves it fired.");
    Emit("  SEVERITY: all WARN worklists except org_same_domain_umbrella (INFO, not a defect).");
}

Emit(new string('=', 72));
Emit($"Errors: {errorViolations}   Warnings: {warnViolations}   CheckFailures: {checkFailures}");

var reportPath = Path.Combine(outDir, $"integrity-report-{stamp}.txt");
await File.WriteAllTextAsync(reportPath, sb.ToString()).ConfigureAwait(false);
Console.WriteLine($"Report written: {reportPath}");

// Non-zero exit only on structural (Error) violations or a check that failed to
// run — Warn drift is reported but does not fail the pipeline.
return (errorViolations > 0 || checkFailures > 0) ? 1 : 0;

// One live canonical org with the counts the survivor choice and the ordering use.
sealed record LiveOrg(
    long Id, string Kind, string DisplayName, string StoredFuzzy, string Domain, string Website,
    string? Clendor, int KorJobs, int People, int Awards, int Narratives)
{
    public int Intel => People + Awards + Narratives + KorJobs;
}

// The rules behind the duplicate-class section. Deliberately small and readable:
// every one of these is a sentence a reviewer can disagree with, which is the point.
static class DupRules
{
    // "JV" as its own word; "Kinetic JV" is a joint venture, "JVM Holdings" is not.
    private static readonly Regex JvToken = new(@"\bJV\b", RegexOptions.Compiled);

    // "&" or " + " with any spacing becomes " and " — the fold NormalizeForFuzzyMatch
    // applies only to the spaced form " & ".
    private static readonly Regex AmpOrPlus = new(@"\s*(&|\s\+\s)\s*", RegexOptions.Compiled);

    public static readonly string[] LoserSurvivorHeader =
        { "LoserId", "SurvivorId", "Key", "LoserName", "LoserKind", "LoserIntel", "SurvivorName", "SurvivorKind", "SurvivorIntel", "SurvivorClient" };

    public static string Alnum(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) b.Append(ch);
        return b.ToString();
    }

    public static string FirstToken(string name)
    {
        var t = name.Trim();
        var i = t.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        return Alnum(i < 0 ? t : t[..i]);
    }

    public static string ShowControls(string s)
        => s.Replace("\r", "<CR>", StringComparison.Ordinal)
            .Replace("\n", "<LF>", StringComparison.Ordinal)
            .Replace("\t", "<TAB>", StringComparison.Ordinal);

    public static string CsvCell(string v)
    {
        var s = v.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string FoldAmpersand(string name) => AmpOrPlus.Replace(name, " and ");

    public static bool IsPublicKind(string kind) => kind is "Buyer" or "Government";

    public static bool IsPublicSuffix(string d)
        => d.EndsWith(".gov", StringComparison.Ordinal) || d.Contains(".gov.", StringComparison.Ordinal)
        || d.EndsWith(".mil", StringComparison.Ordinal)
        || d.EndsWith(".gc.ca", StringComparison.Ordinal) || d == "canada.ca" || d.EndsWith(".canada.ca", StringComparison.Ordinal)
        || d.EndsWith(".edu", StringComparison.Ordinal) || d.Contains(".edu.", StringComparison.Ordinal)
        || d == "alberta.ca" || d.EndsWith(".alberta.ca", StringComparison.Ordinal)
        || d.StartsWith("gov.", StringComparison.Ordinal) || d.EndsWith(".gov.bc.ca", StringComparison.Ordinal);

    public static bool IsCompositeName(string n)
        => n.Contains('/') || n.Contains(';') || n.Contains(" + ", StringComparison.Ordinal)
        || n.Contains("joint venture", StringComparison.OrdinalIgnoreCase)
        || n.Contains("consortium", StringComparison.OrdinalIgnoreCase)
        || JvToken.IsMatch(n);

    // The registrable label of the domain, alphanumeric: stantec.com -> stantec,
    // gga-arch.com -> ggaarch. For an umbrella like www2.gov.bc.ca this is "www2",
    // which is fine — umbrellas are tiered out before the label is consulted.
    public static string BrandLabel(string domain)
    {
        var i = domain.IndexOf('.');
        return Alnum(i < 0 ? domain : domain[..i]);
    }

    public static string DomainTier(IReadOnlyList<LiveOrg> g)
    {
        if (g.All(o => IsPublicKind(o.Kind))) return "U1";
        if (IsPublicSuffix(g[0].Domain)) return "U2";
        if (g.Any(o => IsPublicKind(o.Kind)))
        {
            // Sized 2026-09-04: most of this tier is NOT duplicates. "Dokie Wind
            // Energy Inc." on rdos.bc.ca and "Glacier Aggregates Inc." on
            // mapleridge.ca are commercial firms stamped with the domain of the
            // public body whose project page mentioned them. "City of Medicine Hat"
            // (Developer) on medicinehat.ca next to "The City of Medicine Hat"
            // (Buyer) IS a duplicate — a mis-Kinded twin. A shared significant
            // name word separates the two shapes well enough to say which is which.
            var pub = g.Where(o => IsPublicKind(o.Kind)).ToList();
            var com = g.Where(o => !IsPublicKind(o.Kind)).ToList();
            return com.All(c => pub.Any(p => SharesSignificantWord(p.DisplayName, c.DisplayName))) ? "R1a" : "R1b";
        }
        if (g.Any(o => IsCompositeName(o.DisplayName))) return "R2";
        var label = BrandLabel(g[0].Domain);
        var branded = label.Length >= 3 ? g.Count(o => Alnum(o.DisplayName).Contains(label, StringComparison.Ordinal)) : 0;
        return branded == g.Count ? "S1" : branded > 0 ? "S2" : "R3";
    }

    public static string TierWhy(string tier) => tier switch
    {
        "U1" => "every member is a public-sector Kind",
        "U2" => "public suffix, mixed Kinds",
        "R1a" => "a commercial-Kind row shares a name word with the public body on the same domain: a mis-Kinded twin of it",
        "R1b" => "a commercial-Kind row shares no name word with the public body on its domain: a wrong anchor or a subsidiary, not a merge",
        "R2" => "a member is JV/composite-shaped",
        "S1" => "every name carries the domain brand",
        "S2" => "only some names carry the domain brand (initialism/rename shape)",
        "R3" => "no name carries the domain brand",
        _ => tier,
    };

    // Words that say what SORT of body a name is, not WHICH one. "Vancouver Airport
    // Authority" and "Vancouver Airport Authority (YVR)" share 'vancouver' and
    // 'airport'; "Regional District of Okanagan Similkameen" and "Dokie Wind
    // Energy Inc." share nothing once 'regional' and 'district' are set aside.
    private static readonly HashSet<string> NameStopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "for", "with", "inc", "ltd", "llc", "llp", "corp", "corporation", "company", "co",
        "limited", "group", "holdings", "properties", "property", "projects", "project", "development",
        "developments", "developer", "construction", "constructors", "contracting", "contractors",
        "engineering", "engineers", "architects", "architecture", "architect", "consulting", "consultants",
        "services", "society", "association", "authority", "district", "regional", "city", "town", "village",
        "municipality", "county", "ministry", "department", "division", "branch", "office", "nation", "first",
        "band", "council", "canada", "canadian", "bc", "british", "columbia", "alberta", "university", "college",
        "school", "health", "housing", "capital", "partners", "partnership", "management", "international",
        "america", "americas", "usa", "west", "east", "north", "south", "central", "pacific", "operating",
    };

    private static readonly Regex NonWord = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private static HashSet<string> SignificantWords(string s)
        => NonWord.Split(s.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !NameStopWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);

    public static bool SharesSignificantWord(string a, string b)
    {
        var wa = SignificantWords(a);
        return wa.Count > 0 && SignificantWords(b).Any(wa.Contains);
    }

    // Frozen self-anchors survive; then the Deltek/Clendor-anchored row; then the
    // richest; then the oldest Id. Kind rank is left to BdCanonicalDedup, which
    // upgrades the survivor's Kind to the best in the pair on commit.
    public static LiveOrg SuggestSurvivor(IReadOnlyList<LiveOrg> g)
        => g.OrderByDescending(o => o.Kind is "KorStructural" or "KorClient")
            .ThenByDescending(o => !string.IsNullOrWhiteSpace(o.Clendor))
            .ThenByDescending(o => o.Intel)
            .ThenBy(o => o.Id)
            .First();

    public static List<string[]> LoserSurvivorRows(IEnumerable<IReadOnlyList<LiveOrg>> groups, Func<IReadOnlyList<LiveOrg>, string> key)
    {
        var rows = new List<string[]>();
        foreach (var g in groups)
        {
            var s = SuggestSurvivor(g);
            var k = key(g);
            foreach (var l in g.Where(o => o.Id != s.Id))
                rows.Add(new[] { l.Id.ToString(), s.Id.ToString(), k, l.DisplayName, l.Kind, l.Intel.ToString(), s.DisplayName, s.Kind, s.Intel.ToString(), s.Clendor is null ? "" : "DELTEK" });
        }
        return rows;
    }
}
