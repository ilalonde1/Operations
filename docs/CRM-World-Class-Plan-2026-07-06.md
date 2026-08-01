# THE PLAN — Make the CRM World-Class Without Bombarding Users
**KOR Structural · Kor.Operations.App BD/CRM · 2026-07-06 · final (post-review synthesis of draft + bloat/reinvention/feasibility critiques; all citations verified in source or by the verify pass)**

---

## 1. Current-State Verdict

**The machinery is excellent. The adoption is zero. The passive data is enormous and untouched.**

What exists: a race-safe, audited pursuit lifecycle shipped in late June — atomic Grab (one guarded transaction writing claim + engagement + stage history + assignment log, `Kor.Opportunities.Data/Crm/SqlPursuitGrabStore.cs:48-97`), Overwatch staleness board (set-based, coldest-first, `SqlPursuitOverwatchStore.cs:82-102`), guarded Reassign with Graph email, an honest data-gated Attribution scorecard, RowVersion optimistic concurrency everywhere, and a deep intel supply chain (146k canonical orgs, 12.6k IntelPersons, dossiers, briefs, reports, MCP tools) that is complete and mutually consistent.

What the live database says (verified 2026-07-06): **zero grabs ever** (948 opportunities pooled; CrmEngagementStageHistory=0, OpportunityAssignmentLog=0, owned-Pursuing opportunities=0), **zero user-typed activities ever** (all 114 CrmActivities are importer/Claude-authored), FeeProposalLinks=0, OpportunityNotes=0. The 252 engagements are 75 imported legacy relationships + 177 backfilled historical wins. Meanwhile the passive stores are vast and fresh: 368,512 filed emails current to today, 7.5k transmittal opens, live Deltek ERP (2,864 clients, 36,694 projects, 6,783 contacts), 137k awards, 1,845 plan-taker rows — none of it joined onto the engagement record a pursuit owner actually works.

Where the friction is, concretely:
- Logging one phone call costs ~8 interactions plus a visual scan of an unfiltered 252-row grid (177 of them closed history), can't be backdated, and links to no contact (`Kor.Operations.App/Crm/CrmViewModel.cs:375, 369-376`; no search/filter in `CrmView.xaml:95-122`).
- The grab decision is intel-blind (Bazaar shows tier/source/value/deadline only, `BazaarView.xaml:53-73`) and a successful grab doesn't even open the pursuit.
- The CRM's richest passive asset — the Deltek client panel — renders for **0% of engagements** because it keys on `Opportunity.DeltekClientId` (populated on 0 of 1,646 opportunities) instead of the buyer-org Clendor path that reaches 207/252 (`CrmViewModel.cs:313-318`).
- Two divergent CRM implementations (CrmView vs the stale CrmWindow fork) behave differently from different doors; a free-text Owner box mints identity formats; the 15-item BD nav (`BdWorkspaceWindow.xaml:64-86`) exceeds what 6 part-time users will ever learn.
- Fifteen confirmed defects/integrity issues (below) mean the numbers the module shows are not yet trustworthy enough to market internally.

The verdict: **do not build capability; build usability, honesty, and joins.** The users-don't-type thesis is proven by the module's own data. Every winning move is either a subtraction, a keystroke-elimination, or a passive join of data KOR already owns. (revised) One more finding from review: the draft's own Phase 2/3 quietly accreted 3-4 new Worker jobs — for a one-developer shop each job is a permanent failure domain, so **all future passive detectors are consolidated into a single nightly CRM-enrichment job**, and two detectors were cut outright (Appendix).

---

## 2. Fix-First List

Confirmed-in-code defects. No feature work ships on top of a foundation that stamps wrong identities, loses outcome data at close, and lets stale boards corrupt win attribution. Sizes: S(<½ day) / M(1-3 days) / L(week+).

**(revised) Critical path to the first organic grab — do these first, ~1.5-2 weeks including Phase 1 items 1.1/1.3/1.4/1.6: F1, F3, F5, F6, F9, F10, F11.** The close-semantics package (F2+F7+F8-interim+1.5) must land before the **first live close**, not the first grab — nothing closes in week one. Everything else trails the first grab instead of preceding it.

| # | Defect | Fix + why it must precede features | Size |
|---|---|---|---|
| **F1** (revised) | Grab drops `ExternalSource`/`Region`; proposal-link never copies fee onto `ProposedFee` (`SqlPursuitGrabStore.cs:77-82`; `CrmView.xaml.cs:159-189`; scorecard displays these dimensions, `SqlBdAttributionStore.cs:45,51-58`). Already registered as the audit's own residual (`docs/BD-Audit-2026-07-01.md:179`) — this executes it. | **Time-critical: zero grabbed pursuits exist today; stamping now = 100% coverage of all future history.** Region copies server-side inside GrabSql exactly like `o.BuyerCanonicalOrgId` (`SqlPursuitGrabStore.cs:80`). Two seams the draft missed: (1) the source feed is **not on the Opportunities row** (`SqlOpportunityStore.cs:33-46` has no Source column) — GrabSql needs an OpportunityObservations→OpportunitySources subquery (pattern: `Kor.Opportunities.Worker/Services/Reporting/BdMorningReportJob.cs:139-141`); (2) the ExternalSource **vocabulary must be defined** and must never collide with `'Deltek.CustomProposal'` (268's idempotency key, `Schema/268_WinHistoryBackfill.sql:12,48-59`) — e.g. a `Grab.*` prefix. Store-column widening removed from this item — that is F2's work, not F1's. | M |
| **F2** (revised) | Migration-267 outcome columns have no pen — `SqlCrmEngagementStore.cs:20-25` AllColumns and the UpdateAsync SET list (`:223-249`) omit all 8 columns. | **One "close-semantics" work package with F7, F8-interim, and 1.5's dialog — done in one sitting, effort counted once.** Must land before the first live close (no outcome data is being destroyed *this week*; zero live pursuits exist). Wiring uses targeted UPDATEs / read-modify-write — **never** blind full-row binding, or the first edit of a backfilled row nulls its outcome data and breaks 268's idempotency key. | M (package) |
| **F3** | Reassign has no stage guard — a stale Overwatch board can silently re-own a **closed Won** engagement and its parent opportunity, permanently mis-crediting the win (`SqlPursuitOverwatchStore.cs:112-117`). | One-predicate fix (`AND Stage IN (1,3)` + a distinct outcome value + dialog handling). Attribution keyed on OwnerStaffId is corrupted-by-race until fixed. Critical path. | S |
| **F4** (revised) | Owner identity is 3+ regimes wide and every `OwnerStaffId` GROUP BY splits one human (`SqlBdAttributionStore.cs:60-66`; free-text box `CrmEngagementDialog.xaml.cs:47`; importer writes NVarChar(20)-truncated names `tools/BdResearchImport/Program.cs:7128`). | Honest scope: **stop minting new formats** — picker replaces free text (F5), importer param widened. Note: the DB columns are already 150 wide (migrations `270_WidenStaffIdToUpn.sql`, `272_StageHistoryByStaffIdWiden.sql`) — this is purely client-side param truncation; do not "fix" the schema again. The draft's "reassign stores UPN when roster has one" was oversold — no roster-email→UPN mapping exists; true canonicalization (incl. the 75 legacy first-name rows) is D2-gated. | M |
| **F5** | Edit dialog's free-text Owner box can blank `OwnerStaffId` → owned pursuit vanishes from Overwatch forever while the opportunity stays owned (`CrmEngagementDialog.xaml.cs:47`; board filter `SqlPursuitOverwatchStore.cs:100-101`). | Replace with the existing `IProposalStaffStore` roster picker (reuse the ReassignDialog pattern, `OverwatchView.xaml.cs:148-167`). Kills the vanish bug and the fourth identity format (picker inherits roster-email-vs-UPN ambiguity — that residue is D2's). Critical path. | S |
| **F6** | CrmWindow is a divergent twin: no proposal linkage/auto-advance (silently loses `OpportunityFeeProposalLinks` from the Promote-to-Pursuit door), non-one-shot prefill handlers, pre-audit actor chain (`CrmWindow.xaml.cs:72-106, 127-141, 323-329`). | Retire it. `EnsureEngagementAsync` returns the engagement (`OpportunitiesViewModel.cs:676`), so `OpportunitiesView.xaml.cs:241-245, 260-262` and the legacy hub route directly to `BdWorkspaceWindow.NavigateToPursuit` (`BdWorkspaceWindow.xaml.cs:197-207`). Every feature below is authored once, in CrmView only — the plan's single best subtraction. Critical path. | M |
| **F7** | ClosedAtUtc semantics broken both directions: dialog-Won leaves it NULL; reopen never clears it; re-close keeps the stale first date (`CrmEngagementDialog.xaml.cs:44-53`; `CrmViewModel.cs:347`). | Centralize terminal-stamp/clear in the store's UpdateAsync — it already OUTPUTs `deleted.Stage` (`SqlCrmEngagementStore.cs:242-249`) so terminal-transition detection is nearly free. Part of the F2 close-semantics package. | S |
| **F8** | CRM Won/Lost never syncs the parent Opportunity — two outcome ledgers guaranteed to contradict; the MCP prompt teaches both recipes as co-equal (`CrmViewModel.cs:342-354`; `Kor.Operations.Mcp/Ai/AskService.cs:896-906, 978`). | Direction is Ian's call (D1); the *interim* mitigation ships in the F2 package: one MCP-prompt line declaring outcomes CRM-owned + the CrmEngagements↔KorPursuits overlap warning. Respects the Definitions.Bd.cs↔MCP lockstep rule (warning, not methodology change). | S (interim) / M (sync) |
| **F9** | Fallback claim path drops `BuyerCanonicalOrgId` and the assignment-log row → dead Buyer-intel button + invisible claim (`OpportunitiesViewModel.cs:730-738`; `InsertAsync` writes stage history only, `SqlCrmEngagementStore.cs:174`). Registered as the audit's C3 residual (`docs/BD-Audit-2026-07-01.md:181`) — this closes it. | Fix must be **server-side** (the domain model can't supply the buyer org — `SqlOpportunityStore.cs:33-46` omits it). Same-shaped pursuits must behave identically regardless of creation door. Critical path. | S |
| **F10** | Activity/contact insert after await lands in the wrong engagement's detail panel — no stale-selection re-check (`CrmViewModel.cs:378-379, 396-397` vs the guarded pattern at `:295-298`). | Apply the guard that exists three methods up. Fold in the deferred-register neighbor: `LoadSelectedIntelligenceAsync` has the same missing-Id-guard shape. Critical path. | S |
| **F11** (revised) | **Corrected defect description:** the ProposalSaved handler is *not* window-lifetime-leaked — it detaches on `win.Closed` (`CrmView.xaml.cs:139`), and re-advance is already guarded by the still-Drafting re-read (`CrmView.xaml.cs:172-181`). The real residual: a *repurposed save while the builder stays open* writes a wrong `OpportunityFeeProposalLinks` row. | One-shot the handler on first successful save (or key on prefill identity). Precedes 1.6, which reuses this wiring. Critical path. | S |
| **F12** (revised) | 177 backfilled wins headline a 100% vanity win rate in the CRM strip and are fed verbatim to the AI (`CrmViewModel.cs:158-171, 472-491`). | Segment populations (backfill vs live vs BD-tracking) in the headline + AI context, copying Attribution's honest-caveat pattern; add the MCP prompt line with F8's. **Hidden dependency the draft missed:** `CrmAnalyticsService` computes in-memory over loaded models (`CrmAnalyticsService.cs:69-93`) which can't carry ExternalSource until F2's read-side wiring — so F12 uses the explicit heuristic (Stage=6 ∧ OpportunityId NULL ∧ OwnerStaffId NULL = the 177) now, swapping to ExternalSource-keyed after F2. **Hard gate before 1.7** — no ask surface ships while the AI snapshot contains the vanity rate. | S (heuristic) |
| **F13** (revised) | IntelRetirementJob retires intel on pure 180-day staleness with no CRM exemption (`Kor.Opportunities.Worker/Jobs/IntelRetirementJob.cs:76-82`); mirror the MPI exemption (`Kor.Opportunities.Worker/Services/DataRetirementJob.cs:99,109`). | **Scope widened per review:** DataRetirementJob's CanonicalOrg orphan-sweep branch (`DataRetirementJob.cs:225-230`) can still cascade-retire a pursuit org's intel — add the Stage IN (1,3) predicate in both places, or record the acceptance explicitly. | S |
| **F14** (revised) | BdResearchImport raw-INSERTs engagements with no stage-history row (`tools/BdResearchImport/Program.cs:7116-7138`). | The two draft options are not equivalent: routing through the store changes importer transactionality. Ship the genuinely-S path — document the `COALESCE(OpenedAtUtc)` fallback for stage-age readers (consumed by 2.2a). Also widen the NVarChar(20) `@init` param (F4). | S |
| **F15** (revised) | Reassign email builds raw HTML, bypassing `KorEmailTemplate.Shell` (`OverwatchView.xaml.cs:192-197`). | Fix the app side. **Honesty correction:** `KorEmailTemplate` lives in the App assembly (`Kor.Operations.App/KorEmailTemplate.cs`) — the Worker cannot reference it, so "all CRM mail uses the shell" cannot literally extend to Worker digests without extracting the template to a shared lib. That extraction is now a named decision (D11), not a hand-wave. | S |

**Fix-first total: ~2-3 weeks of work, but only ~1 week of it blocks the first grab.** Nothing here adds a user-facing surface; everything makes the existing ones truthful.

---

## 3. The World-Class Thesis (for THIS team)

Six engineers doing BD on the side, an ocean of passive data, and a proven refusal to type. World-class here is not Salesforce; it is:

**P1 — Capture costs at most one decision.** The user supplies the one fact only they know ("I called Dave; we lost to Bush Bohlman"); the system supplies everything machine-knowable (when, who Dave is, which pursuit, source, region, fee). Today's forms demand five machine-knowable facts and forbid the one human fact (the real date). Every capture surface gets rebuilt to this bar or removed.

**P2 — Passive joins over input fields, always.** 368k filed emails, live Deltek, 137k awards, plan-taker lists, transmittal telemetry already exist and are email-/org-keyed. The engagement record is a thin spine that gets *decorated* by joins — never a form that competes with the passive stores for the same fact. The 0-row tables (Notes, StageHistory pre-July, FeeProposalLinks) are the proof users voted; respect the vote.

**P3 — Decorate the two moments the user is guaranteed present: grab and close.** Grab-time gets "what we know + who we know + is this client profitable/paying" for free; close-time gets one optional click of outcome. Everything between those moments is the system's job (auto-activities from proposal saves, staleness from real signals), not the user's.

**P4 — The system chases the user; the app has one door per question.** Engineers read email. Digests arrive; dashboards are optional. In-app, subtraction beats addition: one CRM implementation, one intel door per key, fewer nav items, and the AI bar available where people already are. (revised) The same discipline applies to *infrastructure*: one nightly enrichment job, not a job per feature — Worker sprawl is bloat the users never see but the developer pays for forever.

**P5 — Honest numbers or no numbers; adoption is the KPI.** Segment backfill from live everywhere; suppress or caption denominatorless rates (Attribution already does this right — extend the pattern, never dilute it). The milestone that matters is not a feature ship: it is *the first organic grab* and *the first user-typed activity*. Every item below is scored against those two events, and the write-only audit tables become the instrument that tells us which surfaces to kill next. (revised) One corollary added in review: features shipped on speculation carry a **pre-committed kill criterion** (see 1.7) — the kill-list ritual is not optional garnish, it is how conditional items exit.

---

## 4. The Plan

Effort: S(<½ day) / M(1-3 days) / L(1-2 weeks). Every item lists what it **REUSES**, the invariants it must respect, and the Tuesday test: *who uses this on a Tuesday and what do they stop doing manually?*

### Phase 1 — Make the shipped lifecycle usable (~2-3 weeks; items 1.1/1.3/1.4/1.6 ride the fix-first critical path)

Goal: the first organic grab and the first user-typed activity happen because they became cheap and worth it.

**1.1 Pursuits grid: live-by-default, pills-as-filters, search, Mine toggle — S/M (revised)**
- *What:* Default filter to live pursuits (Stage IN (1,3)); the StageSummary pills (`CrmViewModel.cs:240-253`, today a display-only ItemsControl `CrmView.xaml:51-66`) become click-filters; debounced search box; "Mine" toggle on resolved UPN.
- *Reuses (revised — nearest seams named):* the dedicated `Debouncer` class (`Kor.Operations.App/Debouncer.cs:9`); OpportunitiesView's existing grid search/filter pattern (closest in-family precedent); `BdTrackingViewModel`'s per-initiator owner filter (`Kor.Operations.App/Crm/BdTrackingViewModel.cs:28-100`) as the in-module "Mine" precedent. No bespoke filter framework.
- *Invariants:* Populations stay segmented per F12; Mine matches UPN + display map only — no guessed normalization of legacy first names (D2). (revised) The AI-context snapshot must deliberately reflect either the filtered or the full population — decide and document; the immutable-snapshot pattern (`BazaarViewModel.cs:36-39`) makes silent drift easy.
- *Tuesday:* Every BD user; stops scanning 252 rows to find their pursuit. Highest adoption-per-hour item in the plan.

**1.2 One-decision activity logging — S (revised — split; rolodex half deferred)**
- *What ships now:* Enter-to-log, optional backdate picker, autocomplete over the **engagement's own contacts**, friendly type names. Backdating is cheaper than the draft implied: `OccurredAtUtc` is init-settable and the store already binds it (`CrmActivity.cs:19`; `SqlCrmActivityStore.cs:36`) — the hardcode is one line (`CrmViewModel.cs:375`).
- *What is deferred (Appendix):* the Deltek-rolodex autocomplete writing `ContactId`. Three reasons from review: the rolodex source (`DeltekContext`) loads for ~0 engagements until 1.3 lands; `ContactId` soft-links to CrmContacts with no FK (`Schema/03_Crm.sql:66`), so picking a rolodex person means auto-creating a CrmContact — a new write path whose dedup semantics nobody has designed (D12); and 100% historical non-adoption of typing says build the polish *after* the first organic activities exist.
- *Invariants:* CrmActivities stays **append-only** — backdating is a parameter, never mutation (`SqlCrmActivityStore.cs:32-42`); F10's stale-selection guard wraps the insert. (revised) The deferred-register's "Contact IsPrimary concurrent-add race" (filtered unique index) folds into this package when the contact half builds.
- *Tuesday:* Anyone who made a call; stops re-typing "who and when" — or realistically, stops not-logging at all.

**1.3 Re-key the Deltek intelligence panel on `BuyerCanonicalOrgId → CanonicalOrg.ClendorClientId` (fallback: `Opportunity.DeltekClientId`) — S (revised)**
- *User-visible:* Billing history, past KOR wins, AR aging, and the 200-contact rolodex light up on 207/252 engagements — from 0 today. The cheapest world-class move in the codebase.
- *Reuses:* `DeltekClientContextService` unchanged (`DeltekClientContextService.cs:21-102`); the exact key path `OrgDossierViewModel` already runs (`OrgDossierViewModel.cs:477-548`); `ClientIntelligenceWindow` for the Details drill. (revised) One seam the draft missed: CrmViewModel has no org-store dependency (5-service ctor, `CrmViewModel.cs:47-58`) — add `ICanonicalOrgStore` (or a lean lookup) to DI. Resolution is by org **id**, satisfying the no-name-equality invariant.
- *Invariants:* Deltek is a bonus, never a backbone — the 45 unlinked engagements render "no Deltek record", never hide CRM data; no new ODBC in views.
- *Tuesday:* A pursuit owner deciding how hard to chase; stops opening Deltek/asking Daler whether this client pays.

**1.4 Intel-informed grab: Bazaar double-click opens buyer dossier; successful grab deep-links into the pursuit — small-M (revised from S)**
- *Reuses:* `OrgDossierWindow(vm, orgId)` spoke recipe (`CrmView.xaml.cs:88-102`); `BdWorkspaceWindow.NavigateToPursuit` (`BdWorkspaceWindow.xaml.cs:197-207`) — closing a documented design gap (design §5) with a call site, not a mechanism. No handler collision (only `GrabButton_Click` exists, `BazaarView.xaml.cs:61`).
- *(revised) Plumbing the draft missed:* the Bazaar pool is domain `Opportunity` models and `SqlOpportunityStore.AllColumns` **omits `BuyerCanonicalOrgId`** (`SqlOpportunityStore.cs:33-46`) — the "disable when unresolved" affordance has no data to key on. Do an on-demand org lookup at double-click (preferred: no model widening, no MapReader-ordinal ripple across every Opportunity consumer). Hence small-M.
- *Invariants:* Grab remains one guarded transaction, untouched; unresolved org disables the affordance gracefully; grab UX honesty (Ignored/AlreadyTaken semantics) preserved.
- *Tuesday:* Someone deciding whether to claim; stops claiming blind or googling the buyer.

**1.5 Optional one-click outcome prompt on Won/Lost — UI face of the F2 close-semantics package (revised — effort counted once with F2)**
- *What:* Marking Lost (either door, post-F7 centralization) offers ONE optional dialog: Lost to [org typeahead] / one-line reason / **Skip is the default and one click**. Won offers nothing extra (WonProjectWbs1 comes via 2.5's manual link).
- *(revised) Credit where due:* this is the deferred-work register's own "NoBid/Withdrawn outcomes have no UI affordance … likely UI: an outcome picker on flip-to-Lost" item, picked up — mark it done in the register when built. The register frames it around `Opportunities.WonLostOutcome`; this targets `CrmEngagements` — D1 decides whether one dialog serves both ledgers.
- *(revised) Correct transaction template:* follow **UpdateAsync's shape** — RowVersion-guarded UPDATE + co-committed stage-history via `OUTPUT deleted.Stage` (`SqlCrmEngagementStore.cs:242-284`) + targeted outcome-column writes. The draft cited the grab store (an insert, no RowVersion) — precisely the "new store method bypasses both protections" breakage the stage-history invariant warns about.
- *Reuses:* Migration-267 columns + `LostToCanonicalOrgId` FK (`Schema/267_PursuitLifecycleFoundation.sql:14-68`; dedup FkTargets `tools/BdCanonicalDedup/Program.cs:86`); `OrgSearchTypeahead`.
- *Invariants:* No mandatory fields, ever; this is *capture*, not the gated attribution *reporting* — the gate stands.
- *Tuesday:* The pursuit owner at the only moment they know who beat us; stops that knowledge evaporating.

**1.6 Passive activity: fee-proposal save auto-appends a Deliverable activity — S**
- *Reuses:* Existing `ProposalSaved` wiring (`CrmView.xaml.cs:136-139`, post-F11), `SqlCrmActivityStore` append, `Deliverable`=6 (`Schema/03_Crm.sql:62`). Makes the Overwatch staleness clock (`SqlPursuitOverwatchStore.cs:95-99`) truthful for the most important pursuit event.
- *Invariants:* Dedupe on (engagement, proposal, day) or accept honest noise; append-only.
- *Tuesday:* Nobody uses it — that's the point. It logs itself.

**1.7 Embed one `AiQueryPanel` in BdWorkspaceWindow — S, conditional (revised)**
- *What:* The BD module's first ask surface (verified: AiQueryPanel is embedded only in Financials/PMTools/PdfToSafe today). `Initialize` is internal, same assembly — one call (`Controls/AiQueryPanel.xaml.cs:34-38`); routes via `AppAiService` → MCP `/ask` (`Services/AppAiService.cs:59-114`), never Anthropic directly.
- *(revised) Ship conditions:* (a) **hard gate on F12** — the 100% vanity win rate is already in the AI snapshot; no ask surface amplifies it; (b) remediate audit n11 (`docs/BD-Audit-2026-07-01.md:161` — several BD VMs' BuildContext still enumerate live collections; an embedded ask surface converts that latent drift into an active worker-thread risk); (c) ship silent, instrument opens via 2.2(c)'s counter, and **pre-commit to removal at the first kill-list review if opens ≈ 0**. It survives review only because it is genuinely S with zero marginal maintenance.
- *Tuesday:* Anyone with a question ("what do we know about this buyer's architect?"); stops window-hunting across four intel doors — if anyone asks. The instrument decides.

**1.8 Nav subtraction: 15 → 11, then data-driven — M (revised)**
- *What:* Verified 15 buttons (`BdWorkspaceWindow.xaml:64-86`). The enumerated folds — Attribution→Dashboard card, BD Tracking→Pursuits read-only tab (rollup header + banner intact), drop the Proposals/Brochures duplicates (Home tiles exist), unregister the orphaned `BusinessDevelopmentWindow` hub (with F6; audit n13's global-search subscription bug independently supports retirement) — **yield 11, not the draft's "~8"; the arithmetic didn't close.** Further cuts come from 2.2(c)'s usage data via the kill-list ritual, not from taste today.
- *Reuses:* All views host unchanged in ContentHost (`BdWorkspaceWindow.xaml:90`); zero view rewrites.
- *Invariants:* BD Tracking's importer-is-source-of-truth stays visually distinct; Attribution's data-gated honesty text moves intact; `canSeeBd` gating unchanged (`HomeWindow.xaml.cs:270-284`). Jim's nod (D8).
- *Tuesday:* Everyone; stops asking "which of these tabs was it in?"

**Phase 1 exit criteria:** first organic grab recorded (StageHistory + AssignmentLog rows exist); first user-typed activity; Deltek panel visibly lit on real engagements; zero '(unrecorded)' sources on post-fix grabs.

### Phase 2 — The system starts chasing the user (gated on first real grabs; ~2-3 weeks, trailing adoption) (revised)

**2.1 Morning report: "owned pursuits closing soon" section; per-owner digest deferred behind an owner-count gate — M (revised — split)**
- *What ships:* One SQL block in the **existing** daily report — engagements Stage IN (1,3) JOIN Opportunities WHERE deadline <14d, using the section pattern already there (h3, suppress-when-zero, `soon` red styling, per-section try/catch, `BdMorningReportJob.cs:129-190` — which is itself the just-shipped weekly-roster-watch-rides-the-daily-email precedent). Deadline data exists (`SubmissionDeadlineUtc` in AllColumns).
- *What is deferred (Appendix):* the per-owner weekly digest — a new inbox artifact with recipient management (today a single `opt.MorningReportRecipient`, `BdMorningReportJob.cs:67`), blank-first-issue risk, and an F4 identity dependency. **Gate: ≥3 owners holding live pursuits** (owner-count, not grab-count — a mail-merge for two people is noise). When built, it extends `BdMorningReportJob` with per-recipient sends — never a second pipeline/job. Until then, the owned-pursuits section covers anti-abandonment via Ian/Jim.
- *Invariants:* Only UPN-format owners resolve to mailboxes at first (F4); email shell per D11 (extract `KorEmailTemplate` to a shared lib — the Worker cannot reference the App assembly; do not duplicate the shell, that is the divergence class F15 exists to kill).
- *Tuesday:* Ian/Jim, passively; stops the grabbed-then-forgotten pursuit dying silently.

**2.2 Readers over the write-only audit tables — M (revised)**
- *What:* (a) Stage-age chip in Pursuits/Overwatch ("Drafting 34d") from `CrmEngagementStageHistory` with the `COALESCE(OpenedAtUtc)` fallback (F14); (b) claim/reassign history line on the engagement from `OpportunityAssignmentLog`. Verified: both tables have **zero readers anywhere today** — pure found value. (c) (revised) Adoption instrumentation is **a saved SQL/MCP `/ask` query Ian runs before each kill-list review** over `BdReportAuditLog` (`Schema/121_BdReportAuditLog.sql`) plus one minimal pane-open counter (the only new write path in this phase — counted in the surface tally). No built report section for an audience of one; build one only if the ritual proves weekly consultation.
- *Reuses:* Writers already transactional (`SqlPursuitGrabStore.cs:86-93`, `SqlCrmEngagementStore.cs:174,281-284`, `SqlBdReportService.cs:851`); `OverwatchRowView` threshold/brush constants extracted, not copied (`OverwatchRowView.cs:18-19,52-57`).
- *Invariants:* Consume the existing tables as-is; AssignmentLog stays FK-free; label sparse early history honestly; weekly aggregates, no per-person shaming.
- *Tuesday:* Jim on Overwatch ("what's been sitting"); Ian on the query — it decides the kill-list with data instead of taste.

**2.3 Plan-takers + buyer award profile on the engagement — S, lowest priority (revised — surface existing UI, don't rebuild)**
- *What:* When the pursuit's opportunity has `OpportunityInterestedFirms` rows or its buyer has award history, show "who else took plans" and the buyer's award pattern; collapse entirely when empty.
- *(revised) Reuses — the draft would have half-rebuilt existing UI:* OpportunitiesView **already renders** an interested-firms panel with collapse-when-empty (`OpportunitiesView.xaml:442-444`, `SelectedInterestedFirms`) — extract/rehost that panel; the buyer drill-down **already exists** as `BuyerProfileWindow`/`BuyerProfileViewModel` (`Opportunities/BuyerProfileViewModel.cs:67`, reached from Market History) and the CRM's existing Buyer Intel dossier already loads award analytics (`OrgDossierViewModel.cs:412`) — link these, render nothing new. Data: `SqlOpportunityInterestedFirmStore` (1,845 rows, two existing Worker refresh jobs); `IVendorAnalyticsStore.GetBuyerProfileAsync`.
- *Invariants:* ~7% coverage → collapse, never imply "no competitors"; render unresolved `RawFirmName` as text without faking org identity. (revised) The analytics API is **name-keyed** (`IVendorAnalyticsStore.cs:10-11`) while the engagement's buyer is an org id — pass the canonical org name, accept the alias fuzziness, and don't let collapse-when-empty hide a name-mismatch miss silently (log it).
- *Tuesday:* Owner sizing the field before bid/no-bid; stops asking around "who else is on this?"

**2.4 Per-pursuit brief button — S (revised — naming + door discipline)**
- *What:* The richest per-pursuit synthesis already computes in `GetOpportunityBriefAsync` (`SqlBriefDataStore.cs:58`) but is reachable only from OpportunitiesView (`OpportunitiesView.xaml.cs:125-141`). One button on the engagement row, gated on `OpportunityId`.
- *(revised) Collision the draft missed:* a `PursuitBriefWindow`/`PursuitBriefViewModel` already exists in the workspace (`BusinessDevelopment/Workspace/PursuitBriefViewModel.cs:18-26`) — it is the *MPI* brief. Name this button distinctly ("Opportunity Brief"), and route MPI-linked engagements (`CrmEngagementProjectLink`, `Schema/49_*.sql`) to the existing PursuitBriefWindow instead of generating a third thing users will all call "the brief." For BD-tracking rows, **do not add a second org-brief button** — the org-brief trigger already exists one click away in OrgDossierWindow (`OrgDossierWindow.xaml.cs:65`); reuse that door.
- *Tuesday:* Owner prepping for a buyer call/go-no-go; stops assembling context by hand from four windows.

**2.5 Post-win Deltek link: manual button, not a nightly detector — S (revised — detector cut, see Appendix)**
- *What:* A "Link Deltek project…" button on Won engagements: lists the buyer's Deltek projects via the 1.3 Clendor path + `IKorWonProjectAccessor.GetForClientAsync` (cached app-side ODBC accessor, `CompositionModules/OpportunitiesModule.cs:243`, `IKorWonProjectAccessor.cs:12-18`); one click writes `WonProjectWbs1` via a **targeted** UPDATE (F2 invariant).
- *Why not the draft's nightly detector:* zero live wins exist — a detector runs silently for months; a win a month is one click a month; and the draft's reuse citation was the wrong tier anyway — the ODBC accessor is app-side and a Worker detector would need `[DELTEK_VP]` linked-server SQL plus a suggestions table plus a banner. If Won volume ever makes manual linking annoying, promote to a detector **by extending the existing nightly `CanonicalOrgKorProjectSignalRefreshJob`** (`Kor.Opportunities.Worker/Services/CanonicalOrgKorProjectSignalRefreshJob.cs:18-23`, registered `Worker/Program.cs:418-436`) — never a new Deltek-scanning job.
- *Invariants:* Suggest/confirm, never auto-write (write-gate doctrine); repeat clients open multiple projects; unlinked buyers show "unlinked", not zero. Bonus: `KorWonProjectRow.FeeBilled` already exists — this is the bridge to the pre-approved lifetime-fee attribution enrichment (3.4) once grab→win history exists.
- *Tuesday:* Ian/Jim confirming a link once a month; stops win→ERP reality being permanently unjoinable.

**2.6 Kill-list review ritual — S (recurring)**
- *What:* Using 2.2(c)'s query, a standing rule: no new BD/CRM surface ships while an equivalent surface shows zero organic opens. Standing dockets: 1.7's removal criterion, the remaining nav cuts past 11, report-catalog consolidation, duplicate intel doors.
- *Tuesday:* Ian; stops the module growing by addition only.

### Phase 3 — The data moat (quarter horizon; one flagship, one nightly job) (revised)

**3.1 Email warmth: one nightly rollup, two consumers (engagement line + Overwatch staleness fusion) — L (revised — absorbs draft 3.2)**
- *What:* A nightly Worker computation aggregating the filed-email corpus per engagement buyer domain: "last filed correspondence with @acme.com: 12d ago · 9 threads/90d · mostly J. Smith." Written to a rollup table in KorOpportunitiesDb. **Consumer 1:** one computed line on the engagement + the AI context. **Consumer 2:** the Overwatch staleness reference fuses the email last-touch (extending the existing single set-based OUTER APPLY, `SqlPursuitOverwatchStore.cs:82-102` — board stays one query, design M6 `docs/BD-Pursuit-Lifecycle-Design-2026-06-25.md:243`), with the touch source labeled. The draft scheduled these as two L items — same rollup, same job; effort counted once.
- *Reuses:* KorEmailIndex (368,512 rows, fresh; `dbo.Emails` carries `FromEmail/ToList/CcList/SentOnUtc`, `Kor.EmailSearch.Core/EmailIndexWriter.cs:161-166`); `CanonicalOrg.WebsiteDomain` + filtered index (migration `271_CanonicalOrgWebsiteDomain.sql`; 2,491 active orgs, 124 engagement buyers). (revised) `DeltekLookupService` is not "dormant" wholesale — only `FindContactAsync` (`Kor.Operations.App/Crm/DeltekLookupService.cs:143`) has zero callers; it is app-side ODBC, so any Worker use needs a tier decision like 2.5's.
- *(revised) Why Worker-tier is mandatory, not preferential:* ToList/CcList are delimited strings — domain matching is LIKE-scans over 368k rows; fine nightly, fatal live. No Worker code touches KorEmailIndex today (verified) — this is genuinely new reach; the "app logins lack grants" claim is a DB-permission check folded into D7.
- *Invariants:* Corpus is project-keyed (`projectNumber` required, `EmailIndexWriter.cs:50`) → skews to delivery-phase clients: UI says "no filed correspondence", never "cold"; exclude generic domains; org-level aggregates only — counts and dates, never bodies. Prototype the join read-only before any UI. **Transmittal-opens fusion is cut** (Appendix). This job is the single nightly "CRM enrichment" job — any future detector (2.5's promotion, 3.3) becomes a section of it, never a sibling.
- *Tuesday:* Owner judging relationship warmth before grabbing/pricing; Jim on a board that is no longer permanently, falsely cold.

**3.2 — merged into 3.1** (revised; see Appendix).

**3.3 Lost-pursuit award matcher — backlog, not scheduled (revised)**
- *Shape when its prerequisites exist* (a real loss population from 1.5; matching precision proven by a read-only dry-run): match Lost engagements' parent opportunities against later `OpportunityAwards` via `UX_OppAwards_SourceRef` (`Schema/08_OpportunityAwards.sql:46`) — noting the opportunity row carries no source/reference itself, so the key comes from the OpportunityObservations/OpportunitySources join (same seam as F1). Confirmations delivered as **morning-report lines Ian confirms — never a queue UI** (a confirm-queue is a new place to look and a discipline loop, exactly the feature species this team kills). On confirm, `LostToCanonicalOrgId` fills and the winner renders via the **existing `CompetitorProfileWindow`** (`Opportunities/CompetitorProfileWindow.xaml.cs`) + `GetCompetitorProfileAsync` (`SqlVendorAnalyticsStore.cs:24`).
- *Standing invariant carried:* before ever enabling `LowValueOrgArchive`, its predicate (checks `CrmEngagements.BuyerCanonicalOrgId`, `DataRetirementJob.cs:141`) must add LostTo/KorPursuits references.

**3.4 Attribution enrichment — deferred by lock, unlocked by data**
- Deltek lifetime fee via `WonProjectWbs1` (2.5, `FeeBilled` already on the row), sector breakdown, per-owner rates — built **only when real grab→win history accrues** (locked gate), and only after F4/D2 identity canonicalization. Pre-approved path per `docs/BD-Audit-2026-07-01.md:179`; the loss-denominator import is D3.

**Deliberately NOT scheduled:** stage-enum expansion Phase 1b (7 lockstep spots incl. the CHECK constraint and MCP prompt; needs Ian at the screen — D5), auto-release to Bazaar (M4 semantics undefined, `BD-Pursuit-Lifecycle-Design-2026-06-25.md:238` — D4), MPI grabbing into the Bazaar pool (revisit only if Forward Pipeline pursuit demand materializes), frontfill project-links (dry-run first, D10).

---

## 5. Anti-Features (deliberately not building)

1. **Manual lead scoring / qualification forms** — RelevanceScore/Tier already automates this at intake; humans would duplicate a machine's job with worse consistency.
2. **Mandatory fields anywhere** — the team abandons *optional* typing; mandatory fields produce abandoned records or "asdf". Outcome capture is one optional click with Skip as the default, forever.
3. **Kanban pipeline board** — 4 deliberately-collapsed stages = two columns of live cards, two of history. Overwatch's coldest-first list answers the real question better.
4. **Per-contact cadence/sequence automation** — 97 contacts, 6 users. `NextActionDueUtc` (verified: zero consumers today) earns at most one digest line, never a queue UI with snooze buttons.
5. **BCC-to-CRM / manual email filing into pursuits** — 368k emails are already captured; the answer is a join (3.1), not a habit.
6. **A second notes surface** — `OpportunityNotes` (0 rows; created by `Kor.Operations.App/Scripts/20260502_opportunities_schema.sql:206-227`) and `CrmContacts.Notes` already exist unused. If a notes timeline is ever wanted, wire `OpportunityNotes` (verify its AuthorStaffId width first — the draft's nvarchar(20) claim was not re-verified at that location); never a new table.
7. **Leaderboards / quotas / per-owner win-rate widgets** — statistically fake (3 identity regimes + unattributed backfill) and culturally toxic for engineers doing BD on the side. Attribution stays data-gated (locked).
8. **Data-quality nags in user flows** — hygiene lives at the platform tier (supervised dedup CLI, gates, retirement). BD users never do janitorial work.
9. **New dashboards or report types before the kill-list review** — 8 reports + 3 visuals + 5 briefs already exceed measured consumption (which is why 2.2(c) instruments first).
10. **Mobile/web/portal/real-time collab/PowerPoint/scheduled report email** — ruled out in the UI plan; stays ruled out.
11. **Per-pursuit task/checklist management** — a third to-do system beside Outlook and Deltek would be ignored first. Pursuit records hold *state*, not chores.
12. **Territory/ownership rules engines** — six people deconflict verbally; the per-person BD-relationship natural key is already the design.
13. **Anything that writes to Deltek, builds on Deltek Activity (17 rows) or KorPursuits outcomes (no Won branch by design, `docs/BD-Audit-2026-07-01.md:173`), reverses Model B, makes IntelActions grabbable, or copies-and-reimports CRM rows** — all locked.
14. **(revised — added in review) Generic retry/catch "hardening" wrappers around `GrabAsync`** — the grab UX-honesty invariant's named breakage class: retries convert honest AlreadyTaken/Ignored outcomes into lies. Grab semantics are load-bearing; refactors keep hands off.
15. **(revised — added in review) One Worker job per feature** — every passive detector lands as a section of the single nightly CRM-enrichment job (3.1) or an extension of an existing job (2.5 → `CanonicalOrgKorProjectSignalRefreshJob`); never a new sibling job. Job sprawl is developer-facing bloat with permanent observability cost.

---

## 6. Open Decisions for Ian / BD

| # | Decision | Blocks | Recommendation offered |
|---|---|---|---|
| **D1** | **Outcome sync direction (F8):** engagement close writes parent `Opportunity.Status`, or all outcome reporting declared CRM-only (Opportunities-side Won/Lost menu removed/guarded for owned rows)? Also: block the RFPs menu from resetting an OWNED opp to New (auto-expiry re-exposure)? (revised) Also determines whether 1.5's outcome dialog serves both ledgers or CRM only. | Any team-facing funnel numbers | Sync-on-close inside the engagement-update transaction + owner-guard the reset; keeps both ledgers true under Model B |
| **D2** | **Legacy owner identity:** normalize the 75 first-name rows to UPNs via a verified mapping table, or leave and display-map? (No guessing — a wrong map fabricates attribution.) (revised) Also covers the roster-email-vs-UPN ambiguity F4/F5 inherit — no mapping exists today. | Mine-filter/digest/attribution fidelity | Small hand-verified map (6 people); migrate with the write-gate ritual |
| **D3** | **Loss denominator:** import the 83 `Deltek.PR` losses (and/or 259 Submitted) with a distinct ExternalSource (never reusing `Deltek.CustomProposal` — 268 idempotency invariant)? | Real win rate | Import losses only, clearly labeled as backfill population |
| **D4** | **Auto-release semantics + staleness threshold N** (design M4: what Status does a released opp get without being auto-expiry bait?) | Any release-to-Bazaar feature | Defer until Overwatch shows a real abandonment pattern post-adoption |
| **D5** | **Stage-enum Phase 1b timing** (Claimed/Contacted/ProposalOut; 7 lockstep spots; needs you at the screen) | — | Defer until stage-age data (2.2a) shows Drafting is genuinely overloaded |
| **D6** (revised) | **Digest policy:** per-owner weekly digest — recipients, cadence, and the launch gate, now **owner-count-based: ≥3 owners holding live pursuits** (not grab-count); does the daily morning report gain team-facing sections or stay yours? | 2.1(b) | Weekly per-owner via extending BdMorningReportJob; morning report stays yours until adoption data says otherwise |
| **D7** | **Email-warmth compute tier:** grant `opportunities_app` read on KorEmailIndex, or compute nightly at Worker into a rollup table? Includes verifying current grants (unverifiable in-repo) + the team-privacy framing (aggregates only). | 3.1 | Worker-computed nightly rollup; no new grants to app logins (also the only performant option — LIKE-scans over delimited recipient lists) |
| **D8** (revised) | **Nav subtraction sign-off** — the two folds (Attribution→Dashboard card, BD Tracking→Pursuits tab) + the two drops (Proposals, Brochures) reach **11**; further cuts to ~8 need three more named candidates chosen from 2.2(c) usage data. Needs Jim's nod. | 1.8 and the follow-on cuts | Ship the four now; pick the rest at the first kill-list review |
| **D9** | **M9 authorization posture** (trust-based actor model incl. `UserUpnOverride` spoofability, `docs/BD-Audit-2026-07-01.md:131-134`): accept consciously or role-gate? | Nothing technically; governance | Accept + document, revisit if headcount grows |
| **D10** | **Frontfill dry-run** (`BdTrackingCrossLink` for the 75 legacy engagements; ceiling ~38/75) — run it for the real hit-rate? | Legacy-row project links | Run the dry-run; decide on numbers |
| **D11** (revised — new) | **Email shell extraction:** `KorEmailTemplate` is App-assembly-bound; Worker mail (2.1 section, future digest) needs it. Extract to a shared lib, or accept two shells? | 2.1 styling parity | Extract once to a shared lib — duplicating the shell is the divergence class F15 exists to kill |
| **D12** (revised — new) | **Rolodex→CrmContact auto-create semantics** for 1.2's deferred contact half: dedup on email? per-engagement copies vs shared contact? (`ContactId` is a soft link, no FK, `Schema/03_Crm.sql:66`.) | 1.2's Deltek-rolodex autocomplete | Decide only when organic activities exist; shared contact deduped on email is the default proposal |

---

**Scope honesty (revised):** Critical path to the first organic grab ≈ 1.5-2 weeks (F1/F3/F5/F6/F9/F10/F11 + 1.1/1.3/1.4/1.6). Close-semantics package (F2+F7+F8-interim+1.5) before the first live close. Remaining fix-first + Phase 1 ≈ 1-2 further weeks. Phase 2 ≈ 2-3 weeks, trailing real grabs. Phase 3 is one L flagship (3.1) gated on D7 and a read-only prototype. These are focused-work weeks, not calendar weeks — takeoff, FileSync, and deploys compete for the same developer; the sequencing above is what makes that survivable, because everything after the critical path trails the first grab rather than blocking it. Net steady-state surface: one AI panel (removable by pre-committed criterion), one optional outcome dialog, one morning-report section, one nightly enrichment job, one minimal pane-open counter, one *eventual* digest — against the removal of CrmWindow, four nav items now (more by data), and the orphaned hub. **Net app surface and net Worker-job count both go down.** A team of one maintains this; nothing forks a component, violates a locked decision, or asks a user to type a fact the platform already knows.

---

## Appendix — Cut in review

| Cut/changed item | One-line reason |
|---|---|
| **2.5 nightly post-win detector** → manual link button | Zero live wins; would run silently for months; the cited reuse (`IKorWonProjectAccessor`) is app-side ODBC a Worker can't touch anyway; a win a month is one click a month. |
| **3.2 as a separate L item** → merged into 3.1 | Same rollup table, same nightly compute, second consumer — scheduling separately double-counted a quarter's work. |
| **Transmittal-opens fusion into staleness** → cut | Delivery-phase-biased "supplementary" signal nobody will trust; a permanent maintenance surface for near-zero decision value. |
| **2.2(c) as a built report section** → saved SQL/MCP query | Audience of one; a query Ian runs before the kill-list review is the honest-cost instrument; build the section only if the ritual proves weekly use. |
| **3.3 scheduled build (L, with confirm-queue UI)** → backlog, morning-report-lines shape | Zero losses exist, match precision unproven, and a confirm queue is a new place to look plus a discipline loop — the exact feature species this team kills. |
| **1.2 Deltek-rolodex/ContactId autocomplete half** → deferred (D12) | Rolodex source is empty until 1.3 lands; ContactId linking requires an undesigned CrmContact auto-create write path; 100% historical non-adoption of typing says earn it first. |
| **2.1(b) per-owner weekly digest as a new pipeline** → deferred + reshaped | New inbox artifact with recipient management and blank-first-issue risk; gate on ≥3 owners with live pursuits; when built, extends BdMorningReportJob — never a second job. |
| **"Nav 15→~8"** → 15→11 now, rest data-driven | The draft's enumerated folds only reach 11; the missing three cuts were unnamed — naming them by usage data beats naming them by taste. |
| **1.7 unconditional AI panel** → conditional | Ships silent + instrumented with a pre-committed removal criterion; engineers who won't type a subject line may not type questions either. |
| **F1's "267-column store widening"** → removed from F1 | Double-counted F2's work; Region copies server-side in GrabSql with no store change. |
| **F2 "must precede the first grab"** → before the first live close | With zero live pursuits, no outcome data is destroyed this week; correct urgency unblocks the grab critical path. |
| **1.5's grab-store transaction template** → UpdateAsync template | Grab is an insert with no RowVersion; copying it would bypass both the RowVersion guard and co-committed stage history — the exact breakage the invariant warns about. |
| **2.3's new competitor/plan-taker rendering** → surface existing panel + link existing windows | OpportunitiesView's interested-firms panel and BuyerProfileWindow/CompetitorProfileWindow already exist; building parallel rendering was reinvention. |
| **2.4's org-brief fallback button** → reuse OrgDossierWindow's existing trigger | A second org-brief button beside an existing one-click door; also renamed to avoid a third "brief" colliding with the MPI PursuitBriefWindow. |
| **Draft's T12/T13/T16 invariant IDs** → replaced with inline statements | Those IDs exist in no repo document (verify-pass shorthand); citing them would read as phantom register entries. M4/M6/M9 retained with real citations. |
| **"EmailIn→'—'" display-drift claim in 1.2** → dropped | Not reproducible in code (`CrmActivityRowView.cs:15` shows raw enum names; options are `Enum.GetValues`); unverified claims don't ship to Ian. |
