# BD fixes 5 — writes that skip the check

Applied 2026-10-13. **Source changes and regression source only; not build/test verified.**
No build, test, application, model request, database tool, deployment or service action was run.
Source/diff review completed; `git diff --check` passed.
None of these five findings was measured against live data. No production incident counts or
affected-population estimates are asserted. The other ten audit findings are outside this change.

## Changes applied

1. **BD-A01:** `ClaimOwnedAsync` now executes exactly the same SQL transaction as `GrabAsync`:
   New, unowned, not dismissed; ownership and status change inside that transaction before
   engagement creation. Promotion no longer falls back after a refused grab. It surfaces an
   explicit refusal asking the user to refresh. Existing engagements can still be opened.
   Creating a new engagement for an already-owned or non-New opportunity is deliberately refused,
   rather than retaining the old fallback's broader eligibility.
2. **BD-A02:** Classification captures the enrichment row ID, refresh timestamp and JSON.
   Persistence requires all three still to match, and refuses if a newer honing row exists.
   The conditional UPDATE uses statement-scoped locks; no lock spans the model call. Zero-row
   writes count as `RejectedStale`, not synthesized. `RefreshNote` reports “N rejected as stale,”
   and the service re-reads after a rejection so it can show the newer assessment.
3. **BD-A03:** Evidence is ordered by update timestamp and action ID. The query reads thirteen
   rows: twelve for synthesis and one lookahead to detect a split timestamp group. It bounds
   evidence by the query's captured database time and retains full recommendation text.
   Persistence uses the last completely included timestamp, never model-completion time.
   If the cap splits a timestamp group, the cutoff remains before that group. Actions arriving
   during the model call with later timestamps remain eligible. The same conditional UPDATE
   also refuses if the interval being consumed contains an action whose ID, timestamp or
   recommendation no longer matches the captured evidence. This catches a late-committing
   action timestamped inside that interval, without holding a lock across the model call.
4. **BD-A04:** Removed title-prefix observation matching entirely. Only actions carrying the
   direct `MajorProjectsInventoryId` relationship are supplied as new evidence. The existing
   engagement/project junction is a many-to-many fuzzy cross-link, not a defensible replacement
   for a direct observation/project identity. No fuzzy replacement was added. The report note
   identifies the narrower evidence source and says unlinked tender observations are not used.
5. **BD-A10:** The picker loads the calendar date in the stored offset. Its save helper combines
   the selected date with the original time-of-day, including fractional seconds, and original
   numeric offset. Clearing the picker clears the deadline. New deadlines retain the documented
   local-midnight default. The editor's label describes these semantics. Ingestion,
   `PublishedClock`, timestamp storage and schema were not changed.

## Regression sources and expectations

These are authored gates, **not executed results**. No pre-fix failure or post-fix pass is claimed.
Each test class documents scope and same-class faults it cannot detect.

| Finding | Gate source | EXPECTED vs ACTUAL comparison authored |
|---|---|---|
| BD-A01 | `BdWriteBoundaryRegressionTests` | Both actual SQL constants must be identical and contain dismissal/status/ownership guards before insertion; a zero-row guard must roll back. The caller must throw a visible refusal and contain no alternate creation fallback. |
| BD-A02 | `BdGuardedSynthesisRegressionTests` | The actual UPDATE predicate must require captured ID, timestamp and exact JSON and exclude newer rows; actual parameter/callback wiring must use the captured snapshot. Refused persistence callbacks must yield zero synthesized and two rejected in both result accounting and report disclosure. |
| BD-A03 | `BdGuardedSynthesisRegressionTests` | Production evidence selection must leave the thirteenth signal and a signal arriving during synthesis beyond its cutoff. An incomplete timestamp group must not advance the cutoff over unseen evidence; a complete earlier group may advance. Input ordering must not change output. The actual UPDATE must write the evidence cutoff, not the wall clock, and refuse if the consumed interval contains uncaptured or changed evidence. |
| BD-A04 | `BdGuardedSynthesisRegressionTests` | Actual classification SQL must use the resolved action FK and ordered bounded evidence, with no observation-name/LIKE match. The supplied recommendation must retain its full text. |
| BD-A10 | `BdDeadlineEditorRegressionTests` | Actual editor conversion must preserve the stored clock time and offset across a date change, an offset date boundary and a DST date change; untouched values must preserve the exact offset/instant. Clearing and new values have explicit expectations, and source checks require the dialog to use the helper and stored-offset calendar date. |

Existing brief 2–4 gates were updated for the new synthesis result, cutoff assignment and date
editor label. Their earlier report/display guarantees remain in scope.

The SQL gates inspect the production statement and binding paths; they do **not** execute a SQL
Server concurrency schedule or prove deployed locking/trigger behavior. Callback refusal tests
prove accounting and disclosure, not SQL execution. Pure evidence/date tests exercise production
helpers, not WPF interaction or database writes. A wrongly assigned action FK, a signal inserted
later with a timestamp before the prior cutoff, a trigger changing writes, or a WPF commit failure supplying
the old selected date would escape these gates.

## Deliberate limits

- More than twelve actions sharing the same boundary timestamp cannot all be consumed in this
  bounded on-demand batch. The cutoff stays before that group, preserving eligibility instead
  of falsely acknowledging the unseen tail. Such a group can be reconsidered by later research.
- The existing TTL policy still controls when due evidence is synthesized. The cutoff fix
  preserves eligibility; it does not promise immediate processing on the next view.
- The CAS comparison covers assessment identity, timestamp and JSON. It does not validate the
  factual quality of the model's response or upstream project identity.
- A preserved numeric offset stays fixed across a changed date; this is the requested stored
  offset policy, not an inferred buyer time zone with DST recalculation.

## Files touched

Production:

- `Kor.Opportunities.Data/Crm/SqlPursuitGrabStore.cs`
- `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs`
- `Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs`
- `Kor.Opportunities.Data/BdReports/SqlBdReportService.cs`
- `Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs`
- `Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml`

Regression sources:

- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdWriteBoundaryRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdGuardedSynthesisRegressionTests.cs` (new)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDeadlineEditorRegressionTests.cs` (new)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdReportMeaningRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdMissingEvidenceRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs`

Handoff:

- `docs/codex/CODEX-BD-FIXES-5-WRITES-THAT-SKIP-THE-CHECK-RESPONSE.md` (this report)

Claude owns the Debug build and targeted test execution; Ian owns any database work. Runtime
verification, including pre-fix negative controls, remains pending. Pre-existing unrelated
untracked files were left untouched.
