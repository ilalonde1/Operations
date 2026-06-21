# BD Audit — Fix Prompt (2026-06-19)

> For an independent Codex/Opus session. Implements the verified findings from
> `docs/BD-Audit-2026-06-19.md` (Codex audit) **after** Claude re-verified each
> against the live DB/code. Every finding below was confirmed real; severities
> reflect Claude's calibration, not the raw audit.

## Working rules (you are evaluated on these)
- **Code + migrations only. Do NOT run any destructive DB/git op yourself, and
  do NOT run `dotnet build` or `dotnet test`** (this environment hangs on them).
  Write the migration/code; the operator applies migrations via SSMS and builds
  locally.
- New migrations start at **229** in `Kor.Opportunities.Data/Schema/`. Each:
  `SET XACT_ABORT ON;` + `BEGIN TRAN`/`COMMIT`, informative `PRINT`s, and
  **GO-separate** any "add column → reference it in an index" (the batch is
  parsed whole before execution).
- **No guessing** — cite the code line/SQL you're changing. **Clean at source**
  — fix the write that creates the problem, not just the symptom.
- Match existing patterns (the m1xx dedup migrations, the dedup tool's
  repoint blocks) rather than inventing new ones.

## Already done by Claude (do NOT redo)
- **M5 backlog**: 250 dead-stage CA permits (SF complete/withdrawn/expired/
  cancelled/disapproved/suspend + SD closed) already soft-retired with
  `RetiredReason` = 'BD audit 2026-06-19 (M5)…'. Your job for M5 is the
  **permanent source gate**, not re-retiring the backlog.

---

## C1 (top gate) — CRM duplicate engagements + identity model
**Tables/Files**: `opportunities.CrmEngagements`,
`Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs:93-110` (blind insert),
`Kor.Opportunities.Data/Schema/48_CrmEngagementBdTracking.sql`, `tools/BdTrackingImport`.

**Confirmed problem**: `BdTrackingImport` blind-inserts one engagement per
spreadsheet row with no natural-key lookup, producing exact-duplicate
engagements. **Key insight (verified): same *buyer* is NOT a duplicate** — a
different `OwnerStaffId` or `Region` is a legitimately separate BD thread
(Omar's vs Islam's Ledcor relationship). The true dups are same
**Buyer + Owner + Region + Stage** with identical content.

**Authoritative survivor map** (Claude-verified; collapse loser→survivor,
repointing `CrmActivities`, `CrmContacts`, `CrmEngagementProjectLink`, then
delete loser):
```
DIALOG        83 -> 44
Greenstone    85 -> 51
JWDA Inc.     88 -> 64
Ledcor        76 -> 31   ;  77 -> 35      (keep Omar #31 + Islam #35 distinct)
Meiklejohn    84 -> 50
METAFOR       74 -> 23
Pinnacle Intl 72 -> 11
SAHURI        82 -> 42
Duke Mgmt     32 -> 63    (32 is AB-mis-regioned; 63 is the correct Okanagan row)
```
**Do NOT collapse** (verified distinct): A&H steel 17/25/33; M'Akola 12/19;
Duke 36/38; RBI group 4/48 — distinct by owner or region.

**Org merges — DONE 2026-06-19 (Claude, via `BdCanonicalDedup`, post-audited):**
`Make Projects #76654 → MAKE Projects Ltd. #11258` and
`RBI group #70831 → RBI Group of Companies #14`. 5 CrmEngagements repointed.
This consolidated the buyers, exposing these **additional engagement dups** to
fold into the migration's survivor map:
```
MAKE Projects Ltd. (#11258):  75 -> 30 (Omar) ; 78 -> 37 (Islam) ; 79 -> 39 (Jim)
RBI Group of Cos. (#14):      90 -> 4  (both Omar/Van-LM)   [keep #48 Omar/Alberta]
```
- For **RBI 90→4**: survivor #4 holds proposal financials (psub 530000 /
  pacc 300000); loser #90 holds this session's meeting activity + richer
  `PotentialProjects` (Richmond Hotel; 137th St; Cobalt; Edmonton). **Preserve
  both** — repoint #90's activity/contact to #4 and merge #90's
  PotentialProjects/Notes into #4 (don't drop the financials or the meeting note).

**Fix (migration 229 + code)**:
1. Migration: execute the survivor map above (repoint child rows, delete
   losers). Pattern = the m1xx merge migrations.
2. Make the write path **idempotent**: in `SqlCrmEngagementStore` and
   `BdTrackingImport`, look up an existing engagement by
   `(BuyerCanonicalOrgId, OwnerStaffId, Region)` before inserting; update or
   skip on hit. Add a **filtered unique index** on those columns
   (`WHERE BuyerCanonicalOrgId IS NOT NULL`) once dups are gone.
3. **Design flag for Ian (do not guess)**: an "engagement" currently conflates
   *relationship* (buyer+owner) with *pursuit* (buyer+owner+project). Recommend
   the natural key and surface the choice — relationship-level (one row per
   buyer+owner, projects tracked underneath) vs pursuit-level (buyer+owner+
   project). State your recommendation; let Ian decide before locking the index
   semantics.

---

## M5 — CA/SF funnel admits late-stage permits (source gate)
**File**: `Kor.Opportunities.Data/Ingestion/Providers/CaSocrataMajorProjectsInventoryProvider.cs`
(stage read ~line 465; `where` from config at 163; silent unfiltered retry at 173-175).
**Confirmed**: `CA_SocrataSF` / `CA_SocrataSanDiego` `ConfigJson.where` filters
only units/value + `'%new construction%'`; no status filter. The funnel's job is
*pre-selection* leads (12-36 mo before SE selection).

**Fix (belt + suspenders)**:
1. **Provider-side stage gate (primary, robust)**: before upsert, reject records
   whose normalized stage ∈ {complete, withdrawn, expired, cancelled, canceled,
   disapproved, suspend(ed), closed, void} (and SD 'Closed'). This survives the
   `$where` silent-fallback path.
2. **Config `$where` (secondary)**: add a status filter to both sources, but
   **validate the SoQL round-trips against the live Socrata endpoint first** —
   line 173 silently drops the whole filter on a 400, so an invalid filter
   no-ops. Confirm SF `status` value casing before committing.
3. **Ian decision (flag, don't act)**: should `issued`/construction-stage
   (SF `issued` 144 rows, SD `Issued`/`Inspection Followup`) stay in a
   pre-selection funnel? Claude left them active pending this call.

---

## M6 — CA address has many permits = one project (count inflation)
**Confirmed bounded**: 33 SF addresses, 86 active rows (e.g. `3773 Sacramento St`
= 6 permits `sf:201912240640-0645`). Upsert is correct by permit key, but
project/address dashboards overcount.
**Fix**: a provider-side or migration rollup that groups permits by address into
one project row (or retires superseded permits per address, keeping the primary).
**Do not blind-retire** — verify whether multiple permits = multiple real units
vs redundant filings before collapsing; pick the survivor deterministically and
document the rule.

---

## M1 — IntelPerson name-only NaturalKey (identity model)
**Files**: `Kor.Opportunities.Data/Intel/IntelNaturalKey.cs:7-16`,
`Kor.Opportunities.Data/Schema/64_IntelEntities.sql:30` (`UQ_IntelPerson_NaturalKey`).
**Confirmed**: key = `SHA1(NormalizeName(displayName))` — two different people
with the same name collide on the unique constraint (hit live 2026-06-19 with a
"John Wu" at two firms; the CRM/AI contact graph depends on this).
**Fix**: add identity anchors to the person key — email or LinkedIn when present,
else name + primary-org (or a source-stable id). Migration + backfill of existing
`NaturalKey`s. **Highest-risk item — design + review carefully**; this key is
referenced by the affiliation key recipe and the hand-ingest contract
(`reference_intelperson_ingest_contract`). Propose the scheme; do not ship the
backfill without a dry-run row-count diff.

---

## M7 — BD-tracking company resolved verbatim (composite-org risk)
**File**: `tools/BdResearchImport/Program.cs:6301`.
**Confirmed**: every other `ResolveAsync` wraps the name in `LeadOperator(...)`
(see :1061, :1207, :1898…); the BD-tracking path passes `company` raw, so a
JV/composite tracking string can mint a composite canonical.
**Fix**: wrap in `LeadOperator(company)` (consistent with peers), unless BD
tracking intentionally treats `company` differently — if so, comment why.

---

## Smaller, verified
- **M2** (Minor): affiliations **13739** (CEI Architecture, live org 4552) and
  **19435** (Arcadis IBI, live org 153) have `SourceEnrichmentId` pointing to a
  retired org's enrichment. Re-anchor each to a surviving enrichment for its live
  org (pattern = the dedup tool's repoint block). Exactly 2 rows.
- **M3** (Minor; framing corrected): no `sp_getapplock` in
  `tools/BdCanonicalDedup/Program.cs`. The Sunday trigger
  (`Worker/Program.cs:850-865`) fires a **hard no-op** job (`CanonicalOrgDedupJob`
  is retired) — it can't race. Real residual = two *manual* CLI runs. Add a
  transaction-scoped `sp_getapplock` around merge planning/commit; optionally
  delete the dead trigger registration.
- **M4** (architectural, not a bug): `dedup-non-similar-allowlist.csv` = 2,500
  rows. Split per-campaign with a generated validation report (survivor/loser
  names, Deltek ids, live child counts, reviewer/date) so the gate-bypass ledger
  stays auditable.
- **Mi1**: `CrmEngagements` Id=1 (OpportunityId=92, all-null buyer/owner/region,
  created by `ilalonde@…`) — confirm with Ian whether to delete (looks like an
  early manual seed row).
- **Mi4**: `Worker/Program.cs:850-852` comment still claims "weekly canonical-org
  dedup … runs here"; the job is retired/no-op. Fix the comment.

## Output
Implement the above as code + numbered migrations (≥229). Produce a short
changelog mapping each change → finding id (C1, M5, …) → files touched →
migration number. List anything you intentionally left for Ian's decision
(C1 key semantics, M5 issued-stage, M6 survivor rule, M1 scheme).
