# Round 14c — Codex Prompt (P1-deferred + P2-deferred + P3 + P4 from Round 14a audit)

**Branch:** `develop` (already at `03ad135` after Round 14b)
**Authoritative audit doc:** `docs/Round14a-Audit.md`
**Convention reminders (do NOT skip):**
- Do **not** run `dotnet build` or `dotnet test` — your environment hangs on them. Claude verifies builds locally after you confirm the edits.
- Migration scripts that ADD a column and then reference it (index, constraint, UPDATE) must split into GO-separated batches. SQL Server parses the whole batch before executing.
- `git add` any new files (especially the new `.sql` migration) immediately so they don't get lost as untracked.
- Don't refactor beyond what's listed — each item below is scoped intentionally.

---

## A. Concurrency-safe upserts (deferred P1: replace MERGE)

Three production stores use `MERGE` against a natural key without `HOLDLOCK`. Concurrent scheduled/manual runs can hit SQL Server's known MERGE race behavior (duplicate-key exceptions, partial state). Replace each with the safe pattern:

```sql
BEGIN TRAN;
UPDATE target WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET <columns>, UpdatedAtUtc = sysdatetimeoffset(), IngestionRunId = COALESCE(@runId, IngestionRunId)
OUTPUT inserted.Id
WHERE <natural key match>;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO target (<columns>) OUTPUT inserted.Id VALUES (<values>);
END
COMMIT TRAN;
```

The OUTPUT clause must return the row Id in both branches (the existing `MERGE ... OUTPUT CASE WHEN $action = 'INSERT' THEN INSERTED.Id ELSE 0 END` shape is what's currently returned — preserve the "0 on update, real Id on insert" semantics so callers don't change).

### A1. `Kor.Opportunities.Data/Awards/SqlOpportunityAwardStore.cs:122` (`UpsertAsync`)
Natural key: `(OpportunitySourceId, ExternalReference)`. Replace the `MERGE` block (lines ~124-155) with the UPDATE-then-INSERT pattern. Keep the existing C# parameter wiring intact. Preserve the post-upsert canonical-resolver block (lines ~182-220) verbatim.

### A2. `Kor.Opportunities.Data/Awards/SqlKorPursuitStore.cs:85`
Natural key: read the existing MERGE to confirm (likely `(ExternalSource, ExternalSourceKey)` or `Id`). Apply the same UPDATE-then-INSERT pattern.

### A3. `Kor.Opportunities.Data/Awards/SqlBuildingPermitStore.cs:60`
Natural key: read the existing MERGE to confirm (likely `(PermitSourceId, ExternalPermitNumber)`). Apply the same UPDATE-then-INSERT pattern. Preserve the snapshot-and-skip-unchanged optimization added in Round 14b around this method.

**Out of scope for 14c:** the other MERGE callers (Heartbeat, ScoringProfile, CanonicalOrg, OrgAlias, OpportunitySourceMappings, VendorSiteCrawl, EnrichmentTracking, OpportunityBids). Audit didn't flag them. Leave them alone.

---

## B. IngestionTriggerPoller hardening (deferred P2)

File: `Kor.Opportunities.Worker/Services/IngestionTriggerPollerBackgroundService.cs`

### B1. Max triggers per wake
In `DrainAsync` (~line 86), add a per-wake counter and break out of the inner `while` loop after processing N triggers. N comes from a new option `OpportunitiesWorkerOptions.IngestionTriggerMaxPerWake` (default **25**). This prevents a Run-Now burst from monopolizing the worker.

### B2. Stranded `InProgress` reclaim
File: `Kor.Opportunities.Data/Sources/SqlIngestionTriggerStore.cs` (the implementation of `ClaimNextPendingAsync`).

Currently the store only claims `Status = 'Pending'`. Extend the claim SQL so it ALSO picks up `Status = 'InProgress' AND ClaimedAtUtc < DATEADD(MINUTE, -@staleMinutes, sysutcdatetime())`. Default `@staleMinutes` = **15**.

Increment a new `ReclaimedCount INT NOT NULL DEFAULT 0` column on the trigger row (see B3) whenever a stranded row is re-claimed. This gives operators a single number to see "this trigger has been claimed N times".

### B3. Migration 28 add
Add the `ReclaimedCount` column to `opportunities.IngestionTriggers` (default 0) in `Schema/28_*.sql` (combined with the schema items in section C).

### B4. Shutdown path
The existing comment at line 154 says "leave the row InProgress — restart will re-claim it via an admin reset". With B2 in place, restart auto-reclaims after 15 minutes. Update that comment to reflect the new behavior. No new code needed; the existing throw on cancellation is correct.

---

## C. Schema hardening — migration 28

Create **new file**: `Kor.Opportunities.Data/Schema/28_SchemaHardening.sql`. Single `.sql` file; uses GO batches.

### C1. KorPursuits CHECK constraints (P3 item)
`opportunities.KorPursuits.Stage` must be one of `PursuitStages.All` (see `Kor.Opportunities.Core/Models/KorPursuit.cs:6` — Considering, Pursuing, Submitted, Won, Lost, Withdrawn, Declined).
`opportunities.KorPursuits.OurRole` must be NULL or one of `KorRoles.All` (Prime, Sub, JV, Support).

Use named CHECK constraints (`CK_KorPursuits_Stage`, `CK_KorPursuits_OurRole`). Guard with `IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = '...')` so the migration is rerun-safe. **Before** adding the constraint, run a defensive `UPDATE opportunities.KorPursuits SET Stage = 'Considering' WHERE Stage NOT IN (...)` to fix any pre-existing bad rows (log how many) — same for OurRole (set to NULL if invalid).

### C2. NewsMention multiplicity (P3 item)
The audit (`SqlNewsStore.cs:162`) notes that `opportunities.NewsArticleOrgMention` is unique by `(NewsArticleId, CanonicalOrgId)`, so when one article mentions the same org for multiple events, later mention types collapse into one row.

Fix: change the uniqueness to include `MentionType`.
- Find the existing unique index/constraint on `(NewsArticleId, CanonicalOrgId)` in migration 25. Drop it. Recreate as a unique index on `(NewsArticleId, CanonicalOrgId, MentionType)`. Treat NULL MentionType as a distinct bucket — use `WHERE MentionType IS NOT NULL` filtered unique index, plus a separate unique filtered index `WHERE MentionType IS NULL`, OR use `ISNULL(MentionType, '')` in the key (cleanest — pick this approach if the existing index allows it).

In `Kor.Opportunities.Data/Awards/SqlNewsStore.cs:159` `RecordMentionAsync` — update the IF NOT EXISTS / UPDATE pair so it keys on all three columns. The UPDATE branch becomes effectively a no-op (only Confidence/Excerpt would be updatable for an exact-match row, and exact-match already has the right MentionType), so simplify to just the IF NOT EXISTS INSERT pattern.

### C3. Fix Vancouver permit source seed name (P4 item)
Migration 26:85 inserted `'City of Vancouver  issued-building-permits'` (double space). In migration 28, run a defensive UPDATE:

```sql
UPDATE opportunities.PermitSource
SET    Name = 'City of Vancouver – issued-building-permits'
WHERE  Name = 'City of Vancouver  issued-building-permits';
```

(Use ` – ` em-dash with single spaces, OR a single regular space — pick the em-dash; cleaner in admin UI.)

Also update `Schema/26_BuildingPermits.sql:85-89` source-of-truth seed to the same new name so a fresh deploy doesn't recreate the bad name. **Do not** alter migration 26's structure beyond the string literal.

### C4. IngestionTriggers.ReclaimedCount (see B3)
```sql
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('opportunities.IngestionTriggers') AND name = 'ReclaimedCount')
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD ReclaimedCount INT NOT NULL CONSTRAINT DF_IngestionTriggers_ReclaimedCount DEFAULT 0;
END
GO
```

### C5. Stale comment cleanup (P4 item, source-only)
`Schema/25_NewsAggregator.sql` mentions "12b classifier" and "set by 12b" in comments — update to the current job names (`NewsMentionClassifyJob` / `NewsMentionClassifier`). **Comments only** — no SQL changes. This is in migration 25's text, not migration 28.

---

## D. Structured warning logs on silent catches (P3 item)

Five spots currently swallow exceptions silently. Add `ILogger.LogWarning` (or `LogDebug` where appropriate) with structured fields so failures show up in logs without changing flow control.

For each: inject `ILogger<T>` via constructor if not already present (most have it). Do not change the catch semantics — keep swallowing the exception (the catch boundary is intentional), but log it.

### D1. `Kor.Opportunities.Data/Opportunities/SqlOpportunityStore.cs:220`
Canonical resolver catch — log `"Canonical resolution failed for buyer '{BuyerName}' on opportunity {SourceKey}"` with the exception.

### D2. `Kor.Opportunities.Data/Awards/SqlOpportunityAwardStore.cs:208`
Canonical-link catch (post-MERGE → post-upsert). Log `"Canonical resolution failed for award (source={SourceId}, ref='{Ref}')"`.

### D3. `Kor.Opportunities.Data/Awards/AwardAgentEnrichmentService.cs:162`
Inner catch around `RecordFailureAsync`. Log `"Failed to record AwardAgentEnrichment failure for award {AwardId}: secondary error after upstream failure"`.

### D4. `Kor.Opportunities.Data/Awards/VendorSiteExtractionService.cs:128`
Inner catch around failure-record. Same pattern: `"Failed to record VendorSiteExtraction failure for award {AwardId}"`.

### D5. `Kor.Opportunities.Data/Awards/NewsMentionClassifier.cs:173`
Inner catch around `MarkArticleClassifiedAsync('failed')`. Log `"Failed to mark NewsArticle {ArticleId} as failed (secondary error)"`.

---

## E. List/MaxRows clamps (P3 items)

### E1. `Kor.Opportunities.Data/Opportunities/SqlOpportunityStore.cs:55` (`ListAsync`)
Currently returns ALL active opportunities (no TOP). Add a `TOP (@max)` clamp. Add an optional `int maxRows = 5000` parameter to the interface and implementation. Default callers use 5000. Don't change return shape.

### E2. `Kor.Opportunities.Data/Awards/SqlAwardQueryStore.cs:57` + `Kor.Opportunities.Data/Awards/SqlCompetitionInfoQueryStore.cs:81`
Both accept caller-supplied `MaxRows`. Clamp to **min(maxRows, 5000)** at the top of each method before passing to SQL — single line per store.

---

## F. DI cleanup (P4 item)

### F1. `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs:75`
`HistoricalOpportunityDetailViewModel` is registered twice (line 73 and 75). **Remove line 75**. Verify by grepping for the type — should be exactly one registration after the edit.

---

## Deliverables

1. Edit the files above. Group SQL changes into a single new `28_SchemaHardening.sql`.
2. `git add Kor.Opportunities.Data/Schema/28_SchemaHardening.sql` immediately.
3. Confirm to Claude:
   - Each file edited (path, brief one-line summary of change).
   - Confirm no `dotnet build` was attempted.
   - Confirm `28_SchemaHardening.sql` exists and is staged.
4. Stop. Claude will then:
   - Build locally.
   - If green, give Ian the SSMS migration block + publish/deploy paste-block.
   - If red, send a follow-up Codex prompt with the specific fix.

Out-of-scope for 14c (defer to 14d):
- Log-level review (P4 #5 — keep Info levels for now; revisit during dashboard work).
- Sibling-column / table-level migration guard refactor (P3 #5, #6 — invasive across 14 migrations; deploy currently matches, so not urgent).
- UI header silent-catches (P4 #4 — cosmetic).
- Cron collision rebalancing (cross-cutting finding — needs separate cadence-design pass).
