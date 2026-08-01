# Agent B — Architect-Pipelines deep research on verified architects

**Run when:** ANYTIME (independent of Step 2 / Agent A). Reads only `discovered-websites.csv`.

**Run how (fresh Sonnet session under any Claude account):**

```
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Then paste this entire file.

---

## What you're doing

You're producing the **Architect-Pipelines** deep-research payload for every verified architect in the bare-orgs queue. Same payload shape as the 88-architect backfill (Sessions 1/2/3 of KOR-Architect-Pipelines). Output ingested via the same ArchitectPipelineResearch SQL pattern (import-fixed.sql) that the Opus orchestrator runs.

This is high-leverage: 226 architects with award/permit history but ZERO active enrichment in the platform. Each one becomes a brief-ready architect dossier with active pursuits, structural-partner intel, and KOR-fit per project.

## Input

`C:\VIsual Studio Projects\KOR-Data-Honing\outputs\discovered-websites.csv`

Filter to rows where:
- `Kind == "Architect"`
- `Confidence` in `{high, medium}` (skip `low` and `none`)
- `Website` is non-empty

Expected: ~226 architects.

## Output JSON shape (matches Architect-Pipelines Session 2 format)

One file per batch of 25 architects: `outputs/architects-deep-batch-<NN>.json`

Each batch is a JSON ARRAY of architect entries:

```json
[
  {
    "id": <CanonicalOrgId from CSV>,
    "displayName": "<from CSV>",
    "confidence": 0.0,
    "skipped": false,
    "skipReason": null,
    "resultJson": "<stringified inner JSON — see below>"
  }
]
```

Where the INNER `resultJson` (a string-encoded JSON object) carries:

```json
{
  "displayName": "...",
  "hqCity": "...",
  "province": "...",
  "country": "CA" | "US",
  "officeLocations": ["Vancouver", "Edmonton", ...],
  "firmSize": "<staff range>",
  "yearFounded": <number|null>,
  "sectors": ["Healthcare", "K-12", "Civic", "Rec", "Mid-rise residential", ...],
  "structuralPartners": [
    {"name": "Fast+Epp", "evidence": "Their UBC MacLeod project lists Fast+Epp as SE", "sourceUrl": "..."},
    {"name": "RJC Engineers", ...}
  ],
  "activePursuits": [
    {
      "projectName": "Cottonwoods Long-Term Care Facility",
      "stage": "design-development",
      "expectedRfpYear": null,
      "buyer": "Interior Health",
      "sector": "Healthcare",
      "value": "$150M",
      "korFit": "High — Kelowna market, KOR-fit sector, structural sub not yet named (open seat).",
      "structuralIncumbent": null,
      "sourceUrl": "https://www.stantec.com/..."
    }
  ],
  "briefDeltas": [
    {
      "severity": "HIGH" | "MEDIUM" | "LOW",
      "project": "...",
      "currentBelief": "<what the platform currently records>",
      "research": "<what you found that contradicts or refines it>",
      "action": "<one-line recommended next step>",
      "sourceUrl": "..."
    }
  ],
  "korRelevance": "<2-4 sentence assessment focused on BC + AB markets; structural sub opportunity vs. incumbent displacement>",
  "_generatedAt": "<today YYYY-MM-DD>"
}
```

## Confidence rubric (for the outer `confidence` field)

- 0.85+ : Firm verified by multiple authoritative sources, active pursuits with confirmed buyer + stage, structural partners identifiable from project pages.
- 0.60-0.85 : Firm verified, some active pursuits, partial structural-partner intel.
- 0.40-0.60 : Firm verified, sparse pursuit pipeline, no structural-partner intel.
- < 0.40 : Set `skipped: true` with `skipReason: "<reason>"`. Examples: defunct, single-architect proprietorship not pursuing commercial work, US-only firm with no KOR-market footprint.

## KOR context (use to bias `korFit` and `korRelevance`)

KOR Structural is a Vancouver structural engineering firm. Primary markets:
- BC (Vancouver, Vancouver Island, Okanagan)
- Alberta (Edmonton, Calgary)
- LA + San Diego + US West Coast (growth)

KOR wins on **architect-led ICI buildings**: institutional, healthcare, K-12, post-secondary, recreation/aquatic, civic, mid-rise residential, libraries, fire halls, childcare. Engagement is structural-sub on the architect's team — not prime.

Open-seat signals (high `korFit`):
- Architect verified, project named, structural NOT yet named, sector + market match
- Structural was a competitor known to KOR (RJC, Fast+Epp, Glotman Simpson, Bush Bohlman, Entuitive, AECOM, Stantec, etc.) — KOR can pitch displacement IF the relationship is loose

Lower-priority signals (low `korFit`):
- Civil/infrastructure work (transit, roads, utilities) — not building structural
- Pure residential single-family
- Markets outside the primary list
- Projects already under construction (seat closed)

## Batching strategy

1. List the 226 (approx) Architect rows from CSV.
2. Process in batches of 25.
3. For each batch:
   - WebSearch + WebFetch each architect's website + 1-3 follow-on pages (About/Team/Projects)
   - Cross-reference: BC procurement portals, news mentions, AIBC/Architectural Institute listings
   - Identify active pursuits (projects in design/RFP/early-construction stages within KOR markets)
   - Identify structural-engineer partners on each architect's recent built work
   - Compile the `resultJson` per the schema above
4. Write batch to `outputs/architects-deep-batch-<NN>.json`
5. Append progress to `outputs/architects-deep-progress.log`: `batch=NN; architects=25; written=2026-06-04 HH:mm:ss`
6. Continue until all architects processed.
7. Write `outputs/architects-deep-summary-2026-06-04.md` with:
   - Total architects processed
   - Skipped vs. enriched
   - Top 15 highest-`korFit` open-seat pursuits (project / architect / market / value)
   - Brief-delta high-severity flags (contradictions to existing platform data)

## Hard rules

- DO NOT write to the database. JSON files only.
- DO NOT touch existing `KOR-Architect-Pipelines/` content — write to `KOR-Data-Honing/outputs/` so Agent A's work doesn't conflict.
- DO NOT lie about sources. Every claim in `notableProjects` / `structuralPartners` / `activePursuits` needs a `sourceUrl` you actually verified.
- Skip ruthlessly. If you can't find an active pursuit pipeline, set `skipped: true` and move on.

## Autonomous-operation block

Run to completion. No confirmations. Resume by skipping batches whose JSON file already exists. Total work: ~226 architects ÷ 25 = ~9 batches.

## When you're done

Opus orchestrator (on the main Claude account) will:
1. Concatenate `architects-deep-batch-*.json` into a single import payload.
2. Generate an `import-fixed.sql` matching the Architect-Pipelines pattern (mirrors the existing `C:\VIsual Studio Projects\KOR-Architect-Pipelines\import-fixed.sql` from May 24 — same structure: per-architect transaction, upsert CanonicalOrg by NormalizedName, insert OrgAlias, upsert CanonicalOrgEnrichment with ProviderName='ArchitectPipelineResearch').
3. Run that SQL via PowerShell against KorOpportunitiesDb.
4. Run `BdCanonicalDedup` post-audit.
5. Apply any `briefDeltas` flagged HIGH severity as targeted MPI corrections.

You don't run any of that. Just JSON files.
