# Round 15a  Adversarial Audit (codex pass, 2026-05-23)

## Summary
- Prior audit docs read: `docs/Round14a-Audit.md` and `docs/Round14c-Prompt.md`.
- Commits reviewed: `03ad135` Round 14b, `6d0333e` Round 14c migration/prompt, `96865c0` Round 14c source, `37fef63` Round 14d. `git log --oneline 03ad135..37fef63` was also reviewed.
- Files reviewed: 31 source/schema/doc files from the mandatory docs, `git show --stat`, targeted `rg`, and line-numbered reads of Round 14 changes plus five extra areas: scoring, Graph email ingestion, historical backfill, award ingestion, WPF opportunities view models, and enrichment dispatch.
- Critical: 1 | High: 3 | Medium: 4 | Low: 0

## Critical
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Opportunities.Data/Ingestion/Providers/GraphEmailOpportunityProvider.cs:140` | Graph email still acknowledges mailbox state before durable ingestion. The provider adds the parsed candidate to the in-memory return list, then marks read or moves the message at `GraphEmailOpportunityProvider.cs:151`/`:164`, records the message id at `GraphEmailOpportunityProvider.cs:171`, and only later returns candidates to `IngestionService` at `GraphEmailOpportunityProvider.cs:209`. Actual DB persistence does not start until `IngestionService.cs:122`, with insert/update at `IngestionService.cs:237`/`:272`. | If observation/opportunity persistence fails after `FetchAsync` returns, the email has already been moved/marked and cached as processed. That is a silent business-opportunity drop path. Round 14b fixed the mark/move failure ordering, but the destructive mailbox ack is still earlier than the durable DB commit. | Move Graph ack out of `FetchAsync`: return an ack handle/message id with each candidate and mark/move only after `ProcessCandidateAsync` succeeds. If that is too large, Graph provider should keep messages unread/unmoved unless ingestion can synchronously confirm persistence. |

## High
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Opportunities.Data/Ingestion/SqlIngestionTriggerStore.cs:69` | Stale trigger reclaim has no lease fencing. Any `InProgress` trigger older than 15 minutes is claimable at `SqlIngestionTriggerStore.cs:69`, but long-running award or generic ingestion can legitimately still be active (`IngestionTriggerPollerBackgroundService.cs:135`, `:150`). Completion then updates by id only at `SqlIngestionTriggerStore.cs:96`, with no `ClaimedBy`, claim token, status, or `ReclaimedCount` guard. | A slow original worker and a reclaiming worker can process the same manual trigger concurrently. Either worker can later overwrite the terminal state and `IngestionRunId`, making the audit trail unreliable and allowing duplicate ingestion side effects. | Add a claim token/lease version returned by `ClaimNextPendingAsync`, pass it into `CompleteAsync`, and update with `WHERE Id = @id AND ClaimToken = @token AND Status = 'InProgress'`. Add heartbeat/lease renewal for legitimately long runs or use a stale window above the maximum expected run time. |
| `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:107` | Trusted-source canonical creation can duplicate CanonicalOrg rows under concurrency. The resolver first does a normalized lookup at `CanonicalOrgResolver.cs:107`, then creates at `CanonicalOrgResolver.cs:123` when no row is found. The store MERGE only matches on non-null `ClendorClientId` (`SqlCanonicalOrgStore.cs:33`-`:35`), so `clendorClientId = null` always falls into insert at `SqlCanonicalOrgStore.cs:42`. The normalized-name index is non-unique (`22_OpportunityCanonicalLinks.sql:24`-`:29`). | Two simultaneous trusted imports for the same new buyer/vendor/permit owner can both miss the normalized lookup and insert separate canonical rows with the same normalized name. That pollutes the master graph even after the Round 14b source-gating fix. | Make normalized-name creation concurrency-safe: add a unique filtered/normalized key where acceptable, or perform lookup plus insert in a transaction using `UPDLOCK, HOLDLOCK` on the normalized-name access path and retry duplicate-key races. |
| `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs:407` | Selected Deltek intelligence can race across WPF selection changes. `LoadSelectedIntelligenceAsync` reads `_selected?.Model.DeltekClientId` at `OpportunitiesViewModel.cs:407`, awaits the load at `OpportunitiesViewModel.cs:416`, and assigns `_selectedIntelligence` at `OpportunitiesViewModel.cs:416`-`:418` without checking that the same opportunity is still selected. `BuildLocalContext` later appends whatever `_selectedIntelligence` contains to the currently selected opportunity at `OpportunitiesViewModel.cs:1033`. | A slow load for opportunity A can complete after the user selected opportunity B, causing AI local context for B to include A's Deltek client intelligence. That is cross-client context leakage and can poison generated guidance. | Capture the selected opportunity id and client id before the await; after the await, assign only if `_selected?.Model.Id` and `DeltekClientId` still match. Clear `_selectedIntelligence` on mismatch. |

## Medium
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |
| `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonOpportunityProvider.cs:97` | The new byte caps are enforced after full response allocation. Generic JSON reads the whole body at `GenericJsonOpportunityProvider.cs:97` before checking length at `:98`; CSV does the same at `GenericCsvOpportunityProvider.cs:56`-`:57`, Vancouver permits at `VancouverOpenDataPermitAdapter.cs:55`-`:56`, and RSS at `NewsFeedPollService.cs:62`-`:63`. | The Round 14b cap limits parsing and downstream work, but it does not protect the worker from a response large enough to allocate a huge string before the check runs. | Check `Content-Length` before reading when present, then stream through a capped reader/copy loop that aborts as soon as bytes exceed `IngestionMaxBytesPerResponse`. |
| `Kor.Opportunities.Data/Scoring/SqlScoringProfileStore.cs:74` | Scoring profile save still uses `MERGE` without target `HOLDLOCK` or the safer update-then-insert pattern. | This is lower cadence than ingestion, but two app instances or two save actions can still race on the single `ProfileKey = 'Default'` row and hit SQL Server MERGE race behavior. The scoring profile affects ranking across all opportunities. | Replace with `UPDATE opportunities.ScoringProfile WITH (UPDLOCK, HOLDLOCK) ...; IF @@ROWCOUNT = 0 INSERT ...` in a transaction, preserving the fixed `ProfileKey`. |
| `Kor.Opportunities.Core/Scoring/RuleBasedOpportunityScoringService.cs:155` | Scoring term matches are raw substring checks. Defaults include short tokens such as `BC` at `ScoringOptions.cs:140`, and all weighted terms use `text.Contains(...)` at `RuleBasedOpportunityScoringService.cs:155`. | Short acronyms can match inside unrelated words or identifiers, inflating relevance scores and explanations. This is data-quality risk for the BD ranking UI, not just cosmetic labeling. | Tokenize text and require word boundaries for short terms/acronyms; keep phrase matching for multi-word terms. Make substring matching explicit per term if needed. |
| `Kor.Opportunities.Worker/Program.cs:31` | The shared retry policy retries HTTP 429 but ignores `Retry-After` and has no retry logging. The policy uses a fixed exponential delay plus jitter at `Program.cs:36`-`:38` and is applied to Anthropic-backed services such as award enrichment (`Program.cs:89`-`:93`) and news classification (`Program.cs:161`-`:165`). | On rate limiting, the worker may retry too early and turn one throttle event into repeated failures. Operators also cannot see whether a slow/failing tick is first attempt latency or retry backoff. | Use a 429-aware policy that honors `Retry-After` when present and add `onRetry` structured logging with service name, status code, attempt, and delay. |

## Low
| File:line | Issue | Why it matters | Suggested fix |
| --- | --- | --- | --- |

## Cross-cutting findings

### Round 14a Fix Re-Verification
| Round 14a item | Status | Evidence |
| --- | --- | --- |
| Critical #1 canonical-org creation from untrusted sources | Fixed for news source gating; residual concurrency issue above | `CanonicalOrgResolver.cs:115` blocks create when `allowCreate=false`; `NewsMentionClassifier.cs:135` passes `allowCreate: false`. |
| Critical #2 GraphEmail processed id before mark/move | Partially fixed, but regressed/incomplete at durable ack boundary | `GraphEmailOpportunityProvider.cs:169` records only after mark/move, but mailbox ack still precedes DB persistence. |
| Critical #3 MERGE in award/pursuit/permit upserts | Fixed for the three audited stores | `SqlOpportunityAwardStore.cs:138`, `SqlKorPursuitStore.cs:91`, `SqlBuildingPermitStore.cs:66` use `UPDLOCK, HOLDLOCK` update-then-insert transactions. |
| High #1 `SqlOpportunityStore` resolver not wired | Fixed | Worker registration passes resolver at `Program.cs:77`-`:80`; app registration was verified in Round 14c scope. |
| High #2/#3/#4/#5 unbounded JSON/CSV/permits/news reads | Partially fixed | Per-run item caps exist, but byte checks happen after full body allocation; see Medium finding. |
| High #6 Anthropic total caps can overshoot batch | Fixed in reviewed jobs | Batch is clamped before service call in `AwardAgentEnrichmentJob`, `VendorSiteExtractionJob`, and `NewsMentionClassifyJob` per `rg` review. |
| High #7 HTTP retry/backoff missing | Mostly fixed; residual 429 behavior above | Shared Polly policy at `Program.cs:31` and handlers at `Program.cs:93`, `:134`, `:147`, `:165`, `:193`, `:218`. |
| High #8 trigger poller unbounded drain/stranded rows | Partially fixed; new reclaim fencing issue above | Max-per-wake at `IngestionTriggerPollerBackgroundService.cs:90`; stale reclaim at `SqlIngestionTriggerStore.cs:69`. |
| Medium #1 opportunity list unbounded | Fixed | `IOpportunityStore.ListAsync`/`SqlOpportunityStore.ListAsync` now take a max rows default and use `TOP (@max)` per Round 14c review. |
| Medium #2 award/competition query MaxRows unclamped | Fixed | Stores clamp caller max rows to 5000 per Round 14c review. |
| Medium #3 canonical resolver/link catches silent | Fixed | Structured warning logging added in `SqlOpportunityStore` and `SqlOpportunityAwardStore` per Round 14c review. |
| Medium #4 secondary failure-record catches silent | Fixed | Warning logs added in award enrichment, vendor extraction, and news classifier per Round 14c review. |
| Medium #5/#6 grouped-column/table-level migration guards | Fixed by verifier layer | `29_SchemaVerifier.sql` adds independent guards for migrations 12-18 and 25-26. |
| Medium #7 KorPursuits CHECK constraints missing | Fixed | `28_SchemaHardening.sql` adds `CK_KorPursuits_Stage` and `CK_KorPursuits_OurRole`. |
| Medium #8 news mention multiplicity collapsed | Fixed and not re-broken by migration 29 | `28_SchemaHardening.sql:69` creates `UX_NewsMention_ArticleOrg_Type`; `29_SchemaVerifier.sql:318` avoids recreating the old unique index when the new one exists. |
| Low #1 duplicate `HistoricalOpportunityDetailViewModel` DI | Fixed | `rg` review showed a single registration after Round 14c. |
| Low #2 Vancouver permit seed name drift | Fixed | `26_BuildingPermits.sql` source literal and `28_SchemaHardening.sql` defensive update reviewed. |
| Low #3 stale news migration comments | Fixed | `25_NewsAggregator.sql` comments updated to current classifier/job names. |
| Low #4 UI header-load silent catches | Fixed | `HeaderLoader` centralizes the safe catch; the three target windows now call `HeaderLoader.ApplyAsync(HeaderBar)` without local empty catches. |
| Low #5 noisy heartbeat/info logs | Fixed for requested cases | DB ping is `LogDebug` at `HeartbeatBackgroundService.cs:56`; disabled-job and zero-row debug logs were added in Round 14d. |
| Cron collision/misfire review | Fixed for requested default collision and misfire coverage | Building permits default is `0 30 6 * * ?` at `Program.cs:486`; all 13 Quartz triggers use explicit cron misfire handling at `Program.cs:411`, `:425`, `:439`, `:452`, `:464`, `:476`, `:489`, `:503`, `:514`, `:527`, `:538`, `:549`, `:564`. |

## Things I Checked And Found Clean
- The three Round 14c UPDATE+HOLDLOCK upserts use the intended natural keys: awards on `(OpportunitySourceId, ExternalReference)`, pursuits on `(ExternalSource, ExternalSourceKey)`, and permits on `(PermitSourceId, ExternalId)`.
- Award and KorPursuit upserts preserve the requested "0 on update, inserted id on insert" semantics with `@inserted` tables and `COALESCE(..., 0)`.
- Building permit upsert intentionally returns the row id on both update and insert, matching `IBuildingPermitStore`'s contract.
- Quartz misfire instructions are present on every default trigger and match the 14d policy split: high-cadence jobs use `DoNothing`, hourly/daily/two-hour jobs use `FireAndProceed`.
- BuildingPermitsImport default cron is offset from SamGov at 06:30 (`Program.cs:486`).
- News mention schema migration 29 does not recreate the old `(NewsArticleId, CanonicalOrgId)` unique index when the new type-aware index exists (`29_SchemaVerifier.sql:318`).
- News classification skips unresolved `allowCreate=false` orgs instead of inserting orphan mention rows (`NewsMentionClassifier.cs:138`-`:145`).
- `CanonicalOrgResolver` denylist and too-short paths record unclassified aliases with `CanonicalOrgId = NULL` and return null (`CanonicalOrgResolver.cs:87`-`:98`).
- `EnrichmentDispatchJob` has `[DisallowConcurrentExecution]`, feature-disabled debug logging, and cancellation passthrough; the dispatcher catches provider failures per canonical id and records attempts.
- Historical opportunity ingestion remains separated behind `source.IsHistorical` in `IngestionService.cs:196` and writes to historical stores rather than the active opportunity tables.
