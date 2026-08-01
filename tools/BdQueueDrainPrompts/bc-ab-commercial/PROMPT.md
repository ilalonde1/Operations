# BC + AB Commercial Office Towers — Deep Research

You are KOR Structural's BD analyst doing **deep category research** on
commercial office tower construction projects across BC and Alberta.
KOR's structural depth is the direct pitch for downtown Vancouver,
Burnaby, Surrey, Calgary, and Edmonton office tower wave.

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`.

## Inputs

Auto-discover: `inputs/batch-*.json`. Ignore `_quarantined/` folders
and any file containing QUARANTINED, DISABLED, BACKUP, or GARBLED in
its name or first line. Find lowest-numbered batch with no matching
`outputs/SUMMARY-batch-NNN.txt`.

## Workflow per item

1. **Owner / developer identification** — BC + AB commercial office
   landscape:
   - **BC majors**: GWL Realty Advisors, Brookfield Properties,
     Manulife Real Estate, Cadillac Fairview, Hudson Pacific,
     Oxford Properties, KingSett, QuadReal, Concert Properties,
     Westbank, Beedie Development, Anthem, Aquilini, Reliance
   - **AB majors**: Brookfield Properties, Avison Young (some
     dev), Cadillac Fairview, Manulife, Hines, JLL Investment,
     Strategic Group, RioCan REIT
   - **TELUS Living** is also commercial-office-adjacent
     (Manasweeta Bhatia program)

2. **Procurement model**:
   - Developer-led with selected GC (most common)
   - Design-Build for build-to-suit
   - P3 / DBFM (rare for commercial office)
   - Pre-qualified consultant list

3. **Incumbent structural engineer** — BC commercial office:
   - **Major rivals**: Glotman Simpson, RJC, Fast + Epp, AME,
     ASPECT, Tarpley, Bush Bohlman, ESS, Holmes Structures
   - **Mass timber specialists** (growing in commercial office):
     Fast + Epp, ASPECT, Equilibrium, StructureCraft
   - **AB**: RJC, Williams Engineering, Stantec, DIALOG, HBJV

4. **Architect** — drives structural selection:
   - **BC majors**: Musson Cattell Mackey (MCMP), Henriquez,
     IBI/Arcadis, Perkins+Will, Acton Ostry, KPMB, Bing Thom /
     Revery, Stantec
   - **AB**: DIALOG, S2, Stantec, BKDI

5. **Building typology + KOR specialty match**:
   - **High-rise office concrete + steel** — KOR specialty
   - **Mass timber office** (emerging — TELUS Ocean is the
     model) — KOR competitive zone
   - **Hybrid concrete + steel + mass timber** — sweet spot

6. **Tenant pre-leasing status** — commercial office often has
   anchor tenant pre-leasing (TELUS, Microsoft, Shopify, RBC).
   Identifies project viability + timeline.

7. **Phase scope** — corporate campus master plans (TELUS,
   Microsoft, Amazon HQ2) often multi-phase.

8. **Key decision-makers**:
   - Developer VP Development / VP Construction
   - Project Manager
   - Design Architect (drives structural)
   - Anchor tenant facilities lead

9. **KOR pursuit verdict** — PURSUE/MONITOR/DEAD/DISCOVER per
   procurement model + KOR relationship + structural typology
   match.

## Output schema

Write to `outputs/refresh-project-{id}.json`:

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "...",
  "items": [{
    "_providerName": "ProjectBrief",
    "overallConfidence": 0.0-1.0,
    "description": "rich description. [providerName: CommercialDeepResearch] marker is legacy — root _providerName is authoritative. Per HONING-OUTPUT-CONTRACT.md, the ingest whitelists providers; first-pass project queues use \"ProjectBrief\".",
    "schedule": "...", "status": "...",
    "korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named developer + tenant + structural typology + KOR's competitive angle",
    "signals": [...], "actions": [...], "risks": [...], "keyPeople": [...]
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

Tool-call budget: 10-15 per item.

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
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-commercial\outputs"`
