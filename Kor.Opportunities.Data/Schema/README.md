# KorOpportunitiesDb schema migrations

Applied **manually** (SSMS or the session's GO-split ADO pattern) against
`KOR-APP01\SQLEXPRESS · KorOpportunitiesDb` — there is no automated runner.
Every migration must be idempotent (guarded `IF NOT EXISTS` / `COL_LENGTH` /
`OBJECT_ID`) and GO-batched (add a column and reference it in **separate**
batches — SQL Server parses a whole batch before executing).

## Ordering caveats (audit 2026-07-01 M6)

Filename number = apply order, with these historical exceptions. Do **not**
renumber them — all are long applied to the live DB and renaming would break
the paper trail; any future tooling must special-case them instead:

| Number | Files sharing it | Correct relative order |
|---|---|---|
| 47 | `47_MpiStructuralGcIndexes.sql`, `47_PrimePipelineView.sql` | either (independent) |
| 48 | `48_BcBidMaxPagesLift.sql`, `48_CrmEngagementBdTracking.sql` | either (independent), **but** `48_CrmEngagementBdTracking` must precede `49_CrmEngagementProjectLink` |
| 49 | `49_BcBidFrequencyAndGridDump.sql`, `49_CrmEngagementProjectLink.sql` | either (independent) |
| 112 | `112_NEVER_USED.md` placeholder — no SQL exists | skip |
| 176 | absent — number was never used | skip |

New migrations: take the next unused number, never reuse or fork one.
Connection-scoped only — no `USE [db]` statements (see 265's header note).

## Replay safety

Historical MPI-retirement migrations `86` and `118` were retrofitted
(2026-07-01) with the `CrmEngagementProjectLink` exemption so a replay can
never retire a project a pursuit is linked to — the same rule
`DataRetirementJob` enforces at runtime.
