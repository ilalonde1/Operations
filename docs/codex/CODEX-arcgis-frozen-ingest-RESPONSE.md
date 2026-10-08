# ArcGIS frozen ingestion — findings and fix

Date: 2026-10-08

**Evidence supports (a): incomplete ArcGIS reads can finalise as successful.**
This was reproduced through the real provider and `IngestionService`, with an
in-memory run store. It is a proven defect, but it is **not a proven cause of the
30 historical Coquitlam runs**. Those runs have no partial-read diagnostics, and
the current 477-row response cannot produce their 483 candidates.

Prior art checked: `tools/ArcGisProbe`, `tools/ArcGisFingerprint`, the existing
provider tests, the supplied brief, migration 299, and the Data/Worker guidance.
The brief already rules out binary/config/gate/cache/layer and trigger-path
investigations; those were not repeated. Live checks were SELECTs and public
ArcGIS GETs only. No production trigger, data change, publish, or deployment.

## Concrete failure path

Line numbers in this paragraph refer to the **pre-fix** provider:
`Kor.Opportunities.Data/Ingestion/Providers/ArcGisFeatureOpportunityProvider.cs`.

- A successful first page accumulates candidates. A later non-success HTTP
  response returns null: lines 94–97 set `truncated` and break. An HTTP-200
  ArcGIS error takes the same path at lines 105–111.
- A non-paging layer with `exceededTransferLimit=true` sets `truncated` at line
  148. Reaching the configured page cap with more results sets it at line 163.
- `truncated` only adds a diagnostic warning at line 175. The accumulated
  candidates are still returned at line 188.
- Missing/non-array `features` at line 114 and an empty features page at line
  154 stop the read without even setting `truncated`.
- Advancing `offset` by the requested page size at line 159 can skip unread rows
  when a non-final page returns fewer records than requested.

`IngestionService.cs:184` sets success solely from `failed == 0`. Lines 186–193
copy diagnostic warnings into `ErrorSummary` without changing that Boolean.
Consequently, a non-empty partial list whose candidates process normally can
finalise with `Success=true`, even with `DEGRADED` in its summary. Empty/malformed
page termination can do so with no summary at all. The counters start at zero
for each run and increment once per candidate outcome (lines 120–173); they do
not copy a prior run's counts or count existing DB rows independently.

## Accounting for 483 versus 477/476

The read-only database aggregation confirmed **30 of 30** completed runs from
September 8 through October 7 have `Success=true`, `0/391/92`, and a null
`ErrorSummary`. The October 8 run has `27/354/95`, also with a null summary.
The DB stores that run at `2026-10-08T13:06:37.6269707-07:00` (20:06 UTC), not
13:06 UTC as labelled in the brief.

On this investigation's live read, the configured layer's count endpoint returns
477. The provider-shaped query at offset 0, page size 2000, returns 477 features
with 476 distinct `PROJECT_NUMBER` values and no `exceededTransferLimit` field.
Its metadata reports maxRecordCount 2000 and supportsPagination true.

Each fetch creates a new dictionary keyed by application reference. Each usable
feature can add at most one entry, and repeated parcel/application references
collapse. Therefore **483 candidates require at least 483 usable feature rows
and distinct application references across the responses that run received**.
Replaying the same first page cannot inflate the count because of that collapse.
A fixed 477-row/476-reference response cannot yield 483 candidates, regardless
of page caps or error termination.

Migration `299_ArcGisCoquitlamMapleRidge.sql:6` records a live probe of **483
Coquitlam applications on September 3**. That is independent evidence that 483
was an earlier upstream population, not a count invented by ingestion. The
current metadata's dataLastEditDate is `2026-10-08T16:07:15.237Z`.

An unchanged older upstream snapshot explains identical 483-item runs followed
by a different 476-application response. This is an **inference**, not proof of
a publisher rebuild: neither historical HTTP bodies nor historical layer edit
metadata are available in this evidence. The partial-read defect alone does
not explain the historical counts. In particular, the warning-producing paths
do not match their null ErrorSummary values.

## Change and verification

The provider now throws with source, offset, row/page counts and reason on HTTP
failure, ArcGIS error payload, missing/invalid features envelope, empty page,
page cap with outstanding results, or non-paging transfer-limit exhaustion.
It checks cancellation before returning and advances by the rows actually read.
The existing `IngestionService.cs:220` exception path records `Success=false`
with that reason. No partial candidates are handed to persistence. A truly
empty source now fails visibly too; an operator must investigate that outcome.

The offset interpretation follows Esri's [Query layer documentation](https://developers.arcgis.com/rest/services-reference/enterprise/query-feature-service-layer/):
`resultOffset` skips records; `resultRecordCount` is an upper bound.

Verification, Debug, changed Data test project only:

- Before the provider fix: **12 failed / 15 passed** provider checks. Failures
  cover incomplete termination and short-page loss.
- Real service completion checks against the original provider: **5 failed /
  1 passed**. Each incomplete case incorrectly recorded success; the complete
  control passed. The fixed source was restored immediately after this check.
- Final `dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj -c Debug -nologo`:
  **180 passed / 0 failed / 0 skipped**, including 33 ArcGIS cases.
- `git diff --check` passed.

Coverage limit: this fix enforces detectable read completeness under the ArcGIS
response contract. It cannot detect a publisher serving a complete but stale
snapshot, lying about remaining results, or replacing rows during paging without
signalling the change. It is not a freshness guarantee for the 30-day historical
incident. The brief's separate unscheduled integrity-check defect remains out
of scope. The historical upstream cause remains unproven.
