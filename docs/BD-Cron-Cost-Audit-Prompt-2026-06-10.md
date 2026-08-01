# Enrichment Cron Cost Audit Prompt

> **For fresh Claude Fable 5 (or equivalent) session.**
> Self-contained: gives all context to audit the BD enrichment cron jobs
> for cost optimization without inheriting prior session's mental model.

---

## Mission

Adversarial deep-review of the **9 scheduled enrichment jobs running on
KOR-APP01** (`Kor.Opportunities.Worker`). Target: identify where the
**$50/day API cost** is going and find optimizations that cut waste
without losing data quality. Produce a prioritized punch-list with
**estimated $/day savings per finding**.

---

## Business context

KOR Structural runs an automated BD enrichment pipeline on KOR-APP01.
Cron-scheduled jobs call Anthropic (Claude Haiku 4.5 per env var) to
research BC + AB construction projects, orgs, and people; the output
decomposes into `Intel*` relational tables consumed by the WPF app +
MCP `/ask`.

**Current state (verified 2026-06-10):**
- ~1,340 enrichment ops in last 24 hours
- Model env var: `KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL = claude-haiku-4-5-20251001`
- Approx cost: $50/day ≈ $0.037/op (consistent with Haiku tier)
- Headline: cost is volume-driven, not model-choice waste. But
  the **prior audit found M22**: model env var is read via options
  in one job and raw env var in two others — appsettings value
  applies inconsistently. Hidden inflation likely.

**Why this matters now:**
- $50/day = $1,500/month → $18K/year
- The WPF in-app build (per `docs/BD-UI-Plan-2026-06-08.md`) is
  about to add new MCP tools that will increase enrichment demand
- A 30% cost reduction = $5.4K/year saved before the build adds load

---

## Scope: what is in/out

### IN — review these files + tables

```
Kor.Opportunities.Worker/Services/
├── ScheduledJobDefinition.cs           (the registry — lines 19-31 list all 9 jobs)
├── AwardAgentEnrichmentJob.cs
├── BcBidHistoricalEnrichmentJob.cs
├── VendorSiteCrawlJob.cs
├── VendorSiteExtractionJob.cs
├── EnrichmentDispatchJob.cs
├── NewsMentionClassifyJob.cs
├── BdDeltekLinkDryRunJob.cs
├── BdResearchQueueBuilderJob.cs
├── CanonicalOrgKorProjectSignalRefreshJob.cs
└── KorPursuitDeltekSyncJob.cs
```

Plus the LLM client + service layer:

```
Kor.Opportunities.Worker/      (search for IAgentEnrichmentClient, AnthropicClient, AskService)
Kor.Opportunities.Data/        (research executor + chokepoints — BdResearchExecutor, etc.)
Kor.Opportunities.Core/        (model/options contracts)
appsettings.json / appsettings.Production.json on KOR-APP01:C:\Program Files\KorOperations\Opportunities\
```

DB tables that drive staleness decisions:

```
opportunities.MajorProjectEnrichment      (LastRefreshAtUtc, NextRefreshAtUtc)
opportunities.CanonicalOrgEnrichment      (same)
opportunities.IntelPerson                 (LastSeenAtUtc)
opportunities.OpportunityAwards           (BcBidHistorical target)
opportunities.OpportunityInterestedFirms
opportunities.JobScheduleStore            (job state tracking, if exists)
```

Env vars on KOR-APP01 (Machine scope) — verify each job actually reads
the variable it should:

```
KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL              (claude-haiku-4-5-20251001 confirmed)
KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTBATCHSIZE     (=3 — suspect low for volume)
KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTCRONSCHEDULE  (=0 7/10 * * * ?)
KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTENABLED       (=true)
KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTTOTALCAP      (=5000)
KOR_OPPORTUNITIES_BCBIDHISTORICALENRICHMENTBATCHSIZE (=100)
KOR_OPPORTUNITIES_ENRICHMENTDISPATCHBATCHSIZE       (=50)
KOR_OPPORTUNITIES_ENRICHMENTDISPATCHENABLED         (=true)
ANTHROPIC_API_KEY                                   (presence/format check only — do not print)
```

### OUT — DO NOT TOUCH

- WPF in-app build / `Kor.Operations.App` — separate scope
- MCP service / `Kor.Operations.Mcp` — separate scope
- FileSync / `Kor.Operations.FileSync` — different module
- Manual drain queues at `C:\ProgramData\KorOperations\QueueDrain\` —
  those are operator-driven Sonnet sessions, not cron-scheduled
- Reports / `tools/BdReportBuilders/` — out of scope
- DataRetirementJob — different concern
- Any change to data quality bars (e.g., DEAD verdict evidence
  requirements) — out of scope; cost optimization only

---

## Authoritative state — where to find facts

**DO NOT** trust this prompt's claims. Verify each against:

### Git

```bash
git log --oneline --since="3 weeks ago" -- Kor.Opportunities.Worker/Services/
git log --oneline --since="3 weeks ago" -- Kor.Opportunities.Data/Research/
git log --oneline -- Kor.Opportunities.Worker/Services/ScheduledJobDefinition.cs
git log --oneline --grep="M22\|enrichment.*model\|agent.*model"
```

### Database (read-only)

```sql
-- Enrichment volume per provider, last 7 days
SELECT ProviderName,
    COUNT(*) AS Last7d,
    AVG(LEN(CAST(ResultJson AS NVARCHAR(MAX)))) AS AvgPayloadChars,
    MIN(LastRefreshAtUtc) AS Earliest,
    MAX(LastRefreshAtUtc) AS Latest
FROM opportunities.MajorProjectEnrichment
WHERE LastRefreshAtUtc >= DATEADD(DAY, -7, sysdatetimeoffset())
GROUP BY ProviderName ORDER BY Last7d DESC;

-- Staleness distribution — is the job re-enriching fresh rows?
SELECT
    ProviderName,
    SUM(CASE WHEN NextRefreshAtUtc <= sysdatetimeoffset() THEN 1 ELSE 0 END) AS DueNow,
    SUM(CASE WHEN NextRefreshAtUtc > sysdatetimeoffset() THEN 1 ELSE 0 END) AS FutureDue,
    SUM(CASE WHEN NextRefreshAtUtc IS NULL THEN 1 ELSE 0 END) AS NoSchedule
FROM opportunities.MajorProjectEnrichment
GROUP BY ProviderName;

-- Were the same rows enriched multiple times in the same day? (duplicate work)
WITH RefreshesPerDay AS (
    SELECT MajorProjectsInventoryId, ProviderName,
        CAST(LastRefreshAtUtc AS DATE) AS Day,
        COUNT(*) AS Refreshes
    FROM opportunities.MajorProjectEnrichment
    WHERE LastRefreshAtUtc >= DATEADD(DAY, -7, sysdatetimeoffset())
    GROUP BY MajorProjectsInventoryId, ProviderName, CAST(LastRefreshAtUtc AS DATE)
    HAVING COUNT(*) > 1
)
SELECT ProviderName, SUM(Refreshes - 1) AS WastedRefreshes
FROM RefreshesPerDay GROUP BY ProviderName;

-- Award enrichment status — is the 5000/day cap binding?
SELECT
    SUM(CASE WHEN AgentEnrichedAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS Enriched,
    SUM(CASE WHEN AgentEnrichedAtUtc IS NULL THEN 1 ELSE 0 END) AS NotEnriched
FROM opportunities.OpportunityAwards;
```

### KOR-APP01 env vars (verify via Invoke-Command — read-only)

```powershell
Invoke-Command -ComputerName KOR-APP01 -ScriptBlock {
    [System.Environment]::GetEnvironmentVariables('Machine').GetEnumerator() |
        Where-Object { $_.Key -like "KOR_OPPORTUNITIES_*" -or $_.Key -like "ANTHROPIC*" } |
        Sort-Object Key
}
```

### Worker logs (if accessible)

```powershell
Invoke-Command -ComputerName KOR-APP01 -ScriptBlock {
    Get-EventLog -LogName Application -Source "Kor.Opportunities.Worker" -Newest 100 |
        Where-Object { $_.Message -like "*enrich*" -or $_.Message -like "*token*" }
}
# Or wherever the Worker writes Serilog output
```

### Files

Read every job file end-to-end. For each:
- Where does it read the model name?
- Where does it read the batch size?
- Where does it choose what to enrich (staleness check)?
- Does it have a per-tick token budget?
- Does it persist costs/tokens-consumed per call?
- Does it retry on transient failures? With backoff?
- Does it deduplicate against in-flight work?

---

## Review dimensions

For each dimension below, identify Critical / Major / Minor issues
**with $/day estimated savings**.

### 1. Model selection consistency (M22 from prior audit — verify fix)

The prior audit (`docs/BD-Audit-2026-06-09.md` M22 area) flagged:
> "KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL read via options in one job
> and raw env var in two others — appsettings value applies
> inconsistently."

Verify each of the 9 jobs:
- Does it read the model name via `IOptions<EnrichmentOptions>`,
  `IConfiguration`, or direct `Environment.GetEnvironmentVariable`?
- If the env var is missing/empty/typo'd, what's the default?
  (A default of `claude-opus-4-7` would be a 5x cost increase silently.)
- Is `claude-haiku-4-5-20251001` being used uniformly?

**Critical** if any job silently defaults to Opus/Sonnet/Claude-3.5.

### 2. Batch sizing efficiency

`AwardAgentEnrichmentJob` has `BATCHSIZE=3` and runs every 10 min.
That's a lot of per-call overhead for Haiku (each call has fixed
setup/auth cost + tokens-per-call has a floor).

For each job:
- What's the batch size? Where's it read from?
- What's the per-call fixed cost vs marginal cost-per-item?
- Could batch sizes be 10-50x larger without breaking throughput?
- Is there a reason for small batches (e.g., per-call timeout?)

**Major** if batch size is <10 for high-volume jobs.

### 3. Staleness re-enrichment (wasted work)

Prior audit M11:
> "Ingest `NextRefreshAtUtc=+90d` vs generator's 30d staleness — two
> competing 'due' definitions."

Verify:
- Does each job filter to `NextRefreshAtUtc <= now`?
- Or does it filter by some other field (LastRefreshAtUtc < N days
  ago)?
- Are jobs re-enriching rows that were fresh-enriched yesterday?
- Run the duplicate-work SQL above and report waste.

**Major** for any job re-enriching >5% of fresh rows.

### 4. Retry + backoff cost

If a Haiku call returns an error (rate limit, timeout, malformed),
does the job:
- Retry immediately (token cost on each retry)?
- Apply exponential backoff?
- Mark the row failed and move on?
- Log enough to attribute retry cost?

**Major** if any job retries hot without backoff, or doesn't
distinguish transient vs permanent failures.

### 5. In-flight deduplication

If job A is processing MPI 6585 and the cron tick fires before A
finishes, does job B (next tick) also try MPI 6585?

Check:
- Is there a lock/lease on the row being processed?
- Does the job filter "not currently in flight"?
- What happens if a job crashes mid-batch?

**Critical** if double-spending on the same row is possible.

### 6. Tool-call overhead per operation

The model is Haiku, but every `web_search` / `web_fetch` Tool Use
call adds output tokens (the tool result is included in subsequent
context).

For each enrichment shape (ProjectBriefHoning, ProjectBrief,
PrimeConsultantResearch, FirmNarrative, IntelPerson):
- How many tool calls per item on average?
- Is there caching of common web fetches (e.g., BC Bid project pages
  searched repeatedly across projects)?
- Could a project be enriched with fewer tool calls?

**Major** if no caching of repeat fetches.

### 7. Schema-extraction overhead

Per `R94 CanonicalSchemaExtractor`, every enrichment runs through
the extractor. Is the extractor itself expensive? Does it call the
model again to canonicalize JSON?

Verify:
- Is the extractor pure code or does it invoke an LLM?
- Per-call cost?

### 8. Cost attribution

For an audit-ready answer to "where does the $50 go?", do the jobs
log:
- Tokens used (input + output) per call?
- Per-job daily token total?
- Per-MPI / per-org cost?

If not, **the $50 is unaccountable** — first fix is observability.

**Critical** if no per-job cost telemetry exists (can't optimize
what you can't measure).

### 9. Throttling + caps

Award job has `TOTALCAP=5000`. Is that being hit? If yes, the cap
might be the bottleneck not the budget.

For each job:
- Is there a daily cap?
- Is the cap hit?
- Is the cap the right shape (per-job vs total-spend)?

### 10. Job overlap / redundancy

`VendorSiteCrawl` + `VendorSiteExtraction` run independently —
do they duplicate work? Does `EnrichmentDispatchJob` re-do work
already done by sub-jobs?

Read the dispatcher and check for cross-job overlap.

### 11. Failed-row cost

If an enrichment fails halfway (model returned malformed JSON,
timeout, etc.) — were tokens spent? Is the partial result thrown
away or salvaged?

Check error-handling code paths for token waste.

### 12. Model env var fallback chain

Verify the **read chain** for the model env var. Likely:
1. `EnrichmentOptions.Model` from `appsettings.Production.json`
2. Override from `KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL` env var
3. Default fallback if both missing — **what is the default?**

If default is anything other than Haiku, that's the silent cost
inflation.

---

## Severity definitions

**Critical**: Active silent token waste, OR cost telemetry missing
(can't measure), OR Opus being used unintentionally, OR
double-spending on same row.

**Major**: Wasted work pattern (re-enriching fresh rows, oversmall
batches, no caching of repeat fetches, no backoff on retries) with
identifiable $/day savings.

**Minor**: Cleanups, log message improvements, dead code, naming.

For each Critical/Major, **estimate $/day savings**. Use last-7-day
volume data + Haiku pricing as the basis.

---

## Output format

```markdown
# Cron Cost Audit — <date>

## Summary
- $50/day current cost
- Estimated waste identified: $X.XX/day
- Estimated optimized cost: $Y.YY/day
- N Critical / N Major / N Minor

## Per-job cost attribution (best estimate)
| Job | Ops/day | Est. cost/day | Cost/op |
|---|---|---|---|
| AwardAgentEnrichment | XXX | $X.XX | $0.0XX |
| EnrichmentDispatchJob | XXX | $X.XX | $0.0XX |
| ... |

## Critical issues
### C1: <Short title>
**File / Job**: <path>
**Problem**: <what's wrong>
**Evidence**: <SQL row, code line, env var value>
**Estimated $/day savings if fixed**: $X.XX
**Recommended fix**: <one-line action>

## Major issues
### M1: ...

## Minor issues
### Mi1: ...

## Architectural concerns (not bugs)
- Pattern concern 1: ...

## Strengths worth preserving
- ...

## Pre-implementation gate
Items to fix BEFORE implementing any cost optimizations:
- ...

## Implementation order (highest $ savings first)
1. <Fix> — $X.XX/day savings
2. ...
```

---

## Constraints

These come from accumulated user feedback:

1. **Read-only DB + filesystem** during audit.
2. **No code changes** during audit — produce punch-list only.
3. **Don't break running cron** — every job is in production right
   now.
4. **Don't change model selection without explicit go-ahead** — Ian
   will decide if Haiku vs Sonnet vs Opus per job is right.
5. **Per `feedback_research_sessions_use_sonnet.md`**: standing rule
   is Sonnet (not Opus) for deep research. If any job is on Opus,
   that's a Critical finding.
6. **Don't second-guess data quality bars** — if a job requires N
   verification searches per item, that's intentional. Cost
   optimization only.
7. **Per `feedback_no_guessing.md`**: every claim verified from source
   data.
8. **Per `feedback_audit_before_proposing.md`**: grep for prior art
   before suggesting new instrumentation. If `JobScheduleStore` or
   similar already tracks per-job metrics, USE IT, don't add a
   duplicate.

---

## Anti-scope

- No code changes during audit (review-only)
- Don't touch WPF App, MCP, FileSync, PdfToSafe
- Don't touch the manual drain queues at
  `C:\ProgramData\KorOperations\QueueDrain\` — those are operator
  Sonnet sessions, not cron-scheduled jobs
- Don't recommend rebuilding any cron from scratch — work within
  existing job class structure
- Don't propose new Anthropic API key rotation or auth changes
- Don't lower data quality bars to save tokens

---

## Compounding-context memories to read

Prioritize reading these for the model-selection + cost-optimization
ground rules:

```
feedback_research_sessions_use_sonnet.md
feedback_top_one_percent_bar.md
feedback_no_guessing.md
feedback_audit_before_proposing.md
feedback_clean_at_source.md
feedback_dont_reinvent_deploy.md
project_business_development_module.md
project_opportunities_module.md
project_mcp_gateway_verified.md
project_alert_system_design.md
reference_kor_opportunities_env_var_naming.md
reference_kor_service_account.md
```

---

## Recent commit window

The 2026-06-09 BD audit closeout window (`docs/BD-Audit-2026-06-09.md`
+ commits since `4750858`) addressed many cron-adjacent fixes
(staleness contracts, env var consistency, etc.). Verify each
relevant fix is actually in place. M22 specifically was the model
env var inconsistency — confirm it was closed.

---

## Final step — required deliverable

In addition to the punch-list, produce a **one-paragraph verdict**:

> "Audit verdict: the $50/day cost breaks down as $X for high-value
> enrichment (ProjectBriefHoning, etc.) and $Y for waste
> (specific waste category). Recommended fixes save $Z/day at zero
> data quality risk. Implementation effort: H hours. Verdict:
> proceed / hold / blocked."

Be direct. No "Option A is faster" framing. State the verdict.
