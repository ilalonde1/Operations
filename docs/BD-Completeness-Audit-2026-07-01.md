# BD Ingestion Completeness Audit (2026-07-01)

**Lens: silent omission** — every place the pipeline degrades by quietly *not ingesting* rather than erroring.
Motivated by the BcBid keyword incident (a "cosmetic" warning was silently dropping real opportunities).
Method: full code sweep of all 19 providers/scrapers + the relevance gate + scheduler, cross-checked against
7 days of live scrape telemetry (candidate-count histograms), run-history analysis, and validate-by-running
fixes with production trigger runs.

## Fixed & verified live (same day)

| Finding | Fix | Verification |
|---|---|---|
| **APC (Alberta) dead-but-green 18 days** — portal moved `/en/find` → 404 since ~June 13; 120 green runs with 0 candidates | BaseUrl → `/search` (config) | Live run: 10 candidates, **2 new Alberta building projects inserted** (incl. "Wellness Centre – New Building") |
| **Amendment-blind dedup hash** — hash was Title\|Buyer\|Location\|Url, so a deadline extension collided with the original observation and returned Duplicate before the refresh block: deadline changes NEVER picked up; opps auto-expired on stale dates | `SubmissionDeadlineUtc` joined the hash (commit `885efaa0`); re-observations flow through the idempotent key-matched refresh | Built, deployed `885efaa0`; formula change is self-healing (first re-observation refreshes each posting) |
| **BcBid 150-row constant (282/282 runs)** | maxPages 10→40 (config) + truncation telemetry: every run logs portal-reported pages; WARNs when maxPages caps below portal | Verdict run: **"portal reports 10 page(s); scraping all"** — 150 = everything the portal offers on the Open view; if it ever reports more we scale to 40 pages and warn beyond. *Residual: verify against the count shown in the BC Bid UI once, manually — Ivalua's hidden maxindex field could itself be capped* |
| BcBid keyword + date-header selectors (precursor session) | prefix header match; `input#body_x_txtQuery` | 0 warnings; keyword live; **10 missed opportunities recovered** |

## Diagnosed — needs decision or follow-up (no code fix tonight)

1. **Surrey/Vernon/Cochrane bids&tenders tenants are EMPTY-BY-DESIGN** — never produced in 122 runs; runs
   finish in ~2.4s via the legitimate "no open bid opportunities" placeholder. The tenants exist but appear
   unused: **Surrey almost certainly posts on its own portal.** Decision: find Surrey's real posting channel
   (and Vernon/Cochrane's) and add proper sources, or disable these to stop fake-green noise.
2. **APC pagination shallow after revival** — 10 candidates in 4.9s suggests page-1 only on the new layout
   (page-size bump + next-button selectors likely stale, and both fail silently — see systemic findings).
   Source is alive; a selector refresh like BcBid's would recover depth.
3. **BidsTendersAwards_\* pinned at 100/run** (award backfills) — cap binding on most streams; raise
   `playwright.maxPages` per source config if deeper award history is wanted.

## Systemic findings (agent sweep — full detail in the session agent report)

- **Six more scrapers can reproduce the BcBid incident today**: every Playwright scraper except SamGov caps
  pages with zero truncation logging (BcBidHistorical, BcBidAwards, BcBidUnverified, BidsAndTenders ×2, APC ×2).
- **`Success=true, Inserted=0` is the signature of both a healthy quiet source and a completely broken one**
  (`IngestionService.success = failed==0`) — that's how APC stayed dead-green for 18 days. No staleness
  watchdog exists ("source produced nothing in N days" alarm). **Top recommendation: add one** — a
  DataHealthAudit sentinel comparing each source's last-produced date against its historical cadence.
- **MPI providers always record 0 inserted in IngestionRuns** (they upsert internally, return empty) — broken
  and healthy MPI feeds produce identical run rows.
- **Filter degradation is best-effort everywhere**: BcBid keyword (fixed), Socrata retries WITHOUT its
  `$where` filter on HTTP 400 (scope silently broadens), APC/BidsTenders page-size bumps fail with no log
  (silent 10× scope cut), BcBid awards date-filter fill failure yields "0 records" as success.
- **Pagination interprets any anomaly as "done"**: bare-catch Next clicks (4 scrapers), tri-locator Next
  probes that a tenant skin update turns into single-page scrapes, `count < pageSize` treated as exhaustion
  (server-clamped `$limit` ends pagination after page 1).
- **BcBid hidden-field default**: `maxpageindex` read failure defaults to 0 → single-page scrape; now at
  least visible via the new pagination log line.
- **Relevance gate false-negative classes** (rejects are LOG-ONLY — nothing persisted for review):
  buildings at "always-irrelevant" facilities (WWTP admin *building*, substation control *building*);
  proper-noun collisions ("**Coal** Harbour Community Centre" hits `\bcoal\b`); prequalification/roster
  notices with no signal words; **French postings systematically rejected** (no French vocabulary);
  "Design-Build Services" literally matches no signal. Recommendation: persist gate rejects (source, title,
  reason) for periodic review, then tune.
- **GraphEmail**: single page of unread (Top-N, no nextLink), parse-failures never marked read (starves older
  mail), in-memory dedup resets on restart.
- **Scheduler**: sources with CrawlDelaySeconds 0/NULL are silently never queued; the name skip-list
  (CanadaBuys/SamGov/...) depends on Quartz jobs existing with no reconciliation.

## Recommended next moves (priority order)

1. **Source-staleness sentinel** (kills the whole dead-but-green class — would have caught APC on day 1).
2. Persist relevance-gate rejects + monthly review (measures the false-negative bleed).
3. Surrey (+ Vernon/Cochrane) real-portal sourcing decision.
4. APC depth refresh (page-size/next selectors).
5. Truncation logging in the remaining six scrapers (copy the BcBid pattern).
6. Socrata: mark run degraded when the `$where` filter is dropped.
