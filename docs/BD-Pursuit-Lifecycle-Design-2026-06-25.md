# BD Pursuit Lifecycle — Design (v2 CRM + Opportunity Bazaar)

**Date:** 2026-06-25  ·  **Status:** Design locked, pre-build  ·  **Author:** Ian Lalonde + Claude

This is the spec for turning the BD module's scattered opportunity surfaces and the
thin CRM into one owned-pursuit lifecycle: **Bazaar (grab) → Pursuit Cockpit (work it)
→ Win/Loss → Attribution**, with a manager overwatch layer. Everything below is
verified against the live code/DB — citations inline.

---

## 1. Decisions (locked)

- **Model B** — `CrmEngagement` becomes the *single owned-pursuit record* and the
  source of truth for stage and win/loss. `KorPursuit` demotes to a **read-only Deltek
  mirror**, never writes outcomes.
- **Win/loss is CRM-owned, entered at close.** Not Deltek-derived (see §3).
- **Intel attaches by live join, never copied** (see §6).
- **Existing CRM data migrates in place** (additive `ALTER`); historical wins backfill
  via staging table (see §10).

## 2. Current state — verified

**Opportunity surfaces today (≈12 spots, 4 entities):**
- Formal RFPs (`Opportunity`): `OpportunitiesView/Window`, `OpportunityEntryDialog`,
  Dashboard "Latest RFPs".
- Pipeline projects (`MajorProjectsInventory` + honing verdicts): `MajorProjectsInventoryView`,
  Dashboard "Forward Pipeline", `OrgDossierView` project list, `PrimePipelineWindow`.
- Actionables (`IntelAction`): Dashboard "Priority Actions" (Done/Dismiss/OpenOrg/→CRM),
  `OrgDossierView` actions (Done/Dismiss). **These are AI nudges, NOT grabbable units.**
- Owned pursuits (`CrmEngagement`): `CrmView/Window`, `BdTrackingView`, `BdReportsWindow`.

**Correction (Codex review):** there IS an existing promote path —
`OpportunitiesViewModel.EnsureEngagementForAsync` (`:657`) creates an engagement from an
opportunity (lazily, when you log activity/add a contact), guarded by a one-per-opportunity
unique index (`48_CrmEngagementBdTracking.sql:70-72`). It is **not atomic** (engagement
created before status change; concurrency caught after) and is not a deliberate "claim" UX.
The Bazaar Grab **hardens/replaces** this path — it is not net-new.

**Live CRM linkage (75 engagements):** all 75 org-linked (`BuyerCanonicalOrgId`) and
owned; **0** linked to a formal Opportunity (they're BD touchpoints); **1** has a project
link; all at Stage=Drafting. 97 contacts, 114 activities.

## 3. The KorPursuit / Deltek trap (why win/loss is CRM-owned)

`KorPursuitDeltekSyncJob.MapStage` (lines 227–234) has **no `Won` branch** — Deltek
`PR.Stage` only emits `InPursuit→Pursuing`, `LOST→Lost`, `DNP→Declined`. Live data
(1068 rows): the recurring Deltek sync (`Deltek.PR` + `Deltek.PRProposals`, 614 rows)
produced **0 wins**, 83 losses. All 177 wins live in `Deltek.CustomProposal`, a one-time
curated import. `ClosedReason` populated on 0 rows; `LostToCanonicalOrgId` on 3.

**Consequence:** attribution counts wins **recorded in the CRM**, only *enriched* by
Deltek financials where the client link exists. Never built on `KorPursuit.Won`.

## 4. Claim lifecycle & idempotency (verified safe)

"Grabbed = it has left the opportunity stream" is already enforced at the data layer:
- **No duplicates on re-ingest.** `IngestionService` matches existing by unique
  `OpportunityKey` (`GetByKeyAsync`, line 244); inserts only when none exists.
- **Claim preserved on refresh.** When found, it builds `refreshed = existing with
  { deadline, rfpDate, buyer-backfill }` (lines 280–289) — a record-copy that keeps
  `Status`/`OwnerStaffId`/outcome untouched — then updates facts only.
- **Retirement can't reap a claim.** Auto-expire fires only `WHERE Status = 1` (New)
  (`DataRetirementJob.cs:56,67`); grabbing moves Status off New. Org retirement already
  exempts orgs with a `CrmEngagement` (line 131).

**The rule:** an opportunity is *unclaimed* only while `Status = New AND OwnerStaffId IS
NULL`. **Grab** sets Status off New + creates the engagement → leaves the Bazaar, shielded
from retirement, survives re-ingestion — all for free.

**MPI asymmetry (🔴 has a live bug — see §13):** MPI rows have no Status/Owner. "Grabbing
an MPI project" = create an engagement + `CrmEngagementProjectLink`. The Bazaar excludes
MPI rows that have an engagement. **Today `DataRetirementJob` retires MPI rows by stage
keyword (incl. `%construction%`) with NO has-engagement exemption, and the link FK is
`ON DELETE CASCADE` — so a grabbed project can be retired out from under its pursuit and
the link silently cascade-deleted.** This MUST be fixed (exemption) in Phase 1 before MPI
grabbing goes live.

## 5. The Bazaar (clearing house)

New `BazaarView` in the BD workspace. Unifies `Opportunity` + PURSUE-verdict
`MajorProjectsInventory` + `PrimePipeline`, **default-filtered to unclaimed**.
- **Grab** = one transaction: set `OwnerStaffId` + `Status` off New + write
  `OpportunityAssignmentLog(Claimed)` + auto-create `CrmEngagement` (+
  `CrmEngagementProjectLink` for MPI) + open the Pursuit Cockpit.
- Ranked by AI-Crucible Fit Score when it lands; until then verdict → value → deadline.
- IntelActions are **not** grabbable; at most they deep-link to the underlying project.

## 6. Pursuit Cockpit (the v2 CRM detail) — smart & efficient

The engagement is an intel hub with three live-join spokes (never copied):

| Spoke | Key | Pulls |
|---|---|---|
| RFP | `OpportunityId` | source facts, deadline, observations |
| Buyer | `BuyerCanonicalOrgId` | `IntelAction/Signal/Work/Risk/Narrative`, people, Deltek client facts |
| Project | `CrmEngagementProjectLink → MajorProjectsInventoryId` | `IntelProjectAction/Signal/Risk/KeyPerson`, honing verdict / KOR angle |

- **Live join, not snapshot** — nightly enrichment keeps the open pursuit current; zero sync.
- **Persist decisions, not ambient intel** — acting on a recommendation writes a durable
  `CrmActivity`. Ambient context stays live; what you *did* is history.
- **Seeded action plan** — open `IntelProjectAction`/`IntelAction` (PursuitAngle,
  ContactStrategy, TimingWindow, HowToGetOnRoster) become the pursuit's starting next-actions.
- **People** — `IntelProjectKeyPerson` + buyer `IntelPerson`s + Deltek contacts → one-click `CrmContact`.
- Project-grain scoping keeps it sharp, not a wall of the buyer's whole history.

## 7. Manager overwatch

- **`OpportunityAssignmentLog`** (new) — who claimed/released/reassigned what, when, why.
  The one genuinely missing piece.
- **Staleness** — days since last `CrmActivity` → "going cold"; optional **auto-release to
  Bazaar** after N untouched days (logged). This *is* "take it and give it to someone else."
- **Manager board** — every pursuit, owner, last-activity age, stage age → one-click reassign.

## 8. Proposal linkage

Wire the dormant `OpportunityFeeProposalLinks` table (exists, unused). `FeeProposalBuilder`
(already launches from CRM) writes the link; a linked proposal auto-advances stage to ProposalOut.
**Caveat:** `FeeProposal` lives in `KorTransmittalsDb`, the link table in `KorOpportunitiesDb`
— the schema deliberately stores the GUID with no cross-DB FK. So it's a soft, app-managed
reference: no cascade, orphan cleanup is on us. Wiring is fine; just not an FK.

## 9. Attribution ledger (manager report)

CRM-sourced, Deltek-enriched. Counts wins recorded in the CRM; enriches each with Deltek
financials (`WonProjectWbs1`, lifetime fee via `DeltekKorWonProjectAccessor`) where the
client link exists — absence of a Deltek link never hides a win. **Data confirms this is the
right posture: only 41/1940 buyers and 86/2077 developers carry a Deltek client link** (live
2026-06-25), so attribution must stand on CRM data alone with Deltek as a bonus, never a
backbone. Output in `BdReportsWindow`:
proposals submitted + projects won attributable to BD, by owner/sector/source feed +
win-rate per stage. Feeds the `BdMarket` COO brief. **Source ROI** falls out: which feeds
produce *won* work.

## 10. Schema deltas & migration

**Additive ALTER (in place — no copy, preserves all Ids/FKs):**
- `CrmEngagement` ADD: `WonLostOutcome`, `OutcomeReason`, `LostToCanonicalOrgId`,
  `WonProjectWbs1` (nullable), `NextActionDueUtc`, `NextActionNote`.
- Expand `CrmEngagementStage`. **Real ints are non-contiguous: `Drafting=1, Submitted=3,
  Won=6, Lost=7`** with `CHECK (Stage IN (1,3,6,7))` (`07_CrmEngagementStage_Constraint.sql`)
  and the mapping hardcoded in `AskService.cs:978` + `Definitions.Bd.cs`. Add new ints
  (e.g. Claimed=2, Contacted=4, ProposalOut=5) → migrate the CHECK + update both formula
  docs in lockstep. Analytics use the enum (safe).
- New tables: `CrmEngagementStageHistory (EngagementId, Stage, EnteredUtc, ByStaffId)`,
  `OpportunityAssignmentLog`.

> Existing 75 engagements + 97 contacts + 114 activities become valid v2 rows the instant the
> nullable columns exist. **Do NOT copy-and-reimport** — child rows FK to `EngagementId`
> (surrogate); new Ids would strand them (the exact gremlin fixed 2026-06-25, commit 744c1308).

**Staging backfill (177 historical wins):** load `KorPursuit` `CustomProposal` rows into a
`#StagedWins` temp table, map → `CrmEngagement` columns, resolve buyers via
`CanonicalOrgResolver`, `INSERT` as historical won engagements. **Stamp each new engagement
with the KorPursuit `ExternalSourceKey` (idempotency) so re-running yields 177, not 354** —
the KorPursuit-side upsert dedup does NOT protect the CRM-side insert. Temp table is correct
*here* because these are new rows from a foreign shape.

**Frontfill (existing 75):** org intel + Deltek facts live-join for free (all 75 org-linked).
The one job: project-link via the existing `BdTrackingCrossLink` matcher
(`tools/BdResearchImport` tag `bd-tracking-crosslink`, confirmed runnable + idempotent)
against `PotentialProjects` — but **only 38 of 75 have that text**, so ~half is the ceiling
before match scoring. **Dry-run first** for the real number. Bonus: match the 97
`CrmContact`s to `IntelPerson` (no FK today) to connect to the people graph.

## 11. Build order

1. Additive schema deltas + the two side tables.
2. Backfill 177 wins (staging) + demote KorPursuit sync to read-only.
3. Bazaar + Grab transaction.
4. Pursuit Cockpit (live-join intel + seeded action plan + win/loss capture + next-action).
5. Manager overwatch + staleness/auto-release.
6. Proposal bridge.
7. Attribution ledger.
8. Frontfill project-link dry-run → commit.

Each is independently shippable. 1–2 foundation, 3–4 daily-use win, 5–7 manager payoff.

## 12. Open items

- Frontfill hit-rate (run the `BdTrackingCrossLink` dry-run).
- Staleness threshold N for auto-release (manager preference).
- AI Crucible (Fit Score / The Play) is a separate roadmap item; the Bazaar consumes its
  score when ready, doesn't block on it.

## 13. Sanity-sweep findings (2026-06-25)

Three independent verification passes (live schema/data + code-dependency + assertion-check)
before build. What shook out:

| # | Finding | Severity | Handling |
|---|---|---|---|
| 1 | MPI retirement retires stage-keyword rows (`%construction%`) with no has-engagement exemption; link FK `ON DELETE CASCADE` → grabbed project deleted out from under pursuit | 🔴 BROKEN | Add has-engagement exemption to MPI retirement in **Phase 1**, before MPI grab |
| 2 | `CrmEngagementStage` ints non-contiguous (1/3/6/7) + `CHECK` constraint + hardcoded in `AskService.cs:978` & `Definitions.Bd.cs` | 🟡 Handle | Add ints 2/4/5, migrate CHECK, update both formulas in lockstep |
| 3 | CRM backfill insert not protected by KorPursuit's external-key dedup | 🟡 Handle | Stamp `ExternalSourceKey` on each backfilled engagement → idempotent |
| 4 | `OpportunityFeeProposalLinks` is cross-DB (FeeProposal in `KorTransmittalsDb`); no FK by design | 🟢 Caveat | Soft GUID reference; app-managed orphan cleanup |
| 5 | Deltek client link on only 41/1940 buyers, 86/2077 developers | 🟢 Validates design | Attribution stands on CRM data; Deltek = bonus |
| 6 | Only 38/75 engagements have `PotentialProjects` text | 🟢 Expectation | Frontfill ceiling ~half; dry-run for real number |

Confirmed-real (were assertions, now verified): `BdTrackingCrossLink` matcher exists and is
re-runnable (`tools/BdResearchImport`); CustomProposal import is one-time + idempotent;
all proposed `CrmEngagement` ADD columns are genuinely absent (clean additive ALTER).

## 14. Codex adversarial review (2026-06-25, read-only, gpt-5.5 high)

Independent pass. Confirmed §13 findings and the Deltek/cross-DB calls. Net-new items to
resolve before build (file refs are Codex's; folder labels approximate but lines verified):

**Critical**
- **C1 — Backfill idempotency has no home.** The plan stamps `ExternalSourceKey` but the
  schema delta never adds it to `CrmEngagement`. Add nullable `ExternalSource` +
  `ExternalSourceKey` with a **filtered unique index**, or a `CrmEngagementExternalLinks`
  side table; backfill upserts on that key.
- **C2 — Backfill child rows can still duplicate.** `CrmContacts`/`CrmActivities` have no
  natural key. Decide if staging creates them; if so, delete-and-replace scoped to the
  imported source, or deterministic keys.
- **C3 — Grab must be one guarded transaction.** Do NOT reuse `EnsureEngagementForAsync`
  (creates engagement, then changes status separately, catches concurrency too late). Use
  `UPDATE … WHERE Id=@id AND Status=1 AND OwnerStaffId IS NULL`, assert exactly 1 row, then
  insert log + engagement + link — all in one tx, fail atomically.
- **C4 — No one-pursuit-per-project guard.** `CrmEngagementProjectLink` project-side index
  is non-unique → two engagements can claim the same MPI. Add a filtered unique guard (or an
  explicit project-claim state) if a project may be owned by only one active pursuit.
- **C5 — MPI retirement gap is broader.** Exemption must cover BOTH runtime paths
  (stage-keyword + completion-year) AND the downstream **orphan project-intel sweep**
  (`DataRetirementJob` ~194-224), AND it leaks via historical migrations
  (`118_DeadVerdictRetireAndSeatOpeningWiden.sql`, `86_RetireGenericDeadEndMpi.sql`) that
  retire MPIs with no engagement awareness.

**Major**
- **M1 — New columns need constraints, not just nullable ADD:** FK+index on
  `LostToCanonicalOrgId`, CHECK on `WonLostOutcome`, index on `NextActionDueUtc`.
- **M2 — Ingestion preserves claim by *caller behavior*, not store safety.** `UpdateAsync`
  is a full-row write; other UI callers (`OpportunityEntryDialog`, `ScoringProfileViewModel`)
  use it too. Add a narrow ingestion-refresh update (facts-only) so it's structurally safe.
- **M3 — Engagement delete strands the opportunity** (no reverse Status/Owner reset; cascades
  contacts/activities). Disallow physical delete for owned pursuits; use a release/void tx.
- **M4 — Auto-release semantics must be exact.** If it leaves Status=Pursuing the Bazaar
  won't show it; if it sets Status=New, deadline-passed rows get auto-expired. Define
  status/owner/engagement-state/cooldown/retirement-exemption precisely.
- **M5 — Authorization for reassign/release unspecified.** App-role grants are broad. Specify
  roles + audit, or route reassignment through permission-checked procedures.
- **M6 — Cockpit needs a real set-based read model.** Current brief loading is per-MPI
  (N+1 risk). Build one set-based query for visible engagements; add filtered live indexes
  where missing (e.g. project risks).
- **M7 — Stage-collapse replay hazard.** `06_CrmStageCollapse.sql` remaps the same int space
  the new stages reuse; lock migration order so old collapse can't run after new-stage data.

**Resolution:** C1–C5 fold into Phase 1 (foundation + the broken-fix already there). M1–M7
become explicit design line-items. None invalidate Model B or the architecture.
