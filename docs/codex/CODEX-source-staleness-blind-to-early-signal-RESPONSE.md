# Source staleness sentinel — measured answer

Date: 2026-10-08

**Expected-answer option (b): the early-signal sources are evaluated, but do not
meet Q9's existing definition of stale.** This is a production-definition issue,
not a missing join, source-type exclusion, or missing schedule. No production
query change is justified as a coverage fix. No application code was changed.

Prior art checked: the supplied brief, the published October 4 report,
`DataHealthAuditJob`, `ScheduledJobDefinition`, `tools/BdIntegrityCheck`, the
ingestion/observation stores, and `tools/`, `docs/`, and the test projects for
an existing Q9 verification harness. There is no dedicated Q9 harness. The
Sunday job is already registered with `0 0 7 ? * SUN` and enabled by default.

## What actually excludes the rows from the displayed table

The existing join matches the real provider names, including:

```text
Coquitlam_DevelopmentApplications (ArcGisFeatureService)
RDN_DevelopmentApplications (EngagementHq)
```

All 15 enabled type 20/21/22 sources reach `SourceProduction`. The decisive
expression in `Q9SourceStalenessSql` is:

```sql
MAX(CASE WHEN r.InsertedCount + r.DuplicateCount > 0
         THEN r.EndedAtUtc END) AS LastRunProducedUtc
```

`LastProducedUtc` then takes the later of this timestamp and the latest
observation. Replaying duplicates therefore refreshes production on every run.
The final condition requiring production older than 14 days is false. Each
early-signal source has at least five recent runs; run volume is not the blocker.
The report table contains threshold matches, not an inventory of evaluated
sources. Its 22 rows do not mean only 22 sources were evaluated.

This behavior is consistent with the executable definition of feed activity.
It does **not** establish that new applications are arriving. A duplicate can
refresh existing opportunity fields, but does not establish new production.
`SqlOpportunityObservationStore` does not refresh `IngestedAtUtc` on a hash
collision, so the duplicate-run timestamp is what masks these two cases.

## Report-date evidence

Replayed against retained data at the actual report timestamp,
`2026-10-04T14:00:00.3563572Z`, limiting completed runs and observations to before
that instant. Current source configuration was used; this is a reconstruction,
not a historical database snapshot. The resulting 22 source names exactly match
the archived report, and all 15 early-signal sources have seven runs in its
seven-day window.

| Source | Completed runs in 30 days | Successful | Inserts | Duplicates | Last duplicate-or-insert run, UTC |
|---|---:|---:|---:|---:|---|
| Coquitlam_DevelopmentApplications | 26 | 26 | 0 | 10,166 | 2026-10-03 20:52:59 |
| RDN_DevelopmentApplications | 26 | 26 | 0 | 260 | 2026-10-04 03:16:14 |

Their latest observations before the report were respectively
`2026-09-04T07:24:26.0212644Z` and `2026-09-05T00:32:14.7305449Z`. Both were
older than 14 days, but the duplicate runs overrode them.

On the October 8 live read, RDN had 30 completed recent runs, zero inserts and
300 duplicates. Coquitlam had 31 completed recent runs, 27 inserts and 12,074
duplicates. Its 27 inserts occurred at `2026-10-08T20:06:41.8483745Z`; a later
completed run had zero inserts and 381 duplicates. These new inserts do not
prove that the separate ArcGIS code change caused the recovery.

## Coverage and exclusions

**115 of 125 enabled sources are evaluated by the existing query**, including
15 of 15 early-signal sources. All 115 have matching run history. There are zero
duplicate source rows, zero within-source duplicated run matches, and zero run
IDs matched to multiple evaluated sources in the live data.

The ten excluded enabled sources and the reasons for their existing exclusions:

| Sources | Count | Existing exclusion and reason |
|---|---:|---|
| BD Outreach | 1 | Type 7: manually imported outreach, no automated polling |
| BcBidHistorical; GovCanada_Construction; GovCanada_EngineeringServices | 3 | `IsHistorical = 1`: archive ingestion uses historical observation tables |
| AB_MajorProjectsInventory; BC_MajorProjectsInventory; CA_CEQAnet; CA_SanJoseCkan; CA_SocrataSanDiego; CA_SocrataSF | 6 | Type 18: internal provider upserts record zero ingestion counters by design; documented Q9 exception |

Q9 also excludes types 0 (Unknown) and 99 (Manual); neither has an enabled row
in this read. Thus every currently enabled row is accounted for. The published
report itself lacks this denominator and exclusion inventory. No coverage
widening was made, so the brief's conditional requirement to add a denominator
when widening was not triggered.

## Concrete alternative signal, measured but not applied

To monitor new production rather than repeated feed activity, change just the
expression above to:

```sql
MAX(CASE WHEN r.InsertedCount > 0 THEN r.EndedAtUtc END)
    AS LastRunProducedUtc
```

Keep the existing observation fallback: production can predate retained run
history. Keep the same source universe and the same 14-day / five-run thresholds.
This catches a repeatedly replayed ArcGIS feed whose latest new observation and
latest insert are both old. It is a review signal, not proof that the upstream
publisher is defective; legitimately quiet feeds can qualify too.

Read-only execution of that alternative at the report timestamp produced:

| Measure | Existing Q9 | Insert-or-observation alternative |
|---|---:|---:|
| Enabled sources | 125 | 125 |
| Evaluated sources | 115 | 115 |
| Evaluated early-signal sources | 15 | 15 |
| Flagged sources | 22 | 41 |
| Unique flagged source names | 22 | 41 |
| Flagged early-signal sources | 0 | 3 |

The three early-signal matches were Colwood, Coquitlam and RDN, all `DEAD-GREEN`.
Both requested examples appeared exactly once. The alternative changes the
freshness meaning across all feeds and adds 19 alerts; it is not a correction
to source coverage, so it has not been silently substituted for Q9.

The independent `BdIntegrityCheck.source_went_silent` check would **also miss
both examples at the report timestamp**. Each had 26 successful recent runs,
zero recent inserts, and zero recorded lifetime inserts, failing its
`LifetimeInserts >= 5` requirement. Their observations predate retained run
production. Grouping by `ProviderName` does not solve that history gap; the Q9
observation fallback is necessary for the demonstrated alternative.

## Verification and scope

- Executed the original Q9 unchanged against the live database: 22 alerts.
- Reconstructed the report date and asserted exact source-name agreement with
  `20261004-140000.md`: passed, 22 names.
- Executed the alternative as a SELECT only: 41 distinct sources, both requested
  examples `DEAD-GREEN` exactly once.
- Checked join coverage and duplicate matches against live source/run rows.
- All database access was read-only. No credentials were written to artifacts.
- No build/test run was needed for this documentation-only diagnostic result.
  No job, schedule, database row, deployed binary, or production report changed.
- Existing ArcGIS edits and unrelated untracked documents were left untouched.
