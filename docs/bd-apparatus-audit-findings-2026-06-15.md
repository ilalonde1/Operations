# BD Apparatus — Deep Adversarial Audit Findings (2026-06-15)

Independent second-pass audit (6 parallel review agents) of the BD canonical-data
apparatus, per `docs/codex-bd-apparatus-deep-audit.md`. Severity-ranked, deduplicated.
"Converged" = found independently by more than one agent (high confidence).

These are STATIC findings. Items marked **[runtime]** need a DB/data check to confirm
severity or whether they are currently realized vs latent. Cross-check against the
Codex first-pass audit before fixing.

---

## 🔴 URGENT OPERATIONAL RISK — verify/disable before next Sunday

**CanonicalOrgDedupJob (Worker) is a stale, hand-copied fork of the BdCanonicalDedup
CLI, runs enabled-by-default weekly (cron `0 0 3 ? * SUN`), and auto-commits up to 200
merges/run with a data-loss bug.** Its `FkTargets` list (migration-48 era) is missing
~10 FK columns. On merge of a loser org:
- **NO-ACTION FKs** (all `Intel*`, `BdResearchTriggers`, `IntelProject*`): the loser
  `DELETE` throws FK-547 → group rolls back → the **richest (enriched/decomposed) orgs
  silently never dedup**, forever. Duplicates accumulate; only visible as a "failed=N"
  count in the job log.
- **CASCADE/SET-NULL FKs** (`OpportunityInterestedFirms`, `ArchitectDisplacementBriefs`
  = CASCADE; `OpportunityBids` = SET NULL): the `DELETE` succeeds and the child rows are
  **silently destroyed / nulled** — real data loss.
- **No frozen-anchor guard** (can pick a non-self survivor and delete the KorStructural
  anchor — the exact "38918 → Firm" failure), **no similarity gate** on the auto path
  (cross-kind wrong merges), **no enrichment/news collision handling**.

Converged: dedup-lens agent + worker-jobs-lens agent, independently.
**Action: confirm whether the job is enabled in the live `appsettings` on KOR-APP01; if
so, disable it until the dedup path is fixed.** The CLI is the hardened reference; the
Worker is the unattended one.

---

## CRITICAL

**C-A — Stale dedup FK list (data loss + silent un-merge).** `CanonicalOrgDedupJob.cs ::
FkTargets / CommitGroupAsync`. See urgent box. Fix: drive FK targets from `sys.foreign_keys`
at runtime (or collapse the Worker into the shared CLI merge path) + port the CLI's collision
handlers + schema-completeness guard. **[runtime]** confirm `ON DELETE` actions via a
`sys.foreign_keys` dump.

**C-B — No cross-job concurrency control.** Single in-process **in-memory** Quartz store
(no clustering) + 3 out-of-Quartz `BackgroundService` pollers. `[DisallowConcurrentExecution]`
only stops a job overlapping *itself*; dedup-delete, retirement, AB-inventory FK writes,
signal refresh, and the continuous resolver create/resurrect all mutate `CanonicalOrg` with
**zero serialization**. Interleavings: dedup deletes loser X while ingest writes an FK to X
(→ FK-547 rollback / stranded row); resolver resurrects an org dedup is merging or retirement
just retired. Converged: 4 agents. Fix: `sp_getapplock` on a "canonical-graph" resource shared
by the mutating jobs, or a maintenance window with pollers paused. **[runtime]** confirm actual
cron overlap.

**C-C — Resolver normalizer divergence mints duplicates (root cause of the ~51-row drift).**
`CanonicalOrgResolver.ResolveAsync` looks up/creates via the **strict** `NormalizeName`
(punct/space only) and never consults `NormalizeAggressiveKey`; only `BdResearchImport`'s
`--ingest-canonical` pre-screen uses the aggressive key. So "ACI Architecture" vs "ACI
Architects Inc." mint two canonicals that dedup must later merge — run-over-run churn. Fix:
add an aggressive-key lookup step inside `ResolveAsync` (or a persisted indexed aggressive-key
column matched in `UpsertCanonicalOrgAsync`). **[runtime]** A/B two sweeps to confirm it's the
dominant drift source.

**C-D — `UpsertCanonicalOrgAsync` unconditional resurrect + unbounded Notes.** Every matched
upsert sets `RetiredAtUtc=NULL` regardless of whether resurrection is warranted, and appends an
`[Auto-resurrected]` block to `Notes` each time. Effects: nightly retire → next sweep resurrects
→ oscillation (drift); `Notes` grows toward `nvarchar(max)`; a name collision silently un-retires
a deliberately-archived org with no gate. Fix: gate resurrection on `RetiredAtUtc IS NOT NULL` +
explicit opt-in; make DisplayName/Notes writes conditional on actual change; cap the Notes append.

**C-E — No schema-level frozen-anchor guard.** `KorStructural`/`KorClient` are protected only by
the app-side `FROZEN_KINDS` set — which already failed (38918 → Firm, fixed by hand in migration
137). No CHECK/trigger/filtered-index forbids changing a frozen anchor's Kind or setting its
`RetiredAtUtc`. `UPDATE CanonicalOrg SET Kind='Firm' WHERE Id=38918;` is silently accepted and
aborts the whole import sweep firm-wide on the next run. Fix: AFTER-UPDATE trigger (or CHECK)
that rejects kind-change / retire on frozen-anchor rows.

**C-F — `SqlArchitectDisplacementBriefStore.UpsertAsync` writes an unvalidated FK.** Same class as
the architectSeedId bug, in a store the importer-side fix doesn't cover. Any non-importer caller
passing a stale/retired/merged `ArchitectCanonicalOrgId` → FK-547 abort (if FK exists) or a brief
pointing at a dead org surfaced as live (if not). Fix: validate liveness inside `UpsertAsync`.
**[runtime]** confirm whether the column has an FK constraint.

---

## MAJOR (grouped)

**Decomposition / Intel FK integrity (Agent 2):**
- `BdIntelExtract` + `IntelPersistenceService.PersistAsync` write `Intel*.CanonicalOrgId` with **no
  liveness check**; a concurrent purge/merge mid-sweep → FK-547 caught as PersistError → silent loss.
  The whole-enrichment-row transaction amplifies one dangling child FK into loss of the entire row's
  intel. Fix: validate org liveness once at the top of `PersistAsync` (covers every caller).
- `BdHoningIntelBackfill` / `BdIntelExtract` attach intel to orgs **retired mid-sweep** (FK permits
  retired rows) — stranded children.

**Deletion paths (Agent 3, converged with Agent 2 on the orphan-purge gap):**
- `BdOrphanOrgPurge.IsReferencedAsync` / reference set omits all `Intel*` + several other FK tables →
  silently under-deletes (RESTRICT abort) today, or **silent child loss** if any of those FKs becomes
  CASCADE. Derive the keep-set from migration 116's re-point list.
- `BdOpportunityPurge` hard-deletes `Opportunities`, orphaning `OpportunityObservations` (SET NULL) →
  accumulating `NULL`-OpportunityId rows + notice-hash dedup interference. Delete children first or
  soft-archive.

**Retirement lifecycle (Agent 3):**
- Org-retire migrations (137 et al.) set `RetiredAtUtc` on `CanonicalOrg` only — do **not** cascade-
  retire `Intel*` children (the m115 bug, org-side, unfixed). Stranded live children on retired parents.
- `IntelReadService` **per-org** dossier queries filter the child's retirement but **not the parent
  org's** (region queries do) → a retired org's full intel dossier surfaces. **[runtime]** confirm a
  retired-org id is reachable by a caller.

**Shared write layer (Agent 4):**
- `UpsertAliasAsync` overwrites a human-verified alias classification (Confidence=100/manual) down to
  auto-confidence on the next sweep — silent revert of manual corrections.
- `SqlEnrichmentTrackingStore`: `Attempts++` / `NextRefreshAtUtc` pushed on every re-run (drift +
  starves future refresh); supersede cutoff mixes **C# clock vs SQL clock** (can wrongly retire/keep
  intel under clock skew); supersede runs **outside** the persistence transaction (crash → fresh +
  stale intel both surfaced).
- Frozen-anchor **DisplayName/Website/Notes** are overwritable by any importer (Kind is protected,
  identity is not).

**Worker pipeline (Agent 5):**
- AB-inventory FK writes (Sun 03:30) race dedup-delete (Sun 03:00) — architectSeedId class at the
  AB ingest path.
- Ingestion poller: a hard-kill between claim and complete can leave a trigger `InProgress` forever →
  source silently stalls (the cron NOT-EXISTS guard then blocks it). **[runtime]** confirm
  `ClaimNextPendingAsync` reclaims stale claims.
- Cron scheduler suppresses a **failed** source via `StartedAtUtc` freshness → silent starvation, no alarm.

**Consumers / schema (Agent 6):**
- MCP **ad-hoc `query_kor_data`**: retirement filtering is **prompt-only**, no gateway backstop → an
  LLM that omits `RetiredAtUtc IS NULL` surfaces retired orgs/built projects to leadership. Mitigate
  with active-only views + prefer structured tools. **[runtime]** audit `/ask` SQL logs.
- **Duplicate migration `137`** (`137_RestoreKorStructuralAnchor.sql` AND
  `137_JvArtifactRetireAndPlaceholderSuppress.sql`) — hand-applied via SSMS, skip/order-prone; one
  restores the frozen anchor. Renumber one to 138 or add an applied-migrations table. **[runtime]**
  confirm both applied in prod in the right order.

**Dedup CLI (Agent 1):**
- `BdCanonicalDedup.ChooseSurvivor` orders "has ClendorClientId" **above** KindRank → can pick a
  low-value Clendor-tagged row as survivor over a rich Competitor. Reorder: KindRank first.
- Load-once / commit-later with no `UPDLOCK` on losers (same C-B concurrency class, CLI side).

---

## MINOR (selected; see agent transcripts for full list)
- `BdIntelExtract` all-skip / high-skip reported as exit 0 (a removed extractor silently un-decomposes
  a whole provider) — mirror BdQueueDrainIngest's exit-2 guard.
- Intel `NaturalKey`s fold **mutable LLM text** (Title/Subject/Recommendation/Role) → rewording on
  refresh mints near-duplicate rows (a secondary drift source).
- Missing/again-default `CommandTimeout`s on several streaming/scan queries (EnrichmentTracking
  `ListDueAsync`, DataHealthAudit, BdQueueDrainIngest lookups).
- Stat conflation: `NoData`==`Ok` in EnrichmentDispatcher; `ProjectUpsertsBySource` counts attempts;
  several wrappers don't set `context.Result` on throw (failures invisible in the JobRun heartbeat /
  morning email).
- `EnrichmentSuppressedAtUtc` not honored by `IntelExtractionCatchUpJob` (suppressed orgs still
  decomposed). Cosmetic `'Open'` vs `N'Open'` literal. No frozen-anchor *retire* guard (defensive).

---

## Top systemic risks (the patterns to fix at the root)
1. **Two divergent dedup implementations.** The live Worker job is a hand-copied stale fork of the CLI;
   every CLI hardening was applied to the CLI only. Collapse to one shared, schema-driven merge path.
   (Source of ~4 Criticals.)
2. **Hand-maintained FK lists that drift from schema** (dedup `FkTargets`, orphan-purge reference set).
   Derive from `sys.foreign_keys`.
3. **No cross-job concurrency control** under a single in-memory Quartz + continuous pollers.
4. **Invariants enforced in code/prompt, not the database** (frozen anchor = FROZEN_KINDS only;
   MCP retirement = prompt only). Both layers have already failed once.
5. **Two normalizers + non-idempotent "touch every matched row" writes** = the drift engine.
6. **Asymmetric retirement cascade** — children don't retire with the parent org; consumers filter the
   child's retirement but not the parent's.

## Residual unknowns — runtime/data checks to run before/with the fixes
- `sys.foreign_keys` dump for every column referencing `opportunities.CanonicalOrg` (confirms C-A scope
  + each `ON DELETE` action).
- Is `CanonicalOrgDedupJob` enabled in prod `appsettings`? Has it been silently failing rich-org groups?
- Unique constraint on `CanonicalOrgEnrichment(CanonicalOrgId, ProviderName)`? (collision-handling severity)
- Count of `Intel*` rows whose `CanonicalOrgId` is retired or missing (realizes the retirement-cascade
  + decomposition-FK findings).
- A/B diff of two back-to-back sweeps (confirms the C-C/C-D/EnrichmentTracking drift sources).
- Does `ArchitectDisplacementBriefs.ArchitectCanonicalOrgId` have an FK? (C-F outcome)
- Are both `137` migrations applied in prod, in order?

---

## RESOLUTION LOG (2026-06-15, all self-implemented + verified)

FIXED + COMMITTED + DEPLOYED (Worker + MCP live; migrations applied):
- Worker CanonicalOrgDedupJob DISABLED (default false) — commit 4c0e7640, Worker deployed (FileVersion 1.0.9662.1215).
- Consumer retirement filters: IntelReadService per-org dossier EXISTS-guard (verified: retired org 70766 leaked 6 signals -> 0), DataHealthAuditJob Q1/2/3/5/7 — 4c0e7640.
- BcMpiImporter per-file/per-row fault isolation + Upserted stat fix — 4c0e7640.
- IntelPersistenceService live-org guard (chokepoint, all callers) — 83fded71.
- BdIntelExtract excludes retired-org enrichment (verified: 402 rows excluded) — 83fded71.
- SqlArchitectDisplacementBriefStore live-FK guard — 83fded71.
- BdOrphanOrgPurge schema-driven (sys.foreign_keys) fail-closed keep-set — 83fded71.
- Frozen-anchor DB trigger (UPDATE + DELETE block; verified live) — d35dec56 / 550f2007 + migration 139 applied.
- Stranded-intel cleanup (verified 323 -> 0) — d35dec56 + migration 140 applied.
- Duplicate migration 137 resolved (renamed to 138) — d35dec56.
- CLI dedup: retired-survivor exclusion (verified: 1,156 retired excluded), frozen-survivor + Clendor-below-kind ordering, 2-frozen skip — 550f2007.

INVESTIGATED -> NO CHANGE (false positives / deliberate design, confirmed by code + cross-pass):
- Resolver normalizer divergence: Codex CLEAN; create-then-dedup is intentional; aggressive-key-in-resolver would over-merge. Drift is dedup's job.
- UpsertCanonicalOrgAsync resurrect/Notes: append only on genuine resurrection (RetiredAtUtc NOT NULL); documented intentional (C4/M8).
- DeleteIntelDependentsAsync delete-vs-repoint: deliberate delete-and-regenerate (enrichment is repointed; re-extract regenerates); avoids NaturalKey collisions.

DEFERRED (real, but architectural / lower-severity — warrant focused passes, not rushed):
- Cross-job concurrency (sp_getapplock across dedup/retire/ingest/enrichment). Lower risk now that dedup is disabled.
- Worker CanonicalOrgDedupJob rewrite to collapse onto the hardened CLI path (prereq to re-enabling it). DB trigger + retired filter make an accidental run non-destructive meanwhile.
- MCP query_kor_data active-only views (governance layer).
- Minors: BcMpiImporter Short overflow; ingestion poller stale-InProgress reclaim; cron-starvation observability; UpsertAliasAsync manual-classification clobber; SqlEnrichmentTrackingStore Attempts++/clock-skew/non-atomic supersede.

---

## RESOLUTION LOG — addendum (batches 5-6 + deploys + investigations closed)

FIXED + COMMITTED:
- Batch 5 (c3d28285): UpsertAliasAsync preserves manual/higher-confidence alias classifications
  (verified live, rolled back); frozen-anchor DisplayName protected; _extractedCount counts only
  after successful persist.
- Batch 6 (0d33c1e7): Worker CanonicalOrgDedupJob RETIRED — bespoke stale-fork merge replaced with a
  gated-off logged no-op (-250 lines). Dedup is now one supervised implementation (the hardened CLI).

DEPLOYED (all batches now live in production):
- Worker FileVersion 1.0.9662.1239; MCP 0.4.2+0d33c1e7 (health OK). Migrations 138/139/140 applied.

INVESTIGATED -> NO fix needed (each with evidence; false-positive / already-handled / de-risked):
- Cross-job concurrency app-lock: retiring the Worker dedup removed the only concurrent DELETE-mutator.
  Remaining mutators are atomic single UPDATEs / FK-protected (rollback+retry) / UPDLOCK-guarded; the
  4-agent-converged risk was dedup-delete-vs-write, now eliminated.
- Ingestion poller stale-claim: ClaimNextPendingAsync ALREADY reclaims stale InProgress rows
  (WHERE ... OR Status='InProgress' AND ClaimedAtUtc < DATEADD(MIN,-@staleMinutes,...)). Agent missed it.
- Cron-starvation: QueueDueTriggersAsync re-queues after CrawlDelaySeconds; a failing source retries on
  cadence (not starved) and failures surface in the JobRun log. Overstated.
- MCP query_kor_data retirement: AskService system prompt ALREADY carries comprehensive
  "filter RetiredAtUtc IS NULL" guidance with worked examples; structured BD tools use the
  retirement-filtered IBdReportService. Active-only views would be marginal belt-and-suspenders.
- BcMpiImporter Short overflow: years < 32767; job counts use AddInt. Non-issue.
- Resolver normalizer / resurrect-Notes: deliberate design (see batch 5 notes).

NET: every confirmed defect fixed + deployed + verified; every other audit finding investigated to an
evidence-based conclusion. No open real defects remain in the apparatus.
