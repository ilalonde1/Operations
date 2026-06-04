# Agent C — Alberta + Calgary/Southern AB forward-pipeline ICI projects

**Trip-prep focused.** Output goes straight into MajorProjectsInventory so Region Brief (Province=AB / City=Calgary) reflects the new pipeline.

**Run how (fresh Sonnet session under any Claude account):**

```
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Then paste this entire file.

---

## What you're doing

Build a forward-pipeline list of **upcoming ICI building projects** (next 12–24 months) across Alberta with a SPECIFIC bias toward Calgary + southern AB (Calgary, Airdrie, Cochrane, Okotoks, Strathmore, Lethbridge, Medicine Hat, Red Deer). These are the projects KOR's structural team should know about before walking into meetings next week.

ICI = Institutional / Commercial / Industrial-light buildings:
- **Healthcare** — hospitals, LTC, community health, mental health
- **K-12** — new schools, additions, replacements, seismic upgrades
- **Post-secondary** — U of C, Mount Royal, SAIT, ACAD, Lethbridge College, Medicine Hat College
- **Civic** — rec centres, aquatic centres, libraries, fire halls, community centres
- **Government** — provincial / municipal buildings, courthouses
- **Mid-rise residential** (4-12 storeys, market or affordable)
- **Modular institutional** (childcare, supportive housing, modular schools)

NOT in scope: highway / civil / transit / utilities / industrial heavy / single-family / strip retail / pure interior fit-out / energy upgrades that aren't structural.

## Owner targets (work through this list systematically)

**City + Region (Calgary + Southern AB priority):**
- City of Calgary (CapEx plan, RFPs)
- City of Lethbridge
- City of Medicine Hat
- City of Red Deer
- City of Airdrie
- Towns: Cochrane, Okotoks, Strathmore, Chestermere, Crowsnest Pass, High River
- Counties: Rocky View County, Foothills County, MD of Bighorn

**School Boards (Calgary + southern AB priority):**
- Calgary Board of Education (CBE)
- Calgary Catholic School District (CCSD)
- Rocky View Schools
- Foothills School Division
- Lethbridge School Division
- Holy Spirit Catholic (Lethbridge)
- Palliser Regional Schools
- Medicine Hat Public + Catholic
- Red Deer Public + Catholic

**Health:**
- Alberta Health Services — Calgary Zone (heavy priority)
- AHS South Zone (Lethbridge, Medicine Hat)
- AHS Central Zone (Red Deer)
- Covenant Health Calgary

**Post-Secondary:**
- University of Calgary (capital plan + IMP)
- Mount Royal University
- SAIT
- Alberta College of Art + Design (ACAD)
- Lethbridge College
- University of Lethbridge
- Medicine Hat College
- Red Deer Polytechnic

**Other institutional:**
- Calgary Public Library
- Calgary Catholic Immigration Society / Bow Valley College
- Calgary Foundation
- Alberta Infrastructure (provincial)

**Provincial-wide secondary (Edmonton + central, lower priority but include):**
- City of Edmonton, AHS Edmonton Zone, Edmonton Public Schools, Edmonton Catholic, U of A, MacEwan, NAIT, Strathcona County, St. Albert, Sherwood Park

## Per-project research

For each project identified, capture:

```json
{
  "projectName": "...",
  "owner": "<canonical owner name — e.g. City of Calgary, AHS Calgary Zone, CBE>",
  "ownerType": "Buyer" | "Developer",
  "sector": "Healthcare" | "K-12" | "Post-secondary" | "Recreation" | "Civic" | "Housing" | "Government" | "Institutional",
  "subSector": "<more specific — e.g. LTC, Community-rec, Library, Fire-hall>",
  "city": "Calgary" | "Lethbridge" | ...,
  "region": "Southern AB" | "Central AB" | "Northern AB" | "Calgary metro" | "Edmonton metro",
  "estimatedCostCad": <number|null>,
  "estimatedCostText": "<original text if range or qualitative>",
  "stage": "Concept" | "Planning" | "Design" | "RFP-Issued" | "Tender-Pending" | "Construction-Awarded" | "Under-Construction",
  "expectedRfpYear": <number|null>,
  "expectedConstructionStart": "<YYYY-MM or null>",
  "expectedCompletion": "<YYYY-MM or null>",
  "architect": "<name or null if not yet announced>",
  "structuralEngineer": "<name or null — IMPORTANT for open-seat identification>",
  "generalContractor": "<name or null>",
  "publicFundingInd": true,
  "indigenousInd": false,
  "korFit": "<one-line: High / Medium / Low + reason. High = KOR market + sector + open seat. Medium = KOR market + sector but structural already named. Low = wrong market or out-of-scope sector>",
  "sourceUrl": "<verified URL where the project is documented>",
  "sourceUrls": ["<all corroborating URLs>"],
  "notes": "<distinctive context: open seat, displacement opportunity, KOR pre-existing relationship, etc.>"
}
```

## Confidence + priority guidance

Prioritize projects where:
- **Calgary or southern AB market** (highest weight — that's where the trip is)
- **Structural NOT yet announced** (open seat — KOR can pitch)
- **Sector matches KOR's sweet spot** (K-12, healthcare, recreation, civic, mid-rise)
- **Value $5M–$150M** (KOR's winnable scale, not mega-P3)
- **Stage = Planning / Design / Pre-RFP** (KOR can court the architect now)

De-prioritize:
- Projects already under construction (structural seat closed)
- Mega-P3 projects ($500M+ where Stantec/PCL/RJC are entrenched)
- Civil/transit/industrial work outside building scope

## Output

Write batches of 25 projects per file to:
- `outputs/alberta-pipeline-batch-001.json`
- `outputs/alberta-pipeline-batch-002.json`
- ...

Each file is a JSON ARRAY of project objects per the schema above.

After each batch append to `outputs/alberta-pipeline-progress.log`:
```
batch=NN; projects=25; calgary=12; lethbridge=2; ...; written=2026-06-04 HH:mm:ss
```

When done, write `outputs/alberta-pipeline-summary-2026-06-04.md` with:
- Total projects identified
- Breakdown by market (Calgary metro vs. Southern AB vs. Central AB vs. Edmonton metro)
- Breakdown by sector
- **Top 20 highest-`korFit` open-seat opportunities** with one-line summary per project (this is the trip-prep gold)
- Owners with the most upcoming work (helps Ian's team prioritize which owners to brief on KOR)

## Hard rules

- Verify every project from a real source (owner capital plan, news article, RFP listing, council minutes). NO HALLUCINATION.
- Every claim needs a `sourceUrl` you actually fetched.
- If a project is named in multiple sources but stage / value / dates differ, pick the most recent and note discrepancies in `notes`.
- Skip rumors. Skip "may be considered". Only projects with documented owner commitment OR clear planning evidence.
- Skip projects under construction unless they have a Phase 2 / follow-on the team is courting.

## Autonomous-operation block

Run to completion. No confirmations. Resume by skipping batches whose JSON file already exists. Target: ~150-300 projects total identified across all batches. If you can't find 25 valid projects for a batch, write fewer and continue (don't pad with rumors).

## Ingestion (after you finish — Opus orchestrator does this)

Opus will:
1. Concatenate `alberta-pipeline-batch-*.json` into a single payload.
2. For each project, resolve the architect/owner/SE/GC against CanonicalOrg (allowCreate=true for new firms).
3. Upsert MajorProjectsInventory rows with Source='AlbertaForwardPipeline-2026-06' and SourceKey from a hash.
4. Run BdCanonicalDedup post-audit.

Region Brief for Province='AB' / City='Calgary' will then surface the new pipeline automatically.
