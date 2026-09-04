# The BD worker — working rules for this module

Loads on top of the root `CLAUDE.md`. This service runs the ingestion crons, the research executor,
the dedup job and the morning report. **Stopping it stops all BD background processing**, so a
deploy is an outage, however short.

## Success does not mean the data is current

This is the defect class this service keeps producing, and it has cost months:

- **BC Major Projects Inventory** — BC Stats discontinued it (last issue Q3 2025, page removed
  30 June 2026). `BcMajorProjectsInventoryJob` kept running weekly, kept downloading the last
  surviving CSV (a frozen Q1-2025 snapshot), and kept recording `Success = true`. Two months passed.
- **`BidsTenders_Surrey`** — 102 runs, all successful, zero rows, ever.
- **GovCanada awards** — capped at 25,000 rows of a 71,408-record corpus and sorted by row-insertion
  id, so it re-read the same alphabetical slice forever and never ingested a federal award newer
  than 2022.

**An ingestion job must assert FRESHNESS, not fetch success.** `tools/BdIntegrityCheck` now carries
`source_went_silent`, `source_never_delivered_anything` and `source_everything_filtered_out` across
all enabled sources. Any new source you add is covered by them on day one — and is not "done" until
a deliberate silence would raise.

## Ingestion mechanics

- Per-source config is key/value in **`opportunities.OpportunitySourceMappings`**, not
  `OpportunitySources.ConfigJson` (NULL for the JSON providers).
- **Runs have a cancellation timeout.** Many small pages hit it; 120 pages × 1,000 rows × 1.5 s was
  cancelled at ~9 minutes, the same corpus in 8 pages of 10,000 was fine. **Few and large.**
- **An in-flight run looks exactly like a failed one** — `Success` stays 0 until the row is
  finalised. Filter `EndedAtUtc IS NOT NULL` before judging one. Reading a run at 7 minutes and
  calling it failed cost a cancelled sibling that had inserted 295 rows.
- Force a source to run by inserting into `opportunities.IngestionTriggers`
  (`OpportunitySourceId`, `RequestedBy`); the poller picks it up within ~15 s.

## Early signal: the ArcGIS adapter

`ArcGisFeatureOpportunityProvider` (SourceType 20) reads any ArcGIS Feature/Map Server layer, which
is what most BC municipalities publish development-permit and rezoning **applications** through. A
new city is an `OpportunitySources` row plus `arcgis.*` mappings — not a scraper.

- **Prove a city before you seed it.** `tools/ArcGisProbe --source <Name>` (or `--layer <url>
  --config <file>`) runs the adapter against the live layer without the Worker and prints the
  row→application collapse, the gate verdict for every row, and the newest kept ones.
- **One application is many rows.** These layers are spatial: a rezoning over nine parcels is nine
  features. Victoria's 258 rows are 146 applications. The adapter collapses them; do not undo that.
- **A "Development Permit AREA" layer is a zoning overlay, not applications.** It returns valid
  features and ingests cleanly as nonsense. Abbotsford's was rejected for exactly this.
- **Seed new sources `IsEnabled = 0`** and enable them after the Worker deploy. A source whose
  SourceType the running binary has no provider for just fails.

See `docs/codex/CODEX-EARLY-SIGNAL-ARCGIS-ADAPTER.md` — including the open finding that the shared
relevance gate is tuned for tender prose and drops planning prose (42 of Victoria's 146, 810 of
Maple Ridge's 849).

## The research path

- `BdResearchExecutorService` is reached two ways — the dossier Refresh button (via
  `BdResearchTriggers`) and the scheduled `BdResearchExecutorJob`. **Change both or neither.**
- Queue a refresh by inserting into `opportunities.BdResearchTriggers` (`CanonicalOrgId`,
  `ProviderName`, `RequestedBy`). No app click needed.
- **`BdResearchExecutor.Enabled` is `false`** in `appsettings.json`, with no production override.
  Leave it. If enabled it would rewrite ~3 orgs a night, quietly, most of them without a website
  anchor.
- Every prompt now opens with an **ENTITY ON FILE** block (website, aliases, affiliated people) and
  `ResearchIdentityGate` refuses the write when the researched entity disagrees. Do not remove the
  anchor to "simplify the prompt" — it is the only thing standing between a plausible paragraph and
  the destruction of a correct one.

## Scheduled jobs must be visible

Every Quartz job that writes or replays BD data belongs in `ScheduledJobDefinitions.All`, or it runs
invisibly to the admin registry. That has now happened **three times** — `KorMapSync`, the Audit-v2
batch, and (2026-09-03) `BdResearchExecutorJob` + `IntelExtractionCatchUpJob`. A job whose kill
switch lives in a different options class still belongs in the list; register it and let it
self-disable at run time.
