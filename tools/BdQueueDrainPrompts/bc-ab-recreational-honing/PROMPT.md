# BC + AB Recreational Centres — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on recreational facility briefs already first-pass
researched.

Two jobs:

1. **VERIFY THE GATE** — has the structural engineering scope been
   awarded? Who? Is this referendum-funded and not yet approved?
2. **BUILD THE PURSUIT PLAY** — named municipality + architect +
   GC contacts, warm-intro paths, KOR long-span specialty match,
   12-month engagement timeline.

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`.

## Inputs

Auto-discover: `inputs/batch-*.json`. Ignore `_quarantined/` folders
and any file containing QUARANTINED, DISABLED, BACKUP, or GARBLED in
its name or first line. Find lowest-numbered batch with no matching
`outputs/SUMMARY-batch-NNN.txt`.

Each batch row has the first-pass ProjectBrief JSON embedded.

## Workflow per item

### PART A: Verify the gate

1. **Award status** — has design / construction been awarded?
   Search:
   - Municipality news / capital projects page
   - BC Bid / Alberta Purchasing Connection for civic RFPs
   - Construct Connect awards
   - BC Recreation and Parks Association news
   - Municipal staff reports + Council agendas (public)
   - Referendum results (BC) — many rec centres are referendum-
     funded and approval triggers the RFP

2. **Procurement model** — recreational:
   - Traditional Design-Bid-Build (most common civic rec)
   - Design-Build (larger projects)
   - Integrated Project Delivery (rare, emerging in BC)
   - Municipal pre-qualified consultant list
   - Province + federal co-funded (BC Growing Communities, AB
     CFEP, federal ICIP / GICB)

   Confirm via **at least 2 independent sources** before recording
   procurement model as confirmed. If only 1 source found, flag as
   unconfirmed.

3. **Incumbent structural engineer** — recreational BC + AB:
   - **Long-span specialists**: Fast + Epp (mass timber +
     long-span dominant), StructureCraft, Equilibrium
   - **Multi-use mid-rise**: RJC, Glotman Simpson, AME, Bush
     Bohlman, ASPECT
   - **AB**: RJC, Williams Engineering, Stantec, DIALOG, HBJV

4. **Architect** — drives structural selection:
   - **BC dominant rec specialist**: HCMA Architecture + Design
   - **BC others**: PUBLIC Architecture, Acton Ostry, NSDA,
     KMBR, Iredale, GBL, Yamamoto, hcma. FaulknerBrowns UK
     collab on flagship pools.
   - **AB**: DIALOG, GEC Architecture, S2 Architecture, Workun
     Garrick (Edmonton), Stantec

5. **Project typology + KOR specialty match**:
   - **Aquatic centre / pool** — long-span roof + MEP + corrosive
     environment detailing = direct KOR specialty
   - **Ice arena** — long-span + low-temp condensation =
     direct KOR specialty
   - **Field house** — large clear-span = direct KOR specialty
   - **Multipurpose centre** — gym + community + admin = mixed
   - **Small community centre** — less differentiated for KOR

6. **Phase scope** — Municipality rec master plans often
   multi-phase (Newton Community Centre, Britannia Renewal).
   Identify Phase 1-N pipeline.

7. **Duplicate detection** — first-pass already flagged:
   - Newton Community Centre appears 4x (IDs 4221, 4589, 5288, 6782)
   - Britannia Community Centre appears 2x (IDs 4196, 6483)
   - Flag any additional dupes for consolidation

### PART B: Build the pursuit play

For every PURSUE / MONITOR:

8. **Named decision-makers**:
   - Municipality CAO
   - GM Parks Recreation Culture Facilities
   - Director Recreation
   - Capital Projects Manager
   - Mayor (political)
   - For each: email, phone, LinkedIn URL

9. **Warm-introduction paths**:
   - Past architect on municipality's prior rec projects
   - Other KOR municipal references
   - BC Recreation and Parks Association events
   - Federation of Canadian Municipalities events
   - Industry: HCMA-led design charrettes

10. **KOR competitive angle**:
    - Aquatic centre long-span + corrosion = direct specialty
    - Ice arena long-span + low-temp = direct specialty
    - Field house clear-span = direct specialty
    - Mass timber + hybrid arena / field house (Whitemud Acres,
      Olds Sportsplex model) = KOR competitive zone

11. **12-month engagement timeline**:
    - Month 1-2: warm-intro to GM Rec or past-architect
    - Month 3-6: in-person municipality facilities meeting
    - Month 6-12: pre-qualified consultant list positioning

### PART C: Revise verdict

- PURSUE — open opportunity, KOR competitive
- MONITOR — locked currently, municipality has more rec pipeline
- DEAD — fully awarded
- DISCOVER — pre-referendum, KOR relationship-build now
- DUPLICATE — flag for MPI consolidation

## DEAD verdict evidence bar

A DEAD verdict REQUIRES:
- Named incumbent structural engineer (not just GC or architect)
  with at least one source URL
- Named architect + GC where known
- Phase scope assessed — is THIS phase DEAD, or is the entire
  rec master plan locked?
- 1 sentence rationale for why no future entry exists on THIS phase

Do not mark DEAD on circumstantial evidence alone. If incumbent
structural is publicly unknown after thorough search, mark MONITOR
with an "INCUMBENT NOT YET PUBLIC" note.

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
    "description": "verified description + procurement model (2-source confirmed or flagged) + named incumbent if DEAD + typology match (pool/arena/field-house) + phase scope + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing + referendum status if applicable + 12-month KOR engagement timeline",
    "status": "AWARDED to <firm> | RFP OPEN | RFP PENDING | RFP CLOSED <date> awaiting award | UNAWARDED PRE-PROCUREMENT | PRE-REFERENDUM",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER/DUPLICATE verdict + named incumbent structural if DEAD (REQUIRED) + procurement model + typology (pool/arena/field-house) KOR specialty match + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move",
    "signals": [
      { "type": "AwardConfirmed|RfpOpen|ReferendumResult|CapitalBudgetApproved|StructuralIncumbent|PhaseLocked|FuturePhaseOpen|DuplicateFlag|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to municipality'. Good: 'Email City of Surrey GM Recreation Jane Doe via prior architect HCMA (Cameron Charlebois) — Surrey has Newton Community Centre (pool + arena) + 2 field houses in 2024-2027 capital plan; KOR's aquatic-long-span depth is the direct differentiator vs Fast + Epp.'",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window — note referendum trigger dates where applicable" }
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
        "notes": "BD-relevant context — decision authority, KOR prior relationship, municipal pipeline visibility" }
    ]
  }]
}
```

Per HONING-OUTPUT-CONTRACT.md: `_providerName` is REQUIRED at the
item root and MUST be `"ProjectBriefHoning"`. The ingest whitelists
providers and REJECTS files whose `_providerName` is absent, empty,
or not in the project whitelist (`ProjectBrief`, `ProjectBriefHoning`,
`PrimeConsultantResearch`). An unmarked output mis-files as
`ProjectBrief` and overwrites the first-pass brief.

## Quality bar

For every PURSUE or MONITOR:
- At least 3 named individuals with role + org
- At least 1 individual with direct email / phone / LinkedIn
- Named procurement model confirmed via at least 2 independent
  sources (or flagged unconfirmed if only 1 source found)
- Named long-span / pool / arena / field-house typology match
- Named incumbent if applicable
- Specific 12-month engagement timeline
- Named warm-intro path

For DEAD projects:
- Named incumbent structural engineer + at least 1 source URL
- Named architect + GC where known
- Phase scope: this phase DEAD, future phases status assessed
- 1 sentence rationale

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

Write "starting" before the first item, "working" (with updated
index/id/name) before each item, "done" after the last SUMMARY file
is written.

**Time bail-out (hard rule):** max ~60 seconds of wall-clock effort
per item. If an item exceeds this limit, write
`outputs/skipped-{id}.txt` with a one-line reason and move to the
next item immediately. Never stall the batch on one item.

Tool-call budget: 15-22 per item.

## Final step — SUMMARY file (REQUIRED)

After the last item in the batch, write
`outputs/SUMMARY-batch-NNN.txt` (NNN = batch number, zero-padded):

```
batch-NNN: X completed, Y skipped, Z total
PURSUE: <count> | MONITOR: <count> | DEAD: <count> | DISCOVER: <count> | DUPLICATE: <count> | SKIPPED: <count>
```

Auto-discovery treats a batch as unfinished until this file exists.

## Output ONLY the per-project JSON files + heartbeat

Do not emit prose to stdout. Do not ask for confirmation.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-recreational-honing\outputs"`
