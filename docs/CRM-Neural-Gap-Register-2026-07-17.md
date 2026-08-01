# CRM Neural Gap Register — 2026-07-17

**Mandate (Ian):** "want all holes and gaps closed so this thing is neural."
**Definition of neural:** every fact the firm learns is (a) captured as structured, queryable data — not prose or PDFs; (b) linked to the people/orgs/projects it concerns; (c) *surfaced back* at the moment it matters. A fact nothing reads is a dead neuron.

Audited 2026-07-17 against live schema (KorOpportunitiesDb), Worker source, and the two relationship passes that exposed the holes (Arcadis 2026-07-16, Ledcor Kelowna 2026-07-17).

---

## The gaps, ranked

### G1 — Engagements can't hold a person `[P1 · small · build-ready]`
`CrmEngagements` links an org (`BuyerCanonicalOrgId`) but not a human. Engagement 375 is "meet Elliot Wood" — Elliot (IntelPerson 20300) is unreachable from it. Relationship-first BD is now the norm (Terry Gray, Elliot Wood — both this week).

**Fix (migration 287):**
```sql
ALTER TABLE opportunities.CrmEngagements ADD ContactIntelPersonId BIGINT NULL
  CONSTRAINT FK_CrmEngagements_ContactIntelPerson REFERENCES opportunities.IntelPerson(Id);
-- backfill 375 -> 20300; app detail pane shows contact name/email/phone (app-zip)
```
Worker/store: include contact in digest + morning report lines. App UI: person picker on engagement pane (lands with next app zip).

### G2 — Next actions are write-only `[P1 · small · build-ready · URGENT pre-Monday]`
`NextActionDueUtc`/`NextActionNote` exist; **grep of the Worker shows zero readers**. Nobody will be reminded of Monday's 10:00 meeting.

**Fix:** `BdMorningReportJob` gains a **"Your actions — due today / overdue / this week"** section from open engagements (`Stage IN (1,3)` AND `NextActionDueUtc IS NOT NULL`), grouped by `OwnerStaffId`; same block feeds the per-owner digest (D6) when active. One query, one section, doctrine-test pin. Worker-only — deployable immediately.

### G3 — No person↔person edges `[P2 · medium]`
"Barry introduced Elliot", "Omar met Terry at the Ledcor event June 24" — the highest-value BD knowledge we hold — exists only as prose in org Notes. The graph has no person-to-person edge type at all.

**Fix (migration 288): `IntelPersonRelation`**
```
Id, FromPersonId, ToPersonId, RelationType  -- IntroducedBy | MetAt | ReportsTo | Colleague | WorkedWith
Context nvarchar(400)                        -- "Ledcor client event 2026-06-24"
EvidencedAtUtc, SourceProviderName, SourceRef, CreatedAtUtc, RetiredAtUtc
```
Backfill the known edges (Omar↔Barry MetAt; Barry→Elliot IntroducedBy; Omar↔Terry MetAt). Dossier generation then answers "who can introduce us to X?" by graph walk — the literal neural question.

### G4 — Org facts are an unstructured Notes blob `[P2 · medium]`
Decompose passes append dated prose to `CanonicalOrg.Notes`. Human-readable, machine-opaque: `/ask`, the dossier engine, and the weekly sheet can't SELECT "orgs that self-perform structural" or "warm channels opened this month."

**Fix (migration 289): `OrgFact`**
```
Id, CanonicalOrgId, FactType  -- SelfPerformsStructural | WarmChannel | DeliveryModel | CompetitorNote | DeltekLink | DuplicateOf | MarketFocus
Body nvarchar(max), SourceUrl, ObservedAtUtc, Confidence (High/Med/Low), SupersededByFactId NULL
```
Decompose passes write facts + a short Notes pointer. Backfill: parse the existing dated blocks (Arcadis 153, Ledcor 69671, DIALOG 6154 — I wrote them, I can parse them). MCP `/ask` prompt gains the table.

### G5 — Touchpoints don't exist `[P2 · medium]`
An engagement is one row with one Notes blob. Monday's meeting outcome, the thank-you to Barry, the next coffee — there's nowhere to log a sequence of contacts, so "when did we last touch Ledcor Kelowna?" is unanswerable.

**Fix (migration 290): `CrmTouchpoint`**
```
Id, EngagementId NULL, CanonicalOrgId, IntelPersonId NULL,
Kind  -- Meeting | Email | Call | Event | Note
OccurredAtUtc, Summary nvarchar(max), CreatedBy, CreatedAtUtc
```
Warmth becomes derivable (`vw_OrgWarmth`: last touchpoint per org — ONE predicate, per doctrine). "Last week's movement" in the weekly sheet gains real relationship motion.

### G6 — Owner identity is free text `[P3 · small]`
`CrmEngagements.OwnerStaffId` holds `'Jim'`/`'Omar'` strings while the D2 `BdStaff` directory exists. Works, but joins to the digest routing are name-string matches.

**Fix:** either FK to BdStaff with a backfill map, or (cheaper) a CHECK/lookup view normalizing the six BD names. Recommend the view first — no app churn.

### G7 — Email threads are hand-carried `[P3 · large · defer decision]`
Every .msg is read manually on request. The neural version: a BD-mailbox (or FILEDROP-style) ingest that turns threads into touchpoints (G5) + person upserts automatically. Real arc, real cost (parsing, identity, privacy). Belongs on the deferred-work register until G1–G5 exist for it to write into — **decision explicitly Ian's**.

---

## Execution order

| Rank | Gap | Size | Deploy | Status |
|---|---|---|---|---|
| 1 | G2 next-action surfacing | S | Worker | ✅ SHIPPED 2026-07-17 (Worker 1.0.9694.1123, commit 5b9535f7) |
| 2 | G1 contact on engagement | S | mig 287 (UI next zip) | ✅ SHIPPED — 375→Elliot Wood backfilled |
| 3 | G3 person edges + backfill | M | mig 288 | ✅ SHIPPED (commit 7da81ba1) — KOR staff as nodes, 4 edges |
| 4 | G5 touchpoints + warmth view | M | mig 290 | ✅ SHIPPED — 6 touchpoints, vw_OrgWarmth |
| 5 | G4 OrgFact + backfill | M | mig 289 + MCP prompt | ✅ SHIPPED — 11 typed facts; /ask taught |
| 6 | G6 owner normalization | S | mig 291 view | ✅ SHIPPED — vw_CrmEngagementOwners |
| 7 | G7 email ingest | L | — | 🅿 deferred (Ian's call) |

**Verification (2026-07-17):** all migrations applied live + re-run idempotent (counts stable: 4 edges / 11 facts / 6 touchpoints / 3 staff nodes). MCP 67/67, Worker doctrine 28/28. End-to-end `/ask` smoke: "who can introduce us to Elliot Wood?" answered by graph walk (Barry Murphy, IntroducedBy edge, with context); warmth read from vw_OrgWarmth (both Arcadis + Ledcor: Warm). The smoke test itself caught one more hole — `/ask` had **no date grounding** and narrated the future Jul-20 meeting as already-happened (the Emeryville bug class); fixed with a TODAY prefix in AskService, redeployed, re-verified ("that date is three days from now — a planned first sit-down, not a completed one").

**Still open:** engagement contact-person picker in the app (rides Ian's next app zip); G7 decision.
