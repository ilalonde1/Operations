# ScoringDiff

Capture once, then compare offline. The raw corpus stays local (for this task,
`.codex_scoring/corpus-before.json`, ignored by the repository). Scores, factors,
source memberships, latest observation descriptions, rowversions, and the existing
Island view's `IsBigTicket` bit are frozen. The before arm never calls new rules.

```powershell
dotnet run --project tools/ScoringDiff -c Debug -- --capture --snapshot .codex_scoring/corpus-before.json --out tools/ScoringDiff/evidence/before
dotnet run --project tools/ScoringDiff -c Debug -- --snapshot .codex_scoring/corpus-before.json --out tools/ScoringDiff/evidence/after --verify
```

Capture reads `KOR_OPPORTUNITIES_OPPORTUNITIESDB` from the process environment;
never put the credential on the command line or in a report. Existing snapshots
cannot be overwritten. The first capture uses the production scorer and profile.
The local before reading was captured at commit `809a6931`, before any scorer change.
It was committed in `cbaa1387`, including the failing contract and rendered chart.
Its SHA-256 is recorded alongside the evidence. Later captures use the scorer at
capture time, including its context; they cannot replace this original baseline.

Compares every row's stored/recomputed/proposed score and tier, factors, descending-score
rank (ID tie-break), per-source/per-kind aggregates, and all tier changes in both
directions. `rows.csv` includes every row; `tier-changes.csv` includes any change
against either the frozen rules or stored tiers. `--verify` checks bid/award score
stability, no gains in the existing Island-small Saanich cohort lacking named
multi-unit form, named-form targets above that cohort's maximum, and Calgary's mean
above Saanich's. The complete Island-small cohort remains visible in the summary.

Does not compare ingestion eligibility, absent/rejected rows, correctness of source
descriptions, UI ordering, Deltek ODBC facts, or future sources. The Worker uses no
Deltek facts; this harness follows that scoring path. A multi-unit project whose
latest description omits its form can stay wrongly ranked in both arms undetected.
The missing-description count measures one limit but cannot detect misleading text.

The named-form cohort is an evaluation assertion, not a production small-project
filter. `vIslandApplications` is the only small-project judgement used here, and it
explicitly includes ambiguous residential titles and some genuine multi-unit proposals
among its exclusions. Its entire gain worklist must be reviewed, not described as a
count of single-family homes.

## Live rescore and verification

The following modes were executed against the frozen corpus on 2026-10-09:

```powershell
dotnet run --project tools/ScoringDiff -c Debug -- --snapshot .codex_scoring/corpus-before.json --out tools/ScoringDiff/evidence/after --verify --check-store
dotnet run --project tools/ScoringDiff -c Debug -- --snapshot .codex_scoring/corpus-before.json --out tools/ScoringDiff/evidence/after --verify --apply --rollback
dotnet run --project tools/ScoringDiff -c Debug -- --snapshot .codex_scoring/corpus-before.json --out tools/ScoringDiff/evidence/after --verify --apply
```

`--apply` requires the contract to pass. It bulk-uploads all application inputs to
a temporary table, checks rowversions, latest observation IDs/descriptions, source
memberships and the profile, then updates **only** score/tier in one serializable
transaction. An input conflict aborts the whole batch. `--rollback` exercises the
actual write then rolls it back. Both modes re-read every corpus row and compare
against the immediate pre-write state. Non-score fields and untouched rowversions
must agree, as must every planned score/tier. Credentials stay in the process
environment. A committed snapshot cannot be reapplied after its rowversions change;
capture/review fresh inputs for another rescore.

The production reader carries `Opportunity.ScoringContext` as transient evidence:
latest description by observation ID, and kind from the shared source authority.
SQL projections enrich the existing read; no extra per-opportunity round trips.
The uncapped paged path read 8,841 rows in 2.27 seconds. Ingestion passes the candidate
description and already-read source mappings without another query; duplicate
sightings retain the newest persisted description. Insert/update/status return paths
retain context so score explanations and later edits keep the evidence.

Planning defaults borrow named residential forms and Calgary districts from
`StructuralRelevanceGate.BuildingSignals`. Spacing/plural variants and
`multi-residential` are aliases. Multi-unit forms receive 30 points; other admitted
commercial/industrial/special districts receive 20. Only the strongest match counts.
Broad admission words (residential, storey, dwelling, setback, suites), bare DC and
R-only codes receive no new bonus. Descriptions do not enter legacy geographic,
country or tender-term matching. There is no second small-project classifier.

## Result

See [after summary](evidence/after/summary.md), [contract](evidence/after/contract.txt),
[live ledger](evidence/after/applied-ledger.json), and
[verification](evidence/after/live-verification.txt).

The rule diff has 2,063 score increases, 2,062 tier promotions, no decreases or
demotions, and no bid/award changes. All 1,863 named-form targets score at least 33
against the control ceiling of 26 (1,843 failed before). All 726 Saanich controls
are unchanged. The existing Island-small view contains 224 gains, including
135 Saanich rows: e.g. ID 25588 names a 278-unit mixed-use development; 25592 names
a six-storey 63-unit apartment; 26056 names a four-unit houseplex. Some entries
are renovations to existing multi-unit buildings. This scorer recognizes named
forms; it does not decide whether a proposal is new construction or small work.

The live write updated 2,108 application rows: 2,054 tier promotions, zero demotions,
53 previously unscored tiers assigned, and one score-only change. All 8,841 rows
passed post-write verification. The extra movements versus the rule diff are
existing stored-score drift. `stored-movement.csv` shows actual persisted rank
movement; `persisted-tier-changes.csv` is the exact tier write list. The broader
`tier-changes.csv` also exposes old bid deadline/profile drift that was NOT written.

The original snapshot also caught a concurrent Worker refresh of audit/classification
timestamps on 50 MERX bids during the rollback rehearsal. Verification therefore
uses an immediate pre-write read for non-score fields; frozen application rowversions
remain mandatory. The successful rollback and committed run both had zero differences.

`PlanningScoringTests` covers description-only forms, district boundaries, no broad
single-family uplift, non-application parity, non-stacking synonyms, applicant-country
isolation and hard-reject precedence. The fast Data gate passed 245/245 tests.

The before/after `BdIntegrityCheck` reports have identical counts for all 98
invariants: one pre-existing `opportunity_merged_across_sibling_sources` error
(BCBID-233507), 39 warnings, zero query failures. See the two integrity reports
and `integrity-diff.txt` in the after evidence; the existing error was not repaired
as part of a scoring change.

Prior-art search covered `tools/`, `docs/`, and `../KOR.Drafter/db/tools/`;
`RelevanceGateDiff` supplied the corpus-diff pattern and `BdIntegrityCheck` the
existing live data gate. No existing scoring-diff harness was found.

The live data rescore is complete. Updated App/Worker assemblies still require the
normal deployment; the running old Worker can restore old scores on its next
application ingestion. No application/service deployment was performed in this task.
