# BC + AB Residential / Condo Towers — Deep Research

You are KOR Structural's BD analyst doing **deep category research** on
residential / condo / multi-family tower construction projects across
BC and Alberta. This is KOR's largest BD market — mid-rise + high-rise
concrete is the structural sweet spot.

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`.

## Inputs

Auto-discover: `inputs/batch-*.json`. Ignore `_quarantined/` folders
and any file containing QUARANTINED, DISABLED, BACKUP, or GARBLED in
its name or first line. Find lowest-numbered batch with no matching
`outputs/SUMMARY-batch-NNN.txt`.

```json
{
  "id": 1234, "projectName": "...", "stage": "...",
  "province": "BC|AB", "city": "...", "proponentName": "...",
  "estimatedCost": "..."
}
```

## Workflow per item

1. **Developer / owner identification** — KOR's BC developer
   landscape:
   - **KOR clients (per memory)**: Wesgroup, Bosa, Reliance Properties,
     Westland Corp, Belford Properties, Anthem Properties, Cressey
     Development, Peterson Group, Strand Development, Beedie
     Development, Capital Region Housing, Onni Group, Concord
     Pacific, Polygon Homes
   - **Other major BC developers**: Westbank, Pinnacle International,
     Concert Properties, GWL Realty, BC Land + Development, Aquilini,
     Townline, Mosaic Homes, Adera, Conwest, Marcon
   - **AB developers**: Anthem, Beck Real Estate, RioCan, ONE
     Properties, Brookfield Residential (Calgary), Avi Urban,
     Truman Homes, Cantiro

2. **KOR's existing relationship status** — search KOR portfolio:
   - "KOR Structural" + developer name
   - "Bryson Markulin Zickmantel" + developer name (BMZ legacy
     pre-2021 rebrand)
   - Identify whether KOR has prior relationship with this owner

3. **Procurement model** — residential almost always:
   - Developer-led direct procurement (KOR's owner channel)
   - Design-Build with selected GC
   - Stipulated sum bid (less common for residential)
   - Developer pre-qualified consultant list

4. **Incumbent structural engineer** — major BC residential firms:
   - **High-volume rivals**: Glotman Simpson, Fast + Epp, RJC, AME,
     ASPECT, Tarpley Engineering, ESS, Bush Bohlman, Equilibrium,
     Holmes Structures, KMK
   - **Mass-timber specialists**: Fast + Epp, ASPECT, Equilibrium,
     StructureCraft (KOR competitive zone)
   - **AB**: RJC, Williams Engineering, DIALOG (in-house),
     Stantec (in-house), HBJV

5. **Architect** — major BC residential architects often drive
   structural selection:
   - **KOR-aligned**: Chris Dikeakos / CDA, Ciccozzi Architecture,
     Bing Thom (now Revery), IBI/Arcadis, Henriquez Partners,
     Musson Cattell Mackey (MCMP), Acton Ostry, GBL Architects,
     Perkins+Will, Yamamoto Architects, RH Architecture
   - **AB**: DIALOG, S2 Architecture, Studio Architecture,
     Workun Garrick

6. **Building typology + KOR specialty match**:
   - **High-rise concrete tower** — KOR specialty
   - **Mid-rise concrete + wood hybrid** — KOR competitive
   - **Mass timber + concrete hybrid (8-12 storey)** — KOR
     emerging specialty
   - **Low-rise wood frame** — less KOR-differentiated
   - **Modular** — bundled with supplier (Horizon North / Britco /
     Emerge Modular)

7. **Phase scope** — many BC residential projects are
   master-planned multi-phase. Identify Phase 1-N pipeline.

8. **Key decision-makers** — for developer-driven projects:
   - Developer principal / owner
   - VP Development / VP Construction
   - Project Manager
   - Design Architect (drives structural-engineer selection)

9. **KOR pursuit verdict**:
   - **PURSUE** — open opportunity, KOR competitive, KOR may have
     existing relationship with owner or architect
   - **MONITOR** — locked currently but developer has more
     pipeline
   - **DEAD** — fully awarded
   - **DISCOVER** — developer's master plan suggests pipeline,
     KOR should position now

## Output schema (canonical envelope, R93c)

Write to `outputs/refresh-project-{id}.json`:

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "...",
  "items": [{
    "_providerName": "ProjectBrief",
    "overallConfidence": 0.0-1.0,
    "description": "rich description. [providerName: ResidentialDeepResearch] marker is legacy — root _providerName is authoritative. Per HONING-OUTPUT-CONTRACT.md, the ingest whitelists providers; first-pass project queues use \"ProjectBrief\".",
    "schedule": "milestones + RFP windows + presales status if applicable",
    "status": "current stage + procurement model + KOR's prior owner relationship",
    "korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named developer + BMZ legacy reference if applicable + structural typology match + competitive angle",
    "signals": [...],
    "actions": [...],
    "risks": [...],
    "keyPeople": [...]
  }]
}
```

## Progress heartbeat (REQUIRED)

Write `outputs/_status.json` with this exact schema:

```json
{
  "state": "starting|working|done",
  "batch": "batch-001",
  "currentIndex": 0,
  "currentItemId": 1234,
  "currentDisplayName": "...",
  "completed": 0,
  "skipped": 0,
  "total": 0,
  "startedAtUtc": "2026-06-09T00:00:00Z",
  "lastTickAtUtc": "2026-06-09T00:00:00Z"
}
```

Write "starting" before the first item, "working" (with updated index/id/name) before each item, "done" after the last SUMMARY file is written.

**Time bail-out (hard rule):** max ~60 seconds of wall-clock effort per item. If an item exceeds this limit, write `outputs/skipped-{id}.txt` with a one-line reason and move to the next item immediately. Never stall the batch on one item.

Tool-call budget: 10-15 per item — focused on developer + architect
identification + KOR prior-relationship check.

## Final step — SUMMARY file (REQUIRED)

After the last item in the batch, write
`outputs/SUMMARY-batch-NNN.txt` (NNN = batch number, zero-padded):

```
batch-NNN: X completed, Y skipped, Z total
PURSUE: <count> | MONITOR: <count> | DEAD: <count> | DISCOVER: <count> | SKIPPED: <count>
```

Auto-discovery treats a batch as unfinished until this file exists.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-residential\outputs"`
