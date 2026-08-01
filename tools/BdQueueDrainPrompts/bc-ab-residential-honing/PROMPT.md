# BC + AB Residential / Condo — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on residential / condo tower briefs already
first-pass researched. This is KOR's largest BD market — relationship
intelligence on each named developer is the differentiator.

Two jobs:

1. **VERIFY THE GATE** — has the structural engineering scope been
   awarded? Who? Is this a developer multi-phase master plan with
   pre-locked structural?
2. **BUILD THE PURSUIT PLAY** — named developer + architect + GC
   contacts, KOR prior relationship status (including BMZ legacy),
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
   - Developer press release / website
   - Construction industry press (Daily Commercial News, ReNew
     Canada, Storeys, Urbanize, BIV)
   - GC announcement (Pinnacle Construction, Wesgroup, Bosa,
     Polygon, Onni in-house)
   - Architect press / portfolio update
   - Municipal development permit application filings
   - Pre-sale launch (signals architect + structural already
     locked)

2. **Procurement model** — residential almost always:
   - Developer-led with in-house GC (Wesgroup, Bosa, Polygon,
     Onni have in-house construction)
   - Developer + selected GC + pre-qualified subs
   - Design-Build for build-to-rent
   - Modular bundled with supplier (Horizon North, Britco,
     Emerge Modular)

   Confirm via **at least 2 independent sources** before recording
   procurement model as confirmed. If only 1 source found, flag as
   unconfirmed.

3. **Incumbent structural engineer** — BC residential market:
   - **High-volume rivals**: Glotman Simpson, Fast + Epp, RJC,
     AME, ASPECT, Tarpley Engineering, ESS, Bush Bohlman,
     Equilibrium, Holmes Structures, KMK
   - **Mass timber specialists**: Fast + Epp, ASPECT,
     Equilibrium, StructureCraft (KOR competitive zone)
   - **AB**: RJC, Williams Engineering, DIALOG (in-house),
     Stantec (in-house), HBJV

4. **Architect** — major BC residential architects often drive
   structural selection:
   - **KOR-aligned per memory**: Chris Dikeakos / CDA Architects,
     Ciccozzi Architecture
   - **Other BC majors**: Bing Thom / Revery, IBI/Arcadis,
     Henriquez Partners, MCMP, Acton Ostry, GBL Architects,
     Perkins+Will, Yamamoto, RH Architecture
   - **AB**: DIALOG, S2 Architecture, Studio Architecture,
     Workun Garrick

5. **KOR's existing relationship status** — CRITICAL:
   - Search "KOR Structural" + developer name
   - Search "Bryson Markulin Zickmantel" + developer name (BMZ
     legacy pre-2021 rebrand)
   - Per memory: KOR clients include Wesgroup, Bosa, Reliance,
     Westland, Belford, Anthem, Cressey, Peterson, Strand,
     Beedie, Capital Region Housing, Onni, Concord, Polygon

6. **Building typology + KOR specialty match**:
   - **High-rise concrete tower (20+ storeys)** — direct KOR
     specialty
   - **Mid-rise concrete + wood hybrid (8-12 storeys)** — KOR
     competitive
   - **Mass timber + concrete hybrid** — KOR emerging specialty
   - **Low-rise wood frame** — less KOR-differentiated
   - **Modular** — bundled with supplier

7. **Phase scope** — many BC residential projects are master-
   planned multi-phase. Identify Phase 1-N pipeline.

### PART B: Build the pursuit play

For every PURSUE / MONITOR:

8. **Named decision-makers**:
   - Developer: VP Development / VP Construction / Principal
   - Architect: Principal-in-Charge
   - For each: email, phone, LinkedIn URL

9. **Warm-introduction paths**:
   - **BMZ legacy reference** if KOR worked with this developer
     pre-2021
   - Past architect on developer's prior projects
   - Industry events: UDI Awards (Urban Development Institute),
     Vancouver Real Estate Forum, NAIOP BC
   - KOR prior commercial-residential references

10. **KOR competitive angle**:
    - High-rise concrete = KOR core
    - Mass timber hybrid (TELUS Ocean / UBC Brock Commons model)
      = emerging KOR specialty
    - BMZ legacy = 30+ years of BC residential relationships
      pre-rebrand

11. **12-month engagement timeline**:
    - Month 1-2: warm-intro to developer VP Development
    - Month 3-6: positioned on developer pre-qualified consultant
      list
    - Month 6-12: RFP response on next developer project

### PART C: Revise verdict

- PURSUE — structural slot open, KOR competitive
- MONITOR — phase locked but developer pipeline open
- DEAD — fully awarded
- DISCOVER — pre-launch, KOR relationship-build now
- DUPLICATE — flag for MPI consolidation

## DEAD verdict evidence bar

A DEAD verdict REQUIRES:
- Named incumbent structural engineer (not just GC or architect)
  with at least one source URL
- Named architect + GC (or developer in-house team)
- Phase scope assessed — is THIS phase DEAD, or is the entire
  developer master plan locked? Many BC developers have 3-5 phase
  pipelines; Phase 1 DEAD does not mean DEAD on the developer
  relationship
- 1 sentence rationale for why no future entry exists on THIS phase

Do not mark DEAD on circumstantial evidence alone. If incumbent
structural is publicly unknown after searching pre-sales marketing,
development permit filings, and GC portfolio, mark MONITOR with
an "INCUMBENT NOT YET PUBLIC" note.

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
    "description": "verified description + procurement model (2-source confirmed or flagged) + KOR developer relationship status (BMZ check) + named incumbent if DEAD + typology match + phase scope + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing + presales status if applicable + 12-month KOR engagement timeline",
    "status": "AWARDED to <firm> | RFP OPEN | RFP PENDING | PRE-SALE LAUNCH | UNAWARDED PRE-PROCUREMENT | DEVELOPER IN-HOUSE SELECTION",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER/DUPLICATE verdict + named incumbent structural if DEAD (REQUIRED) + KOR developer relationship status (existing client or BMZ legacy = call this out) + typology match (high-rise/mid-rise/mass-timber) + phase pipeline (Phase 1 DEAD may open Phase 2) + KOR competitive angle + warm-intro path + named target + first move",
    "signals": [
      { "type": "AwardConfirmed|PreSaleLaunch|DevelopmentPermit|StructuralIncumbent|PhaseLocked|FuturePhaseOpen|KorRelationship|BmzLegacy|DuplicateFlag|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to Bosa'. Good: 'Call Bosa Properties VP Construction James Chan directly — KOR's BMZ-era Bosa relationship (2018 Bosa Waterfront) is the warm intro. Bosa has Phase 2 Brentwood + Surrey Central master plan still open on structural. Reference BMZ Bosa history + KOR's mass-timber hybrid depth vs Glotman Simpson.'",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window — residential moves faster than institutional" }
    ],
    "risks": [
      { "type": "...", "description": "...", "mitigation": "..." }
    ],
    "keyPeople": [
      { "name": "...", "title": "...",
        "side": "Owner|Developer|Architect|GC|Structural|Funder|WarmIntro|Other",
        "orgName": "...",
        "email": "..." or null, "phone": "..." or null,
        "linkedinUrl": "..." or null,
        "notes": "BD-relevant context — KOR prior relationship, BMZ legacy, developer pipeline visibility" }
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
- Named developer + architect identified
- KOR prior relationship status checked (including BMZ legacy)
- Procurement model confirmed via at least 2 independent sources
  (or flagged unconfirmed)
- Specific 12-month engagement timeline
- Named warm-intro path

For DEAD projects:
- Named incumbent structural engineer + at least 1 source URL
- Named architect + GC (or in-house team)
- Phase scope: this phase DEAD, developer pipeline assessed for
  future phases
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

Tool-call budget: 12-18 per item (residential verifies faster than
hospitals — less procurement-model complexity).

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
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-residential-honing\outputs"`
