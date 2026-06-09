# BD Module — Comprehensive Adversarial Audit Prompt

> **For new session on Claude Opus 4.8 (or equivalent fresh model).**
> Self-contained: gives all context a fresh model needs to audit the
> entire BD module without inheriting any prior session's mental
> model.

---

## Mission

Adversarial deep-review the **entire BD (Business Development)
module** of the KOR Operations app. Output a prioritized punch-list
of issues (Critical / Major / Minor) plus architectural concerns,
based on the actual code + database state — not the user's
description of it.

This is the single most important audit before the next major build
phase (in-app WPF report generation per
`docs/BD-UI-Plan-2026-06-08.md`). Anything missed here becomes
load-bearing for that build.

---

## Business context

**KOR Structural** is a Vancouver-based structural engineering firm.
Markets: BC, LA, San Diego today; growth = Edmonton + US West Coast
(WA/OR). KOR was Bryson Markulin Zickmantel (BMZ) before 2021 rebrand.

**BD goal**: identify + pursue structural-engineering opportunities
across public + private construction. The BD module captures
opportunities, enriches them with intelligence (decision-makers,
procurement model, competitors), and surfaces actionable pursuits
to KOR partners (John Bryson, James Desroches, John Markulin, plus
Ian Lalonde as ops lead).

**Recent commercial push**: past 2 weeks of intensive build — 10
research drain categories (hospitals, schools, indigenous, BC
housing, defense, EllisDon, recreational, commercial office,
post-secondary, residential) + 14 data-hygiene migrations
(m106-m114) + 12 Word report builders + cross-cutting call sheet.
THIS WORK MUST BE REVIEWED, not just trusted.

---

## Scope: what is the BD module

### Code projects

```
Kor.Opportunities.Core/         (interfaces, models, contracts)
Kor.Opportunities.Data/         (SQL schema, migrations 1-114, EF
                                 stores, query helpers)
Kor.Opportunities.Worker/       (background worker on KOR-APP01,
                                 provider ingestion cron, research
                                 executor)
Kor.Opportunities.Capture/      (web scraping / API capture utils)
Kor.Opportunities.ApcImport/    (Alberta Purchasing Connection
                                 email-alert ingest)
```

Plus BD-related code inside the main WPF app:
```
Kor.Operations.App/             (Org Dossier, Project Brief, Region
                                 Brief, BD Dashboard, Priority
                                 Actions, BD module global search
                                 typeahead, IAiContextProvider
                                 registrations for BD VMs)
```

Plus MCP service that BD AI queries flow through:
```
Kor.Operations.Mcp/             (text-to-SQL gateway, /ask endpoint,
                                 KorMcp DB, SystemPrompt with KPI
                                 methodology; BD Phase 4 deferred)
```

### Tools (operational scripts + CLIs)

```
tools/BdBriefSmoke/                       smoke-test brief generation
tools/BdCanonicalDedup/                   programmatic org dedup
tools/BdDeltekLink/                       Deltek client linking
tools/BdGatherIntel/                      manual intel gather
tools/BdIntelExtract/                     intel extraction
tools/BdOpportunityPurge/                 retire stale opps
tools/BdOrphanOrgPurge/                   orphan org cleanup
tools/BdPersonResearchExecutorSmoke/      person research smoke
tools/BdProjectResearchExecutorSmoke/     project research smoke
tools/BdQueueDrainBatchGenerate/          drain queue batch generation
tools/BdQueueDrainIngest/                 drain queue ingest tool
tools/BdQueueDrainPrompts/                10 PROMPT.md templates
tools/BdReportBuilders/                   12 PowerShell Word docx builders
tools/BdResearchExecutorSmoke/            executor smoke test
tools/BdResearchImport/                   research import pipeline
tools/BdSeedImport/                       initial seed data import
tools/BdTrackingImport/                   tracking import
tools/ApcInterestProbe/                   APC interest probe
tools/ApcInterestBackfill/                APC backfill
tools/BcBidInterestProbe/                 BcBid interest probe
tools/BcMpiImporter/                      BC Major Projects Inventory
tools/BidsAndTendersInterestProbe/        BidsAndTenders probe
tools/GovCanEngineeringImport/            Gov of Canada engineering
```

### Database

- **KorOpportunitiesDb** on the production SQL Server
- Schema: `opportunities.*`
- 114 migrations applied (Schema/1_*.sql through Schema/114_*.sql)
- Core tables: MajorProjectsInventory, CanonicalOrg, IntelPerson,
  IntelSignal, IntelAction, IntelWork, IntelRisk,
  IntelPersonAffiliation, IntelProjectAction, IntelProjectSignal,
  IntelProjectRisk, IntelProjectKeyPerson,
  MajorProjectEnrichment, CanonicalOrgEnrichment,
  OpportunityBids, OpportunityAwards, OpportunityInterestedFirms

### Sister queues (drain queue infrastructure)

```
C:\ProgramData\KorOperations\QueueDrain\
├── bc-ab-hospitals/             (+ -honing/)
├── bc-ab-schools/               (+ -honing/)
├── bc-ab-postsecondary/         (+ -honing/)
├── bc-ab-residential/           (+ -honing/)
├── bc-ab-commercial/            (+ -honing/)
├── bc-ab-recreational/          (+ -honing/)
├── bc-housing/                  (+ -honing/)
├── indigenous-projects/         (+ -honing/)
├── defense-military/            (+ -honing/)
├── orgs/                        (+ honing-orgs/)
├── people/                      (+ honing-people/)
├── projects/                    (+ honing-projects/)
├── okanagan-*                   (Okanagan sub-queues)
├── vanisland-*                  (VanIsland sub-queues)
├── orgs-buyers/, orgs-trip/, etc.
```

### Out of scope

- FileSync Command Center (separate ops concern, not BD)
- PdfToSafe (separate product)
- Email Search (separate module)
- Deltek revenue/billing logic (separate)

---

## Authoritative state — where to find facts

**DO NOT** trust this prompt's narrative. Verify everything against:

### Git
```bash
git log --oneline --since="2 weeks ago"
git log --oneline c945b46..HEAD       # recent BD buildout window
git log --oneline -- Kor.Opportunities.Data/Schema/   # migration history
git log --oneline -- tools/BdQueueDrainPrompts/       # PROMPT history
git log --oneline -- tools/BdReportBuilders/          # builder history
git diff <baseline>..HEAD -- Kor.Opportunities.*      # what changed
```

### Database (read-only verification)
Connection string in env var `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
Run queries to verify counts, orphans, FK soundness — never trust
prompt's claims about row counts.

Suggested verification queries:
```sql
-- All applied migrations
SELECT TOP 30 name, create_date FROM sys.objects
WHERE type IN ('U','V','P') AND schema_id = SCHEMA_ID('opportunities')
ORDER BY create_date DESC;

-- Active vs retired MPIs by category
SELECT
    SUM(CASE WHEN RetiredAtUtc IS NULL THEN 1 ELSE 0 END) AS Active,
    SUM(CASE WHEN RetiredAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS Retired,
    COUNT(*) AS Total
FROM opportunities.MajorProjectsInventory;

-- Orphan Intel rows (pointing to retired MPIs)
SELECT 'IntelProjectAction' AS T, COUNT(*) FROM opportunities.IntelProjectAction ia
INNER JOIN opportunities.MajorProjectsInventory m ON m.Id = ia.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL
UNION ALL
SELECT 'IntelProjectSignal', COUNT(*) FROM opportunities.IntelProjectSignal ia
INNER JOIN opportunities.MajorProjectsInventory m ON m.Id = ia.MajorProjectsInventoryId
WHERE m.RetiredAtUtc IS NOT NULL
-- ... etc for all Intel* tables

-- Enrichment freshness distribution
SELECT ProviderName,
    COUNT(*) AS Total,
    SUM(CASE WHEN LastRefreshAtUtc >= DATEADD(DAY,-30,sysdatetimeoffset()) THEN 1 ELSE 0 END) AS Last30d,
    SUM(CASE WHEN LastRefreshAtUtc < DATEADD(DAY,-90,sysdatetimeoffset()) THEN 1 ELSE 0 END) AS OlderThan90d
FROM opportunities.MajorProjectEnrichment
GROUP BY ProviderName ORDER BY Total DESC;
```

### Files
- Read every migration in `Kor.Opportunities.Data/Schema/` from 100
  onwards (assume earlier ones audited prior)
- Read every PROMPT.md in `tools/BdQueueDrainPrompts/`
- Read every builder in `tools/BdReportBuilders/`
- Read `Kor.Opportunities.Worker/Program.cs` + composition modules
- Read `Kor.Operations.App/CompositionModules/` for BD VM
  registrations + IAiContextProvider wiring
- Read `Kor.Operations.Mcp/SystemPrompt.cs` (or wherever it is — find
  via grep)

### User memory
The user keeps persistent project memory at:
```
C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\memory\
```
Read these BD-relevant files in particular:
- `project_bd_module_deferred_work.md` — authoritative deferred backlog
- `project_business_development_module.md` — module overview
- `project_opportunities_module.md` — Worker/DB pipeline state
- `project_contractradar_relationship.md` — sibling KOR project context
- `project_bd_platform_vision.md` — north star
- `project_bd_ai_layer_deferred.md` — Phase 4 plan
- `project_opportunity_relevance_gate.md` — intake gate
- `project_data_retirement_lifecycle.md` — nightly retire job
- `project_r95_data_hygiene_state.md` — what shipped in R95
- `project_r95_extra_morning_plan.md` — m migrations context
- `project_alert_system_design.md` — Phase 11d alert plan
- `project_prime_consultant_strategy.md` — pursuit strategy
- `project_deltek_bd_fusion.md` — Deltek integration plan
- All `feedback_*.md` files — important behavioral constraints

### UI plan
- `docs/BD-UI-Plan-2026-06-08.md` — the committed forward plan for
  in-app report generation. Review for completeness BEFORE the
  WPF build starts.

---

## Review dimensions

For each dimension below, identify Critical / Major / Minor issues.

### 1. Data model integrity

- **Intel* FK soundness** — m110 (UHNBC), m111 (Vernon Jubilee), m114
  (Newton + Britannia) repoint Intel* FKs across consolidations. Are
  there orphan Intel rows pointing to retired MPIs that didn't get
  repointed?
- **MajorProjectEnrichment** — UQ_MajorProjectEnrichment_ProjectProvider
  unique constraint. Are there sibling-category honing batches that
  could collide? (We hit this on hospitals pt1+pt2.)
- **CanonicalOrgEnrichment** — same unique-key concern across honing
  sources?
- **IntelPersonAffiliation** — natural key uniqueness; m108 + m109
  added manual rows. Are they all valid?
- **Soft-retire pattern** — `RetiredAtUtc IS NULL` filter is the
  authoritative active-set marker. Is it consistently applied across
  reads in the WPF app + MCP + report builders?
- **OpportunityBids.BidderCanonicalOrgId** (added in R187) — is it
  populated everywhere it should be? Any orphans?

### 2. Migration history (m100-m114)

Read every migration in `Kor.Opportunities.Data/Schema/` from m100
onwards. For each:
- Is `SET XACT_ABORT ON` set?
- Is the work wrapped in BEGIN TRAN / COMMIT?
- Are rollback semantics clear?
- Are PRINT messages informative for ops?
- Were any conditional UPDATEs missed (e.g., NULL guards)?
- Any survivor pick that's debatable (e.g., picked lower-Intel
  survivor by mistake)?
- m113 false-positive call on MPI 502 MacEwan/U-of-C — was the
  reasoning correct? Audit that specific decision.

### 3. Provider ingestion pipeline

- **CanadaBuys** — interest filter (R76), structural relevance gate
  (R97 / R97a-b)
- **APC** — email-alert ingestion only (no API); detail-page
  enrichment (R177-R178)
- **BcBid** — keyword filter + engineering-only source (R99);
  bcbid.keyword filter; scraper column-position assumption (R186)
- **MERX** — Defense Construction Canada notices
- **BidsAndTenders** — interest probe
- **buyandsell.gc.ca** — DCC notices for defense
- **SAM.gov** — US west coast (deferred per memory)
- **Gov of Canada Engineering** — engineering commodity codes
- **BcCanadaBuys** — false-positive substring filter pre-refactor
  (memory `project_canadabuys_filter_false_positives.md`)

For each: is the StructuralRelevanceGate applied at intake?
Is the BdOpportunityPurge nightly job effective? (Per
`project_data_retirement_lifecycle.md`, DataRetirementJob lives
2026-05-28.)

### 4. Research execution pipeline

The two paths:
- **BdResearchExecutor** (R83 / R84) — automated Sonnet research
  refresh via API
- **Sonnet drain queues** (recent) — manual Sonnet sessions consuming
  `inputs/batch-*.json` and writing `outputs/refresh-*.json`

For drain queues:
- 10 PROMPT.md templates in `tools/BdQueueDrainPrompts/` — are they
  consistent in verification rigor? Any "Yurkovich-class error"
  catches missing from any of them?
- Each honing PROMPT must verify procurement model + name
  incumbent if applicable + name decision-makers + give 12-month
  engagement timeline + named warm-intro path.
- `tools/BdQueueDrainIngest/Program.cs` — recently fixed to detect
  `[providerName: XHoning]` description marker. Any edge cases
  missed? What if `_providerName` AND description marker
  conflict?
- `tools/BdQueueDrainBatchGenerate/` — does it handle re-honing
  (item already has ProjectBriefHoning)? Was the
  NOT-EXISTS-OR-REFRESH dual-pattern carried into the tool?

### 5. WPF UI integration

- **BD Dashboard Priority Actions** (R82) — does it surface IntelAction
  with Status='Pending'? Mark-Done/Mark-Dismissed buttons?
- **Org Dossier** (R175 + R199) — Intel section above raw research
  sources, Refresh button, freshness chip
- **Project Brief** (R204) — surfaces existing Intel rows
- **Region Brief** — cross-org actionables section
- **Global search typeahead** (R88 / R202) — across MPI + Org + Person?
- **IAiContextProvider** — registered for every BD VM per
  `feedback_ai_context_provider.md`?
- **Displacement Briefs tab** (R175) — still working?

### 6. MCP / AI integration

- `/ask` endpoint = text-to-SQL gateway with `query_kor_data` tool
- Smoke-verified 2026-05-08, MCP service live on KOR-APP01
  (http://kor-app01:5500), SQL = mcp_app login, KorMcp DB
- KPI methodology duplicated in SystemPrompt per
  `feedback_mcp_kpi_methodology_drift.md` — is it still in sync
  with `Definitions.*.cs`?
- BD Phase 4 (AI tool use / function calling) — deferred. Anything
  in code suggesting it was partially started?

### 7. Report builders (12 PowerShell scripts)

- 7 category builders + 1 call sheet + 1 defense + 1 EllisDon update
  + 1 Graham brief = 12 scripts
- Recent fixes: MakeTable robustness, Safe() helper, defensive
  guard against empty shared `.all-honed.json`
- Drift between builders? Inconsistent verdict-extraction regex?
  Inconsistent data-pull queries?
- Defense builder is fully static — should it become DB-driven like
  the others?
- The 5 `.json` data files at `C:\Users\ilalonde\Desktop\Polish\.*-final.json`
  are operational shared state — fragile pattern, captured for UI
  build to fix.

### 8. DB hygiene (the long tail)

- Programmatic dedup beyond what Sonnet flagged. m110 (UHNBC 9 dupes),
  m111 (Vernon Jubilee 5), m114 (Newton 4 + Britannia 2) — high
  duplicate rate suggests intake is admitting dupes. Where's the
  intake-side dedup pass?
- 32 hospital DUPLICATEs flagged in pt1; we only resolved 5. Other
  27 still in DB.
- Recreational pt1 honing flagged 38 DUPLICATE verdicts (possibly
  regex-overcount). Audit those.
- Olympic Village Elementary appears 5x in DB (MPIs 4436, 4558,
  5260, 6843, 6845) — schools dupe candidate.
- DB-wide fuzzy duplicate audit on `(normalized name, province,
  municipality)` — surface ALL un-flagged dupes.
- Junk source rows — `feedback_clean_at_source.md` rule. Are
  source-side filters tight? (Per `feedback_canadabuys_filter_false_positives.md`,
  word-boundary fix was applied — verify still in place.)
- Province bugs — m113 caught one (UCalgary listed as BC). How many
  other Province mislabels exist? Run a sanity check on
  `(Province, MunicipalityName)` pairs.

### 9. Sister queue infrastructure

- 60 KOR-* research stream folders per
  `reference_bd_research_streams.md` — are they all still populated
  / current? Any orphan folders?
- BdResearchImport handlers: 2 tags (data-honing=orgs,
  projects-honing=projects); opps-validated has NO importer per
  `reference_bdresearchimport_honing_tags.md`
- Heartbeat + bail-out + auto-discovery in PROMPT.md — was that
  rolled out across ALL queue PROMPTs? Per
  `feedback_drain_self_discovery_heartbeat.md`, this was after a
  5h silent-failure incident.
- "Don't fan out with Workflow/Agent" rule per
  `feedback_drain_no_workflow_no_agent.md` — is it in every PROMPT?

### 10. Deltek integration

- BdDeltekLink — auto-link MPI Owner to Deltek Client
- Per `reference_customproposal_join_path.md`, CustomProposal.ClientID
  is NULL in KOR's instance — use CustomProposal.WBS1 → PR (WBS2=WBS3=' ')
  → Clendor
- Per `project_deltek_bd_fusion.md`, BD↔Deltek fusion must REUSE
  Financials → Clients/Historicals + ClientIntelligence — don't
  rebuild
- Per `reference_deltek_pursuit_tables.md`, PR table has both
  projects + pursuits differentiated by Stage. PR.LostTo =
  competitor ClientID.

### 11. Deploy / operations

- Worker on KOR-APP01 (Kor.Opportunities.Worker). Service name?
  Deploy runbook?
- Env vars on KOR-APP01 NOT dev box per
  `feedback_env_vars_run_on_server.md`
- Deploy from KOR-1001 to KOR-APP01 per
  `reference_opportunities_deploy_runbook.md`
- No reverse-proxy / TLS per `feedback_lan_only_services.md`
- Robocopy + stop-loop race per
  `feedback_deploy_script_stop_race.md` +
  `feedback_robocopy_silent_skip.md`

### 12. Backlog + deferred items

`project_bd_module_deferred_work.md` is the authoritative deferred
register. For each open item, is it still load-bearing? Anything
that became urgent but stayed in the deferred bucket?

---

## What "Critical / Major / Minor" means

**Critical**: data loss risk, security exposure, would block
production use of the in-app build, would prevent a partner from
trusting the data, FK orphans, schema-breaking constraints.

**Major**: significant correctness or UX issue but not catastrophic,
e.g., wrong DataSource attribution, decompose pipeline silently
skipping a category, IAiContextProvider not registered for a BD VM
so AI can't see the data, missing source-side filter admitting junk
into intake.

**Minor**: cosmetic, code style drift, log message phrasing,
documentation gap, dead code that should be removed.

---

## Output format

Produce a single Markdown document with this structure:

```markdown
# BD Module Audit — <date>

## Summary
- N Critical / N Major / N Minor issues identified
- Top 3 must-fix-before-WPF-build:
  1. ...
  2. ...
  3. ...

## Critical issues
### C1: <Short title>
**File / Table**: <path or sql object>
**Problem**: <what's wrong>
**Evidence**: <SQL row, code line, git diff hunk — be specific>
**Why critical**: <impact>
**Recommended fix**: <one-line action>

### C2: ...

## Major issues
### M1: ...

## Minor issues
### Mi1: ...

## Architectural concerns (not bugs)
- Pattern concern 1: ...
- Pattern concern 2: ...

## Strengths worth preserving
- The Safe()/MakeTable/Guard defensive triple in builders is
  good — apply pattern to future OpenXml generators
- Soft-retire with RetiredReason field is excellent for ops
  reconstruction
- ...

## Pre-WPF-build gate
Recommend the following be fixed BEFORE the WPF in-app report
generation build (per docs/BD-UI-Plan-2026-06-08.md) starts:
- ...
- ...
```

---

## Constraints (rules for the auditor)

These come from accumulated user feedback (in `memory/feedback_*.md`).
You will be evaluated against these:

1. **Highest standards, no demo risk** (`feedback_highest_standards_no_demo_risk.md`)
   — no "Option A is faster" framing, no shortcut suggestions
2. **No guessing** (`feedback_no_guessing.md`) — verify from source
   every claim. Don't say "this seems wrong" without showing the SQL
   row or code line.
3. **Audit before proposing** (`feedback_audit_before_proposing.md`)
   — grep for prior art before declaring something missing
4. **Clean at source** (`feedback_clean_at_source.md`) — band-aid
   filters are "amateur hour"; trace junk to the write that created it
5. **Verify docstring matches impl** (`feedback_docstring_vs_impl.md`)
6. **Never recommend wrapping up / calling it a night**
   (`feedback_dont_suggest_stopping.md`) — be exhaustive
7. **Senior architect** (`feedback_senior_architect.md`) — no
   guessing, known solutions only, one at a time
8. **Architecture first** (`feedback_architecture_first.md`) — check
   DI wiring in CompositionModules/ before touching any service;
   real stores are in Kor.Opportunities.Data (SQL Server), not
   Kor.Operations.Core
9. **Namespace ≠ assembly** (`feedback_namespace_vs_assembly.md`)
   — most Kor.Operations.Services.* classes live in Kor.Operations.App.dll
10. **Memory observations vs tasks** (`feedback_memory_observations_vs_tasks.md`)
    — old project memories are observations, not a to-do list

---

## Anti-scope (what NOT to do)

- Do NOT write any code or migrations during the audit. This is
  review-only. Produce the punch-list; user decides what gets
  fixed.
- Do NOT delete or modify any files. Read-only DB access only.
- Do NOT touch `Kor.Operations.FileSync.Service` — out of BD scope.
- Do NOT touch PdfToSafe — out of BD scope.
- Do NOT recommend pulling in ContractRadar code — that's a
  personal sibling project per `project_contractradar_relationship.md`.
- Do NOT propose new MCP tools — the SystemPrompt extension
  pattern is preferred per `project_mcp_gateway_verified.md`.

---

## Compounding-context section (read these memories specifically)

If you can read the user's auto-memory folder, prioritize reading
these files (paths given relative to memory/):

```
project_business_development_module.md
project_opportunities_module.md
project_bd_module_deferred_work.md
project_bd_platform_vision.md
project_bd_brief_feature.md
project_bd_ai_layer_deferred.md
project_opportunity_relevance_gate.md
project_data_retirement_lifecycle.md
project_r95_data_hygiene_state.md
project_r95_extra_morning_plan.md
project_alert_system_design.md
project_prime_consultant_strategy.md
project_deltek_bd_fusion.md
project_canadabuys_filter_false_positives.md
project_contractradar_relationship.md
project_email_source_vsto_ghost.md
project_kor_geographic_footprint.md
project_kor_pnl_data_sources.md
project_deltek_revenue_generation_off.md
project_deltek_tkdetail_currency.md
project_mcp_gateway_verified.md
project_mcp_production_live.md
project_ai_methodology_in_context.md
reference_bd_research_streams.md
reference_bdresearchimport_honing_tags.md
reference_deltek_schema.md
reference_deltek_pursuit_tables.md
reference_customproposal_join_path.md
reference_kor_partners.md
reference_kor_authorities.md
reference_kor_bmz_rename.md
reference_kor_deltek_orgs.md
reference_deltek_account_codes.md
reference_deltek_linkedserver.md
reference_apc_ingestion_path.md
reference_gvrd_geography.md
reference_kor_opportunities_env_var_naming.md
reference_kor_opportunities_sql_migration_db_context.md
reference_test_csproj_path.md
reference_compile_include_pattern.md
reference_kor_service_account.md
reference_filesync_service_name.md
feedback_codex_adversarial_review.md
feedback_architecture_first.md
feedback_highest_standards_no_demo_risk.md
feedback_no_guessing.md
feedback_audit_before_proposing.md
feedback_clean_at_source.md
feedback_ai_context_provider.md
feedback_lan_only_services.md
feedback_publish_to_v22.md
feedback_compile_include_pattern.md
feedback_sql_batch_column_reference.md
feedback_namespace_vs_assembly.md
feedback_verbatim_string_quotes.md
feedback_mcp_kpi_methodology_drift.md
feedback_mcp_publish_not_build.md
feedback_postingest_enrichment.md
feedback_use_existing_research_streams.md
feedback_drain_self_discovery_heartbeat.md
feedback_drain_no_workflow_no_agent.md
feedback_honing_merge_audit.md
feedback_research_context_overflow.md
feedback_use_platform_not_oneshots.md
feedback_govcanada_waf_quirks.md
feedback_top_one_percent_bar.md
```

---

## Recent commit window (the past 2 weeks)

The user's BD pipeline buildout happened in commits `c945b46..HEAD`
on branch `develop`. Highlights:

- `c945b46` fix(bd): clean junk display names at source
- m106-m107 US/AB project stage backfill
- m108 Graham strategic canonical
- m109 Defense MPI cleanup + EllisDon canonical layering
- m110 UHNBC consolidation (9 dupes → 1)
- m111 Vernon Jubilee Psychiatric consolidation (5 → 1)
- m113 Post-Sec corrections (Province bug + NAIT dupe + UVic delay)
- m114 Newton + Britannia consolidation (4 + 2 → 2)
- 10 honing PROMPT.md files added to tools/BdQueueDrainPrompts/
- 12 report builders added to tools/BdReportBuilders/
- `tools/BdQueueDrainIngest/Program.cs` description-marker
  auto-detection
- `docs/BD-UI-Plan-2026-06-08.md` forward plan
- Defensive helpers (Safe() + MakeTable chunking + Guard pattern)
  applied across builders

Verify each via git show.

---

## Final step

After producing the audit punch-list, also draft a **one-paragraph
verdict**: is the BD module in a state where the WPF in-app build
(per `docs/BD-UI-Plan-2026-06-08.md`) should start NOW, or are
there gates that must close first? Be direct. If there are gates,
list them with file paths.

This is THE pre-build gate. Don't soften the verdict.
