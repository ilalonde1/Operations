# Round 14a  Adversarial Audit (codex pass, 2026-05-24)

## Summary
- Prior audit docs read: 0. `docs/Round14a-Audit.md` was created without reading any prior contents.
- Files reviewed: 301 tracked scoped files from `git ls-files` plus schema migrations 12-26, live SQL schema metadata, worker/app DI, tests, and memory.
- Critical: 3 | High: 8 | Medium: 8 | Low: 5
- Live schema checked against `KOR-APP01\SQLEXPRESS` / `KorOpportunitiesDb` via the worker environment connection string. The deployed schema currently matches migrations 12-26 for tables, columns, indexes, and FKs checked, but several migrations are not repair-safe after partial deployment.
- No committed hardcoded API keys/passwords were found in scoped worker appsettings or source; `Kor.Opportunities.Worker/appsettings.json:9` leaves `OpportunitiesDb` empty.

## Critical
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:54` | Canonical org rows are auto-created from any nonblank raw name, and the resolver is called from award names (`SqlOpportunityAwardStore.cs:189`), permit owner/applicant/contractor names (`VancouverOpenDataPermitAdapter.cs:110`), and AI-extracted news mentions (`NewsMentionClassifier.cs:131`). | This is graph-pollution/data-poisoning risk. Noisy permit text, scraped award strings, or hallucinated/ambiguous AI names become first-class canonical entities and aliases. | Split "resolve" from "create". Only auto-create for trusted/manual sources; stage external/AI names as unclassified aliases with validation, denylist/generic-name filters, confidence, and review before canonical creation. |
| `Kor.Opportunities.Data/Ingestion/Providers/GraphEmailOpportunityProvider.cs:112` | `ProcessedMessageIds` is a static in-memory cache and message IDs are added before best-effort mark-read/move operations (`GraphEmailOpportunityProvider.cs:149`, `GraphEmailOpportunityProvider.cs:156`). The mark/move helpers swallow failures after logging (`GraphEmailOpportunityProvider.cs:296`, `GraphEmailOpportunityProvider.cs:325`). | A parsed email whose Graph write fails can remain unread/unmoved but be skipped forever by this process. The static dictionary also grows until restart. That is a silent business-data drop path. | Persist processed state after durable success, or remove the ID when mark/move fails. Treat mark/move failure as a failed ingestion result, and use a bounded TTL cache only as a duplicate guard. |
| `Kor.Opportunities.Data/Awards/SqlOpportunityAwardStore.cs:125` | Three write paths use SQL Server `MERGE` without `HOLDLOCK`/transaction protection: awards (`SqlOpportunityAwardStore.cs:125`), pursuits (`SqlKorPursuitStore.cs:85`), and permits (`SqlBuildingPermitStore.cs:60`). | Concurrent scheduled/manual runs against the same natural key can hit SQL Server MERGE race behavior, unique-key exceptions, or partial state; canonical linking is done after award upsert, widening the inconsistency window. | Replace MERGE with `UPDATE ... WITH (UPDLOCK, HOLDLOCK); IF @@ROWCOUNT = 0 INSERT ...` inside a transaction, or add target `WITH (HOLDLOCK)` and handle duplicate-key retries explicitly. |

## High
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs:33` | `SqlOpportunityStore` is registered without `CanonicalOrgResolver` in the app, and the worker does the same (`Kor.Opportunities.Worker/Program.cs:66`). `SqlOpportunityStore` only resolves buyers when `_canonicalResolver` is non-null (`SqlOpportunityStore.cs:205`). | `BuyerCanonicalOrgId` exists in deployed schema, but active opportunity inserts/updates never populate it. The code silently loses buyer graph links for the core opportunities table. | Register `SqlOpportunityStore` with `CanonicalOrgResolver` where intended, or remove the optional resolver path and make canonical linking an explicit service with visible failure handling. |
| `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonOpportunityProvider.cs:86` | Generic JSON ingestion parses the full response document and then materializes all resolved items with `.ToList()` (`GenericJsonOpportunityProvider.cs:93`). | A large or poisoned source response can OOM the worker before the ingestion service gets a chance to dedupe or cap work. | Enforce content-length limits, streaming item parsing, and a per-source `MaxItemsPerRun` before materialization. |
| `Kor.Opportunities.Data/Ingestion/Providers/GenericCsvOpportunityProvider.cs:48` | Generic CSV ingestion reads the entire body into a string and parses all records (`GenericCsvOpportunityProvider.cs:55`) with no row or byte cap. | A large CSV endpoint can exhaust memory and block the worker. | Stream rows, reject oversized bodies, and stop after a configured per-source maximum. |
| `Kor.Opportunities.Data/Awards/VancouverOpenDataPermitAdapter.cs:42` | Vancouver permits import reads the whole export, parses the whole JSON array (`VancouverOpenDataPermitAdapter.cs:48`), and loops every row (`VancouverOpenDataPermitAdapter.cs:61`) without a total cap. | The endpoint is an open-data export, not a bounded page API. Growth or upstream shape changes can turn the daily import into an unbounded memory/run-time job. | Use the API paging/query endpoint, enforce max rows per run, and persist a high-water mark. |
| `Kor.Opportunities.Data/Awards/NewsFeedPollService.cs:53` | News polling reads each full RSS/Atom body to a string, parses all items (`NewsFeedPollService.cs:54`), and inserts every item (`NewsFeedPollService.cs:57`) with no feed-level item cap. | A large or malformed feed can pull an unbounded number of articles and memory into one Quartz run. | Add feed byte limits, item limits, and incremental polling based on known GUID/date cutoffs. |
| `Kor.Opportunities.Worker/Services/AwardAgentEnrichmentJob.cs:41` | Anthropic spend caps are checked only before taking a batch in award enrichment; the job then processes the full batch (`AwardAgentEnrichmentJob.cs:54`). The same pre-batch-only pattern exists for vendor extraction (`VendorSiteExtractionJob.cs:37`) and news classification (`NewsMentionClassifyJob.cs:40`). | A run can overshoot `TotalCap` by up to `BatchSize` every time it starts just below the cap. | Compute remaining allowance and clamp the batch size before each expensive call; re-check inside the per-row loop after each successful chargeable unit. |
| `Kor.Opportunities.Worker/Program.cs:204` | Most HTTP clients are registered without retry/backoff; only GraphEmail has a Polly retry policy (`Program.cs:281`). Anthropic calls also use raw `PostAsync` (`AwardAgentEnrichmentService.cs:209`, `VendorSiteExtractionService.cs:177`, `NewsMentionClassifier.cs:217`). | Transient upstream failures become failed rows/runs, and expensive AI work gets no standard retry/jitter policy. | Add scoped retry/backoff policies per provider type, with no retry for hard 4xx and bounded retries for 408/429/5xx/network failures. |
| `Kor.Opportunities.Worker/Services/IngestionTriggerPollerBackgroundService.cs:90` | The trigger poller drains pending triggers in an unbounded loop and, on shutdown, leaves an in-progress trigger for manual reset (`IngestionTriggerPollerBackgroundService.cs:152`). The store only claims `Status = 'Pending'` (`SqlIngestionTriggerStore.cs:65`). | A burst of manual triggers can monopolize the worker. A host shutdown mid-run can strand a trigger in `InProgress` indefinitely. | Add a max triggers per wake, stale `InProgress` reclaim/lease expiry, and a shutdown path that marks abandoned work as retryable when safe. |

## Medium
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Opportunities.Data/Opportunities/SqlOpportunityStore.cs:55` | `ListAsync` returns all active opportunities ordered by update time with no `TOP`, paging, or date filter. | UI or service callers can load the entire active opportunity table into memory as data grows. | Add paging/query filters and make the unbounded path private/test-only or remove it. |
| `Kor.Opportunities.Data/Awards/SqlAwardQueryStore.cs:57` | Award and competition query stores accept caller-supplied `MaxRows` with large defaults and no clamp (`SqlCompetitionInfoQueryStore.cs:81`). | A bad UI option or future caller can request an excessive result set and pressure SQL/app memory. | Clamp max rows centrally and expose paging for broad analytics screens. |
| `Kor.Opportunities.Data/Opportunities/SqlOpportunityStore.cs:220` | Canonical resolution failures are swallowed in `SqlOpportunityStore`; award canonical linking also catches all exceptions (`SqlOpportunityAwardStore.cs:208`). | Canonical-link failures become invisible data-quality gaps. Because the active store resolver is currently not wired, this also hides that the path is effectively disabled. | Log structured warnings with source key/name, surface counters, and persist retryable resolution failures. |
| `Kor.Opportunities.Data/Awards/AwardAgentEnrichmentService.cs:162` | Failure-recording failures are swallowed in award enrichment; vendor extraction has the same nested empty catch (`VendorSiteExtractionService.cs:128`), and news classification swallows mark-failed failures (`NewsMentionClassifier.cs:173`). | If a DB write fails while recording an upstream/AI failure, rows can remain eligible for repeated attempts without a trustworthy audit trail. | Log the secondary failure and add idempotent retry/dead-letter tracking for failure-record writes. |
| `Kor.Opportunities.Data/Schema/12_AwardAgentEnrichment.sql:7` | Migrations add groups of sibling columns under a single "first column exists" guard. Migration 18 does the same for extraction columns (`18_VendorSiteCrawl.sql:32`). | Deployed schema currently matches, but a partial deployment would not self-repair missing sibling columns on rerun. This explains why migration files can drift from actual DB history. | Make every column/index/constraint independently idempotent, or add a schema verifier migration that repairs missing siblings. |
| `Kor.Opportunities.Data/Schema/25_NewsAggregator.sql:9` | Table-level idempotency for news tables means missing indexes/FKs are not repaired if the table already exists; permits use the same pattern (`26_BuildingPermits.sql:26`). | A partially deployed table can look "done" to the migration while lacking constraints or indexes. | Guard each index/FK/column separately, not only the table. |
| `Kor.Opportunities.Data/Schema/20_KorPursuits.sql:10` | `KorPursuits.Stage` and `OurRole` are enum-like strings in code (`KorPursuit.cs:6`, `KorPursuit.cs:22`), but the schema has no CHECK constraints. | Invalid stage/role strings can enter the DB through imports or manual SQL and break filters/reporting. | Add CHECK constraints or a lookup table matching `PursuitStages.All` / `KorRoles.All`. |
| `Kor.Opportunities.Data/Awards/SqlNewsStore.cs:162` | News mentions are unique by article/org and existing rows are overwritten/merged (`SqlNewsStore.cs:171`). | If one article mentions the same org for multiple events, later mention types collapse into one row; downstream intelligence loses multiplicity. | Include mention type or extracted event key in the uniqueness model, or store mention events separately from article/org rollups. |

## Low
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs:73` | `HistoricalOpportunityDetailViewModel` is registered twice, again at `OpportunitiesModule.cs:75`. | Harmless at runtime, but it is DI drift and makes registration reviews noisier. | Remove the duplicate registration. |
| `Kor.Opportunities.Data/Schema/26_BuildingPermits.sql:85` | Seed name contains a double space: `City of Vancouver  issued-building-permits`. | Naming drift leaks into deployed data and admin UI. | Normalize the seed name and update any dependent references. |
| `Kor.Opportunities.Data/Schema/25_NewsAggregator.sql:4` | Migration comments say mentions are populated by "12b classifier" and `ClassifiedAtUtc` is "set by 12b" (`25_NewsAggregator.sql:42`). | Stale round labels slow future audits and can send maintainers to the wrong implementation history. | Update comments to current job/service names. |
| `Kor.Operations.App/Opportunities/CompetitionInfoWindow.xaml.cs:23` | Several UI windows silently swallow header load failures; examples include competition (`CompetitionInfoWindow.xaml.cs:23`), competitor profile (`CompetitorProfileWindow.xaml.cs:22`), and historical detail (`HistoricalOpportunityDetailWindow.xaml.cs:22`). | Low severity because the header is cosmetic, but silent catches make UI startup issues harder to diagnose. | Log at debug/trace or centralize a safe header loader that records one diagnostic event. |
| `Kor.Opportunities.Worker/Services/HeartbeatBackgroundService.cs:47` | Worker startup and DB ping log at Information; Quartz default logging is also Information in appsettings (`appsettings.json:6`). | Not wrong, but the subsystem already has many frequent batch jobs; high-frequency Info logs make operational noise more likely. | Keep startup Info, but review per-run batch summaries for Debug where dashboards already expose counts. |

## Cross-cutting findings
### Quartz cron table
| Job | Cron | Notes |
| --- | --- | --- |
| AwardAgentEnrichment | `0 7 * * * ?` (`Program.cs:326`) | Collides with VendorSiteExtraction at minute `:07`. |
| VendorSiteCrawl | `0 5/15 * * * ?` (`Program.cs:341`) | Collides with BcBidHistoricalEnrichment at `:05/:20/:35/:50`. |
| VendorSiteExtraction | `0 2/5 * * * ?` (`Program.cs:355`) | Collides with BcBidHistoricalDocumentDownload at `:02/:12/:22/:32/:42/:52`, and with NewsFeedPoll at `:12/:42`. |
| EnrichmentDispatch | `0 9/10 * * * ?` (`Program.cs:369`) | No default same-minute collision found. |
| NewsFeedPoll | `0 12/30 * * * ?` (`Program.cs:382`) | Collides with VendorSiteExtraction and BcBidHistoricalDocumentDownload at `:12/:42`. |
| NewsMentionClassify | `0 3/5 * * * ?` (`Program.cs:394`) | No default same-minute collision with listed fixed schedules. |
| BuildingPermitsImport | `0 0 6 * * ?` (`Program.cs:406`) | Collides with SamGov at 06:00, plus GraphEmail and BcBidHistoricalEnrichment at minute `:00`. |
| BcBidHistoricalDocumentDownload | `0 2/10 * * * ?` (`Program.cs:419`) | Collides with VendorSiteExtraction; also with NewsFeedPoll at `:12/:42`. |
| CanadaBuys | `0 0 0/2 * * ?` (`Program.cs:432`) | Collides with GraphEmail and BcBidHistoricalEnrichment at even-hour `:00`. |
| CanadaBuysNew | `0 15 0/2 * * ?` (`Program.cs:445`) | Collides with GraphEmail and BcBidHistoricalEnrichment at even-hour `:15`. |
| SamGov | `0 0 6 * * ?` (`Program.cs:456`) | Collides with BuildingPermitsImport at 06:00, plus GraphEmail and BcBidHistoricalEnrichment. |
| GraphEmail | `0 0/15 * * * ?` (`Program.cs:467`) | Collides with all `:00/:15/:30/:45` schedules. |
| BcBidHistoricalEnrichment | `0 */5 * * * ?` (`Program.cs:480`) | Collides with many jobs by design because it runs every 5 minutes. |

### Cap / flag inventory
| Service | Cap? | Enforced inside batch? |
| --- | --- | --- |
| AwardAgentEnrichment | `Enabled`, `BatchSize`, `MaxAttempts`, `TotalCap` (`OpportunitiesWorkerOptions.cs:65`, `:80`, `:83`, `:109`) | No; pre-batch check only (`AwardAgentEnrichmentJob.cs:41`). |
| VendorSiteCrawl | `Enabled`, `BatchSize`, `MaxAttempts`, `TotalCap` (`OpportunitiesWorkerOptions.cs:86`) | No; pre-batch check only (`VendorSiteCrawlJob.cs:37`). |
| VendorSiteExtraction | `Enabled`, `BatchSize`, `MaxAttempts`, `TotalCap` (`OpportunitiesWorkerOptions.cs:93`) | No; pre-batch check only (`VendorSiteExtractionJob.cs:37`). |
| EnrichmentDispatch | `Enabled`, `BatchSize`, cron (`OpportunitiesWorkerOptions.cs:100`) | No total cap or max attempts at job level (`EnrichmentDispatchJob.cs:34`). |
| NewsFeedPoll | `Enabled`, cron (`OpportunitiesWorkerOptions.cs:150`) | No batch or total cap; polls all active feeds (`NewsFeedPollService.cs:31`). |
| NewsClassification | `Enabled`, `BatchSize`, `TotalCap` (`OpportunitiesWorkerOptions.cs:154`) | No; pre-batch check only (`NewsMentionClassifyJob.cs:40`). |
| BuildingPermitsImport | `Enabled`, cron (`OpportunitiesWorkerOptions.cs:160`) | No batch/total cap; imports all active sources (`BuildingPermitsImportJob.cs:38`). |
| BcBidHistoricalDocumentDownload | `BatchSize`, `MaxAttempts`, archive root (`OpportunitiesWorkerOptions.cs:52`) | Batch/max attempts only (`BcBidHistoricalDocumentDownloadJob.cs:32`). |
| GraphEmail | `MaxEmailsPerRun`, `Smoke`, cron (`OpportunitiesWorkerOptions.cs:133`) | Max emails is clamped in provider; no durable processed-state cap (`GraphEmailOpportunityProvider.cs:84`). |
| SamGov | API key, lookback, cron (`OpportunitiesWorkerOptions.cs:20`, `:29`) | Hard-coded `MaxPages = 5`, `PageLimit = 1000` in provider; no retry/backoff. |

### Schema drift
| Migration | File matches DB? | Notes |
| --- | --- | --- |
| 12 AwardAgentEnrichment | Yes | Columns/indexes present. Partial-column guard at line 7 can miss sibling repair. |
| 13 AwardAgentCompanyProfile | Yes | Deployed `AgentVendor*` profile columns present. Same grouped-column idempotency risk. |
| 14 AwardAgentKorOverlapScore | Yes | Deployed overlap score column present. |
| 15 AwardAgentContractProjectType | Yes | Deployed contract project type column present. |
| 16 AwardAgentEnrichmentAttempts | Yes | Deployed attempt/error columns present. |
| 17 VendorWebsite | Yes | Deployed vendor website column present. |
| 18 VendorSiteCrawl | Yes | Table/indexes and award extraction columns present; table/first-column guards are not repair-safe. |
| 19 CanonicalOrg | Yes | Canonical org, alias, normalized-name index present. |
| 20 KorPursuits | Yes | Table/indexes/FKs present; schema lacks CHECK constraints for enum-like strings. |
| 21 CanonicalOrgEnrichment | Yes | Enrichment table/indexes and BC registry columns present. |
| 22 AwardCanonicalLinks | Yes | Award canonical FK columns/indexes/FKs present. |
| 23 BcRegistryTopicId | Yes | BC registry topic column/index present. |
| 24 KorPursuitExternalKeys | Yes | External key columns/unique index present. |
| 25 NewsAggregator | Yes | News tables/indexes/FKs present; stale "12b" comments and table-level guard risk. |
| 26 BuildingPermits | Yes | Permit tables/indexes/FKs present; table-level guard risk and double-space seed name. |

### DI completeness
| Interface | Worker registered? | App registered? | Lifetime |
| --- | --- | --- | --- |
| `IHeartbeatStore` | Yes (`Program.cs:65`) | Yes (`OpportunitiesModule.cs:32`) | Singleton |
| `IOpportunityStore` | Yes (`Program.cs:66`) | Yes (`OpportunitiesModule.cs:33`) | Singleton; resolver not wired |
| `IOpportunitySourceStore` / `IOpportunityObservationStore` | Yes (`Program.cs:67`) | Yes (`OpportunitiesModule.cs:34`) | Singleton |
| Historical opportunity stores | Yes (`Program.cs:69`) | Yes (`OpportunitiesModule.cs:35`) | Singleton |
| `IIngestionRunStore` / `IIngestionTriggerStore` | Yes (`Program.cs:73`) | Yes (`OpportunitiesModule.cs:39`) | Singleton |
| `ICanonicalOrgStore` / `CanonicalOrgResolver` | Yes (`Program.cs:108`) | Yes (`OpportunitiesModule.cs:65`, `:70`) | Singleton |
| `IOpportunityAwardStore` | Yes (`Program.cs:100`) | No write store; app uses query stores | Singleton |
| `IAwardQueryStore` / analytics/query stores | No | Yes (`OpportunitiesModule.cs:52`, `:55`) | Singleton |
| `INewsStore`, `IBuildingPermitStore`, `IVendorSiteCrawlStore` | Yes (`Program.cs:122`, `:154`, `:105`) | No | Singleton |
| `IEnrichmentProvider` | Yes for BC Registry (`Program.cs:119`) | No | HttpClient/typed client |
| `IOpportunityProvider` / `IAwardProvider` | Worker only (`Program.cs:204`, `:233`) | No | Singleton/typed client |
| `IDeltekClientFactsAccessor` | Null accessor (`Program.cs:191`) | Real app accessor (`OpportunitiesModule.cs:46`) | Singleton |

### Test coverage gaps
The test project is `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj`, with assembly name `Kor.Operations.App.Tests` (`Kor.Operations.App.Tests.csproj:8`) and references to app/core/data projects (`Kor.Operations.App.Tests.csproj:35`). It includes parser/static-analysis coverage such as CivicInfo URL parsing (`CivicInfoHtmlOpportunityProviderTests.cs:11`), RSS parsing (`RssOpportunityProviderTests.cs:11`), JSON dot-path tests, CSV filter tests, and email adapter tests.

Missing coverage for the audited risks:
- Canonical-org creation policy and graph-pollution defenses.
- SQL-store concurrency/upsert behavior for award, pursuit, and permit natural keys.
- GraphEmail mark-read/move failure with static processed ID behavior.
- Anthropic cap enforcement inside batches.
- Cron collision regression tests.
- Live schema verifier tests for migrations 12-26.
- Building permits, news classifier, vendor crawl/extraction, and trigger lease/reclaim behavior.

### Dead code candidates
- No old BC Registry `/formatted` detail call remains in `BcRegistryProvider`; the current code explicitly relies on v4 search results (`BcRegistryProvider.cs:75`).
- `HistoricalOpportunityDetailViewModel` duplicate DI registration in `OpportunitiesModule.cs:73` / `:75`.
- Several "methodology emission removed" comments remain in app view models, but they are comments rather than live dead code paths.

### Memory entries that are stale
- `feedback_no_code_changes_until_explicit_go_ahead` is superseded for this task by the explicit instruction to create and `git add` `docs/Round14a-Audit.md`.
- `project_opportunities_module.md` says phases 1-5 plus provider ports/redesign are live as of 2026-05-16; it is now incomplete because later work added vendor site crawl/extraction, enrichment providers, news aggregation, building permits, and Round 12/13-era jobs.
- `project_bd_module_deferred_work.md` still mentions retry/backoff as deferred. That remains true for most providers; GraphEmail now has Polly retry while most HTTP/Anthropic paths still do not.
- `reference_test_csproj_path.md`, `reference_kor_opportunities_sql_migration_db_context.md`, `reference_opportunities_deploy_runbook.md`, and `reference_kor_opportunities_env_var_naming.md` remain consistent with this audit.
