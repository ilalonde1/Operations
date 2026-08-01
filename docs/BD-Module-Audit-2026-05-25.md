# BD Module Audit - 2026-05-25

Overall risk assessment: the BD/Opportunities module is broadly coherent after the rapid build, but the highest-risk issues are operational/data-integrity failures rather than syntax-level problems: the Gov Canada re-enable migration can leave enabled sources unusable or absent, award canonical linking can downgrade curated org kinds, and the WPF/AI-context surfaces still contain thread-safety and binding hazards that match prior crash patterns. The MPI upsert pattern itself appears structurally race-safe, but several import/source-key and normalization mismatches can still produce quiet data drift.

| Severity | Count |
| --- | ---: |
| Critical | 0 |
| High | 6 |
| Medium | 8 |
| Low | 4 |

## Critical

No critical findings from static review.

## High

### H1. Gov Canada migration can silently no-op when source rows do not already exist

References:
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:17`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:21`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:30`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:34`
- `Kor.Opportunities.Worker/Services/SourceBootstrapHostedService.cs:43`
- `Kor.Opportunities.Worker/Services/SourceBootstrapHostedService.cs:69`
- `Kor.Opportunities.Worker/Services/SourceBootstrapHostedService.cs:93`
- `Kor.Opportunities.Worker/Services/SourceBootstrapHostedService.cs:109`

What is wrong: migration 41 updates `GovCanada_Construction` and `GovCanada_EngineeringServices`, then only prints a message if either row is missing. The worker bootstrap seeds `CanadaBuys`, `CanadaBuysNew`, `SamGov`, and `BdAlerts`; it does not bootstrap either Gov Canada source.

Impact: on a fresh or drifted database, applying the migration can report completion while no Gov Canada source exists or is enabled. `GovCanEngineeringImport` also depends on the engineering source existing and will error at startup if it is missing.

Recommended fix: make migration 41 idempotently `INSERT` missing Gov Canada source rows, or add both sources to `SourceBootstrapHostedService` with the same BaseUrl, delay, and mapping configuration. Avoid relying on pre-existing manual rows.

### H2. Gov Canada migration does not ensure the required GenericJsonAwardProvider mappings exist

References:
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:48`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:53`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:61`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:62`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:63`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:64`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:574`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:590`

What is wrong: migration 41 removes the old SQL-query marker and upserts only pacing-related mappings: `json.pageSize`, `json.pageDelayMs`, `json.maxPagesPerRun`, and `json.maxRowsPerRun`. The provider still requires field/path mappings such as `json.itemsPath`, `json.externalRefPath`, `json.titlePath`, `json.awardedToPath`, `json.contractValuePath`, and `json.contractDatePath`.

Impact: a source can be re-enabled and paced but still fail at runtime with a mapping error if a target database lacks the full older mapping set or has incomplete manual configuration.

Recommended fix: migration 41 should upsert the full required GenericJsonAwardProvider mapping set for both Gov Canada sources, not just the pacing keys.

### H3. Award ingestion can downgrade curated CanonicalOrg kinds

References:
- `Kor.Opportunities.Data/Awards/SqlCanonicalOrgStore.cs:60`
- `Kor.Opportunities.Data/Awards/SqlCanonicalOrgStore.cs:63`
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:67`
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:70`
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:123`
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:130`

What is wrong: `SqlCanonicalOrgStore.UpsertCanonicalOrgAsync` updates an existing row with `Kind = COALESCE(@kind, Kind)`. The resolver passes generic award kinds such as `Buyer` and `Vendor` when resolving buyer/vendor names.

Impact: a research-curated org classified as `Developer`, `Competitor`, `GeneralContractor`, or another stronger kind can be overwritten later by award ingestion as `Buyer` or `Vendor`. That weakens Org Dossier classification and can undo dedup/kind reconciliation work.

Recommended fix: introduce a deterministic kind-rank update policy. Generic `Buyer`, `Vendor`, and `Unknown` should not overwrite stronger curated/research kinds. Apply the same policy in import tools and resolver-backed award ingestion.

### H4. Resolver normalization and persisted CanonicalOrg normalized names are inconsistent

References:
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:168`
- `Kor.Opportunities.Data/Awards/SqlCanonicalOrgStore.cs:30`
- `Kor.Opportunities.Data/Awards/SqlCanonicalOrgStore.cs:34`
- `Kor.Opportunities.Data/Schema/22_OpportunityCanonicalLinks.sql:14`
- `Kor.Opportunities.Data/Schema/22_OpportunityCanonicalLinks.sql:18`

What is wrong: the resolver removes all non-alphanumeric characters with a regex. The database computed `NormalizedName` and SQL lookup remove only a specific punctuation set: spaces, periods, commas, apostrophes, hyphens, ampersands, slashes, parentheses, and plus signs.

Impact: names containing other punctuation or Unicode characters can normalize differently in .NET than in SQL. The resolver may miss an existing row and create a duplicate even though the user-facing name appears equivalent.

Recommended fix: centralize normalization. Either compute normalized names only in the database and mirror that exact logic in .NET tests, or persist a normalized value generated by shared .NET code. The resolver and SQL store must use identical rules.

### H5. Major Projects detail panel still has read-only `Run.Text` bindings without `Mode=OneWay`

References:
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:177`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:179`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:223`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:225`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:252`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryWindow.xaml:254`
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml:60`
- `Kor.Operations.App/Crm/ClientIntelligenceWindow.xaml:126`

What is wrong: several `<Run Text="{Binding Selected.*}">` bindings in the MPI detail panel point at read-only record properties and omit `Mode=OneWay`. Nearby fixed examples in `OrgDossierWindow` and `ClientIntelligenceWindow` explicitly use `Mode=OneWay`.

Impact: this is the same WPF binding class that previously crashed on read-only/record properties. Opening or refreshing a selected project can still trigger runtime binding failures in the Major Projects window.

Recommended fix: add `Mode=OneWay` to every `Run.Text` binding that targets read-only model/record properties. Audit the whole WPF tree with a search for `<Run Text="{Binding` and `<TextBox Text="{Binding`.

### H6. AI context snapshots enumerate ObservableCollections from a worker thread

References:
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs:181`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs:195`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs:306`
- `Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs:312`
- `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs:168`
- `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs:193`
- `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs:400`
- `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs:415`

What is wrong: `BuildContext`/`BuildLocalContext` snapshot `ObservableCollection<T>` instances with `ToArray()` while comments acknowledge that context building can run on a worker thread. The same collections are mutated during `LoadAsync` on the UI continuation.

Impact: `ObservableCollection<T>` enumeration is not thread-safe. AI context generation can throw `InvalidOperationException`, capture a torn view, or race with window load/close.

Recommended fix: keep immutable snapshot arrays updated on the UI thread and have AI context read only those arrays, or marshal snapshot creation through the dispatcher. If locking is used, lock both mutation and enumeration consistently.

## Medium

### M1. GenericJsonAwardProvider timeout covers the whole paginated run, including multi-page delay time

References:
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:72`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:73`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:84`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:178`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:61`
- `Kor.Opportunities.Data/Schema/41_GovCanada_PacedReEnable.sql:64`

What is wrong: one timeout cancellation source is created before the pagination loop and `CancelAfter` is applied once. A paced run can now include up to 25 requests plus 1.5-second inter-page delays, but migration 41 does not update a source request timeout.

Impact: a default timeout that was acceptable for one request can cancel the whole run before reaching the page cap, especially under network jitter. This looks like a provider failure rather than polite throttling.

Recommended fix: apply timeout per HTTP request inside the loop, or explicitly raise/set `RequestTimeoutSeconds` for paced paginated sources.

### M2. GenericJsonAwardProvider row-cap warning can be misleading at exact cap boundaries

References:
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:126`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:133`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:182`
- `Kor.Opportunities.Data/Ingestion/Providers/GenericJsonAwardProvider.cs:187`

What is wrong: the row cap is checked before processing each item, but the warning is emitted whenever `processed >= MaxRowsPerRun`. If a source naturally returns exactly the configured maximum, the log can say the run stopped due to the row cap even if no additional rows were skipped.

Impact: worker logs can create false alarms and obscure real truncation events.

Recommended fix: track an explicit `stoppedByRowCap` flag only when the loop actually breaks because another row would exceed the cap.

### M3. GovCanEngineeringImport does not count value/date parse failures

References:
- `tools/GovCanEngineeringImport/Program.cs:99`
- `tools/GovCanEngineeringImport/Program.cs:106`
- `tools/GovCanEngineeringImport/Program.cs:145`
- `tools/GovCanEngineeringImport/Program.cs:147`
- `tools/GovCanEngineeringImport/Program.cs:201`
- `tools/GovCanEngineeringImport/Program.cs:215`
- `tools/GovCanEngineeringImport/Program.cs:218`
- `tools/GovCanEngineeringImport/Program.cs:233`

What is wrong: the importer reports parse failures for malformed JSON or mapping exceptions, but `contract_value` and `contract_date` failures return null silently.

Impact: a dry-run can report a clean import while many awards lose value/date data. This weakens award history and Org Dossier financial summaries without an obvious warning.

Recommended fix: increment separate `valueParseFailures` and `dateParseFailures` counters when non-blank source fields fail parsing, and include them in the final summary.

### M4. LA market SourceKey can collide for same-named projects in one municipality

References:
- `tools/BdResearchImport/Program.cs:626`
- `tools/BdResearchImport/Program.cs:632`

What is wrong: LA market project keys are based on `ProjectName|Municipality` only. That follows the import spec, but it is weak for a large metro market with phased, repeated, or reused project names.

Impact: distinct LA projects can overwrite one another through the MPI `(Province, SourceKey)` upsert path.

Recommended fix: include a stable source URL, proponent, address, or payload slug in the SourceKey when available. If the short key must remain for compatibility, log duplicate raw-key collisions during import.

### M5. PacNW blank-state fallback can create duplicates when State is later corrected

References:
- `tools/BdResearchImport/Program.cs:620`
- `tools/BdResearchImport/Program.cs:626`
- `tools/BdResearchImport/Program.cs:628`

What is wrong: PacNW projects with blank state are imported as province `WA`, and the normalized/fallback state is included in the hash input.

Impact: if a later payload supplies the correct state, the same project gets a different SourceKey and inserts a second MPI row instead of updating the first one.

Recommended fix: use the raw payload state with an explicit sentinel in the hash, or treat blank State as an import warning/skip for PacNW projects rather than defaulting into a real province.

### M6. USD/CAD conversion rate is hardcoded and not auditable

References:
- `tools/BdResearchImport/Program.cs:1062`
- `tools/BdResearchImport/Program.cs:1063`
- `tools/BdResearchImport/README.md:33`

What is wrong: LA and PacNW project costs convert USD to CAD with a hardcoded `1.36` rate. The rate date/source is not captured in structured fields.

Impact: cost comparisons drift over time, and users cannot tell whether a CAD estimate is source-native or converted using an old assumption.

Recommended fix: make the FX rate a CLI/config value and include rate/date/provenance in `EstimatedCostText` or a structured raw metadata field.

### M7. US market firm kind mapper is narrower than other kind mapping logic

References:
- `tools/BdResearchImport/Program.cs:978`
- `tools/BdResearchImport/Program.cs:987`

What is wrong: the LA/PacNW firm mapper recognizes only exact normalized values for `competitor`, `gc`, `architect`, and `developer`. Other local mapping code recognizes broader general-contractor tokens.

Impact: payload values such as `General Contractor` or `GeneralContractor` can become `Unknown`, reducing the quality of canonical org classification.

Recommended fix: reuse a shared kind mapper across all BD research sources, with accepted aliases for `GeneralContractor`, `GC`, `Contractor`, and similar values.

### M8. Dedupe dry-run row-after summary is optimistic when groups may fail during commit

References:
- `tools/BdCanonicalDedup/Program.cs:133`
- `tools/BdCanonicalDedup/Program.cs:144`
- `tools/BdCanonicalDedup/Program.cs:153`
- `tools/BdCanonicalDedup/Program.cs:154`

What is wrong: dry-run computes projected rows after by subtracting all planned loser rows. Commit mode catches and skips failed groups individually, so the actual post-commit row count may be higher than dry-run projected.

Impact: dry-run can overstate expected cleanup success, especially when a previous live run had failed groups.

Recommended fix: label the count as “projected if all groups commit,” and include a separate committed-success/failed-group count after live runs.

## Low

### L1. Migration 37 is operational data mutation rather than a stable schema migration

References:
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:15`
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:25`
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:28`
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:39`
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:42`
- `Kor.Opportunities.Data/Schema/37_OllamaBackfill_QueueFilter.sql:48`

What is wrong: migration 37 updates operational retry state for low-value tenders and can affect different rows on subsequent runs as data changes.

Impact: it is mostly harmless, but it is not a deterministic schema/data seed migration and can hide newly inserted rows from enrichment if re-run later.

Recommended fix: move this into an ops/maintenance script or document it clearly as one-time queue hygiene, not a normal idempotent schema migration.

### L2. Dedupe dynamic SQL is safe today but fragile if FK targets ever become configurable

References:
- `tools/BdCanonicalDedup/Program.cs:45`
- `tools/BdCanonicalDedup/Program.cs:65`
- `tools/BdCanonicalDedup/Program.cs:656`
- `tools/BdCanonicalDedup/Program.cs:661`

What is wrong: table and column names are interpolated into SQL strings. They currently come from a private hard-coded list, not user input.

Impact: no immediate injection finding, but future edits that load target definitions from config or CLI would make this dangerous.

Recommended fix: keep the target list private and constant, add a comment/assertion that identifiers are trusted constants, or quote identifiers through a whitelist helper.

### L3. Import tools log research names and project titles at info level

References:
- `tools/BdResearchImport/Program.cs:705`
- `tools/BdResearchImport/Program.cs:782`
- `tools/BdResearchImport/Program.cs:887`
- `tools/GovCanEngineeringImport/Program.cs:68`

What is wrong: dry-runs and imports print business names, project names, file paths, and source IDs directly to the console.

Impact: this is usually acceptable for local disposable tools, but console capture can leak confidential research targets or paths into shared logs.

Recommended fix: add a `--quiet`/summary mode for production runs, or reduce planned-write logging to counts unless verbose mode is enabled.

### L4. Org Dossier project hyperlinks do not visibly distinguish missing/non-absolute URLs

References:
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml:133`
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml:136`
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml.cs:69`
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml.cs:72`

What is wrong: project names are rendered as hyperlinks, but the navigate handler only opens absolute URIs. Null, blank, or relative URLs are silently ignored.

Impact: users can see link styling that does nothing.

Recommended fix: render plain text when no absolute `SourceUrl` exists, or disable the hyperlink style for invalid URLs.

## Looks Correct / Verified Safe

- MPI upsert race shape: the importers/providers use `UPDATE ... WITH (UPDLOCK, HOLDLOCK)` followed by `INSERT`, with the schema enforcing `UX_MPI_Province_SourceKey`. Static review suggests this is race-safe when SQL Server uses the unique key path.
- Newest-issue-wins logic: the BC MPI importer compares incoming issue year/quarter scores with stored issue scores before overwriting issue-scoped fields, while still updating `LastSeenAtUtc`.
- Resource disposal: reviewed data/provider paths use `await using`/`using` for `SqlConnection`, `SqlDataReader`, HTTP response objects, and file streams in the audited areas.
- WPF load mutation: `LoadAsync` methods are invoked from window loaded handlers and use normal UI continuations, so collection mutation itself appears to happen on the UI thread. The outstanding risk is cross-thread AI snapshot enumeration.
- AI registration lifecycle: `MajorProjectsInventoryWindow` and `OrgDossierWindow` register with `AppAiContextBuilder` in the constructor and unregister on close.
- Dedupe FK list matches the static schema references found for `CanonicalOrg`: BuildingPermit 3, MajorProjectsInventory 2, OpportunityAwards 2, Opportunities 1, KorPursuits 2, NewsArticleOrgMention 1, OrgAlias 1, and CanonicalOrgEnrichment 1.

## Needs Live Verification

- Whether `GovCanada_Construction` and `GovCanada_EngineeringServices` already exist in production; static repo review only found migration 41 updating them.
- Whether the production Gov Canada sources already have the full required `json.*` mappings.
- Why the reported 43 dedupe groups failed or rolled back. Plausible causes from static review include concurrent FK writes to loser rows during merge, a live FK not in the hard-coded list, schema drift causing unique collisions, or data length/constraint failures. Exact cause requires live commit logs or DB state.
- Whether the raw-brace CKAN filter BaseUrl in migration 41 is accepted by the runtime URI construction and KOR-APP01 network path exactly as stored.
- Whether the deployed WPF runtime/theme attempts a TwoWay update for all affected `Run.Text` bindings. Prior crash history says this is a real risk; static review cannot execute the binding engine.
