# MPI freshness and report coverage — result, 2026-10-08

Implemented in `DataHealthAuditJob` and its `SourceFreshness` partial. Worker
version bumped to 1.1.1. No deployment, source changes, provider edits, database
writes, or changes to `tools/BdIntegrityCheck`. `Q9SourceStalenessSql` is unchanged.

Read both earlier source-staleness documents first. Searched the existing audit,
provider/store code, tests, `tools/`, `docs/`, and neighboring database tools.
The current report and provider SourceKey conventions supplied the prior art.

## Section I: wording and denominator

Before, the zero-alert branch asserted:

> All enabled sources produced within their window. ✔

After, both branches print the live denominator and a named exclusion inventory:

> 115 of 125 enabled sources evaluated by §I; 10 excluded or unevaluated.

The zero-alert branch now says:

> No Q9 staleness thresholds were met among the evaluated sources; this does not prove recent production for sources below the run-count thresholds.

Coverage reuses Q9's `SourceRuns` CTE verbatim, joining its IDs back to the entire
enabled source table. It does not copy Q9's WHERE policy into a second list.
Undefined source-type values are explicitly UNEVALUATED because their storage
contract is unknown, even if Q9's negative type filter mechanically includes them.
The live ten exclusions are three historical sources, BD Outreach, and six MPI
sources. Every excluded row is named in the artifact below.

## Section J: scope and windows

The query starts from **every enabled type-18 source** and LEFT JOINs an explicit
attribution/cadence policy. Unknown type-18 names and historical feeds remain in
the report as UNEVALUATED with a reason. The six supported feeds are evaluated
separately; one active California source cannot refresh another one's timestamp.

- Production: `MAX(FirstSeenAtUtc)`, including retired records. CA has a 21-day
  window (three weekly cycles). AB/BC have a 92-day window (the longest calendar
  quarter), reflecting publisher release cadence rather than worker fetch cadence.
- Fetch evidence: `MAX(LastSeenAtUtc)`, with 14 days allowed for two weekly runs.
  Those weekly schedules already exist in `ScheduledJobDefinitions`.
- `UpdatedAtUtc` is not freshness evidence: enrichment can update it independently.
- AB/BC use `AB-`/`BC-` SourceKeys; CEQAnet uses `ceqa:`. The other CA feeds use
  their effective `sourceKeyPrefix` (mapping overrides ConfigJson, then source name),
  matching the provider's precedence. The query uses literal prefix comparison,
  not LIKE wildcards. Retired rows still prove provider production/re-seeing.

All gap verdicts are review prompts, not error conditions. **An Alberta fixture at
98 days with recent re-seeing renders `DEAD-GREEN — review (fetch current, no new
projects)`**, and the test asserts the report says gaps are not errors.

The live AB result is **137 days**, not the brief's province-wide 98: its 835
provider-owned `AB-` rows last gained a project on May 25. The 631 other AB rows
include other imports, with a newer July FirstSeen maximum; crediting them would
mask the feed's own production age. Consequently the live table has 835/36
provider-owned total/live rows, not the province-wide 1,466/308 totals. This is an
attribution distinction, not a change to retirements or a claim the publisher is
incomplete. The 137-day live result is also a review prompt, and the job succeeds.

**Coverage:** compares per-feed row-arrival and re-seeing timestamps against the
two cadence windows. **Not covered:** publisher completeness, column quality,
changes to existing project content, relevance/retirement correctness, or merged
rows retaining another importer's key. **Same-class miss:** a feed can lose columns
or pages and still pass if it continues inserting some rows. These limits appear
both in §J's source header comment and the rendered artifact.

## Section J query actually executed

```sql
WITH FeedPolicies AS (
    SELECT * FROM (VALUES
        (N'AB_MajorProjectsInventory', N'AB', N'AB-', 92, 14),
        (N'BC_MajorProjectsInventory', N'BC', N'BC-', 92, 14),
        (N'CA_CEQAnet', N'CA', N'ceqa:', 21, 14),
        (N'CA_SocrataSF', N'CA', NULL, 21, 14),
        (N'CA_SocrataSanDiego', N'CA', NULL, 21, 14),
        (N'CA_SanJoseCkan', N'CA', NULL, 21, 14)
    ) p(Name, Province, FixedPrefix, ProductionDays, FetchDays)
), EnabledMpi AS (
    SELECT s.Name, s.IsHistorical, p.Province, p.ProductionDays, p.FetchDays,
           CASE WHEN p.Name IS NULL THEN NULL ELSE COALESCE(p.FixedPrefix,
               COALESCE(NULLIF(LTRIM(RTRIM(COALESCE(sm.ValueJson,
                   JSON_VALUE(CASE WHEN ISJSON(s.ConfigJson) = 1 THEN s.ConfigJson ELSE N'{}' END,
                       '$.sourceKeyPrefix')))), N''), s.Name) + N':') END AS KeyPrefix
    FROM opportunities.OpportunitySources s
    LEFT JOIN FeedPolicies p ON p.Name = s.Name
    LEFT JOIN opportunities.OpportunitySourceMappings sm
        ON sm.OpportunitySourceId = s.Id AND sm.[Key] = N'sourceKeyPrefix'
    WHERE s.IsEnabled = 1 AND s.SourceType = 18
)
SELECT s.Name, s.Province, s.KeyPrefix, s.ProductionDays, s.FetchDays, s.IsHistorical,
       m.Rows_, m.LiveRows, m.LastProducedUtc, m.LastSeenUtc,
       DATEDIFF(day, m.LastProducedUtc, SYSDATETIMEOFFSET()) AS DaysSinceProduced,
       DATEDIFF(day, m.LastSeenUtc, SYSDATETIMEOFFSET()) AS DaysSinceSeen
FROM EnabledMpi s
OUTER APPLY (
    SELECT COUNT_BIG(*) AS Rows_,
           COUNT_BIG(CASE WHEN RetiredAtUtc IS NULL THEN 1 END) AS LiveRows,
           MAX(FirstSeenAtUtc) AS LastProducedUtc, MAX(LastSeenAtUtc) AS LastSeenUtc
    FROM opportunities.MajorProjectsInventory
    WHERE Province = s.Province AND s.IsHistorical = 0
      AND LEFT(SourceKey, LEN(s.KeyPrefix)) = s.KeyPrefix
) m
ORDER BY s.Name;
```

## Verification

All **12 targeted tests passed in Debug**:

- `CoverageQuery_ReusesQ9SourceUniverseWithoutItsAlertThresholds`
- `Report_WithOrWithoutAlerts_PrintsDenominatorAndNamedExclusions` (both branches)
- `EmptyUniverse_DoesNotClaimAllSourcesProduced`
- `ProductionAndFetchWindows_AreIndependentReviewPrompts` (six boundary/age cases)
- `NeverProduced_AndUnknownMpiFeed_AreVisible`
- `AlbertaAt98Days_RendersReviewAndCoverageLimits_NotAnError`

Command run:

```powershell
dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --filter "FullyQualifiedName~DataHealthAuditSourceFreshnessTests" -nologo
```

Also executed the actual Q10 SQL against read-only VALUES fixtures on SQL Server:
per-feed separation, prefix boundaries, mapping precedence, retired-row evidence,
unknown/historical source visibility, and disabled-source exclusion all passed.
The local probe is under `%TEMP%/kor-mpi-freshness-probe`; it invokes the real
`DataHealthAuditJob.Execute` with a local output directory and a mock Quartz context.
It does not start the Worker host, schedule work, or write database rows.

The complete live report was generated successfully at
`%TEMP%/kor-mpi-freshness-probe/report/latest.md`. Production `latest.md` was read for
comparison and left untouched. Q9 currently returns 45 alerts on the live read;
that number can change with ingestion and time, and its SQL was not modified.

## Live rendered sections I and J (verbatim)

## I. Source staleness sentinels
115 of 125 enabled sources evaluated by §I; 10 excluded or unevaluated.

| Excluded / unevaluated source | Reason |
|---|---|
| AB_MajorProjectsInventory | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| BC_MajorProjectsInventory | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| BcBidHistorical | Historical archive: uses historical observation tables; not evaluated here. |
| BD Outreach | Manual BD outreach: no automated polling. |
| CA_CEQAnet | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| CA_SanJoseCkan | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| CA_SocrataSanDiego | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| CA_SocrataSF | Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported. |
| GovCanada_Construction | Historical archive: uses historical observation tables; not evaluated here. |
| GovCanada_EngineeringServices | Historical archive: uses historical observation tables; not evaluated here. |

DEAD-GREEN, NEVER-PRODUCED and GATED are review prompts, not proof of a provider defect.

| Source | Verdict | Last produced | Last run | Runs 7d |
|---|---|---|---|---:|
| BidsTenders_Campbellriver | DEAD-GREEN | 2026-08-05 19:15:01 | 2026-10-08 21:51:43 | 28 |
| BidsTenders_Camrose | DEAD-GREEN | 2026-07-01 21:19:01 | 2026-10-08 21:27:15 | 28 |
| BidsTenders_Cityofgp | DEAD-GREEN | 2026-09-22 12:38:44 | 2026-10-08 22:08:26 | 28 |
| BidsTenders_Comoxvalleyrd | DEAD-GREEN | 2026-07-01 19:49:50 | 2026-10-08 22:07:10 | 28 |
| BidsTenders_Coquitlam | DEAD-GREEN | 2026-09-04 00:24:06 | 2026-10-08 21:22:05 | 28 |
| BidsTenders_CountyGP | DEAD-GREEN | 2026-09-21 18:58:14 | 2026-10-08 21:52:30 | 28 |
| BidsTenders_EPCOR | DEAD-GREEN | 2026-09-21 18:58:33 | 2026-10-08 21:52:22 | 28 |
| BidsTenders_Lethbridge | DEAD-GREEN | 2026-09-24 13:22:01 | 2026-10-08 21:51:45 | 28 |
| BidsTenders_MapleRidge | DEAD-GREEN | 2026-09-04 00:24:12 | 2026-10-08 21:47:08 | 28 |
| BidsTenders_Nanaimo | DEAD-GREEN | 2026-09-15 10:50:55 | 2026-10-08 21:41:40 | 28 |
| BidsTenders_Okotoks | DEAD-GREEN | 2026-07-01 21:29:00 | 2026-10-08 21:52:33 | 28 |
| BidsTenders_PortCoq | NEVER-PRODUCED | never | 2026-10-08 22:07:12 | 28 |
| BidsTenders_PrinceGeorge | DEAD-GREEN | 2026-09-17 18:15:56 | 2026-10-08 21:27:12 | 28 |
| BidsTenders_Rdco | DEAD-GREEN | 2026-07-14 14:59:09 | 2026-10-08 21:51:53 | 28 |
| BidsTenders_Squamish | DEAD-GREEN | 2026-08-05 14:12:30 | 2026-10-08 21:51:51 | 28 |
| BidsTenders_TOL | DEAD-GREEN | 2026-09-18 19:36:43 | 2026-10-08 21:26:40 | 28 |
| BidsTendersAwards_Camrose | DEAD-GREEN | 2026-09-16 21:41:40 | 2026-10-08 00:20:12 | 7 |
| BidsTendersAwards_DNV | DEAD-GREEN | 2026-09-22 22:11:43 | 2026-10-08 00:18:38 | 7 |
| BidsTendersAwards_Okotoks | DEAD-GREEN | 2026-09-11 21:40:03 | 2026-10-07 23:42:41 | 7 |
| BidsTendersAwards_PortCoq | NEVER-PRODUCED | never | 2026-10-08 00:22:50 | 7 |
| BidsTendersAwards_PortMoody | DEAD-GREEN | 2026-09-22 20:26:09 | 2026-10-08 21:42:16 | 28 |
| Bonfire_BCLC | DEAD-GREEN | 2026-07-21 16:21:21 | 2026-10-08 20:37:11 | 80 |
| Bonfire_BCTransit | DEAD-GREEN | 2026-07-01 18:25:30 | 2026-10-08 20:37:08 | 79 |
| Bonfire_Burnaby | NEVER-PRODUCED | never | 2026-10-08 20:36:37 | 80 |
| Bonfire_CapilanoU | NEVER-PRODUCED | never | 2026-10-08 21:56:41 | 80 |
| Bonfire_Epsb | DEAD-GREEN | 2026-09-21 13:44:41 | 2026-10-08 20:36:41 | 79 |
| Bonfire_ICBC | DEAD-GREEN | 2026-07-01 18:16:11 | 2026-10-08 21:56:40 | 80 |
| Bonfire_IslandHealth | DEAD-GREEN | 2026-07-15 15:06:52 | 2026-10-08 21:56:38 | 80 |
| Bonfire_KPU | DEAD-GREEN | 2026-09-15 10:50:33 | 2026-10-08 21:56:41 | 80 |
| Bonfire_MacEwanU | DEAD-GREEN | 2026-09-23 13:23:12 | 2026-10-08 20:37:14 | 79 |
| Bonfire_Nelson | DEAD-GREEN | 2026-07-09 14:53:20 | 2026-10-08 20:36:42 | 79 |
| Bonfire_Penticton | DEAD-GREEN | 2026-09-21 18:30:41 | 2026-10-08 20:37:15 | 79 |
| Bonfire_PrrdBc | DEAD-GREEN | 2026-07-10 15:00:25 | 2026-10-08 21:56:40 | 80 |
| Bonfire_Saanich | DEAD-GREEN | 2026-09-04 02:00:19 | 2026-10-08 20:36:41 | 79 |
| Bonfire_SFU | NEVER-PRODUCED | never | 2026-10-08 21:57:08 | 80 |
| Bonfire_StrathconaCty | DEAD-GREEN | 2026-09-22 16:04:28 | 2026-10-08 21:56:37 | 81 |
| Bonfire_TravelAlberta | DEAD-GREEN | 2026-07-01 18:25:29 | 2026-10-08 20:37:12 | 80 |
| Bonfire_VCH | DEAD-GREEN | 2026-09-08 13:45:52 | 2026-10-08 20:37:14 | 79 |
| CampbellRiver_DevelopmentApplications | DEAD-GREEN | 2026-09-24 18:56:39 | 2026-10-08 20:36:38 | 7 |
| Colwood_DevelopmentApplications | DEAD-GREEN | 2026-09-10 12:38:20 | 2026-10-08 14:06:24 | 7 |
| Courtenay_DevelopmentApplications | DEAD-GREEN | 2026-09-22 18:35:00 | 2026-10-08 00:02:37 | 7 |
| CoV_AwardedContracts | DEAD-GREEN | 2026-09-05 23:20:49 | 2026-10-08 00:57:29 | 7 |
| QualicumBeach_DevelopmentApplications | DEAD-GREEN | 2026-09-22 18:34:53 | 2026-10-08 20:37:09 | 7 |
| RDN_DevelopmentApplications | DEAD-GREEN | 2026-09-04 17:32:14 | 2026-10-08 20:37:11 | 7 |
| SamGov | DEAD-GREEN | 2026-07-28 06:00:06 | 2026-10-08 06:00:01 | 7 |

## J. MPI source freshness (type 18)
6 of 6 enabled type-18 sources evaluated; 0 unevaluated (named below with the reason).

Production = MAX(FirstSeenAtUtc); fetch = MAX(LastSeenAtUtc), per provider-owned SourceKey, including retired rows. Dates are UTC.
Production windows: California 21 days (three weekly cycles); AB/BC MPI 92 days (one full calendar quarter). Fetch window: 14 days (two weekly scheduled runs). Gaps are review prompts, not errors; a quiet publisher can be complete.

| Source | Scope | Rows / live | Newest project | Days since new | Last seen | Days since seen | Production / fetch days | Verdict |
|---|---|---:|---|---:|---|---:|---|---|
| AB_MajorProjectsInventory | AB: AB-* | 835 / 36 | 2026-05-25 07:01:06 | 137 | 2026-10-04 10:30:04 | 5 | 92 / 14 | DEAD-GREEN — review (fetch current, no new projects) |
| BC_MajorProjectsInventory | BC: BC-* | 741 / 249 | 2026-09-13 11:00:06 | 26 | 2026-09-20 11:00:05 | 19 | 92 / 14 | REVIEW — fetch overdue |
| CA_CEQAnet | CA: ceqa:* | 288 / 288 | 2026-10-04 11:30:50 | 5 | 2026-10-04 11:30:50 | 5 | 21 / 14 | Within windows |
| CA_SanJoseCkan | CA: sanjose:* | 61 / 61 | 2026-09-13 11:30:26 | 26 | 2026-10-04 11:30:47 | 5 | 21 / 14 | DEAD-GREEN — review (fetch current, no new projects) |
| CA_SocrataSanDiego | CA: sdcity:* | 57 / 50 | 2026-09-13 11:30:20 | 26 | 2026-10-04 11:30:45 | 5 | 21 / 14 | DEAD-GREEN — review (fetch current, no new projects) |
| CA_SocrataSF | CA: sf:* | 1534 / 239 | 2026-10-04 11:30:39 | 5 | 2026-10-04 11:30:40 | 5 | 21 / 14 | Within windows |

Coverage limits: measures row arrival and re-seeing, not publisher completeness, field quality, updates to existing projects, or retirement correctness. A feed losing columns or pages while still inserting rows can pass. Name-merged rows retaining another importer's key are not attributed to this feed. UpdatedAtUtc is not used because enrichment can refresh it.
