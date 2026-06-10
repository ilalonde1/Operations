# BC + AB Commercial Office — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on commercial office tower briefs already
first-pass researched.

Two jobs:

1. **VERIFY THE GATE** — has the structural engineering scope been
   awarded? Who? Is this a corporate campus master plan that has
   pre-locked structural across all phases?
2. **BUILD THE PURSUIT PLAY** — named developer + tenant + design
   architect contacts, warm-intro paths, KOR competitive angle,
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

1. **Award status** — has design / construction been awarded? Search:
   - Developer press release
   - GC announcement (PCL, Ledcor, EllisDon, Graham, ICI, Bird)
   - BC Bid / commercial tender platforms (rare for private office)
   - Construction industry press (Daily Commercial News, ReNew Canada)
   - Tenant anchor pre-lease (TELUS, Microsoft, Shopify, RBC, BMO)

2. **Procurement model** — commercial office almost always:
   - Developer-led with selected GC + pre-qualified subs
   - Design-Build for build-to-suit corporate campuses
   - P3 / DBFOM (rare)

   Confirm via **at least 2 independent sources** before recording
   procurement model as confirmed. If only 1 source found, flag as
   unconfirmed.

3. **Incumbent structural engineer** — major BC + AB office market:
   - **BC majors**: Glotman Simpson (Vancouver office dominant),
     RJC, Fast + Epp, AME, ASPECT, Tarpley, Bush Bohlman, ESS,
     Holmes Structures
   - **Mass timber specialists** (growing for office): Fast + Epp,
     ASPECT, Equilibrium, StructureCraft
   - **AB**: RJC, Williams Engineering, Stantec, DIALOG, HBJV

4. **Anchor tenant pre-lease** — defines project viability +
   timeline. If anchor tenant not yet secured, RFP timing is
   speculative.

5. **Phase scope** — corporate campus master plans (TELUS Ocean,
   Microsoft campus, Amazon HQ2, RBC) often multi-phase. Phase 1
   structural may be locked but Phase 2+ open.

### PART B: Build the pursuit play

For every PURSUE / MONITOR:

6. **Named developer + GC + architect + tenant contacts**:
   - Developer: VP Development / VP Construction
   - GC: Pre-construction Manager
   - Architect: Principal-in-Charge
   - Tenant: Facilities Lead (Cushman & Wakefield, JLL,
     Avison Young if outsourced)
   - For each: email, phone, LinkedIn URL

7. **Warm-introduction paths**:
   - Past architect on developer's prior projects
   - Past GC pre-con team on this developer's pipeline
   - Industry events: NAIOP BC Annual Awards, ULI BC conferences,
     BOMA BC, BIV CEO of the Year events
   - KOR's prior commercial-office references

8. **KOR competitive angle**:
   - Mass timber + hybrid (TELUS Ocean model) = direct KOR
     specialty for tech-tenant towers
   - Mid-rise concrete office (Burnaby, Surrey, Edmonton CBD) =
     KOR core
   - Seismic retrofits of older office stock = KOR specialty

9. **12-month engagement timeline**:
   - Month 1-2: warm-intro outreach to developer or architect
   - Month 3-6: positioned on pre-construction team
   - Month 6-12: structural sub-list before RFP

### PART C: Revise verdict

- PURSUE — structural slot open, KOR competitive
- MONITOR — phase locked but developer pipeline open
- DEAD — fully awarded
- DISCOVER — pre-anchor-tenant, KOR relationship-build now

## DEAD verdict evidence bar

A DEAD verdict REQUIRES:
- Named incumbent structural engineer (not just GC or architect)
  with at least one source URL
- Named architect + GC (or consortium partners)
- Phase scope assessed — is this phase only DEAD, or are future
  phases also locked?
- 1 sentence rationale for why no future entry exists on THIS phase

Do not mark DEAD on circumstantial evidence alone. If incumbent
structural is publicly unknown, mark MONITOR with a note that the
gate cannot be confirmed yet.

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
    "description": "verified description + procurement model (2-source confirmed or flagged unconfirmed) + named incumbent if DEAD + phase scope + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing + anchor-tenant status + 12-month KOR engagement timeline",
    "status": "AWARDED to <firm> | RFP OPEN | RFP PENDING | RFP CLOSED <date> awaiting award | UNAWARDED PRE-PROCUREMENT | PRE-ANCHOR-TENANT",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + procurement model + named incumbent if DEAD + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move",
    "signals": [
      { "type": "AwardConfirmed|RfpOpen|AnchorTenantLease|DesignBuildAward|PhaseLocked|FuturePhaseOpen|StructuralIncumbent|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to developer'. Good: 'Email Westbank VP Development Ian Gillespie via prior architect Henriquez Partners (Mark Shieh) — Westbank has 3 towers in Surrey + Burnaby pipeline; KOR's mass-timber hybrid angle is the differentiator vs Glotman Simpson.'",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window" }
    ],
    "risks": [
      { "type": "...", "description": "...", "mitigation": "..." }
    ],
    "keyPeople": [
      { "name": "...", "title": "...",
        "side": "Owner|Developer|Architect|GC|Structural|Tenant|Funder|WarmIntro|Other",
        "orgName": "...",
        "email": "..." or null, "phone": "..." or null,
        "linkedinUrl": "..." or null,
        "notes": "BD-relevant context — decision authority, prior KOR relationship" }
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
- Named incumbent if applicable
- Specific 12-month engagement timeline
- Named warm-intro path

For DEAD projects:
- Named incumbent structural engineer + at least 1 source URL
- Named architect + GC (or named consortium partners)
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
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-commercial-honing\outputs"`
