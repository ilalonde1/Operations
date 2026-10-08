# CODEX — the weekly source-staleness sentinel cannot see the early-signal sources

**One defect. Do not fix anything else. Do not refactor. Do not build a new scheduled job —
the schedule already exists and works.**

## Start here: the mechanism is NOT missing

`DataHealthAuditJob` is registered in `ScheduledJobDefinitions.All` (Category `Audit`, cron
`0 0 7 ? * SUN`), is enabled by default with no production override, and **has produced a report
every Sunday without fail**. On `KOR-APP01`:

```
C:\ProgramData\KorOperations\DataHealthAudit\
  20260823-140000.md  20260830-140000.md  20260906-140000.md  20260913-140000.md
  20260920-140000.md  20260927-140000.md  20261004-140000.md  latest.md
```

Its **§I "Source staleness sentinels"** already does exactly the job that is wanted: it classifies
sources `DEAD-GREEN` (produced before, nothing in 14 days despite ≥5 recent runs) and
`NEVER-PRODUCED` (≥10 runs all-time, never produced).

**So the gap is not scheduling, and not the absence of a check.** Do not propose running
`tools/BdIntegrityCheck` on a schedule, and do not duplicate its 1,143 lines of invariant SQL into
the Worker.

## The defect, measured

§I of the 2026-10-04 report lists **22 sources**. Every one is `BidsTenders_*`, `Bonfire_*`, or
`SamGov`. **Zero** of the 15 enabled early-signal sources appear — the entire
`SourceType IN (20, 21, 22)` layer (ArcGIS Feature Service, Tempest/Prospero, EngagementHQ) is
invisible to the sentinel.

Two sources that **should** have been flagged and were not:

| Source | Type | Evidence | Expected verdict |
|---|---|---|---|
| `Coquitlam_DevelopmentApplications` | 20 ArcGIS | last produced **2026-09-01**; 29 runs in 30 days, all `Success=1`, `InsertedCount=0` | `DEAD-GREEN` |
| `RDN_DevelopmentApplications` | 22 EngagementHQ | 30 runs in 30 days, all `Success=1`, `InsertedCount=0` | `DEAD-GREEN` |

This matters disproportionately: the early-signal layer is the one the platform exists for
(`Kor.Opportunities.Data/CLAUDE.md` — *"Do not treat tenders as pipeline"*). It delivered ~3,864
development applications in 90 days. It is the layer with **no** staleness monitoring at all, and a
feed stayed silent for a month without the weekly report mentioning it once.

## The question to answer

**Why does `Q9SourceStalenessSql` exclude SourceType 20/21/22 sources, and what is the minimal
change that makes the sentinel cover every enabled source?**

Determine whether the exclusion is:

- **(a)** a join or filter that only matches certain `ProviderName` / `OpportunitySources` shapes;
- **(b)** a "produced" definition that the early-signal providers never satisfy (e.g. keyed on a
  table or column those providers do not write); or
- **(c)** a threshold (`>=5 recent runs`, `>=10 runs all-time`, 14-day window) that these sources
  legitimately fail to trip — in which case say so, and state what they would need instead.

## Where to look

- `Kor.Opportunities.Worker/Services/DataHealthAuditJob.cs`
  — `Q9SourceStalenessSql` (declared line 153; the verdict CASE is at lines 186–188; the comment
  block at 143–152 states the intended contract). Note the existing dated carve-out comment near
  line 173 about a source whose run history begins 2026-06-01 — that is prior art for how an
  exception is meant to be expressed.
- `Kor.Opportunities.Worker/Services/ScheduledJobDefinition.cs` — confirm registration only; do not
  add a job.
- `tools/BdIntegrityCheck/Program.cs` — **reference only.** Its `source_went_silent` invariant
  (`RecentOkRuns >= 4 AND RecentInserts = 0 AND LifetimeInserts >= 5`, keyed on
  `IngestionRuns.ProviderName`) is a second, independent implementation of the same idea that DOES
  cover these sources. Compare the two definitions; do not merge or move either.

## The rule that must hold afterwards

**Every enabled `OpportunitySources` row is either covered by the staleness sentinel, or is
explicitly and visibly excluded with a stated reason.** A source that can go quiet for a month
without appearing in the weekly report is the exact failure this sentinel was built to prevent.

If the fix widens coverage, the report must state its own denominator — "N of M enabled sources
evaluated" — so a future coverage gap is visible in the artifact itself rather than inferable only
by someone who already suspects it.

## Shape of the expected answer

Either:

- **(a)** the specific clause that excludes these sources, plus the minimal corrected query and the
  before/after source counts; or
- **(b)** a demonstration that they are excluded by legitimate thresholds, with the concrete
  alternative signal that would catch a silent ArcGIS feed.

State which. Do not widen the query without showing the new row count and confirming no source is
double-counted.

## Reproduce

```sql
-- the two that should have been flagged
SELECT ProviderName, COUNT(*) AS Runs30d,
       SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS Ok,
       SUM(ISNULL(InsertedCount, 0)) AS Inserts,
       MAX(StartedAtUtc) AS LastRun
FROM opportunities.IngestionRuns
WHERE EndedAtUtc IS NOT NULL
  AND StartedAtUtc >= DATEADD(day, -30, sysdatetimeoffset())
  AND (ProviderName LIKE 'Coquitlam_Dev%' OR ProviderName LIKE 'RDN_%')
GROUP BY ProviderName;

-- the whole early-signal layer, none of which reaches the report
SELECT Id, Name, SourceType, IsEnabled
FROM opportunities.OpportunitySources
WHERE SourceType IN (20, 21, 22)
ORDER BY SourceType, Name;
```

Report §I to compare against:
`\\KOR-APP01\C$\ProgramData\KorOperations\DataHealthAudit\latest.md`

## Out of scope

- `tools/BdIntegrityCheck` not being scheduled. Given §I already exists and runs, that tool's role
  is an open question, not a defect to fix in this change.
- The ArcGIS partial-read fix shipped on 2026-10-08 (`CODEX-arcgis-frozen-ingest.md`). Separate.
