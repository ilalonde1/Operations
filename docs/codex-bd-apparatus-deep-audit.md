# Codex Deep Adversarial Audit — BD Canonical-Data Apparatus

## Why this exists

Reactive crash-patching of the BD research-import pipeline exposed **fundamental,
latent defects** — a single-point-of-failure run loop, three sites that wrote
unverified foreign keys (FK violations that aborted whole runs), a frozen
self-anchor silently collapsed by a dedup pass, and a kind column that could be
overwritten with a URL. Each was found by *crashing into it*, not by design.

That is unacceptable for a system the firm's BD decisions depend on. This audit
exists to find **everything else** in one exhaustive, adversarial pass, so we get
either a clean bill or a complete defect list — never another runtime surprise.

**Your mindset: assume this apparatus is broken and prove how.** Do not report
"this looks risky." For every finding, give a concrete failure scenario — the
exact input, state, or interleaving that makes it misbehave, and the data
corruption / abort / loss that results. A finding without a repro path is a
question, not a finding; mark it as such.

**Do NOT fix anything. Produce the findings report only.** We review, then batch
the fixes with validation.

---

## Scope — the apparatus, in tiers

### Tier 1 — the data-integrity spine (audit EXHAUSTIVELY, line by line)
The canonical-org lifecycle: ingest → resolve/canonicalize → enrich → decompose
→ dedup/merge → retire → consume.

- `tools/BdResearchImport/Program.cs` — research JSON → CanonicalOrg + Enrichment + MPI
- `tools/BdQueueDrainIngest/Program.cs` — honing-drain batches → DB
- `tools/BdIntelExtract/Program.cs` — Enrichment → Intel* tables (decomposition)
- `tools/BdCanonicalDedup/Program.cs` — canonical merge/dedup (survivor selection, FK re-pointing)
- `tools/BdOrphanOrgPurge/Program.cs`, `tools/BdOpportunityPurge/Program.cs` — deletion paths
- `tools/BcMpiImporter/Program.cs`, `tools/BdHoningIntelBackfill/Program.cs`, `tools/BdSeedImport/Program.cs`
- Stores: `Kor.Opportunities.Data/Awards/SqlCanonicalOrgStore.cs`, `CanonicalOrgResolver`,
  `Kor.Opportunities.Data/Awards/SqlEnrichmentTrackingStore.cs`,
  `Kor.Opportunities.Data/MajorProjects/SqlMajorProjectsInventoryStore.cs`,
  `IntelPersistenceService`, `IntelExtractorRegistry` + all `IIntelExtractor` impls,
  `Kor.Opportunities.Data/Awards/SqlArchitectDisplacementBriefStore.cs`
- Models: `Kor.Opportunities.Core/Models/CanonicalOrg.cs` (frozen kinds: KorStructural, KorClient)

### Tier 2 — the live pipeline jobs (audit for the Tier-1 defect classes + concurrency)
`Kor.Opportunities.Worker/Services/`: `EnrichmentDispatchJob`, `CanonicalOrgDedupJob`,
`DataRetirementJob`, `DataHealthAuditJob`, `AbMajorProjectsInventoryJob`,
`CanonicalOrgKorProjectSignalRefreshJob`, `BdResearchQueueBuilderJob`, the ingestion
jobs (`CanadaBuys*`, `SamGov*`, `GraphEmail*`, `BuildingPermits*`, `BcBid*`), and the
job scheduler/poller infrastructure (`OpportunitySourceCronScheduler`,
`JobTriggerPollerJob`, `JobScheduleStore`).

### Tier 3 — consumers (audit only for: do they correctly honor retirement + null FKs?)
`SqlBdDashboardStore`, `SqlBdReportService`, `SqlPursuitBriefStore`, `SqlBriefDataStore`,
the MCP read path (`Kor.Operations.Mcp/Ai`), `Kor.Opportunities.Data/Schema/*.sql`.

---

## Known-fixed — do NOT re-report these (they are DONE in commit 0d179fee)

1. `BdResearchImport.Main` per-tag fault isolation (`RunTagAsync`) — wrapped.
2. Per-record `try/catch` + `RecordRowFailure` in BdResearchImport importer loops.
3. `--ingest-canonical` per-record guard (`RecordsFailed`).
4. Stale-FK validation via `ValidatedSeedIdOrNullAsync` (rejects missing AND
   soft-retired) at: team-awards `architectSeedId`, displacement-briefs `architectId`,
   decision-makers `orgId`.
5. `ResolveKorStructuralOrgAsync` non-fatal (WARN+skip).
6. `TryLoadJson` malformed-payload → skip+WARN.
7. `DataHoningTargetKind` fall-through returns null.
8. BD-tracking: `options.FxRate`, InvariantCulture dates, cross-link CommandTimeouts,
   guarded report writes; success counters increment after writes.
9. Migration `137_RestoreKorStructuralAnchor.sql`.

Report only NEW defects, or the SAME defect class found in components OTHER than
where it was already fixed.

---

## Audit dimensions — run EVERY one against Tier 1; apply where relevant to Tiers 2–3

For each dimension, the checklist is the *minimum*; hunt beyond it.

### D1 — Referential integrity / foreign keys
- Every write of a `*CanonicalOrgId` (or any FK) — is the id guaranteed to reference
  a **live** row at write time? Trace its provenance (JSON seed? cached? resolved?).
- Same class as the architectSeedId bug: any id taken from external/JSON/cached state
  and written to an FK without existence validation. Check ALL Tier-1 tools + jobs.
- Orphan creation: rows that reference a parent that a concurrent/later delete or
  merge can remove. Check `BdOrphanOrgPurge`/`BdOpportunityPurge` delete order vs FKs.
- Intel* tables (Person/Signal/Narrative/Action/Risk/Work/Project) — can decomposition
  attach a child to a CanonicalOrg that dedup is about to merge away?

### D2 — Idempotency / re-run safety
- We observed a ~51-row enrichment drift when re-running an unchanged sweep. Find the
  source: which writes are NOT idempotent? Upserts keyed on a mutating value, hash keys
  over unstable inputs, `INSERT` without an existence guard, auto-resolution that mints
  a new canonical on a name variant each run.
- Every tool/job: is running it twice on the same input a no-op the second time? If not,
  name the non-idempotent write and the duplication/drift it causes.

### D3 — Dedup & merge correctness (`BdCanonicalDedup`, `CanonicalOrgDedupJob`)
- Survivor selection: `KindRank` logic — does it ever pick the WRONG survivor?
  (History: wrong SurvivorId twice — Abbotsford SD → Alterra Power Corp.) Prove correctness
  or find the failing pair shape.
- On merge, are ALL inbound FKs (MPI proponent/architect/structural/GC, Intel*,
  Enrichment, aliases, briefs) re-pointed to the survivor BEFORE the loser is removed?
  Any table that references CanonicalOrg and is NOT re-pointed = stranded/orphaned rows.
- Frozen kinds (KorStructural/KorClient): is a frozen self-anchor protected from being
  merged away or kind-downgraded? (It was NOT — id 38918 became `Firm`.) Where is the
  guard, and is it complete across every merge/update path (importers AND dedup AND jobs)?
- Alias preservation: does the loser's name survive as an alias so future resolution
  still matches it?

### D4 — Retirement lifecycle (`DataRetirementJob`, `RetiredAtUtc`)
- Archive-not-delete invariant: does anything HARD-delete a row that should be retired?
- Resurrection (`UnretireAsync`): correct on re-discovery? TOCTOU on the retire/unretire
  race? (See the IntelPerson resurrection TOCTOU already fixed in commit 83fd5043 — is
  the SAME race present for CanonicalOrg or other retire-able entities?)
- Stranded children: intel/enrichment/MPI rows left pointing at a retired parent —
  are they re-homed, suppressed, or silently surfaced to consumers?
- Consumer filtering: do ALL Tier-3 reads filter `RetiredAtUtc IS NULL` where they
  should? Find any dashboard/brief/MCP query that surfaces retired rows.

### D5 — Fault isolation / resilience (apply to EVERY tool + job)
- The SPOF pattern: any batch/loop where one item's throw aborts the rest. We fixed
  BdResearchImport — find it everywhere else (BdQueueDrainIngest, BdIntelExtract,
  BdCanonicalDedup, every Worker job that loops).
- Setup-phase throws that abort before the loop (the kor-capability class).
- Partial-commit recovery: if a tool dies mid-run, is the DB left consistent? Can a
  re-run recover, or does it double-apply / wedge?

### D6 — Concurrency / TOCTOU
- Worker jobs on the cron scheduler — can dedup, ingest, retire, and enrichment run
  CONCURRENTLY against the same canonical rows? Enumerate the dangerous interleavings
  (e.g., dedup merges org X while ingest writes an FK to X; retire fires while extract
  attaches intel to it).
- Check-then-act races (Exists→Insert, Get→Update) without transaction/locking.
- Connection/transaction isolation levels on the MERGE/UPDLOCK paths — correct?

### D7 — Resource management
- `SqlConnection`/`SqlCommand`/`SqlDataReader`/`JsonDocument`/file handles — every one
  disposed (`using`/`await using`)? Connection-per-row antipatterns in loops.
- `CommandTimeout`: every long/streaming/full-scan query has a non-default timeout?
  (Default 30s fires mid-stream under load — we hit this.)

### D8 — Silent data loss / observability
- Swallowed or over-broad `catch` that logs nothing or returns success on failure.
- `[WARN]`/`continue`/skip that hides a REAL expected-input gap (vs. benign).
- No-op updates that mask a stale id (UPDATE ... WHERE Id=stale → 0 rows, no error).
- Uncounted drops; stat counters that over/under-report vs. actual persisted rows.

### D9 — Correctness
- Culture-sensitive parse/format (Parse without InvariantCulture), money/FX consistency
  across ALL streams, date handling, field-name mismatches (reading "stage" vs "Stage"),
  off-by-one, null-coalescing that masks a missing required field.

### D10 — SQL safety
- Any dynamically-built SQL (string concat/interpolation of external values) →
  injection risk. All user/JSON-derived values parameterized?

### D11 — Schema / migration integrity (`Kor.Opportunities.Data/Schema/*.sql`)
- Migrations that ADD a column AND reference it in the same batch (must be GO-separated —
  SQL Server parses the whole batch first).
- Idempotency / re-runnability of migrations (guards on `IF NOT EXISTS` etc.).
- FK constraints in schema that match (or contradict) code's write assumptions.
- Is there a schema-level guard protecting the frozen-kind anchors?

### D12 — End-to-end lifecycle invariants
Trace a single canonical org through ingest → enrich → decompose → dedup → retire →
consume and assert no step can strand, orphan, duplicate, or corrupt it. State each
invariant you checked and whether it holds. Example invariants:
- "Every Intel* row's CanonicalOrgId references a live (or intentionally-retired) org."
- "Exactly one live KorStructural row exists at all times."
- "Re-running any importer changes no row it didn't need to."
- "No consumer surfaces a retired org."

---

## Deliverable format

A single structured report. Group by Tier, then by component. For EACH finding:

```
[SEVERITY: Critical | Major | Minor]  (Dimension: D#)
Component: <file> :: <method> (~line N)
Invariant violated: <which assertion fails>
Failure scenario: <concrete input/state/interleaving that triggers it, and the
                   resulting corruption / abort / loss — adversarial repro>
Fix: <specific, minimal change>
Confidence: <High | Medium — needs runtime confirmation>
```

Severity rubric:
- **Critical** — silent data corruption, an abort that kills a multi-item run, an FK/
  integrity violation, or a frozen-anchor/dedup error.
- **Major** — single-component data loss, a non-idempotent write causing drift, a
  resource/timeout failure under load, a missing retirement filter in a consumer.
- **Minor** — cosmetic, stat miscount, defensive hardening with low real-world impact.

End the report with an **Apparatus Confidence Summary**:
- A table: each Tier-1 component → CLEAN / N findings (by severity).
- The top 5 systemic risks (defect classes that recur across components).
- An explicit statement of what you could NOT verify statically (needs a runtime/data
  check), so we know the residual unknowns.

Be exhaustive. The goal is that after we fix this list, there is nothing left to find.
