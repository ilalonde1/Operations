# US West Coast Projects — Honing / Verification Pass (us-projects-honing)

## Mission

Verification + pursuit-play pass on US West Coast projects (CA/OR/WA) that
already have first-pass research. Each batch item embeds the first-pass
brief (`firstPassBrief`) — read it, verify what changed, and render a
VERDICT with the evidence bar below. KOR Structural's US entity (Deltek
Org `USA`) runs from **Los Angeles and San Diego**; WA/OR are growth
targets. California seismic, healthcare (HCAI), and education (DSA) work
maps directly onto KOR's BC specialty.

Work autonomously end-to-end. Do NOT use the Workflow or Agent tools —
process items sequentially (fan-out fails with "args too large").

## Inputs / auto-discovery

- `inputs/batch-NNN.json`; process the lowest-numbered batch with no
  matching `outputs/SUMMARY-batch-NNN.txt`.
- **Parallel operation**: if the operator's launch message names a specific
  batch, process THAT batch instead of auto-discovering, and write the
  heartbeat to `outputs/_status-batch-NNN.json` so concurrent sessions
  don't clobber each other. Output files are per-item and never collide.
- Ignore `_quarantined/` folders and inputs marked QUARANTINED / DISABLED /
  BACKUP / GARBLED.

## Per-item workflow

1. Read `firstPassBrief`. Verify its claims before extending them — the
   correction pass exists because first passes contain wrong incumbents
   and stale stages. Budget 12-20 tool calls; **HARD LIMIT: ~60 seconds
   per item** — over it, write `outputs/skipped-{id}.txt` with a reason
   and move on.
2. **Procurement model — confirmed via at least 2 independent sources.**
   Name the model (CA: Lease-Leaseback / CMAR / Design-Build / DBB /
   progressive DB / P3; WA: GC/CM; OR: CM/GC) and whether the structural
   seat rides with the architect, the design-builder, or the owner's
   bench.
3. **Decision-makers**: at least 2 named people with titles (owner capital
   side and/or design lead), with a direct-contact path where findable.
4. **12-month engagement timeline**: what KOR should do, when, anchored to
   real procurement dates.
5. **Warm-intro path**: named route (shared client, architect KOR knows,
   industry org) — or state plainly that only a cold path exists.

## Verdicts

PURSUE_URGENT | PURSUE | MONITOR | DEAD | DISCOVER | DUPLICATE

**DEAD evidence bar** — a DEAD verdict REQUIRES: named incumbent
structural engineer (or named design-builder whose bench covers it) +
architect + GC where known + at least one source URL. "Probably taken" is
MONITOR, not DEAD.

**DUPLICATE** — name the surviving MPI id in korAngle ("See ID X").

## Output — one file per item: `outputs/refresh-project-{id}.json`

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "<ISO8601>",
  "items": [{
    "_providerName": "ProjectBriefHoning",
    "overallConfidence": 0.0,
    "honingPass": {
      "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DEAD|DISCOVER|DUPLICATE",
      "procurementModel": "<named model + 2 source URLs>",
      "incumbentStructural": "<named firm or 'NOT YET SELECTED' or 'NOT PUBLIC'>",
      "korAngle": "<verdict-led pursuit play; LA/SD office relevance>",
      "engagementTimeline": "<12-month dated plan>",
      "warmIntroPath": "<named route or 'cold only'>",
      "keyPeople": [{ "name": "", "title": "", "side": "Owner|Architect|GC|Other" }],
      "actions": [{ "type": "", "recommendation": "", "targetPerson": "", "targetOrg": "", "timingNotes": "" }],
      "signals": [{ "type": "", "subject": "", "detail": "", "occurredAt": "", "sourceUrl": "" }],
      "risks": [{ "type": "", "description": "", "mitigation": "" }]
    }
  }]
}
```

The root `_providerName: "ProjectBriefHoning"` field is REQUIRED — the
ingest whitelists providers and REJECTS files it cannot resolve (see
HONING-OUTPUT-CONTRACT.md). The legacy `[providerName: X]` description
marker is recognized but no longer sufficient on its own.

## Heartbeat — `outputs/_status.json` after every item

```json
{ "state": "starting|working|done", "batch": "batch-NNN",
  "currentIndex": 0, "currentItemId": 0, "currentDisplayName": "",
  "completed": 0, "skipped": 0, "total": 0,
  "startedAtUtc": "", "lastTickAtUtc": "" }
```

## Final step — REQUIRED

Write `outputs/SUMMARY-batch-NNN.txt` after the last item: completed /
skipped / total + per-verdict tally (PURSUE_URGENT / PURSUE / MONITOR /
DEAD / DISCOVER / DUPLICATE). Auto-discovery treats the batch as
unfinished until this file exists.

## Ingest (operator runbook)

`dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir C:\ProgramData\KorOperations\QueueDrain\us-projects-honing\outputs`
