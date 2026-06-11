# Cron Cost Audit — 2026-06-10

> **Disposition addendum (2026-06-10, end of day — punch-list closed
> except M1):**
> - **C1 / M3 / Mi2**: closed-by-retirement — AwardAgentEnrichmentJob
>   disabled at three layers (see bottom addendum).
> - **C2**: G1-verified, implemented, deployed (1.0.9657.1107), and
>   live-verified — org 484 went from 196,895 flat input tokens to
>   1,252 uncached + 54,495 cache-read. See the G1 section.
> - **C3**: DONE (1.0.9657.1209) — the three executor jobs now set
>   `context.Result`, so JobRuns.Summary carries
>   considered/executed/ok/failed/inputTok/outputTok per run. First
>   populated rows land with the 2026-06-11 morning runs.
> - **M2**: DONE — `KOR_OPPORTUNITIES_BUILDINGPERMITSCRONSCHEDULE`
>   deleted on KOR-APP01; JobSchedules confirms permits back to daily
>   06:30 (was 288 runs/day).
> - Dead config `KOR_OPPORTUNITIES_DELTEKACCESSENABLED` (read by
>   nothing) deleted from KOR-APP01.
> - **M1** (per-executor Model options are dead config in
>   `AnthropicResearchExecutorService`): the only remaining open item.
>   $0 cost today; fix when per-executor model tiers are actually
>   wanted.
> - Also fixed same day (found during the audit's restart): the Deltek
>   capability probe never passed credentials — see the second bottom
>   addendum. KorPursuitDeltekSync + won-project signal refresh are live
>   for the first time.

Adversarial cost review of the Anthropic-billed enrichment jobs on
`Kor.Opportunities.Worker` (KOR-APP01). All numbers verified from source:
Serilog files at `C:\ProgramData\KorOperations\Opportunities\logs\`,
`opportunities.JobRuns` / `OpportunityAwards` / `MajorProjectEnrichment` /
`BdResearchTriggers`, live Machine env vars, and the Worker codebase at
commit `acbc263`.

Pricing basis: Sonnet 4.6 $3/M in, $15/M out; Haiku 4.5 $1/M in, $5/M out;
web_search $10/1,000 searches.

## Summary

- **$50/day observed ≈ $46/day attributed** (gap = estimate error on award
  input tokens + occasional manual "Refresh now" runs).
- **The prompt's framing was wrong in two load-bearing ways**, both verified:
  1. The ~1,340 ops/day in `MajorProjectEnrichment` are **mostly operator
     QueueDrain sessions** (bursty hourly write patterns, e.g. 540 rows in
     hour 0 of 06-09), not cron. Actual cron API calls ≈ **490/day**.
  2. The Haiku model env var (`AGENTENRICHMENTMODEL`) only governs the
     *cheap* jobs. **~$37/day (75%+) is three Sonnet 4.6 research executor
     jobs** the prompt didn't list (`BdResearchExecutorJob`,
     `BdProjectResearchExecutorJob`, `BdPersonResearchExecutorJob`) — Sonnet
     is policy-compliant per `feedback_research_sessions_use_sonnet.md`, but
     that's where the money goes.
- Estimated addressable waste: **~$21–27/day** ($16–22 uncached Sonnet input
  + ~$4.7 award-failure burn when that job resumes).
- Estimated optimized cost at identical volume/quality: **~$20–25/day**.
- **2 Critical / 5 Major / 3 Minor.**

## Per-job cost attribution (best estimate, avg of 06-07→06-09 logs)

| Job | Model | Ops/day | Tokens/day (in/out) | Searches/day | Est. $/day | $/op |
|---|---|---|---|---|---|---|
| BdResearchExecutorJob (FirmNarrative) | Sonnet 4.6 | 20 | 3.74M / 172K | ~166 | $15.50 | $0.77 |
| BdProjectResearchExecutorJob (ProjectBrief) | Sonnet 4.6 | 20 | 3.63M / 151K | ~163 | $14.80 | $0.74 |
| BdPersonResearchExecutorJob (PersonBrief) | Sonnet 4.6 | 20 | 1.72M / 71K | ~99 | $7.20 | $0.36 |
| AwardAgentEnrichmentJob | Haiku 4.5 | 430 calls (~215 net rows) | ~2.2M / 0.4M (est.) | ~430 | $8.40 | $0.020/call, $0.039/row |
| NewsMentionClassifyJob | Haiku 4.5 | 0 (nothing pending; 60 classified, 0 unclassified) | — | — | $0 | — |
| VendorSiteExtractionJob | Haiku 4.5 | 0 (TotalCap 500 reached: 500/592 extracted) | — | — | $0 | — |
| EnrichmentDispatchJob | none (BcRegistry HTTP only) | — | — | — | $0 | — |
| VendorSiteCrawl / BcBidHistorical / APC / permits / Deltek / queue-builder | none (Playwright/HTTP/SQL) | — | — | — | $0 | — |
| **Total** | | | **~9.5M / 0.8M** | **~860** | **~$45.9** | |

`AwardAgentEnrichmentJob` hit its `TOTALCAP=5000` at **2026-06-10 06:07**
(5,000 enriched / 131,251 not) and is now a no-op — today's run-rate is
already ~$37/day, not $50.

## Critical issues

### C1: AwardAgent burns 45–61% of its spend on discarded failures
**File / Job**: `Kor.Opportunities.Data\Awards\AwardAgentEnrichmentService.cs:194` (`max_tokens = 1024`)
**Problem**: Daily log aggregates show attempted=432 / failed=194 (06-07),
432/209 (06-08), 429/262 (06-09). Every failure in the last 7 days has the
same stored reason — `Agent returned no parseable JSON` (570 rows) — and 861
rows burned a **second** full attempt (`maxAttempts=2`). Tokens + the $0.01
web search are spent either way; the result is thrown away. Most likely
cause: `max_tokens: 1024` truncates the ~18-field JSON the system prompt
demands (response has no `stop_reason` check, so truncation is
indistinguishable from garbage).
**Evidence**: `OpportunityAwards.AgentLastError` group-by (570 ×
no-parseable-JSON, zero other reasons); log lines `Scheduled Award agent
enrichment: attempted=…`.
**Estimated $/day savings if fixed**: ~$4.70 of the job's $8.40 (applies
when the cap is raised and the job resumes; $0 while paused).
**Recommended fix**: raise `max_tokens` to 2048, log/persist `stop_reason`
and `usage` from the response, and only count a row "attempted" when the
model actually completed.

### C2: No prompt caching on the Sonnet web-search loops — the single biggest lever
**File / Job**: `Kor.Opportunities.Worker\Services\Research\AnthropicResearchExecutorService.cs:198-218` (search phase)
**Problem**: Each research item reports 86K–283K input tokens for 7–12 tool
calls — the classic uncached tool-loop context re-read, billed at full $3/M.
That's 9.1M input tokens/day = **$27/day of the $37 executor cost is input
tokens**. `Anthropic.SDK 5.5.1` supports `cache_control`; no breakpoint is
set on system/tools/messages.
**Evidence**: log lines `SearchInTok=196895 … ToolCalls=8` for a ~2K-token
prompt; daily sums above.
**Estimated $/day savings if fixed**: **$16–22/day** (cache reads at $0.30/M
on the re-read prefix; assumes intra-request caching applies to the
server-side web_search loop — see pre-implementation gate G1).
**Recommended fix**: add a cache breakpoint (system + tools) in
`SearchPhaseAsync`, then read back `cache_creation_input_tokens` /
`cache_read_input_tokens` from `Usage` on one instrumented run to confirm
the discount before declaring victory.

### C3 (observability, per the audit brief's severity definition): cron cost telemetry is not persisted
**File / Job**: the three executor jobs (`Kor.Opportunities.Worker\Jobs\Bd*ResearchExecutorJob.cs`)
**Problem**: `JobRuns.Summary` is **empty** for all three executor runs even
though the code already computes `BdResearchExecutorRunResult` with
input/output token totals. The only cost record is Serilog text (rolling
files), which is why this audit required log archaeology.
`BdResearchTriggers.InputTokens/OutputTokens` exists but only manual
refreshes write it (2 rows ever). `ApcInterestEnrichmentJob` proves the
pattern: it sets a result string and the existing JobRuns listener persists
it.
**Estimated $/day savings**: $0 directly; prerequisite for measuring every
other fix.
**Recommended fix**: have each executor job set `context.Result =
"n=20 ok=20 fail=0 inTok=3.7M outTok=172K"` so the existing
`JobRunLoggingListener` persists it. No new tables — prior art per
`feedback_audit_before_proposing.md`.

## Major issues

### M1: `BdProjectResearchExecutor:Model` / `BdPersonResearchExecutor:Model` are dead config (M22 recurrence)
**File**: `AnthropicResearchExecutorService.cs:100,201,266` — all three
executors share this singleton, which reads `Model`, `MaxOutputTokens`, and
`ApiKey` **only from `BdResearchExecutorOptions` (the org section)**.
Project/person options declare their own `Model`/`MaxOutputTokens` that are
never read. $0 today (all default Sonnet), but setting
`KOR_OPPORTUNITIES_BdPersonResearchExecutor__Model=claude-haiku-4-5…` would
silently do nothing — same class of bug the 06-09 audit's M22 fixed for the
Haiku jobs. **Fix**: thread the per-executor options (or Model) through
`ResearchTarget`, like `StructuredOutputJsonSchema` already is.

### M2: `BUILDINGPERMITSCRONSCHEDULE = 0 */5 * * * ?` — daily import running every 5 minutes
**Evidence**: env var on KOR-APP01 (default is `0 30 6 * * ?` daily);
`JobRuns` shows 575 runs in 2 days. Looks like a copy-paste of the BcBid
historical cron. No Anthropic cost, but it hammers the Vancouver Open Data
API 288×/day and churns the permit store. **Fix**: delete the env var (falls
back to daily 06:30). ~$0 API savings, real load/risk reduction.

### M3: Award cap hit silently — 131,251-row backlog needs a deliberate decision, not a raised cap
The job now logs "paused: cap reached" every 10 minutes. Clearing the full
backlog at current shape ≈ **$2,600 one-time** and 10 months at 430
calls/day. Before raising the cap: fix C1, then add an eligibility filter
(KOR-relevant geographies + recency + construction-adjacent categories) to
shrink the queue to the rows that can ever matter for BD. Decision is Ian's
per the no-model-changes constraint.

### M4: Two competing "due" definitions persist (M11 echo) — `NextRefreshAtUtc` is dead data on the executor paths
The chokepoint writes `NextRefreshAtUtc = now + StalenessDays` into the
enrichment tracking tables, but all three executors select candidates from
`Intel*` `LastSeenAtUtc` rollups instead; nothing reads `NextRefreshAtUtc`
(`DueNow=0` on all 4,635 rows). No double work observed (duplicate-refresh
SQL returned **0 wasted refreshes**, and the project executor's unscoped
rollup correctly defers to drain-freshened projects) — but the dead column
will mislead the next person who queries it. **Fix**: document or stop
writing it.

### M5: Staleness contracts are arithmetically unattainable — the executors are effectively "20 most-stale per day, forever"
Pools: 11,033 eligible orgs (21-day staleness ⇒ needs ~525/day; capacity
20), 7,320 people (90-day ⇒ needs ~81/day), 1,145 projects (60-day ⇒ needs
~19/day — this one is balanced). Orgs get re-researched roughly every **18
months**, not 21 days. Not a bug, but the $37/day is an open-ended
commitment whose coverage promise is fiction. **Fix options for Ian**: tier
the org pool (e.g. KorClient+Competitor+hotlist first), or accept and
re-document StalenessDays as a floor, not a contract.

## Minor issues

### Mi1: Zombie tickers
`VendorSiteExtractionJob` (cap 500 reached) and `VendorSiteCrawlJob` no-op
every 5/15 min forever; `NewsMentionClassifyJob` ticks every 5 min against
an empty queue, running a `COUNT(*)` each time. Negligible cost; disable via
env vars or leave.

### Mi2: Award cron env override (every 10 min) outran the code's own cost docs
`Program.cs:678` still documents "hourly … ~$2/day worst-case"; the env var
made it 6×/hour = 432 calls/day ≈ $8.4/day. Align the comment and the
env var when the cap decision lands.

### Mi3: Executor failure tokens aren't counted against the output-token budget
`BdResearchExecutorService.cs:108-109` adds tokens to the budget only on
success; a phase-2 failure spends ~$0.70 invisibly. Failure rate is
currently ~0 (60/60 succeeded on 06-09), so this is bookkeeping, not money.

## Architectural concerns (not bugs)

- The drain sessions and the cron executors write to the same
  `MajorProjectEnrichment` / `Intel*` tables with no marker distinguishing
  operator-driven from cron-driven rows. Cost attribution by table is
  impossible; only `RequestedBy`-style provenance (as `BdResearchTriggers`
  already has) would fix that.
- `EnrichmentDispatchJob`'s framework has exactly one provider (BcRegistry).
  Fine, but its batch-50/10-min cadence is sized for a fleet it doesn't have.

## Strengths worth preserving

- Per-call token logging in `AnthropicResearchExecutorService` — this audit
  was only possible because of it.
- Polly retry with `Retry-After` honoring + exponential backoff + jitter on
  every Anthropic HttpClient (`Program.cs:41-74`) — no hot-retry waste found.
- `[DisallowConcurrentExecution]` on all LLM jobs + offset cron minutes — no
  in-flight double-spend path found.
- Duplicate-refresh SQL: **zero** same-day re-enrichments in 7 days. The
  staleness scoping (org executor's FirmNarrative-scoped rollup, person
  executor's dedicated-PersonBrief contract) is doing its job.
- Hard caps (`TOTALCAP`) did exactly what they were designed to do: stopped
  runaway spend at $40 worth of Haiku.
- `DailyOutputTokenBudget` guard on all three executors.

## Pre-implementation gate

1. **G1 (before C2)**: ~~run ONE instrumented research call~~ **DONE
   2026-06-10 evening — CONFIRMED.** One production-shaped call
   (claude-sonnet-4-6, real shared/system.md + FirmNarrative/user.md,
   `web_search_20250305` max_uses=6, one `cache_control: ephemeral`
   breakpoint on the last system block, system padded above the
   2048-token Sonnet 4.6 caching minimum) returned, for 5 searches:
   `input_tokens=840`, `cache_creation_input_tokens=35,290`,
   `cache_read_input_tokens=61,982`. The server-side web_search loop
   caches intra-request AND covers the growing search context, not just
   the static prefix — 48% input-cost reduction at 5 iterations, scaling
   up with depth (production runs 8-12). Production today bills flat
   (`InputTokens=196K+` in the executor logs, no cache fields), so the
   breakpoint is the switch. **Revised C2 estimate: $14–17/day** (55-65%
   of the executors' ~$27/day input spend). Implementation notes:
   (a) Anthropic.SDK 5.5.1 must set cache_control on the system block in
   `AnthropicResearchExecutorService.SearchPhaseAsync`; (b) the current
   static prefix is only ~1.2K tokens — below the 2048 minimum, it
   silently won't cache — so the fix must also grow system+tools past
   2048 tokens with legitimately useful static reference content (field
   semantics / schema documentation); (c) verify with the same usage
   fields post-deploy.

   **C2 IMPLEMENTED + VERIFIED LIVE same evening (deploy 1.0.9657.1107,
   18:27 PT):** `PromptCaching = AutomaticToolsAndSystem` + ephemeral
   cache_control on the system block in
   `AnthropicResearchExecutorService.SearchPhaseAsync`; all three
   ResearchPrompts system.md files grew FIELD SEMANTICS / SEARCH
   STRATEGY / CONFIDENCE CALIBRATION appendices to clear the 2048-token
   minimum (count_tokens verified: 2,220 / 2,161 / 2,120 system-only);
   the success log line now carries SearchCacheWriteTok/SearchCacheReadTok,
   and trigger-row InputTokens reports total context (uncached + write +
   read). Live proof, org 484/FirmNarrative: 06-09 run billed
   SearchInTok=196,895 flat; post-deploy run logged SearchInTok=1,252,
   CacheWrite=49,053, CacheRead=54,495 (6 tool calls, 36s). First full
   measurement lands with tomorrow's 07:00/07:30/08:00 executor runs.
2. **G2 (before C1 matters)**: Ian decides the award-cap policy (M3). No
   point fixing the failure rate of a paused job unless it resumes.
3. **G3 (first PR)**: C3 telemetry — every later fix needs a before/after
   number in `JobRuns.Summary`.

## Implementation order (highest $ savings first)

1. **C2** — prompt caching on the Sonnet executors — **$16–22/day** (gated on G1; ~3–4h)
2. **C1** — award `max_tokens` 1024→2048 + `stop_reason` logging — **~$4.70/day when resumed** (~1h)
3. **C3** — persist run summaries to `JobRuns.Summary` — $0, enables measurement (~2h)
4. **M2** — delete `BUILDINGPERMITSCRONSCHEDULE` env var — $0 API, kills 287 junk runs/day (~5 min)
5. **M1** — thread per-executor Model through `ResearchTarget` — $0 today, closes the trap (~1–2h)
6. **M3/M5** — cap + pool-tiering decisions — Ian's call, savings depend on choice

---

## Audit verdict

The $50/day cost breaks down as **~$37.50 for the three Sonnet research
executors** (FirmNarrative $15.50 + ProjectBrief $14.80 + PersonBrief $7.20 —
high-value, policy-compliant per the standing Sonnet-for-research rule, but
running entirely uncached) and **~$8.40 for AwardAgentEnrichment**, of which
**~$4.70/day was pure waste** (45–61% of calls failed on unparseable JSON —
almost certainly `max_tokens=1024` truncation — and got retried at full
price); that job hit its 5,000-row cap at 06:07 today and now costs $0. The
cheap Haiku jobs the prompt worried about (news classify, vendor extraction)
have been idle for at least 5 days, and the "1,340 ops/day" figure conflates
operator drain sessions with cron work — real cron volume is ~490 API
calls/day. Recommended fixes (prompt caching on the executor loop, award
max_tokens bump, persisted token telemetry) save an estimated **$21–27/day
(~45–55%) at zero data-quality risk**, since none of them change models,
prompts, batch shapes, or verification bars. Implementation effort: **~8
hours** total, gated on one cheap caching-behavior verification call.
**Verdict: proceed** — C2 first after G1 verification, with C3 telemetry in
the same window so the savings are provable.

---

## Addendum — AwardAgentEnrichmentJob retired (2026-06-10 ~15:37 PT)

**Decision (Ian):** the per-award enrichment model is not worth resuming.
Rationale, all verified same-day: 73% of historical spend researched vendors
scored 0-2 (unrelated); the queue is per-award not per-vendor (131,251
backlog rows = 40,428 distinct vendors; Stantec alone appears 865 times, WSP
~1,334 across two spellings); old awards have no pursuit value; and vendor/
competitor research now lives in the canonical-org pipeline (FirmNarrative /
CompetitorProfile / ContractorResearch — 1,140 backlog vendors already
covered, 821 Competitor orgs in the daily Sonnet rotation).

**What the 5,000 enriched rows delivered (kept):** 651 competes_with_kor
flags, 288 direct competitors (score 7-10), feeding CompetitionInfo /
CompetitorProfile and vendor analytics. That data stays; nothing is deleted.

**Changes applied (ops only, no code):**
1. `opportunities.JobSchedules.Enabled = 0` for `AwardAgentEnrichmentJob` —
   user-authoritative flag, survives restarts, admin grid shows Disabled,
   `EnabledTriggerListener` vetoes scheduled fires (veto observed in the log
   at 15:37:00 before the restart).
2. KOR-APP01 Machine env: `KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTENABLED=false`
   (job-level guard — also covers manual `MT_` triggers, which bypass the
   veto listener).
3. KOR-APP01 Machine env: `KOR_OPPORTUNITIES_AWARDAGENTENRICHMENTCRONSCHEDULE`
   **deleted** — trigger reverts to the hourly default (`0 7 * * * ?`,
   confirmed in JobSchedules after restart), closing Mi2.
4. `BATCHSIZE=3` and `TOTALCAP=5000` env vars left in place as a third
   backstop if the job is ever re-enabled.
5. `Kor.Opportunities.Worker` restarted 15:37:16, Running, 22 schedules
   registered, `Enabled=0` preserved across the restart (UpsertScheduleAsync
   only writes Enabled on first INSERT — verified in code).

**Future shape, if award intel is ever wanted again** (design note, not a
commitment): new awards only (12-24 months, KOR geographies), enriched once
per distinct vendor via the canonical-org pipeline, award rows linking to
the vendor's CanonicalOrg.

**Side observation from the restart — investigated and FIXED same evening:**
the Deltek capability probe had logged `UNAVAILABLE — Insufficient
information to connect` on every Worker startup in retained history (back to
2026-05-12, i.e. it never worked). Root cause: `DeltekCapabilityProbe.TryPing`
opened bare `DSN=Deltek;` with no UID/PWD, but the DataDirect HDP 4.6 System
DSN carries no embedded LogonID — credentials live in the
`KOR_OPPORTUNITIES_DELTEKUSER/PASSWORD` env vars, which every real accessor
passes via `VpOdbcDsnFactory`. Proven side-by-side on the server: bare DSN
fails, DSN+env-creds opens and `SELECT 1` succeeds. Because Program.cs gates
DI on the probe, `NullKorWonProjectAccessor`/`NullKorPursuitDeltekAccessor`
were registered on every boot: `KorPursuitDeltekSyncJob` had synced **0
pursuits ever**, the nightly KOR-project signal refresh never ran (both
no-op'd safely — no data damage), and Worker-side scoring never had
won-history. `BdDeltekLinkDryRunJob` was unaffected (own options-based
connection since R63).

Fix deployed 2026-06-10 17:11 (FileVersion 1.0.9657.1029): probe now builds
DSN+UID+PWD from the bound startup options, mirroring `VpOdbcDsnFactory`.
Verified live: startup logs `Deltek capability: AVAILABLE` (first time ever);
Run-Now `KorPursuitDeltekSyncJob` synced **611 pursuits** (261 explicit +
350 promotional, 0 resolution failures, 2.9s). AwardAgentEnrichmentJob
confirmed still vetoed after the redeploy.

Also found: `KOR_OPPORTUNITIES_DELTEKACCESSENABLED=true` on KOR-APP01 maps
to no property anywhere in the codebase — dead config, candidate for
removal.
