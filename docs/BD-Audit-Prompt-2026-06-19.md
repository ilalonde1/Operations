# BD Module — Comprehensive Adversarial Audit Prompt (2026-06-19)

> **For an independent fresh session (Codex / Claude Opus, no shared context).**
> Self-contained. Audit the **entire BD module from the actual code + live
> database state — never from this prompt's narrative.** Every claim you make
> must cite a SQL row, a code line, or a git hunk.

---

## Mission

Adversarial deep-review the **entire BD (Business Development) module** of the
KOR Operations app and produce a prioritized punch-list (Critical / Major /
Minor) plus architectural concerns and a blunt foundation verdict.

This audit exists because the module just went through ~10 days of very heavy
change (see "What changed since the last audit") and the owner's explicit bar
is: **"I need this foundation clean and solid"** before the next phase (the AI
layer / Arc 7 BD MCP tools, the Deltek↔BD fusion, and commercial/scale use).
Anything you miss becomes load-bearing for that next phase.

**Build on the prior audit, don't repeat it.** A full audit was done
2026-06-09 → `docs/BD-Audit-2026-06-09.md` (9 Critical / 21 Major / 18 Minor).
For each prior finding: is it **fixed** (cite the migration/commit), **still
open**, or **regressed**? Do not re-report a prior finding as new without
checking. Focus your net-new effort on everything that landed *after* that
audit.

---

## Business context

**KOR Structural** — Vancouver structural engineering firm (was Bryson
Markulin Zickmantel / BMZ before the 2021 rebrand). Markets today: BC + LA +
San Diego; active growth pushes into **California (SF/Sacramento/East Bay/LA/SD)**
and Edmonton/AB. On public work KOR wins as the architect's structural sub
("prime consultant" = the architect). CA has a hard licensing gate (no BC
reciprocity; needs a CA-licensed SE to stamp).

The BD module captures opportunities across public + private construction,
enriches them with intelligence (decision-makers, procurement model,
competitors, SE-seat status), dedups/canonicalizes the org + person graph,
fuses to Deltek history, and surfaces actionable pursuits + a CRM to KOR
partners (John Bryson, Jim Desroches, Omar, Conor, Islam, Rory, Kevin, Andrea;
Ian Lalonde = ops lead / builder).

---

## Scope: what is the BD module

### Code projects
```
Kor.Opportunities.Core/      interfaces, models, contracts
Kor.Opportunities.Data/      SQL schema (228 migrations), EF stores, query helpers,
                             CanonicalOrgResolver, IntelNaturalKey, BdReports generators
Kor.Opportunities.Worker/    background worker on KOR-APP01 (provider ingestion cron,
                             research executor, retirement job, enrichment chokepoint)
Kor.Opportunities.Capture/   web scraping / API capture utils
Kor.Opportunities.ApcImport/ Alberta Purchasing Connection email-alert ingest
```
BD code inside the WPF app:
```
Kor.Operations.App/BusinessDevelopment/{Briefs,Reports,Workspace}/
Kor.Operations.App/Crm/                  (CRM — now LIVE, see dimension below)
Kor.Operations.App/CompositionModules/   (DI wiring + IAiContextProvider registration)
```
MCP gateway BD AI flows through:
```
Kor.Operations.Mcp/   text-to-SQL /ask gateway, KorMcp DB; BD vertical tools (Arc 7) DEFERRED
```

### Tools (operational CLIs/scripts) — note the growth since last audit
```
tools/BdCanonicalDedup/    programmatic org dedup; has a FUZZY-NAME SIMILARITY GATE +
                           source-controlled override file dedup-non-similar-allowlist.csv
tools/BdTrackingImport/    imported Jim's tracking spreadsheet into CrmEngagements
tools/BdQueueDrainIngest/ + BdQueueDrainBatchGenerate/ + BdQueueDrainPrompts/
tools/BdResearchImport/, BdIntelExtract/, BdContactEnrich/, BdDeltekLink/
tools/BdOpportunityPurge/, BdOrphanOrgPurge/, BdPersonBriefRepair/, BdVerdictBackfill/
tools/BdPrimesRecovery/, BdSeedImport/, BdHoningIntelBackfill/, BdReportBuilders/
tools/BcMpiImporter/, BcBidInterestProbe/, ApcInterest*/, GovCanEngineeringImport/
(+ *Smoke test harnesses)
```

### Database
- **KorOpportunitiesDb**, schema `opportunities.*`, env var
  `KOR_OPPORTUNITIES_OPPORTUNITIESDB` (read-only for this audit).
- **228 migrations** in `Kor.Opportunities.Data/Schema/` (prior audit covered
  through ~114 — focus on 115→228).
- Core tables: MajorProjectsInventory, CanonicalOrg, OrgAlias, IntelPerson,
  IntelPersonAffiliation, IntelSignal/Action/Work/Risk/Narrative,
  IntelProject{Action,Signal,Risk,KeyPerson}, MajorProjectEnrichment,
  CanonicalOrgEnrichment, OpportunityBids/Awards/InterestedFirms, KorPursuits,
  **CrmEngagements / CrmContacts / CrmActivities / CrmEngagementProjectLink**,
  ArchitectDisplacementBriefs, NewsArticle*, JobRuns/JobSchedules/IngestionRuns.

### Out of scope
FileSync Command Center; PdfToSafe; Email Search; Deltek revenue/billing logic;
ContractRadar (personal sibling project — do not pull its code in).

---

## What changed since the last audit (verify each — don't trust this list)

1. **~113 new migrations (115→228).** Read them all. Many are data-only
   `chore(bd-dedup)` merges, not schema.
2. **Seven waves of firm-family canonicalization + JV-string decomposition**
   (commits `fc41a00a` wave1 → `010376f2` wave7; `20e32028`, `9facf381`,
   `e6a16258`, `a608f2b0`, `8bcd552d`, `fc7d98a0`, `8ffc3edf`). **Hundreds of
   org/person merges.** This is the single biggest net-new surface. The owner's
   standing rule (`feedback_honing_merge_audit`) is that BdCanonicalDedup has
   shipped **wrong SurvivorIds twice** — so every survivor pick and FK repoint
   here is suspect until verified.
3. **Dedup apparatus reshaped** (`project_bd_apparatus_audit_2026_06_15`): the
   nightly Worker dedup job was **DISABLED**, a **DB frozen-anchor trigger** was
   added, and `BdCanonicalDedup` got the fuzzy-similarity gate + allowlist. A
   Worker dedup rewrite + a concurrency app-lock were **deferred**. Audit
   whether this set is coherent and gap-free.
4. **`5e424ebe` fix(bd-dedup): repoint IntelPersonAffiliation on merge instead
   of deleting** — verify no affiliations were lost in merges committed *before*
   that fix.
5. **California subsystem** (~25 `feat(bd-ca)` commits): City-of-San-Diego CSV +
   CEQAnet providers, SF address-name recomputation, open-seat pursuit pipelines,
   **Apollo email/title enrichment** for CA contacts. `feedback_apollo_org_match_verify`
   warns Apollo returns same-name people at the WRONG company — verify CA
   contact↔org bindings.
6. **CRM is now live** — `BdTrackingImport` loaded Jim's spreadsheet into
   `CrmEngagements`, and a 2026-06-19 session added engagements/contacts/
   activities. Previously dormant; now a real surface to audit.
7. **Region taxonomy normalized + funnel/dedup gate fix** (migration ~177,
   `project_bd_funnel_dedup_gate_fix`, `project_bd_enrichment_2026_06_15`):
   regional funnels were inflated by dup MPI rows + gate gaps.
8. **IntelPerson NaturalKey is name-only** (`SHA1(NormalizeName(displayName))`
   in `Kor.Opportunities.Data/Intel/IntelNaturalKey.cs`). Two different people
   with the same name collide on the unique key (hit 2026-06-19 with a "John
   Wu" at two firms). Architectural limitation — assess blast radius.

---

## Authoritative state — where to find facts

**Verify everything. Never trust narrative (including this prompt's).**

### Git
```
git log --oneline --since="2026-06-09"
git log --oneline -- Kor.Opportunities.Data/Schema/          # migration history
git show <hash>                                              # inspect each dedup wave
git log --oneline -- tools/BdCanonicalDedup/                 # dedup tool + allowlist history
```

### Database (READ-ONLY)
Connection string in `KOR_OPPORTUNITIES_OPPORTUNITIESDB`. Run your own counts —
never trust claimed row counts. Useful starting queries:
```sql
-- duplicate orgs still in the graph (post-dedup): same normalized name, not retired
SELECT NormalizedName, COUNT(*) c, STRING_AGG(CAST(Id AS varchar(12)),',') ids
FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NULL
GROUP BY NormalizedName HAVING COUNT(*) > 1 ORDER BY c DESC;

-- orphan / dangling FKs after the merge waves (repeat per child table)
SELECT 'IntelPersonAffiliation' t, COUNT(*) FROM opportunities.IntelPersonAffiliation a
LEFT JOIN opportunities.CanonicalOrg o ON o.Id=a.CanonicalOrgId WHERE o.Id IS NULL
UNION ALL SELECT 'OrgAlias', COUNT(*) FROM opportunities.OrgAlias a
LEFT JOIN opportunities.CanonicalOrg o ON o.Id=a.CanonicalOrgId WHERE o.Id IS NULL;

-- IntelPerson name-key collisions (different people, same normalized name)
SELECT NormalizedName, COUNT(*) c FROM opportunities.IntelPerson
WHERE RetiredAtUtc IS NULL GROUP BY NormalizedName HAVING COUNT(*) > 1;

-- CRM engagements: duplicates per buyer org, null owners/regions, dangling buyer FK
SELECT BuyerCanonicalOrgId, COUNT(*) c FROM opportunities.CrmEngagements
GROUP BY BuyerCanonicalOrgId HAVING COUNT(*) > 1;

-- province / municipality sanity (m113 caught U-of-C mislabeled BC)
SELECT Province, COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL GROUP BY Province ORDER BY 2 DESC;
```

### Files to read
- Every migration `Kor.Opportunities.Data/Schema/115..228`.
- `Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs` (NormalizeName /
  NormalizeForFuzzyMatch — the dedup gate's truth source) and
  `Kor.Opportunities.Data/Intel/IntelNaturalKey.cs` + `IntelPersistenceService.cs`.
- `tools/BdCanonicalDedup/Program.cs` + `dedup-non-similar-allowlist.csv`
  (every allowlisted gate-bypass pair is a manual override — spot-check that
  each is a genuine same-entity pair, not an Abbotsford-SD-class mistake).
- The DB frozen-anchor trigger (find it: `SELECT name FROM sys.triggers`).
- `Kor.Opportunities.Worker/` Program + composition (which jobs are enabled vs
  disabled; the dedup job should be OFF).
- `Kor.Operations.App/Crm/*` and `Kor.Operations.App/CompositionModules/*`
  (IAiContextProvider registration for every BD + CRM VM).
- The prior audit `docs/BD-Audit-2026-06-09.md` (fixed-vs-open cross-check).

### User memory (read these — they encode constraints + state)
```
project_business_development_module        project_opportunities_module
project_bd_module_deferred_work            project_bd_platform_vision
project_bd_apparatus_audit_2026_06_15      project_bd_funnel_dedup_gate_fix
project_bd_enrichment_2026_06_15           project_bd_data_hygiene_2026_06_16
project_california_bd_initiative           project_ca_prime_consultant_reality
project_opportunity_relevance_gate         project_data_retirement_lifecycle
project_bd_ai_layer_deferred               project_arc7_bd_tools_next
project_deltek_bd_fusion                   project_prime_consultant_strategy
project_bc_seismic_procurement_channels    project_kor_seismic_credential_reality
reference_intelperson_ingest_contract      reference_bd_research_streams
reference_bdresearchimport_honing_tags     reference_deltek_odbc_access
feedback_honing_merge_audit                feedback_apollo_org_match_verify
feedback_clean_at_source                   feedback_no_guessing
feedback_audit_before_proposing            feedback_ai_context_provider
feedback_open_seat_stage_gate              feedback_highest_standards_no_demo_risk
```

---

## Review dimensions (Critical / Major / Minor for each)

1. **The dedup campaigns (waves 1–7 + JV decomposition) — TOP PRIORITY.**
   Sample survivor picks across waves: is the survivor the right legal entity
   (not a fragment / not a JV-string)? Did every child FK repoint
   (IntelPersonAffiliation, OrgAlias, Enrichment, OpportunityAwards/Bids/
   InterestedFirms, KorPursuits, MPI role-FKs, CrmEngagements.BuyerCanonicalOrgId)?
   Any affiliations *deleted* (not repointed) before `5e424ebe`? Any developer-arm
   vs GC-arm of the same brand wrongly fused (or correctly kept separate)?
2. **Dedup-system coherence.** Nightly dedup job OFF? Frozen-anchor trigger:
   what exactly does it lock, and can the merge tool or enrichment bypass it?
   The allowlist: each row is a manual gate-bypass — are they all real
   same-entity pairs? Is there still no concurrency app-lock (deferred), and
   what's the race window?
3. **Residual duplicates.** After all the merges, run the duplicate queries
   above. How many same-normalized-name active orgs / people remain? Is intake
   still admitting dupes (clean-at-source), or is dedup forever chasing its tail?
4. **IntelPerson name-key collision.** Quantify how many real same-name/diff-org
   collisions exist or are silently suppressed; assess whether the name-only
   NaturalKey is safe for the contact graph the CRM + AI will rely on.
5. **CRM integrity (now live).** CrmEngagements/Contacts/Activities: dup
   engagements per org, null/invalid OwnerStaffId or Region (valid set:
   Vancouver/LowerMainland, VancouverIsland, Alberta, Okanagan-BcInterior, USA,
   EasternCanada), Stage enum validity (Drafting=1/Submitted=3/Won=6/Lost=7),
   dangling BuyerCanonicalOrgId after merges, BdTrackingImport correctness.
6. **California subsystem.** New providers (City of SD CSV, CEQAnet) — relevance
   gate applied at intake? SF address-naming regression guard real? Apollo
   enrichment: verify a sample of CA contact↔org bindings (wrong-company risk).
   Open-seat pursuits respect the stage gate (`feedback_open_seat_stage_gate`:
   "open" only if EARLY-stage)?
7. **Data model integrity** (carry forward prior dimension): Intel* FK soundness
   vs retired MPIs; unique-constraint collisions on MajorProjectEnrichment /
   CanonicalOrgEnrichment / IntelPersonAffiliation natural keys; soft-retire
   (`RetiredAtUtc IS NULL`) applied consistently across app + MCP + builders.
8. **Migrations 115–228 hygiene.** `SET XACT_ABORT ON`, BEGIN/COMMIT, NULL
   guards, debatable survivor picks, batch-separation where a column is added
   then referenced (`feedback_sql_batch_column_reference`).
9. **Provider ingestion + relevance gate.** StructuralRelevanceGate at intake
   for every provider (incl. new CA ones); BdOpportunityPurge / DataRetirementJob
   effective; CanadaBuys word-boundary filter still in place.
10. **Research / drain pipeline.** PROMPT.md consistency (heartbeat, bail-out,
    auto-discovery, no-Workflow/Agent rule); ingest marker edge cases.
11. **WPF UI + AI context.** IAiContextProvider registered for every BD + CRM VM
    (`feedback_ai_context_provider`); dossier/brief/region/dashboard reads use
    the active-set filter; CRM VM wiring.
12. **MCP / AI readiness.** KPI methodology still in sync between SystemPrompt
    and `Definitions.*.cs` (`feedback_mcp_kpi_methodology_drift`); any partial
    Arc-7 / Phase-4 code that shouldn't be there yet.
13. **Deltek fusion.** BdDeltekLink correctness; reuse Financials→Clients path,
    don't rebuild KPIs; DeltekClientId linkage soundness post-merge.
14. **Backlog.** `project_bd_module_deferred_work` — any deferred item now
    load-bearing / urgent?

---

## Critical / Major / Minor

- **Critical**: data-loss risk, FK orphans, a wrong merge that fused two real
  distinct entities (or split one), security exposure, anything that makes a
  partner distrust the data or blocks the AI/Deltek phase.
- **Major**: real correctness/UX issue, not catastrophic — wrong attribution,
  a provider skipping the relevance gate, an unregistered IAiContextProvider, a
  source-side filter admitting junk, a CRM dup.
- **Minor**: cosmetic, style drift, log phrasing, doc gap, dead code.

## Output format

Single Markdown doc → `docs/BD-Audit-2026-06-19.md`:
```markdown
# BD Module Audit — 2026-06-19
## Summary  (N Critical / N Major / N Minor; top 3 must-fix-before-AI/Deltek-phase)
## Prior-audit reconciliation  (each 2026-06-09 finding: fixed <cite> / open / regressed)
## Critical issues   ### C1 — title / File|Table / Problem / Evidence (SQL row|code line|git hunk) / Why critical / Recommended fix
## Major issues      ### M1 ...
## Minor issues      ### Mi1 ...
## Architectural concerns (not bugs)
## Strengths worth preserving
## Foundation verdict
```

## Constraints (you are evaluated against these)
1. **No guessing** — every claim cites a SQL row / code line / git hunk.
2. **Clean at source** — trace junk to the write that created it; band-aid
   filters are not acceptable.
3. **Audit before proposing** — grep for prior art before calling anything
   "missing"; cross-check the 2026-06-09 audit.
4. **Highest standards** — no "Option A is faster" framing, no shortcut suggestions.
5. **Honing-merge skepticism** — survivor picks are guilty until verified.
6. **Verify docstring matches impl.**
7. **Be exhaustive** — do not soften, do not wrap up early.

## Anti-scope
- **Read-only.** Write NO code, NO migrations, modify/delete NO files, make NO
  DB writes. Produce the punch-list only.
- **Do NOT run `dotnet build` or `dotnet test`** — the Codex environment hangs
  on them, and this is static/DB analysis anyway. Read the code; query the DB.
- No destructive git/DB ops. Don't touch FileSync / PdfToSafe / Email Search.
- Don't propose new MCP tools (SystemPrompt-extension pattern is preferred).

## Final step
End with a blunt one-paragraph **foundation verdict**: is the BD data + apparatus
clean and solid enough to start the next phase (Arc-7 AI BD tools + Deltek↔BD
fusion + scale), or are there gates that must close first? If gated, list each
gate with its file/table/migration. Do not soften it.
```
