# Codex prompt — BD Module Comprehensive Gap Analysis (READ-ONLY)

**Goal:** Produce a comprehensive, QUANTIFIED gap-analysis report of KOR's Business Development
module — the `KorOpportunitiesDb` data **and** the ingestion / resolver / dedup / importer code —
so we can clean and solidify it to production quality.

**THIS IS READ-ONLY ANALYSIS.** Do NOT modify any data, run any migration, change any code, or run
`dotnet build`/`dotnet test`. Query the DB read-only; read the code. The ONLY file you write is the
report. Do not propose to auto-fix — this is analysis for human-gated remediation.

**DB:** connection string is in env var `KOR_OPPORTUNITIES_OPPORTUNITIESDB` (SQL Server, schema
`opportunities`). Read-only queries only.

**Context:** ~51k `CanonicalOrg`, ~10k `MajorProjectsInventory`, ~9k `IntelPerson`, 117
`OpportunitySources`. Orgs enter via ingestion providers → `CanonicalOrgResolver`
(`Kor.Opportunities.Data/Awards/CanonicalOrgResolver.cs`, alias→strict-normalized→create).
Research enrichment via `tools/BdResearchImport` (~6,300-line monolith, 30+ `--only <tag>` branches,
each with a bespoke payload schema) + `IntelPersistenceService`. Dedup/merge via
`tools/BdCanonicalDedup`. KOR wins as the architect's/GC's structural sub; the highest-value signal
is **open structural-engineer seats** on projects.

For EVERY finding give: **severity** (Critical/Major/Minor), **evidence** (DB counts *with example
ids*, or code `file:line`), and a **concrete remediation**. Numbers, not adjectives.

## 1. Duplicate & data-quality landscape (DB)
- Fuzzy-duplicate **clusters** among BD-relevant orgs (`Kind` in Developer/GC/Architect/Competitor/
  Buyer/KorClient/Subcontractor, `RetiredAtUtc IS NULL`). Normalize by stripping corporate suffixes
  (Ltd/Inc/LP/Corp/Co/Limited/Incorporated/Architects/Architecture/Group/Partners) + punctuation/
  spaces — mirror `NormalizeForFuzzyMatch`/`NormalizeAggressiveKey` in `CanonicalOrgResolver.cs` — and
  group. Report: # clusters, total orgs involved, and the **top 30 worst clusters** (ids + names +
  kinds), flagging the survivor each should merge to.
- **Mislabeled** orgs: Vendor/Unknown rows referenced as Proponent/Architect/GC/StructuralEngineer
  on non-retired `MajorProjectsInventory` (they should be Developer/Architect/GC/Competitor). Count +
  top 30 (id, name, current Kind, role played, suggested Kind).
- **Name-integrity messes**: DisplayNames that concatenate multiple org names (multiple
  "Authority"/"Ltd"/"Inc" tokens jammed together — e.g. id 794), JV-strings. Count + examples.
- Barren BD orgs (`Website IS NULL`, not `Notes LIKE 'WebSearchNotFound:%'`) by Kind.

## 2. Ingestion coverage + source health (DB)
- All `OpportunitySources` (`IsEnabled=1`) joined to `IngestionRuns`: last run, success/failure
  counts, rows produced. Identify ENABLED-but-DEAD sources (no recent successful run / 0 rows) — silent
  gaps. One row per source.
- Geographic coverage of `MajorProjectsInventory` + `Opportunities` by Province + a Northern probe
  (NWT/Yukon/Northern BC/Northern AB). Quantify the holes.
- `BuildingPermit` coverage by City (confirm/deny Vancouver-only).

## 3. Extraction completeness — "are we pulling everything?" (code)
- Review active providers (`Kor.Opportunities.Data/Ingestion/Providers/*`, `.../Scraping/*`). For
  each: what the SOURCE exposes vs what we PERSIST. Flag dropped high-value fields (architect/GC/SE/
  value/contacts available but not captured).
- `tools/BdResearchImport`: build a **lossiness map** of the `--only` tags — per project/org tag,
  which high-value fields (StructuralEngineer, GeneralContractor, seatStatus, owner, contacts, focus)
  it reads vs drops. Flag the lossy tags (e.g. `bc-dev` resolves only proponent+architect).

## 4. Pipeline integrity (code)
- `BdCanonicalDedup` `FkTargets`: cross-check against **all** FK columns referencing `CanonicalOrg`
  in the DB (`sys.foreign_keys`). Any CanonicalOrg-referencing FK NOT handled by repoint or the
  `IntelDeleteTargets` set = silent merge-breakage. List any missing.
- `CanonicalOrgResolver`: where strict-normalized match vs the fuzzy/aggressive key diverge such that
  the resolver CREATES a dup the dedup tool must later merge (the dup-creation cycle). Pinpoint it.

## 5. Missing ingestion sources + pursuit-signal completeness
- Cross-reference the research recs (`docs/KOR-Events-IngestSources-Research-2026-06-21.md`,
  `docs/KOR-Northern-*-2026-06-21.md`) against configured `OpportunitySources` — list high-value
  sources we're NOT ingesting (municipal building permits beyond Vancouver; NWT/Yukon procurement;
  Infrastructure BC pipeline; health-authority capital; First Nations) with type + URL + what they'd
  surface.
- `SeatStatus`/`StructuralEngineerCanonicalOrgId` coverage on active MPI rows: what % capture the
  structural seat vs blank.

## Output
Write the full report to `docs/BD-Module-GapAnalysis-2026-06-21.md`, structured by the 5 dimensions,
every finding severity-ranked with hard evidence (counts+ids or file:line) and a concrete remediation.
Open with an executive summary listing the **top 10 highest-priority gaps**. Be skeptical and precise:
flag anything you could not verify rather than asserting it.

**Constraints:** READ-ONLY DB; no migrations; no data writes; no code changes; no build/test. Write
only the one report file.
