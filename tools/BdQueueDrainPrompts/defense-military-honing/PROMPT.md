# Defense / Military — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on defense/military project briefs that have
already been first-pass researched.

Two equal jobs:

1. **VERIFY THE GATE** (Yurkovich correction): Has the structural
   engineering scope already been awarded, and if so, to whom?
2. **BUILD THE PURSUIT PLAY**: For every project where there IS an
   entry point (PURSUE or MONITOR), produce the named contacts,
   warm-intro paths, KOR's competitive angle, and the 12-month
   engagement timeline.

The first-pass brief gave the rough lay of the land. Your honing pass
either kills it (DEAD with named incumbent) or hands KOR a concrete
playbook to win the work.

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`.

## Inputs

Auto-discover: `inputs/batch-*.json`. Ignore `_quarantined/` folders
and any file containing QUARANTINED, DISABLED, BACKUP, or GARBLED in
its name or first line. Find lowest-numbered batch with no matching
`outputs/SUMMARY-batch-NNN.txt`.

Each batch row has the first-pass ProjectBrief JSON embedded:

```json
{
  "id": 1234, "projectName": "...", "stage": "...",
  "province": "BC|AB", "city": "...", "proponentName": "...",
  "firstPassBrief": { /* full ProjectBrief from first pass */ }
}
```

## Workflow per item

### PART A: Verify the gate (Yurkovich correction)

Answer DEFINITIVELY with named sources:

1. **Award status** — has DCC (Defense Construction Canada) or PSPC
   awarded the prime consultant / design-build contract? Search:
   - "DCC awards" + project name
   - buyandsell.gc.ca tender award database
   - "Defense Construction Canada" + project name
   - "Public Services and Procurement Canada" + project name
   - The proponent / base public capital project pages
   - If YES, who got it? Pull the named prime, sub-consultants,
     structural engineer if disclosed.
   - If NO, RFP open / closed / pending? When does/did procurement
     close?

2. **Incumbent structural engineer** — common DND structural firms
   to verify: Stantec, WSP, AECOM, Williams Engineering, MMM Group
   (legacy), Read Jones Christoffersen, Glotman·Simpson, Fast + Epp,
   Tetra Tech, GHD, Hatch.

3. **Procurement model** — DCC standing-offer pre-qualified pool /
   open BC Bid / buyandsell.gc.ca RFP / Design-Build with structural
   as sub / P3 / direct award.

4. **Phase scope** — is THIS the active phase, or a downstream phase?
   CFHA housing rolls out in 50-100 unit phases. Hangar replacements
   have planning + design + construction phases.

5. **Security clearance gate** — Reliability vs Secret-level
   required for structural design team. Canadian-controlled-firm
   requirement.

### PART B: Build the pursuit play (only if PURSUE or MONITOR)

For every project that is NOT DEAD, produce the concrete playbook:

6. **Named decision-makers + contact info** — go deep:
   - Defense Construction Canada Project Director / Regional Manager
     (BC/Alberta regional office)
   - Base Commander / Wing Commander / RCN Formation Commander where
     applicable
   - DND civilian Capital Projects Officer (CPO)
   - For each: email, phone, LinkedIn URL — search RocketReach,
     ZoomInfo, LinkedIn, DCC org charts, government directories
   - Push HARD on contact info. Direct contacts are the report's
     gold.

7. **Warm-introduction paths** — KOR rarely cold-calls DND. Who
   could intro KOR to the buyer?
   - Past architect / contractor on this base
   - Standing-offer prime consultant who has worked the base
     before
   - KOR alumni / connections at DND or DCC
   - Industry events / conferences where DND decision-makers
     appear (e.g., CADSI, AFCEA Defence Industry Day)
   - Indigenous procurement partners on CFB joint projects

8. **KOR's competitive angle** — be specific:
   - Why KOR vs the incumbent (faster, local, specialty)
   - Past KOR projects of similar size/type to reference
   - Why KOR's structural depth matters for this project type
     (e.g., hangars = long-span, magazines = blast-rated, CFHA =
     mid-rise residential)

9. **12-month engagement timeline**:
   - Month 1-2: warm-intro touchpoint + DCC pre-qualification check
   - Month 3-6: in-person meeting with named DCC PM or base CO
   - Month 6-9: position on incumbent prime's sub-list
   - Month 9-12: RFP response or standing-offer renewal
   - Adjust to the project's actual procurement window

10. **Risk and dropout signals** — what would make KOR drop pursuit:
    - Hard security-clearance gate KOR can't meet
    - Incumbent prime's standing-offer covers this project type
    - Procurement bundled with adjacent base where KOR has no
      relationship

### PART C: Revise the verdict

Based on Parts A + B, update korAngle to:

- **PURSUE** — structural scope open, KOR competitive, play is named
- **MONITOR** — locked currently, but future phases at this base /
  with this owner are open
- **DEAD** — fully awarded, no entry. Name the incumbent.
- **DISCOVER** — KOR should be on next RFP cycle for this owner;
  no immediate project entry

## Output schema (canonical envelope, R93c)

Write to `outputs/refresh-project-{id}.json`:

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "...",
  "items": [{
    "_providerName": "ProjectBriefHoning",
    "overallConfidence": 0.0-1.0,
    "description": "verified description + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing with named sources + 12-month KOR engagement timeline",
    "status": "AWARDED to <firm> | RFP OPEN | RFP PENDING | RFP CLOSED <date> awaiting award | UNAWARDED PRE-PROCUREMENT",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent if DEAD + KOR competitive angle + warm-intro path + named target decision-maker + first move",
    "signals": [
      { "type": "AwardConfirmed|RfpOpen|RfpClosed|StructuralIncumbent|PhaseLocked|DecisionMakerMove|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to DCC'. Good: 'call DCC Western Region PM Joe Smith re: CFB Edmonton CFHA Phase 3 standing-offer renewal, position KOR's mid-rise structural depth against incumbent Williams Engineering'.",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window" }
    ],
    "risks": [
      { "type": "...", "description": "...", "mitigation": "..." }
    ],
    "keyPeople": [
      { "name": "...", "title": "...",
        "side": "Owner|Architect|GC|Structural|Funder|Champion|WarmIntro|Other",
        "orgName": "...",
        "email": "..." or null, "phone": "..." or null,
        "linkedinUrl": "..." or null,
        "notes": "BD-relevant context — past pursuits, relationship history, decision authority" }
    ]
  }]
}
```

Set `_providerName: "ProjectBriefHoning"` at the item root (see schema above). The `[providerName: ProjectBriefHoning]` description marker is legacy — still recognized as a fallback but no longer sufficient. Per HONING-OUTPUT-CONTRACT.md: the ingest whitelists providers and REJECTS files whose `_providerName` is absent, empty, or not in the project whitelist.

## Quality bar

For every PURSUE or MONITOR project, the brief MUST contain:
- At least 3 named individuals with role + org
- At least 1 individual with direct email OR phone OR LinkedIn URL
- A named warm-introduction path (specific person OR firm OR event)
- A specific 12-month engagement timeline
- A named competitive angle vs incumbent (if applicable)

For DEAD projects, the brief MUST contain:
- Named incumbent structural engineer with award source URL
- Named buyer + procurement gate + award date
- 1 sentence rationale for why no future entry exists

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

Tool-call budget: 18-25 per item — production-quality BD intelligence
pass. Default to MORE searches when in doubt.

## Final step — SUMMARY file (REQUIRED)

After the last item in the batch, write
`outputs/SUMMARY-batch-NNN.txt` (NNN = batch number, zero-padded):

```
batch-NNN: X completed, Y skipped, Z total
PURSUE: <count> | MONITOR: <count> | DEAD: <count> | DISCOVER: <count> | SKIPPED: <count>
```

Auto-discovery treats a batch as unfinished until this file exists.

## Output ONLY the per-project JSON files + heartbeat

Do not emit prose to stdout. Do not ask for confirmation. Run until
batch is done.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\defense-military-honing\outputs"`
