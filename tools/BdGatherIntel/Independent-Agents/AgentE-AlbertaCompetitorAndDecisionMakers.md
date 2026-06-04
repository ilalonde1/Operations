# Agent E — Alberta competitor presence + decision-maker intel

**Trip-prep focused.** Two outputs: (1) which structural competitors are already entrenched in Calgary/southern AB so KOR knows who they're up against, and (2) the named decision-makers at top owners so the BD team can call ahead.

**Run how:**

```
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Paste this entire file.

---

## Part 1 — Calgary + Southern AB structural competitor presence

For each major structural-engineering competitor with operations in Calgary / southern AB, capture:
- Office location(s)
- Approximate staff size in AB
- Sectors they dominate (Healthcare, K-12, Civic, Commercial, Multi-family, etc.)
- Top 5 recent Calgary wins (with year, sector, value if known)
- Key architects they partner with regularly (this identifies who KOR is competing for)
- Weaknesses / displacement opportunities (capacity issues, sector gaps, pricing reputation, recent project failures)
- Leadership in AB (Calgary office MD, key principals)

### Competitor targets (work this list)

**Big incumbents:**
1. **RJC Engineers (Calgary)** — Calgary office, dominant in healthcare + commercial
2. **Read Jones Christoffersen (separate listing if it's a different entity)** — same thing? verify
3. **Bush Bohlman & Partners (any AB presence?)** — likely BC-only but verify
4. **Entuitive (Calgary)** — competitor with strong design presence
5. **DIALOG (Calgary)** — integrated A&E, structural in-house
6. **Stantec (Calgary structural)** — large multi-discipline
7. **Walters Group (Calgary?)** — steel-focused
8. **Bantrel Calgary** — large engineering firm with structural practice
9. **HCM Contractors Engineering** — niche
10. **WSP (Calgary structural)** — large multi-discipline
11. **WSB / Weiler Smith Bowers (any AB presence?)**

**Specialist firms:**
12. **Fast+Epp (Calgary office?)** — verify; if no Calgary office, note Vancouver-based
13. **Glotman Simpson (Calgary office?)** — verify
14. **StructureCraft (Calgary?)** — mass timber specialist
15. **Tetra Tech Calgary** — civil + structural
16. **GENIVAR / WSP Calgary** — merger lineage
17. **Klohn Crippen Berger (Calgary)** — geo + structural
18. **Reinbold Engineering (Calgary)** — institutional
19. **Protostatix Engineering (Calgary/Edmonton)** — Englobe subsidiary

**Local AB firms:**
20. **Read Jones Christoffersen Engineering Calgary (different from RJC?)** — verify
21. **MEG Engineering (Calgary)** — niche commercial
22. **Westgard Engineering** — central AB
23. **Crosby Hanna & Associates** — Lethbridge
24. **MEG Consulting Group** — central AB

### Output per competitor

```json
{
  "displayName": "...",
  "kind": "Competitor",
  "abPresence": "headquartered" | "branch-office" | "satellite-office" | "no-office-but-active" | "not-active",
  "abOfficeCities": ["Calgary", "Edmonton"],
  "abStaffApprox": "<range>",
  "sectors": ["Healthcare", "Commercial high-rise", ...],
  "topRecentAbWins": [
    {
      "projectName": "...",
      "year": <number>,
      "sector": "...",
      "value": "<$Xm>",
      "architect": "<the architect they partnered with>",
      "sourceUrl": "..."
    }
  ],
  "frequentArchitectPartners": ["<architect names — most-frequently-paired-with>"],
  "displacementOpportunity": "<is there a weakness KOR can exploit? capacity issue / sector gap / pricing / recent failure / leadership churn>",
  "leadership": [
    {"name": "...", "title": "Calgary MD" | "Principal" | "Director of Healthcare", "email": "<if found>", "sourceUrl": "..."}
  ],
  "_providerName": "CompetitorProfile",
  "_confidence": 0.0,
  "_generatedAt": "2026-06-04"
}
```

Write to `outputs/alberta-competitors-batch-001.json` and `-002.json` (cap each at 12 competitors).

---

## Part 2 — Top-25 AB owner decision-makers

For the top 25 owners with the most upcoming work (cross-reference with Agent C's pipeline output if available; otherwise use the owner targets listed in AgentC's prompt), identify the named decision-makers KOR should reach out to:

- **Capital Projects Director / Manager**
- **Director of Facilities Planning**
- **Procurement lead for capital projects**
- **Project executive** for any specific marquee project

### Output per owner

```json
{
  "displayName": "<owner canonical name>",
  "kind": "Buyer",
  "hqCity": "Calgary",
  "decisionMakers": [
    {
      "name": "...",
      "title": "Director, Capital Projects",
      "email": "<if found from public sources>",
      "phone": "<if found>",
      "responsibilities": "<one-line — e.g. 'leads structural-engineer prequalification for institutional builds'>",
      "tenure": "<approx start year if found>",
      "sourceUrl": "..."
    }
  ],
  "procurementMethod": "<RFP only / DBFM / GC-led / Construction Manager / IPD>",
  "structuralEngineerPrequalification": "<is there a roster? what's the entry process?>",
  "recentProjectsAwarded": [
    {"projectName": "...", "year": <number>, "architect": "...", "structural": "...", "sourceUrl": "..."}
  ],
  "korAngle": {
    "currentRelationship": "none" | "introduced" | "past-work" | "active",
    "approach": "<what's the right way for KOR to engage them — direct call, via architect, via prequalification>",
    "theAsk": "<one specific ask the BD team should make>"
  },
  "_providerName": "PublicSectorResearch",
  "_confidence": 0.0,
  "_generatedAt": "2026-06-04"
}
```

Write to `outputs/alberta-decision-makers-batch-001.json` and `-002.json` (cap each at 12 owners).

---

## Cross-cutting rules

- Verify every name + project + relationship from a real source. NO HALLUCINATION on people names.
- Decision-maker names + titles MUST come from a public source (LinkedIn, owner's website, news article, conference panel, RFP signatory). Note the source.
- Emails: only include if found from a public source (do NOT guess `firstname.lastname@owner.org`).
- For Part 1 (competitors): focus on AB-active firms. If a firm has no AB office and no AB project wins, set `abPresence: "not-active"` and skip the rest of the fields.
- For Part 2 (decision-makers): focus on the people who actually pick structural engineers. If procurement is purely GC-led with no owner influence on SE selection, note that and de-prioritize the owner.

## Final summary

Write `outputs/alberta-competitors-decisionmakers-summary-2026-06-04.md` with:

**Competitor section:**
- Heat map: which competitor dominates which sector in Calgary
- 5 firms with highest displacement potential (capacity issues / pricing / sector gaps)
- 5 architect-competitor pairs that are entrenched (Read X always uses RJC → high friction to displace)
- 5 architect-competitor pairs that are loose (Read X uses 3 different SEs → KOR can break in)

**Decision-maker section:**
- Top 10 owners by upcoming work value
- Top 10 named individuals (with title + market) KOR's BD team should call before / during the trip
- Procurement method spread (RFP-only vs. CM-led vs. IPD) — informs KOR's pitch approach

## Autonomous-operation block

Run to completion. No confirmations. Resume by skipping existing batch files.

## Ingestion (Opus does this)

1. Concatenate Part 1 batches → write CompetitorProfile enrichment rows.
2. Concatenate Part 2 batches → write PublicSectorResearch + DecisionMakers enrichment rows.
3. BdCanonicalDedup post-audit.
4. Region Brief for AB / Calgary surfaces both layers, plus competitor displacement intel feeds Architect Displacement Briefs.
