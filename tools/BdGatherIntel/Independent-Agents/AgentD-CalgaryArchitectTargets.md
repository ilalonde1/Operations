# Agent D — Calgary + Southern AB architect targets + active pursuits

**Trip-prep focused.** Output goes into ArchitectPipelineResearch enrichment so OrgDossier (architect tab) and Region Brief surface active-pursuit intel per Calgary architect.

**Run how:**

```
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Paste this entire file.

---

## What you're doing

Build a deep-research dossier on the **top 30 Calgary + southern AB ICI architects** Ian's BD team should know cold before next week's trip. For each: active pursuits with KOR-fit per project, structural-partner relationships (who their incumbent SE is — that's the seat KOR competes for), recent built work + recognition, decision-makers, and a one-line "the ask" per firm.

This is THE prep material for the trip. Quality over quantity — 30 deep dossiers beats 100 thin ones.

## Architect targets (work through this list; add any you discover during research)

### Tier 1 — Calgary metro ICI heavy hitters (priority)
1. **NORR Architects (Calgary office)** — large integrated A&E with healthcare/civic focus
2. **Gibbs Gage Architects** — ICI heritage / institutional
3. **GEC Architecture** — corporate / institutional / mixed-use
4. **S2 Architecture (Calgary)** — multi-family residential / community / commercial
5. **Sahuri + Partners Architecture** — community / civic / mid-rise
6. **MMP Architects** (formerly McKinley Burkart) — civic / boutique commercial
7. **McKinley Burkart Architects** — restaurants / civic / interiors-led
8. **Riddell Kurczaba Architectural** — institutional / heritage / civic
9. **Modern Office of Design + Architecture (MODA)** — boutique commercial / civic
10. **Studio C Architecture** — community + recreation
11. **BR2 Architecture (Calgary)** — civic / cultural / institutional
12. **CivicWorks Planning + Design** — civic / institutional
13. **Workun Garrick Partnership** — institutional / civic / community
14. **WZMH Architects (Calgary office)** — corporate / institutional
15. **Group2 Architecture** — community / civic / multi-family
16. **DIALOG (Calgary office)** — integrated A&E, large institutional
17. **Stantec Architecture (Calgary)** — large institutional / healthcare
18. **Kasian (Calgary office)** — healthcare / institutional
19. **B+H Architects (Calgary)** — corporate / institutional
20. **MTaO Architects** — civic / cultural

### Tier 2 — Southern AB ICI firms
21. **OCL Architecture (Lethbridge)** — institutional / civic / community
22. **WAA Architects (Medicine Hat)** — civic / institutional
23. **Berry Architecture + Associates (Red Deer)** — civic / community
24. **Wills+Patel Architecture (Lethbridge/Calgary)** — institutional / community
25. **Hodgson Schilf Evans / HSEA Architecture (Edmonton, but Calgary work)** — institutional
26. **MMP Architects (Calgary + Red Deer)** — civic / multi-family

### Tier 3 — emerging / specialist Calgary firms (include 2-4 of these)
27. **Reimagine Architects (Calgary)** — sustainability-focused civic
28. **METAFOR (Calgary + Edmonton)** — high-end commercial / civic
29. **Stantec (Calgary)** — duplicate of #17 — skip if covered
30. **Field Lievers Architecture (Grande Prairie)** — northern AB ICI

**Hard rule:** if your research finds the same firm twice under different names (rebrand), pick the current name and note the rebrand. If a firm on the list is defunct or no longer active in ICI, set `skipped: true` with reason.

## Per-architect research

For each architect, capture:

```json
{
  "id": <CanonicalOrgId — look up against the platform via a query you submit to Opus separately if needed; for now set to null and Opus will resolve at ingest>,
  "displayName": "...",
  "confidence": 0.0,
  "skipped": false,
  "skipReason": null,
  "resultJson": "<stringified inner JSON below>"
}
```

The INNER `resultJson` carries:

```json
{
  "displayName": "...",
  "hqCity": "Calgary" | "Lethbridge" | ...,
  "province": "AB",
  "country": "CA",
  "officeLocations": ["Calgary", "Edmonton", ...],
  "firmSize": "<staff range — e.g. '40-60', '~150'>",
  "yearFounded": <number|null>,
  "sectors": ["Healthcare", "K-12", "Civic", ...],
  "leadership": [
    {"name": "...", "title": "Principal" | "Managing Partner" | "Director of Healthcare", "email": "<if found>", "phone": "<if found>", "sourceUrl": "..."}
  ],
  "structuralPartners": [
    {
      "name": "RJC Engineers",
      "evidence": "Cited as SE on Cottonwoods LTC Kelowna (architect-published project page)",
      "frequency": "primary" | "occasional" | "single-project",
      "sourceUrl": "..."
    }
  ],
  "activePursuits": [
    {
      "projectName": "...",
      "stage": "concept" | "design-development" | "RFP-issued" | "tender-pending" | "under-construction",
      "expectedRfpYear": <number|null>,
      "buyer": "<owner name>",
      "sector": "...",
      "value": "<$Xm or text>",
      "city": "Calgary" | ...,
      "korFit": "<High / Medium / Low + reason. High = Calgary/southern AB, KOR sector, SE not yet named OR known displaceable>",
      "structuralIncumbent": "<known SE on this project, or null = open seat>",
      "sourceUrl": "..."
    }
  ],
  "recentBuiltWork": [
    {
      "projectName": "...",
      "year": <number>,
      "sector": "...",
      "structuralEngineer": "<who they used>",
      "sourceUrl": "..."
    }
  ],
  "awards": [
    {"name": "...", "year": <number>, "project": "..."}
  ],
  "korAngle": {
    "currentRelationship": "none" | "introduced" | "past-collaboration" | "active",
    "edge": "<why KOR is a fit for this architect's work — sector overlap, geographic presence, technical fit>",
    "incumbentVulnerability": "<is their current SE displaceable? on what types of projects?>",
    "theAsk": "<one specific thing the BD team should ask of this firm next week>"
  },
  "_generatedAt": "2026-06-04"
}
```

## KOR context (use to bias `korFit` and `korAngle`)

KOR Structural is a Vancouver structural engineering firm. Has worked in:
- BC (Vancouver, Vancouver Island, Okanagan) — primary
- Alberta (already some presence; the trip is partly to deepen this)
- LA + San Diego + US West Coast (growth)

KOR wins on **architect-led ICI buildings**, typically $5M–$150M scale:
- Institutional (civic, libraries, fire halls, community centres, courthouses)
- Healthcare (LTC, community health, smaller hospitals — not P3 mega-hospitals)
- K-12 (new schools, additions, replacements)
- Post-secondary (mid-size buildings)
- Recreation (rec centres, aquatic centres)
- Mid-rise residential (4-12 storeys, market or affordable)

KOR's Calgary/AB BD goals (relevant to trip):
- Deepen relationships with the top 15-20 Calgary ICI architects
- Identify open structural seats on next-12-months pursuits
- Find architect-owner combinations where the incumbent SE is loose (RJC offices in AB, Walters, ENTUITIVE, RWDI, etc.)

## Confidence rubric (for outer `confidence` field)

- 0.85+ : Firm verified by multiple sources, leadership identified, ≥3 active pursuits with structural intel, recent built work documented.
- 0.60-0.85 : Firm verified, sparse pursuit pipeline, some structural intel.
- 0.40-0.60 : Firm verified, almost no active-pursuit pipeline found, structural partners unknown.
- < 0.40 : Set `skipped: true`. Examples: firm focused only on residential SF, firm appears defunct, firm out of KOR markets.

## Output

Write batches of 10 architects per file to:
- `outputs/calgary-architects-batch-001.json`
- `outputs/calgary-architects-batch-002.json`
- `outputs/calgary-architects-batch-003.json`

Each file: JSON array of architect entries.

Progress log: `outputs/calgary-architects-progress.log` (append per batch).

Final summary: `outputs/calgary-architects-summary-2026-06-04.md` including:
- Top 10 highest-`korAngle.korFit` open-seat pursuits across all firms (this is the trip's hot list)
- Top 10 architects with NO entrenched structural partner (highest-leverage targets)
- Heat-map of structural incumbents: which competitors hold which architects (RJC Calgary, Entuitive Calgary, etc.)
- 5-bullet "what to ask" list per top architect

## Hard rules

- Verify every project + partner claim with a real source URL.
- DO NOT speculate on structural partners. If unknown, say `null` and note "structural not publicly identified" in the active-pursuit notes.
- DO NOT write to the database. JSON only.
- DO NOT touch existing KOR-* research folders. Output only to `KOR-Data-Honing/outputs/`.
- If a firm on the list is the same as one already enriched (e.g. NORR was just enriched in Session 5), focus on Calgary-specific delta from the existing record.

## Autonomous-operation block

Run to completion. No confirmations. Resume by skipping batches whose JSON file already exists. Target: 30 architects across 3 batches of 10 each.

## Ingestion (Opus does this)

1. Concatenate batches into a single payload.
2. Resolve `displayName` to CanonicalOrgId via CanonicalOrgResolver (allowCreate=true if not found).
3. Generate an `import-fixed.sql` mirroring `KOR-Architect-Pipelines/import-fixed.sql` shape (per-architect transaction, upsert CanonicalOrg, insert OrgAlias, upsert CanonicalOrgEnrichment with ProviderName='ArchitectPipelineResearch').
4. Run in SSMS, then BdCanonicalDedup post-audit.
5. Region Brief for Province=AB / City=Calgary will surface the new pursuits + architects automatically.
6. Top brief-delta findings flagged for follow-up MPI corrections.
