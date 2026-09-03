# CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT response

Date: 2026-09-03

Scope: read-only audit of BD canonical organization identity and refresh paths. I did not run `dotnet build`, `dotnet test`, migrations, service restarts, or data writes. Database evidence below came from SELECT-only queries against the live opportunities database.

## Covered

- Canonical org birth, lookup, alias, fuzzy attach, retired reuse, and creation paths in `CanonicalOrgResolver` and `SqlCanonicalOrgStore`.
- Batch/manual dedupe mechanics in `tools/BdCanonicalDedup`, including `--merge-dba`, aggressive keys, survivor selection, alias preservation, merge ledger, and current FK target coverage.
- Org brief same-brand fallback behavior in `SqlBriefDataStore`.
- FirmNarrative refresh paths from the WPF dossier button through `BdResearchTriggers`, the trigger poller, `BdResearchExecutorService`, Anthropic research execution, enrichment tracking, extraction, and intel persistence.
- Scheduled and CLI replay paths that can re-extract or refresh org narratives.
- Person resolver and person-affiliation behavior where org identity is already polluted.
- Live data for the Continuum incident and same-class candidate populations.

## Not covered

- I did not inspect every non-org research extractor schema in the same depth as FirmNarrative.
- I did not prove historical pre-2026-06-25 canonical merge edges from backups; the live `CanonicalOrgMerge` ledger has no Continuum row and starts on 2026-06-25.
- I did not validate web-search output truthfulness from Anthropic itself; this audit is about whether code anchors the result to the intended canonical entity before writing.

## Findings

### 1. FirmNarrative manual refresh can overwrite an org with a different real-world entity without website, region, or source anchor checks

Risk: highest. This is the live path that reproduced the Continuum corruption.

Mechanism:

- The WPF org dossier refresh button enqueues `ProviderName = FirmNarrative` for a `CanonicalOrgId` in `Kor.Operations.App/Opportunities/OrgDossierViewModel.cs:668`.
- `BdResearchTriggerPollerBackgroundService` drains that queue and calls `BdResearchExecutorService.ExecuteOneAsync(trigger.CanonicalOrgId, trigger.ProviderName)` in `Kor.Opportunities.Worker/Services/Research/BdResearchTriggerPollerBackgroundService.cs:140`.
- `BdResearchExecutorService.ExecuteOneAsync` loads only `Id`, `DisplayName`, and `Kind` for the target org in `Kor.Opportunities.Worker/Services/Research/BdResearchExecutorService.cs:258`. The research target then contains only id, display name, kind, provider, system prompt, and user prompt in `Kor.Opportunities.Worker/Services/Research/BdResearchExecutorService.cs:156`.
- The Anthropic schema requires `displayName`, `kind`, provider, confidence, and narrative fields, but has no required website, domain, region, registry id, source URL match, or "this was the exact entity" assertion in `Kor.Opportunities.Worker/Services/Research/AnthropicResearchExecutorService.cs:23`.
- `BdResearchExecutorService.PushThroughChokepointAsync` records the result through `IEnrichmentTrackingStore.RecordAttemptAsync` in `Kor.Opportunities.Worker/Services/Research/BdResearchExecutorService.cs:296`, which immediately extracts and persists intel in `Kor.Opportunities.Data/Awards/SqlEnrichmentTrackingStore.cs:184`.

Continuum live repro path:

- Canonical org `74300` is active as `Continuum Partners, LLC`, kind `Developer`, with no website.
- Its preserved `DedupeMerge` aliases are `Wil Wiens DBA: Continuum Architecture Inc` and `Continuum Architecture Inc`.
- Active people on the same org now include the old Victoria architecture firm principals from `FirmNarrativeHoning` plus Denver developer people from `FirmNarrative`.
- Current active `IntelNarrative` rows `8730` and `8731` were created on 2026-06-13 and updated on 2026-09-03 by source enrichment `69883`. Row `8730` now says Continuum Partners is a Denver mixed-use developer. Row `8731` now gives an EYRC Architects entry-point action. Row `18567` now explicitly states that the prior record was populated for the wrong organization.

Affected live population:

- Live canonical orgs: 9,694.
- Live canonical orgs with no website: 7,204.
- Live orgs with active narratives: 6,313.
- Live orgs with active narratives and no website: 4,317.
- Active `IntelNarrative` rows: 14,879.
- Active `FirmNarrative` narrative rows: 10,179.
- Distinct orgs with completed `FirmNarrative` manual triggers: 225.

Current operational state:

- The scheduled `BdResearchExecutorJob` is registered in Quartz and has successful daily `JobRuns`, but current summaries show `considered=0; executed=0` because `BdResearchExecutor.Enabled=false` in `Kor.Opportunities.Worker/appsettings.json:13`.
- If enabled with current default candidate logic, a SELECT-only simulation found 9,186 eligible live orgs, including 6,826 with no website.
- The manual trigger path is live today; recent trigger history showed completed `FirmNarrative` triggers through 2026-09-03.

Silent-corruption reason:

The write target is a stable `CanonicalOrgId`, but the research identity is only a weak text label and kind. If the canonical row is already conflated or ambiguous, refresh faithfully writes a high-confidence narrative for the wrong real-world entity into the existing org.

### 2. `IntelNarrative` persistence is destructive and non-versioned, so wrong refreshes erase the previous truth surface

Risk: very high because it removes the ability to compare before/after from live tables.

Mechanism:

- `IntelPersistenceService.MergeNarrativeAsync` uses `MERGE opportunities.IntelNarrative` keyed by `NaturalKey` in `Kor.Opportunities.Data/Intel/IntelPersistenceService.cs:587`.
- The natural key is SHA1 of `CanonicalOrgId` plus `NarrativeType`, not source URL, content hash, entity anchor, or provider-run identity, in `Kor.Opportunities.Data/Intel/IntelPersistenceService.cs:612`.
- On match, the MERGE updates `CanonicalOrgId`, `NarrativeType`, `ParagraphText`, provider, enrichment id, confidence, and timestamps in place in `Kor.Opportunities.Data/Intel/IntelPersistenceService.cs:591`.
- `SqlEnrichmentTrackingStore.RetireSupersededIntelAsync` retires stale affiliations, signals, actions, work, and risks, but explicitly omits `IntelNarrative` because it "upserts cleanly" in `Kor.Opportunities.Data/Awards/SqlEnrichmentTrackingStore.cs:325`.

Continuum repro path:

- Rows `8730` and `8731` kept their original `CreatedAtUtc` from 2026-06-13 but were updated on 2026-09-03 to the Denver developer narrative/action for source enrichment `69883`.
- The prior Victoria architecture narrative text is not retained in `IntelNarrative`; it can only be inferred from the newly written `History` note or recovered externally.

Affected live population:

- Active `IntelNarrative` rows: 14,879.
- Active `FirmNarrative` narrative rows: 10,179.
- Live narrative orgs without website anchors: 4,317.

Silent-corruption reason:

The table preserves only the latest paragraph per org/type. When the latest paragraph is for the wrong entity, downstream consumers see a normal current row rather than a retired conflicting row or a version history.

### 3. `--merge-dba` plus aggressive keys can collapse unrelated firms to one bare brand stem

Risk: very high because it corrupts the canonical graph, then every later refresh writes into the wrong container.

Mechanism:

- `tools/BdCanonicalDedup/Program.cs:41` parses `Person DBA: Company`.
- With `--merge-dba`, `BuildKeys` adds both the primary aggressive key and the post-DBA company key in `tools/BdCanonicalDedup/Program.cs:818`.
- `NormalizeAggressiveKey` strips business/legal/domain words including `inc`, `llc`, `architects`, `architect`, `architecture`, `partnership`, `partners`, and `group` in `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs:608`.
- `BuildGroups` creates union-find groups from those keys in `tools/BdCanonicalDedup/Program.cs:703`.
- `ChooseSurvivor` prefers frozen, then lower kind rank, Clendor, FK count, and id in `tools/BdCanonicalDedup/Program.cs:849`; `KindRank` ranks `Developer` ahead of `Architect` in `tools/BdCanonicalDedup/Program.cs:43`.
- Loser names are preserved as `OrgAlias` source `DedupeMerge` in `tools/BdCanonicalDedup/Program.cs:1403`, then loser canonical rows are deleted in `tools/BdCanonicalDedup/Program.cs:1463`.

Continuum repro path:

- `Continuum Partners, LLC` normalizes aggressively to `continuum` because `partners` and `llc` are stripped.
- `Continuum Architecture Inc` normalizes aggressively to `continuum` because `architecture` and `inc` are stripped.
- `Wil Wiens DBA: Continuum Architecture Inc` adds the DBA company key, also `continuum`.
- The survivor is the developer because `Developer` ranks before `Architect`.
- Live row `74300` now carries `DedupeMerge` aliases for both `Wil Wiens DBA: Continuum Architecture Inc` and `Continuum Architecture Inc`.
- `CanonicalOrgMerge` has no row involving `74300`; the live merge ledger contains 223 rows and starts on 2026-06-25, after this merge evidence.

Affected live population:

- `OrgAlias` rows with `Source = DedupeMerge` and `RawName LIKE '% DBA:%'`: 3,919 canonical targets total.
- Of those, 1,542 point at currently active canonical orgs.
- A lower-bound contradiction query found 19 active DBA-merge orgs with active affiliated people spanning at least two distinct email domains. This is not proof all 19 are wrong, but it is the same failure class and needs review.

Silent-corruption reason:

The alias evidence looks intentional after merge, and the losing org row is gone. Later resolvers treat old loser names as aliases for the survivor, so new data for either real-world firm continues to land in one org.

### 4. Same-brand dossier redirect can silently show a richer different org that dedupe would not merge

Risk: high for UI/report identity correctness. This is mostly a read-time corruption path, but it changes what a user believes an org dossier represents.

Mechanism:

- `SqlBriefDataStore.GetOrgBriefAsync` treats an org as thin when it has zero KOR projects, zero recent projects, and zero contacts in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:763`.
- It then calls `FindRicherSameBrandCanonicalAsync` in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:766`.
- Prefix matches are considered redirect-safe when the normalized prefix length is at least six and the remainder starts with a broad corporate token such as `properties`, `property`, `developments`, `development`, `group`, `homes`, `construction`, `contracting`, `partners`, or `ventures` in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:810`.
- If `RedirectSafe`, the method recursively returns the richer org's brief in `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:776`.

Same-class repro paths from live data:

- Thin org `867` `Vulcan`, fuzzy key `vulcan`, can redirect to `68710` `Vulcan Real Estate`, fuzzy key `vulcanrealestate`.
- Thin org `70674` `Townline`, fuzzy key `townline`, can redirect to richer `Townline Ventures`, `Townline Group of Companies`, or `Townline Homes Inc.` candidates depending richness.
- Thin org `717` `Sundre` can redirect to `16252` `Sundre Contracting Co.`.

Affected live population:

- A SELECT-only approximation of the exact redirect logic found 254 redirect-safe thin-to-rich pairs, across 221 thin orgs and 213 richer orgs, where the two sides do not share the same `FuzzyNormalizedName`.

Silent-corruption reason:

This read path is more permissive than the write-time dedupe fuzzy gate. It can present a richer entity as the answer for a thin same-prefix org without requiring a canonical merge, website match, or source agreement.

### 5. Person affiliation resolution compounds org-level identity mistakes

Risk: medium-high. It does not create the original org merge, but once the org is wrong it makes the wrong entity look richer and more legitimate.

Mechanism:

- The current `opportunities.ResolveIntelPerson` procedure resolves by non-generic email, then LinkedIn, then normalized name plus active org affiliation in `Kor.Opportunities.Data/Schema/277_ResolveIntelPersonResurrect.sql:94`.
- If no match exists, the natural key falls back to normalized name plus `CanonicalOrgId` in `Kor.Opportunities.Data/Schema/277_ResolveIntelPersonResurrect.sql:192`.
- `IntelPersistenceService.PersistAsync` only guards that the parent org exists and is active in `Kor.Opportunities.Data/Intel/IntelPersistenceService.cs:81`; it does not compare person email domains, geography, provider identity, or expected org website.

Continuum repro path:

- Active affiliations on org `74300` now include Victoria architecture principals from `FirmNarrativeHoning` and Denver developer principals from `FirmNarrative`.
- The old Victoria names have no email-domain anchor in live rows, so they do not trip the non-generic email branch and remain attached to the conflated org.

Affected live population:

- The Continuum row has six active affiliated people from two different provider/entity contexts.
- Across active DBA-merge orgs, a lower-bound query found 19 active orgs with at least two distinct active affiliated person email domains.

Silent-corruption reason:

Contacts become evidence that the conflated org is rich, which then influences dossier richness, refresh prompt context, and future resolver choices.

### 6. Scheduled job registry omits two live research/intel jobs, so operators can miss writer activity

Risk: medium. This does not itself corrupt identity, but it weakens the operational control surface around refresh/extraction writers.

Mechanism:

- `ScheduledJobDefinition.All` is the source used by `JobScheduleRegistryHostedService` to upsert admin-visible schedules in `Kor.Opportunities.Worker/Services/JobScheduleRegistryHostedService.cs:39`.
- The list includes `BdResearchQueueBuilderJob`, `EnrichmentDispatchJob`, and `IntelRetirementJob`, but not `BdResearchExecutorJob` or `IntelExtractionCatchUpJob` in `Kor.Opportunities.Worker/Services/ScheduledJobDefinition.cs:17`.
- `Program.cs` still schedules `IntelExtractionCatchUpJob` at `Kor.Opportunities.Worker/Program.cs:930` and `BdResearchExecutorJob` at `Kor.Opportunities.Worker/Program.cs:1089`.

Live DB evidence:

- `opportunities.JobSchedules` has rows for `BdResearchQueueBuilderJob` and `EnrichmentDispatchJob`.
- It has no rows for `BdResearchExecutorJob` or `IntelExtractionCatchUpJob`.
- `opportunities.JobRuns` nevertheless has recent successful rows for both omitted jobs.

Silent-corruption reason:

Admin schedule views built from `JobSchedules` can under-report the active writers/replayers that touch intel. That makes it easier to miss a bad refresh path after it is re-enabled or after catch-up replays a poisoned enrichment.

## Items checked but not raised as new findings

- Dedup FK coverage for migrations 289/290: current `tools/BdCanonicalDedup/Program.cs` includes `OrgFact` and `CrmTouchpoint` FK targets. This is an already-established fix, not a new finding.
- Perkins&Will / ampersand behavior: the code still has a known narrow `NormalizeForFuzzyMatch` behavior around `" & "` versus compact `&`, but a live SELECT-only active canonical-name blast-radius query found zero active ampersand/and fold groups today. I did not raise this as a current data finding.
- Active fuzzy duplicate keys: a live query found zero active `CanonicalOrg` duplicate `FuzzyNormalizedName` groups with length at least six. This means the current dedupe state is clean on that exact key, not that future fuzzy attach is safe.
- JV-string rows: the forward fix note says existing combined names were decomposed separately; migration 228 repointed 44 live MPI role FKs. A current query for active long/composite names found no obvious remaining live `project on hold` or `relationship terminated` JV-string orgs. I did not raise this as current live corruption.

## Same-class fault not caught by current paths

The current paths do not catch same-brand, different-entity substitutions when the name stem is shared but the real-world entity is not. Continuum is one instance, but the same class appears in the dossier redirect population: 221 thin orgs have redirect-safe richer candidates with different fuzzy keys. Examples include `Vulcan` -> `Vulcan Real Estate`, `Townline` -> `Townline Ventures`, and `Sundre` -> `Sundre Contracting Co.`. The current UI redirect path accepts these based on prefix/corporate-token richness, while the dedupe fuzzy-key path would not merge them without a separate rule.

The current refresh path also does not catch "wrong but plausible web result" substitutions for no-website orgs. The manual FirmNarrative trigger has 225 completed orgs and 4,317 live narrative orgs have no website anchor. The research target and persistence layer do not require any source-domain, registry, address, region, or website agreement before replacing current narrative text.

## Useful guardrails implied by the audit

These are not fixes applied here; they are the smallest controls that would block the observed class.

- Before accepting FirmNarrative output, require an identity assertion with evidence fields: website/domain, headquarters/location, source URLs, and "same entity as canonical row" confidence.
- For no-website orgs, do not allow in-place narrative replacement unless the output matches an existing stable anchor or is written as a candidate/review record.
- Version `IntelNarrative` or preserve prior paragraph text on every content-changing update.
- Disable or quarantine `--merge-dba` groups where the bare aggressive key is a single brand stem shared across different org kinds, people, domains, or source families.
- Make `ScheduledJobDefinitions.All` cover every Quartz job that can write or replay BD intel, including `BdResearchExecutorJob` and `IntelExtractionCatchUpJob`.
