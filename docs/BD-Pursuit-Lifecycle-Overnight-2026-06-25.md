# Overnight build brief — BD Pursuit Lifecycle (2026-06-25 → morning)

Autonomous build, verified with Codex as I went. **Nothing is broken; the DB is intact.**

## What shipped (2 commits on `develop`)

| Commit | Phase | What | Status |
|---|---|---|---|
| `b2d387dc` | 1a | Migration 267 (additive schema) + MPI-retirement fix + dedup FK fix | applied + verified |
| `aef587bc` | 2 | Migration 268 — backfilled 177 historical wins into the CRM | applied + verified |

**Phase 1a** — `CrmEngagements` gained 8 columns (WonLostOutcome, OutcomeReason, LostToCanonicalOrgId, WonProjectWbs1, NextActionDue/Note, ExternalSource/Key); new `CrmEngagementStageHistory` + `OpportunityAssignmentLog` tables. `DataRetirementJob` now exempts engagement-linked MPIs from all three retirement paths (the broken-thing the sweep caught). All additive/idempotent.

**Phase 2** — 177 wins (the only win source, the one-time `Deltek.CustomProposal` import) seeded as closed-won `CrmEngagements`. Idempotent (ran twice → still 177/252). Reversible: `DELETE FROM opportunities.CrmEngagements WHERE ExternalSource='Deltek.CustomProposal';`

## Verification (measured, not assumed)
- **Full DB backup** taken first: `…\MSSQL\Backup\KorOpportunitiesDb_pre267_20260625.bak` + table snapshot `opportunities.CrmEngagements_bak20260625`.
- `BdIntegrityCheck`: **0 errors, 0 check-failures** before and after every step. (18th warning = the backup snapshot table itself; drops to 17 when you `DROP TABLE opportunities.CrmEngagements_bak20260625` after you're satisfied.)
- Dedup schema-guard **passes** with the new FK (verified live).
- Builds green; idempotency proven by double-apply.

## Codex caught a real one (fixed)
Codex review flagged a **P1** before I applied 267: the new `LostToCanonicalOrgId → CanonicalOrg` FK would trip `BdCanonicalDedup`'s FK-completeness guard and block every org merge. Fixed by registering it in `FkTargets` (mirrors the existing `KorPursuits.LostToCanonicalOrgId`). Verified the guard now passes. *This is exactly the cross-tool break that would have surfaced as a gremlin days later.*

## Waiting for you (decisions / prod steps — I did NOT guess these)
1. **Deploy the Worker** (retirement fix) — daytime, you-present. Risk tonight is negligible: only 1 pre-existing MPI link exists, so the old job's exposure is ~1 row.
2. **Backfill scope** — I imported **Won only (177)** per the design. Whether to also import the 259 `Submitted` and/or wire the 83 `Deltek.PR` losses (for a real win-rate denominator) is a BD call for the attribution phase.
3. **Stage-enum expansion (Phase 1b)** — deferred on purpose: it touches the CRM UI brushes + analytics + AI definitions (7 lockstep spots), and I can't verify UI behavior unattended. Needs you watching the app run.
4. **The UI phases** (Bazaar, Pursuit Cockpit, manager overwatch) — same reason; built only when we can drive the app and screenshot it.

## Noticed but NOT touched (out of scope — no rabbit holes)
Codex incidentally flagged 3 P2s in `Kor.Operations.EngineeringTools.Core` (rebar/takeoff: `RebarChangeService`, `TakeoffCsvImporter`, `VolumeCalculator`) — pre-existing, unrelated to BD. Logged here for you; left alone.
