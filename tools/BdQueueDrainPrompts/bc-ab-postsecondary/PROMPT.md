# BC + AB Post-Secondary — Deep Research

You are KOR Structural's BD analyst doing **deep category research** on
post-secondary institution construction projects (universities,
colleges, institutes) across BC and Alberta. Post-secondary is
relationship-driven, master-plan-driven, with multi-year capital
forecasts published publicly — high-quality target market.

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`.

## Inputs

Auto-discover: `inputs/batch-*.json`. Ignore `_quarantined/` folders
and any file containing QUARANTINED, DISABLED, BACKUP, or GARBLED in
its name or first line. Find lowest-numbered batch with no matching
`outputs/SUMMARY-batch-NNN.txt`.

## Workflow per item

1. **Institution identification** — BC + AB landscape:
   - **BC research universities**: UBC (Vancouver + Okanagan),
     SFU, UVic, UNBC
   - **BC teaching universities**: VIU, UFV, Kwantlen, Capilano,
     Royal Roads, Emily Carr
   - **BC colleges**: Camosun, Douglas, Langara, BCIT, JIBC,
     Selkirk, NIC, COTR, Okanagan College, Justice Institute
   - **AB research universities**: U of A, U of C, U of L,
     Athabasca
   - **AB teaching universities**: Mount Royal, MacEwan, U of
     Alberta Augustana, Concordia
   - **AB polytechnics**: SAIT, NAIT, Olds, Lakeland, Lethbridge
     College, Medicine Hat College

2. **Capital plan visibility** — post-secondary publish 5-year
   capital plans publicly. Search:
   - Institution capital plan PDF on website
   - BC Ministry of Post-Secondary Education capital announcements
   - Alberta Advanced Education capital announcements
   - bog (Board of Governors) meeting agendas
   - Senate / Board capital project approvals

3. **Procurement model** — post-secondary capital varies:
   - Direct institution RFP (most common)
   - Ministry-led for major capital (BC: Ministry of P-SE; AB:
     Alberta Infrastructure)
   - P3 / DBFM for student housing (TELUS Ocean / UBC Lower Mall
     model)
   - Federal SIF (Strategic Infrastructure Fund) co-funding for
     research facilities
   - Donor-named (e.g., Yurkovich-style philanthropy)

4. **Incumbent structural engineer** — post-secondary BC market:
   - **Major rivals**: RJC, Glotman Simpson, Fast + Epp, AME,
     Bush Bohlman, ASPECT, Tarpley, Equilibrium
   - **Mass timber academic specialists**: Fast + Epp,
     StructureCraft, ASPECT (UBC Brock Commons + Earth Sciences
     Building model)
   - **AB**: RJC, Williams Engineering, Stantec (in-house),
     DIALOG (in-house)

5. **Architect** — academic-specialist architects:
   - **BC**: Hughes Condon Marler (HCMA), Acton Ostry, IBI/Arcadis,
     Perkins+Will, Public Architecture, KMBR, Henriquez, Diamond
     Schmitt
   - **AB**: DIALOG, GEC Architecture, S2 Architecture, Stantec,
     Workun Garrick
   - **Heritage/seismic specialists**: KMBR (UBC heritage), KPMB,
     Perkins+Will

6. **Building typology + KOR specialty match**:
   - **Academic/teaching building** — moderate complexity, KOR
     fit
   - **Research / lab building** — high MEP + vibration =
     specialty structural opportunity
   - **Student housing** — mass timber + concrete hybrid (UBC
     Brock Commons, UBC Lower Mall model) = KOR sweet spot
   - **Sport / recreation campus** — long-span = KOR specialty
   - **Library + community** — adaptive reuse + heritage
   - **Faculty / admin** — less differentiated

7. **Key decision-makers**:
   - Institution VP Finance & Administration / VP Facilities
   - Director Campus Planning
   - Project Manager
   - Capital Planning Director
   - Board Chair (political)
   - Provincial Minister of Post-Secondary

8. **KOR pursuit verdict** — PURSUE/MONITOR/DEAD/DISCOVER +
   institution's broader capital plan visibility (compounding
   opportunity).

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
    "description": "rich description. [providerName: PostSecondaryDeepResearch] marker is legacy — root _providerName is authoritative. Per HONING-OUTPUT-CONTRACT.md, the ingest whitelists providers; first-pass project queues use \"ProjectBrief\".",
    "schedule": "...", "status": "...",
    "korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named institution + 5-year capital plan context + structural typology + KOR's competitive angle",
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

Tool-call budget: 12-18 per item.

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
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-postsecondary\outputs"`
