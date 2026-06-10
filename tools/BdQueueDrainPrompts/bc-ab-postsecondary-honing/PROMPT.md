# BC + AB Post-Secondary — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on post-secondary briefs already first-pass
researched.

Post-secondary capital plans are PUBLIC (5-year forecasts on
institution websites). The honing pass deepens the capital-plan
context + verifies award status + builds the institution-relationship
play.

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
   - Institution capital projects page
   - BC Ministry of Post-Secondary Education news.gov.bc.ca
   - Alberta Advanced Education capital announcements
   - BC Bid / Alberta Purchasing Connection for procurement
   - Board of Governors meeting agendas (PUBLIC for most
     institutions)
   - Construct Connect awards
   - Federal SIF / CFREF (Canada First Research Excellence Fund)
     announcements for research facilities

2. **Procurement model** — post-secondary:
   - Direct institution RFP (most common)
   - Ministry-led major capital (BC P-SE; AB Infrastructure)
   - P3 / DBFM (student housing — UBC Lower Mall, Brock Commons
     model)
   - Federal co-funded research (SIF + CFREF + CFI)
   - Donor-named building

   Confirm via **at least 2 independent sources** before recording
   procurement model as confirmed. If only 1 source found, flag as
   unconfirmed.

3. **Incumbent structural engineer** — post-secondary BC market:
   - **Major rivals**: RJC, Glotman Simpson, Fast + Epp, AME,
     Bush Bohlman, ASPECT, Tarpley, Equilibrium
   - **Mass timber academic specialists**: Fast + Epp (UBC
     Brock Commons), StructureCraft, ASPECT (Earth Sciences),
     Equilibrium
   - **AB**: RJC, Williams Engineering, Stantec (in-house),
     DIALOG (in-house), HBJV

   The incumbent structural engineer MUST be identified for every
   awarded project. Check: institution project page, Construct
   Connect awards, architect firm portfolio, LinkedIn project posts,
   industry press (Canadian Architect, Daily Commercial News).
   If genuinely not publicly named after thorough search, document
   the search attempts and flag as "INCUMBENT NOT YET PUBLIC".

4. **Institution capital plan context** — search:
   - Institution 5-year capital plan PDF
   - Recent Board of Governors approvals
   - Other projects in institution pipeline

5. **Phase scope** — many post-secondary projects are
   multi-phase (UBC Brock Commons 1-2, UBC Earth Sciences,
   SFU Surrey expansion).

### PART B: Build the pursuit play

For every PURSUE / MONITOR:

6. **Named decision-makers**:
   - Institution VP Finance & Administration / VP Facilities
   - Director Campus Planning
   - Project Manager (active projects)
   - Board Chair
   - Provincial Minister of Post-Secondary
   - For each: email, phone, LinkedIn URL

7. **Warm-introduction paths**:
   - Past architect on institution's prior projects
   - Other institution projects KOR has worked
   - BMZ legacy (KOR pre-2021 name)
   - Industry events: APPA (educational facilities), Canadian
     Higher Education Construction Conference
   - Faculty contacts (research building champions)

8. **KOR competitive angle**:
   - Mass timber academic (Brock Commons + Earth Sciences model)
     = direct sweet spot
   - Research lab + vibration-sensitive = specialty structural
   - Student housing mid-rise concrete + mass timber = recurring
   - Heritage seismic upgrade of older campus buildings

9. **12-month engagement timeline**:
   - Month 1-2: warm-intro to VP Facilities or past-architect
   - Month 3-6: introductory meeting + capital plan walk-through
   - Month 6-12: position on institution pre-qualified
     consultant list

### PART C: Revise verdict

- PURSUE — open opportunity, KOR competitive
- MONITOR — current locked, institution has more pipeline
- DEAD — fully awarded, no future entry on THIS project
- DISCOVER — pre-procurement, institution capital plan signals
  5-year pipeline, KOR relationship-build now

## DEAD verdict evidence bar

A DEAD verdict REQUIRES:
- Named incumbent structural engineer (not just GC or architect)
  with at least one source URL
- Named architect + GC (or named design-build team / consortium)
- Institution capital plan context — is DEAD on this project
  only, or is this institution's entire pipeline locked?
- Phase scope assessed — future phases status
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
    "description": "verified description + institution capital plan context + procurement model (2-source confirmed or flagged) + named incumbent if DEAD + phase scope + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing with named sources + 12-month KOR engagement timeline",
    "status": "AWARDED to <firm> | RFP OPEN | RFP PENDING | RFP CLOSED <date> awaiting award | UNAWARDED PRE-PROCUREMENT | DONOR-NAMED PENDING",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent structural if DEAD (REQUIRED) + institution capital plan pipeline + KOR competitive angle (mass timber / lab / seismic) + warm-intro path + named target + first move",
    "signals": [
      { "type": "AwardConfirmed|RfpOpen|BoardApproval|CapitalPlanItem|FederalFunding|StructuralIncumbent|PhaseLocked|FuturePhaseOpen|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to UBC Facilities'. Good: 'Email UBC VP Campus + Community Planning Marc Johnson via prior architect HCMA (Michael Heeney) — UBC has Earth Sciences Phase 2 + Marine Drive student housing + 5 additional capital projects in 2024-2028 plan; KOR's mass-timber depth (Brock Commons model) is the direct differentiator vs Fast + Epp.'",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window — note institution procurement cadence" }
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
        "notes": "BD-relevant context — decision authority, KOR prior relationship, institution pipeline visibility" }
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

For every PURSUE / MONITOR:
- At least 3 named individuals with role + org
- At least 1 individual with direct email / phone / LinkedIn
- Institution capital plan context (other projects in pipeline)
- KOR's prior institution relationship check (BMZ legacy included)
- Procurement model confirmed via at least 2 independent sources
  (or flagged unconfirmed if only 1 source found)
- Specific 12-month engagement timeline
- Named warm-intro path

For DEAD projects:
- **Named incumbent structural engineer** with at least 1 source URL
  (this is REQUIRED — not optional for post-secondary)
- Named architect + GC (or design-build team / donor)
- Institution capital plan context (this project DEAD, pipeline
  status for next projects)
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
PURSUE: <count> | MONITOR: <count> | DEAD: <count> | DISCOVER: <count> | SKIPPED: <count>
```

Auto-discovery treats a batch as unfinished until this file exists.

## Output ONLY the per-project JSON files + heartbeat

Do not emit prose to stdout. Do not ask for confirmation.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-postsecondary-honing\outputs"`
