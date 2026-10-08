# CODEX — ArcGIS source reports success and inserts nothing for 30 consecutive days

**One defect. Do not fix anything else. Do not refactor.**

## The symptom, measured

`Coquitlam_DevelopmentApplications` (SourceType 20, `ArcGisFeatureOpportunityProvider`) ran
**30 times between 2026-09-08 and 2026-10-07**, every run `Success = 1`, and every run reported
**byte-identical counts**:

```
ins=0   dup=391   skip=92        ← every single run, unchanged, for 30 days
```

`391 + 92 = 483` items processed.

On **2026-10-08 13:06 UTC** the same source, same deployed binary, same config, was run via a
manually queued `opportunities.IngestionTriggers` row and reported:

```
ins=27  dup=354   skip=95        ← 354 + 27 = 381 keeps, 95 drops
```

Those 27 inserts are real applications dated 2026-09-23 → 2026-09-29 that had been missing,
including two Polygon Coronation TitleCo towers (one is 31 storeys / 300 rental units).

## The contradiction to explain

`tools/ArcGisProbe --source Coquitlam_DevelopmentApplications`, run against the live layer on
2026-10-08, reports:

```
477 row(s) over 1 page(s) collapsed to 476 application(s); 0 row(s) skipped.
gate KEEP : 381
gate DROP : 95
```

The probe's 381/95 **matches the manual run exactly**. The 30 scheduled runs instead claim
**483 items, 391 keeps, 92 drops** — a set that is 7 items larger and whose keep count is exactly
the number of Coquitlam rows already stored (391). In other words the scheduled runs behave as
though they fetched precisely the data already in the database and nothing newer, 30 times running,
without ever changing by a single row.

## What has already been ruled out — do not re-investigate these

- **Not stale binaries.** Deployed `Kor.Opportunities.Data.dll` and `Kor.Opportunities.Core.dll` on
  `Kor-APP01` are both **2026-09-28**, newer than the last source change to the provider
  (2026-09-23, `cab24987`) and to the gate (2026-09-04, `9b8eb07d`).
- **Not config.** `arcgis.applicantField=APPLICANT`, `externalRefField=PROJECT_NUMBER`,
  `titleField=ADDRESS`, `descriptionField=PROJECT_DESCRIPTION`, `postedDateField=SUBMISSION_DATE`,
  `pageSize=2000`, `maxPagesPerRun=10`, **no `arcgis.where`**, no status filter. 477 records fit in
  one page.
- **Not the relevance gate.** The missing records pass it — the probe shows them in KEEP, and they
  are absent from `opportunities.RelevanceGateRejects`.
- **Not our caching.** There is no `MemoryCache`, no static `HttpClient` field, no conditional-request
  logic anywhere under `Kor.Opportunities.Data/Ingestion/`.
- **Not CDN caching.** The layer responds `Cache-Control: public, max-age=30, s-maxage=30`.
- **Not a scheduled-vs-manual code difference.** `OpportunitySourceCronScheduler` does **not** call
  ingestion; its `QueueDueTriggersAsync` writes `IngestionTriggers` rows, which
  `IngestionTriggerPollerBackgroundService` drains. Both paths therefore reach the same
  `IngestionService`. **This was my hypothesis and it is wrong — do not pursue it.**
- **Not the wrong layer.** `Development_Information_Demo` is the publisher's own name. The only two
  other candidates in that ArcGIS org (`Planning and Development`, `Planning_and_Development`) carry
  zoning/OCP/boundary layers, not applications.

## The question to answer

**Can `ArcGisFeatureOpportunityProvider` return a stale or partial result set while reporting
`Success = true` and a plausible non-zero item count — and if so, through what code path?**

Specifically account for how a run can process **483** items when the layer serves **477 rows /
476 applications**, and do so identically 30 times.

## Where to look

- `Kor.Opportunities.Data/Ingestion/Providers/ArcGisFeatureOpportunityProvider.cs`
  — the paging loop around line 92 (`BuildQueryUrl` → `GetJsonAsync`), the `truncated` flag set on a
  null document or an `error` payload, the row→application collapse, and `ArcGisMapping.Paginate`
  (`arcgis.supportsPagination`, default **true**; Coquitlam does not set it).
- `Kor.Opportunities.Data/Ingestion/IngestionService.cs` — how `InsertedCount` /
  `DuplicateCount` / `SkippedCount` are accumulated, and whether a partial or failed page can still
  finalise a run as `Success = 1`.
- `Kor.Opportunities.Worker/Services/IngestionTriggerPollerBackgroundService.cs` and
  `OpportunitySourceCronScheduler.cs` — only to confirm the two paths converge, not to find a
  difference.

## The rule that must hold afterwards

**A run that did not observe the complete current result set must not finalise as `Success = 1`.**
Partial, truncated, errored or empty-page outcomes must surface — the Worker's own `CLAUDE.md` states
the governing principle: *"An ingestion job must assert FRESHNESS, not fetch success."*

## Shape of the expected answer

Either:

- **(a)** a concrete code path in which the provider or `IngestionService` yields a stale/partial set
  and still reports success — name the lines and the conditions that reach them; or
- **(b)** a proof that no such path exists, in which case the behaviour is upstream (the
  `*_Demo` service being rebuilt by the publisher) and the required change is detection rather than
  a provider fix.

Do not change behaviour without stating which of (a) or (b) the evidence supports.

## Reproduce

```sql
-- the frozen runs
SELECT TOP 40 StartedAtUtc, Success, InsertedCount, DuplicateCount, SkippedCount, ErrorSummary
FROM opportunities.IngestionRuns
WHERE ProviderName LIKE 'Coquitlam_Dev%' AND EndedAtUtc IS NOT NULL
ORDER BY StartedAtUtc DESC;

-- force a run; the poller picks it up within ~15s
INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), Id, N'Pending', sysdatetimeoffset(), N'codex-repro'
FROM opportunities.OpportunitySources WHERE Name = 'Coquitlam_DevelopmentApplications';
```

```
dotnet run --project tools/ArcGisProbe -c Release -- --source Coquitlam_DevelopmentApplications --show 5
```

## Out of scope for this brief

The unscheduled `tools/BdIntegrityCheck` (its `source_went_silent` check is correct and would have
caught this, but nothing runs it) — that is a separate, already-understood defect and must not be
bundled into this change.
