# Codex — Close the BD writes that discard state they never checked

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 5 of 7, from the second audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-ADVERSARIAL-AUDIT-2026-10-13.md`
is your specification** — you wrote it against `3df392b8`, and each finding carries the file and
line references, the failure case and the required invariant. Read the finding before each fix.
Do not restate it; fix it.

These five share one shape: **a write lands without checking the state it is about to replace.**
A fallback path that skips the guard the main path has, a synthesis that overwrites a newer
verdict, a cutoff that claims evidence it never read, evidence that belongs to another project,
and a date edit that throws away the time.

⚠ **The fix is a check in the same transaction as the write, or a write that refuses.** Not a
retry, not a lock held across a model call, not a framework.

Diagnose each your way. If the fix differs from what the finding implies, say so and do the
right one.

## Fix 1 — BD-A01 (High): the fallback claim path bypasses the dismissal guard

`GrabAsync` was guarded in brief 1 (`AND DismissedAtUtc IS NULL`, gated by
`BdWriteBoundaryRegressionTests` and the `dismissed_opportunity_has_engagement` invariant).
`ClaimOwnedAsync`, reached when the grab fails and no engagement exists, creates the engagement
with no dismissal, status or ownership check. Every engagement-creation path gets the same
predicate, in the same transaction, and a 0-row result is a refusal the caller surfaces, not a
silent fallback.

## Fix 2 — BD-A02 (High): synthesis overwrites a newer assessment

The persist does not require the row it read to still be the latest, or its refresh timestamp to
match. Make the UPDATE conditional on what was read; on a mismatch, do not write, and count it as
rejected in the result the service already returns (brief 3 added `RefreshNote` disclosure — it
must be able to say "N rejected as stale").

## Fix 3 — BD-A03 (High): the cutoff advances past evidence that was never read

`TOP 12` observations and `TOP 12` actions, unordered, then `LastRefreshAtUtc = now`. The cutoff
must mean "evidence up to here was considered": bound the set deterministically and set the
cutoff to the newest timestamp actually included, or do not advance it past what was read.
Anything that arrived during the call stays eligible.

## Fix 4 — BD-A04 (High): another project's tender becomes this project's evidence

Evidence is matched on `LIKE` with the first fourteen characters of the project name, no buyer,
no geography, no resolved link, and no escaping of SQL wildcards. Evidence that can change a
persisted verdict needs a defensible association — a resolved project link, or nothing.
⚠ Do not widen this into fuzzy matching. Narrow it. If a project then has no evidence, that is
the correct answer.

## Fix 5 — BD-A10 (High): editing the date discards the closing time

Brief 4 deliberately left the date editor alone and said so. It is now a finding: the picker
loads a timestamp into a date-only control and saves midnight in the workstation's offset.
Changing the calendar date must preserve the stored time-of-day and offset (or replace them
explicitly). ⚠ `Kor.Opportunities.Data/Ingestion` and `PublishedClock` are closed and gated —
do not touch storage semantics; this is the editor.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, that fails before the change and passes after.
  Each gate states what it covers and **what it does not**, per CLAUDE.md rule 11.
- For Fix 1, extend `BdWriteBoundaryRegressionTests`; the invariant already exists.
- For Fixes 2 and 3, the gate proves the write is REFUSED or the cutoff is NOT advanced — a test
  that only shows the happy path writes is not a gate.
- Do not quantify a population you cannot measure. None of these five was measured on live data;
  say so in the response.

## Out of scope — do not do these

- The other ten findings. They are in briefs 6 and 7.
- Data changes, migrations, deploys, service actions.
- Any retry, locking or resilience framework. If a fix genuinely needs one, stop and say so.

Ping when applied, with the list of files touched.
