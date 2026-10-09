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
