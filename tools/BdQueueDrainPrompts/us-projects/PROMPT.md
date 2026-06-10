# US West Coast Projects — First-Pass Research Drain (us-projects)

## Mission

First-pass BD research on US major construction projects (California,
Oregon, Washington) in KOR's growth market. KOR Structural operates a US
entity (Deltek Org `USA`) with offices in **Los Angeles and San Diego**;
WA/OR are growth targets. For each project in the batch, produce a research
brief: what it is, who owns it, who designs it, how it procures, and
whether KOR has a structural-engineering seat to pursue.

Work autonomously end-to-end. Do NOT use the Workflow or Agent tools —
process items sequentially in this session (fan-out fails with "args too
large").

## Inputs / auto-discovery

- Inputs live in `inputs/batch-NNN.json`. Find the lowest-numbered batch
  with no matching `outputs/SUMMARY-batch-NNN.txt` and process it.
- Ignore `_quarantined/` folders and any input marked QUARANTINED /
  DISABLED / BACKUP / GARBLED.
- Each item: `{ id, projectName, stage, province, city, proponentName,
  sector, estimatedCost }`. `province` is the US state code (CA/OR/WA).

## Per-item workflow

1. Web-search the project (name + city + state). Aim for 8-12 tool calls
   per item; **HARD LIMIT: ~60 seconds of effort per item** — if an item
   exceeds it, write `outputs/skipped-{id}.txt` with a one-line reason and
   move on. Never stall the batch on one item.
2. Establish: current status/stage, owner/proponent (verify the name),
   architect if selected, structural engineer if public, GC/CM if selected,
   estimated cost, schedule.
3. US procurement context to identify (different from BC/AB):
   - **CA**: DSA approval status for K-14 schools; UC/CSU capital programs;
     Lease-Leaseback and CMAR delivery on public work; OSHPD/HCAI review
     for healthcare; seismic retrofit programs (direct KOR specialty).
   - **OR/WA**: CMAR/GCCM delivery (WA GC/CM certification), state portals
     (OregonBuys, WEBS), school bond programs.
   - Federal: SAM.gov solicitations where the owner is federal.
4. KOR angle: is the structural seat open? Who is the likely design lead?
   Note LA/San Diego proximity for CA work and any seismic/healthcare/
   education specialty match.

## Output — one file per item: `outputs/refresh-project-{id}.json`

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "<ISO8601>",
  "items": [{
    "_providerName": "ProjectBrief",
    "overallConfidence": 0.0,
    "description": "<what the project is, owner, size, status — 3-6 sentences>",
    "schedule": "<known dates / phase timeline>",
    "status": "<current procurement/construction status>",
    "korAngle": "<is there a pursuable structural seat; who to approach; LA/SD office relevance>",
    "keyPeople": [{ "name": "", "title": "", "side": "Owner|Architect|GC|Other" }],
    "actions": [{ "type": "ContactStrategy|Research|Monitor", "recommendation": "", "targetPerson": "", "targetOrg": "", "timingNotes": "" }],
    "signals": [{ "type": "ProcurementStage|Funding|TeamSelection|Other", "subject": "", "detail": "", "occurredAt": "", "sourceUrl": "" }],
    "risks": [{ "type": "", "description": "", "mitigation": "" }]
  }]
}
```

The root `_providerName: "ProjectBrief"` field is REQUIRED — the ingest
whitelists providers and REJECTS files it cannot resolve (see
tools/BdQueueDrainPrompts/HONING-OUTPUT-CONTRACT.md).

## Heartbeat — `outputs/_status.json` after every item

```json
{ "state": "starting|working|done", "batch": "batch-NNN",
  "currentIndex": 0, "currentItemId": 0, "currentDisplayName": "",
  "completed": 0, "skipped": 0, "total": 0,
  "startedAtUtc": "", "lastTickAtUtc": "" }
```

## Final step — REQUIRED

After the last item, write `outputs/SUMMARY-batch-NNN.txt`: completed /
skipped / total counts + one line per notable find (open structural seats,
named architects). Auto-discovery treats the batch as unfinished until
this file exists.

## Ingest (operator runbook)

From the repo root after the session completes:
`dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir C:\ProgramData\KorOperations\QueueDrain\us-projects\outputs`
