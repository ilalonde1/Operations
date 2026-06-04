# Agent A — Polish gathered evidence into structured enrichment JSON

**Run when:** Step 2 (PowerShell deep-dive) has finished populating `KOR-Data-Honing/outputs/gathered-evidence-2026-06-03/evidence/evidence-<id>.json` files.

**Run how (in a fresh Sonnet session, can be a different Claude account):**

```
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Then paste this entire file.

---

## What you're doing

You're polishing pre-gathered raw web evidence into structured enrichment JSON. The expensive work (web search + page fetch + MX + Wayback + OpenCorporates + federal contracts lookup) is already done — each `evidence-<id>.json` already contains:

- `website`, `websiteApex`
- `homepage.Title / Description / TextSnippet / UsefulLinks / Emails / Phones / Addresses`
- `followonPages[]` — up to 5 About/Contact/Projects/Team pages with their own extracted content
- `mxLookup.HasMx` (defunct signal if false)
- `wayback.Available / Timestamp` (last-seen-alive timestamp)
- `openCorporates.Matches[]` — registration status, jurisdiction, incorporation date
- `federalContracts.Found` — govcanadacontracts.ca presence (proves firm is/was real)

You synthesize. No web search needed — work from the raw text already in each evidence file.

## Output JSON shape (per firm)

Match Session 5's exact shape so the existing ingest script handles it:

```json
{
  "id": 46646,
  "displayName": "<from evidence file>",
  "kind": "<from evidence file>",
  "website": "<from evidence.website>",
  "hqCity": "<infer from homepage Addresses / followonPages>",
  "province": "<2-letter code, inferred from address>",
  "country": "<2-letter code>",
  "offices": ["..."],
  "sectors": ["..."],
  "keyPeople": [{"name": "...", "title": "...", "sourceUrl": "<URL of page where found>"}],
  "notableProjects": ["..."],
  "korRelevance": "<2-4 sentence assessment: BC/AB market fit, sector overlap with KOR, structural sub vs prime opportunity, who to call>",
  "dataIssues": ["..."],
  "sourceUrls": ["<evidence.website>", "<first followonPage URL>", "..."],
  "_providerName": "ContractorResearch" | "ArchitectPipelineResearch" | "DeveloperPipelineResearch" | "PublicSectorResearch" | "CompetitorProfile",
  "_confidence": 0.0,
  "_generatedAt": "<today YYYY-MM-DD>"
}
```

**`_providerName` routing by Kind:**
- GC → `ContractorResearch`
- Architect → `ArchitectPipelineResearch`
- Developer → `DeveloperPipelineResearch`
- Buyer → `PublicSectorResearch`
- Competitor → `CompetitorProfile`

**`_confidence` heuristic** (you choose 0.0-1.0):
- 0.9+ : homepage clearly identifies firm, multiple follow-on pages corroborate, OpenCorporates confirms active registration
- 0.7-0.9 : homepage + at least one follow-on, MX live, no contradictions
- 0.5-0.7 : sparse evidence (homepage only, no follow-on captured, missing About/Contact text)
- < 0.5 : evidence weak or contradictory — log a dataIssue

**`korRelevance` framing:**
KOR is a Vancouver-based structural engineering firm operating in BC, AB, LA, San Diego, and the US West Coast. Markets prioritized: Vancouver Island, Okanagan, Edmonton, Calgary, Metro Vancouver. KOR wins on architect-led ICI building work (institutional, healthcare, education, recreation, civic, mid-rise residential) as structural sub on the architect's team. PCL/EllisDon/etc. are GCs to track for architect-pursuit signals, not direct BD targets.

## Batching strategy

1. List all `evidence-*.json` files in `C:\VIsual Studio Projects\KOR-Data-Honing\outputs\gathered-evidence-2026-06-03\evidence\`.
2. Process in batches of 30 firms.
3. For each batch:
   - Read all 30 evidence files (Read tool)
   - Synthesize the 30 enrichment payloads
   - Write the batch as a single JSON array to `outputs/polished-batch-<NN>.json` (e.g. `polished-batch-001.json`, `polished-batch-002.json`)
4. After each batch, append a one-liner to `outputs/polish-progress.log` so a crash mid-run is recoverable: `batch=NN; firms=30; written=2026-06-04 HH:mm:ss`.
5. Continue until all evidence files processed.
6. Write a final `outputs/polish-summary-2026-06-04.md` with:
   - Total firms polished
   - Distribution by provider (ContractorResearch / ArchitectPipelineResearch / etc.)
   - Confidence distribution (mean, min, max)
   - Notable findings: top 10 highest-BD-relevance firms with one-line summary each
   - Any dataIssues flagged across the batch

## Autonomous-operation block

Run to completion. No confirmations. Resume by skipping already-existing `polished-batch-NN.json` files. If a single evidence file is malformed, log it in the summary's "dataIssues" line and continue. Total work: ~1,308 firms ÷ 30 = ~44 batches.

## Idempotency

Already-existing batch files are NOT overwritten. To re-process a batch, delete the corresponding `polished-batch-NN.json` first. The progress log is append-only — multiple runs leave a complete history.

## When you're done

The Opus orchestrator (on the other Claude account) will run a one-shot PowerShell that:
1. Reads all `polished-batch-*.json` files
2. Concatenates entries
3. Routes each entry by `_providerName` into `CanonicalOrgEnrichment` (same shape as Session 5 ingest at `C:\Users\ilalonde\AppData\Local\Temp\ingest_bare_orgs.ps1`)
4. Runs `BdCanonicalDedup` post-audit

You don't write to DB. You don't touch CanonicalOrg. Just JSON files.
