# BC + AB Hospital Construction — Verification + Pursuit Play Honing

You are KOR Structural's BD analyst doing a **verification + pursuit
play honing pass** on hospital construction project briefs that have
already been first-pass researched.

**This is the Yurkovich correction pass.** Hospitals are where KOR's
original BD intel got the Yurkovich Pavilion wrong — first-pass
research marked it PURSUE but the Alliance Partnership had locked
structural with Entuitive at team selection. This honing pass must
catch every Alliance / P3 / DBFOM / captive-structural pattern
BEFORE marking anything PURSUE.

Two equal jobs:

1. **VERIFY THE GATE — RIGOROUSLY**: Has the structural engineering
   scope already been awarded, locked in an Alliance, captive in a
   vertically-integrated firm, or otherwise made unavailable to KOR?
2. **BUILD THE PURSUIT PLAY** for PURSUE/MONITOR items — named
   health authority contacts, warm-intro paths, KOR competitive
   angle, 12-month engagement timeline.

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
  "firstPassBrief": { /* full ProjectBrief */ }
}
```

## Workflow per item

### PART A: Verify the gate (THE YURKOVICH CHECK)

Answer DEFINITIVELY with named sources. **This is where the first-pass
brief most often gets it wrong. Be skeptical, search hard, cite
sources.**

1. **Procurement model identification** — CRITICAL DECISION POINT:

   Search for the project's procurement model. Hospital procurement
   in BC + AB falls into FIVE patterns, each with different KOR
   pursuit implications:

   - **ALLIANCE / INTEGRATED PARTNERSHIP** (Yurkovich pattern):
     Health authority + GC + design lead + structural + MEP all
     in one Alliance agreement. Structural is LOCKED at team
     selection (RFQ + RFP). Search "Alliance" + project name +
     "Infrastructure BC". If Alliance: STRUCTURAL IS LOCKED.
     Examples: Richmond Hospital Yurkovich (Graham + HDR +
     Entuitive), Cowichan District Hospital (EllisDon + Parkin +
     ZGF + Bush Bohlman), Nanaimo NRGH likely Alliance.

   - **P3 / DBFOM** (Design Build Finance Operate Maintain):
     Private consortium delivers + operates. Structural is sub of
     consortium GC, locked at consortium selection. Search "P3"
     OR "DBFOM" + project name. Common P3 firms: Plenary,
     Fengate, Forum Equity, EllisDon Capital. If P3:
     STRUCTURAL IS LOCKED.

   - **Progressive Design-Build with Captive Structural** (Cariboo
     pattern): Design-build awarded to firm with in-house structural
     (Stantec, Stantec-led). No external structural sub slot.
     Identify if Stantec is on the team. If captive: STRUCTURAL IS
     LOCKED IN-HOUSE.

   - **Progressive Design-Build with External Structural** (Dawson
     Creek pattern): Design-build awarded to GC + non-vertically-
     integrated architect (HDR, ZGF, Parkin solo). Structural sub
     may be external. **THIS IS WHERE KOR CAN PURSUE** if
     structural slot is open. Verify: who is the structural sub?

   - **Traditional Design-Bid-Build**: Sequential procurement.
     Structural-eng RFP issued separately. KOR can pursue openly.
     Verify: when is/was the structural RFP?

2. **Award status detection** — search:
   - Infrastructure BC project page (infrastructurebc.com)
   - Health authority press releases (vch.ca, fraserhealth.ca,
     islandhealth.ca, interiorhealth.ca, northernhealth.ca,
     albertahealthservices.ca, covenanthealth.ca)
   - Government news (news.gov.bc.ca, alberta.ca/news)
   - Construction industry press: ReNew Canada, On-Site Magazine,
     Healthcare Facilities Today, Canadian Architect, Daily
     Commercial News, Construction Canada, HCO News, REMI Network
   - Each named GC's project page (Graham, EllisDon, PCL, Bird,
     ICI Construction, etc.)
   - Each named architect's project page (HDR, Parkin, ZGF,
     Stantec, DIALOG, IBI/Arcadis, Adamson, MTBA, NORR)

3. **Incumbent structural engineer identification** — common BC + AB
   healthcare structural firms:
   - **Alliance / P3 winners**: Entuitive (Richmond), Bush Bohlman
     (Cowichan), Read Jones Christoffersen (multiple)
   - **Stantec captive** (locked when Stantec is design lead)
   - **External sub specialists**: Glotman·Simpson, Fast + Epp,
     Equilibrium, RJC, AME Group, Tarpley, ASPECT, ESS
   - **AB healthcare**: Read Jones, Williams Engineering, Stantec,
     DIALOG, McElhanney
   - **Mass timber specialists** (cultural buildings, wellness
     centres): ASPECT, Fast + Epp, Equilibrium, StructureCraft

4. **Health authority capital plan context** — search for the
   health authority's broader capital plan:
   - VCH: Multi-billion redevelopment program (UBC Renewal,
     Richmond, others)
   - FHA: Surrey Memorial expansion, Royal Columbian, Burnaby
   - Island Health: Cowichan complete, Nanaimo NRGH planning,
     Westshore LTC, Campbell River LTC
   - Interior Health: Vernon Jubilee Psychiatric, Royal Inland
     Hospital Kamloops
   - Northern Health: Dawson Creek (Graham), Stuart Lake (Graham),
     Mills Memorial Terrace
   - AHS: Stollery Children's planning, Royal Alexandra
     redevelopment, Edmonton Hospital
   - Covenant Health: Misericordia redevelopment

5. **Phase scope** — many hospital projects are multi-phase:
   - Phase 1 may be locked, Phases 2-4 may be open future
   - Example: Richmond Hospital Phase 2 Yurkovich = LOCKED
     (Entuitive). Phases 3-4 (South Tower + Ilich Pavilion +
     North Tower) = open future
   - Cowichan complete = Phase 1. Phases 2-3 expansion = future

6. **Verdict logic — Yurkovich correction baked in**:
   - If Alliance / P3 / DBFOM AND award announced → DEAD on
     current phase. Identify if future phases exist (MONITOR).
   - If Stantec is design lead → STRUCTURAL CAPTIVE = DEAD
   - If progressive D-B with HDR / Parkin / ZGF / external
     architect AND structural sub not publicly named → INTEL
     GAP / PURSUE
   - If traditional D-B-B and structural RFP not yet awarded →
     PURSUE
   - If pre-procurement (concept / planning) → DISCOVER (build
     health authority relationship now)

### PART B: Build the pursuit play (only if PURSUE or MONITOR)

For every project that is NOT DEAD:

7. **Named decision-makers with contact info**:
   - Health authority Chief Project Officer (CPO) / Director of
     Capital Projects
   - VP Infrastructure / VP Capital Planning
   - Foundation CEO (community champion)
   - Health Minister (BC: Adrian Dix or successor; AB: current
     Hospital + Surgical Services Minister)
   - Hospital Site Project Manager (active projects)
   - For each: email, phone, LinkedIn — health authority
     directories often publish staff info

8. **Warm-introduction paths**:
   - Past architect on this health authority's prior projects
   - **Past GC who's now on a different active pursuit**
     (Graham — BC healthcare wave 4 projects, EllisDon — Cowichan
     + others, PCL — Stollery likely)
   - Industry events: Canadian Healthcare Engineering Society
     (CHES), Healthcare Facilities Today conferences
   - BC Infrastructure conferences
   - AHS supplier events
   - KOR's prior healthcare references (BMZ-legacy era 1990s-
     2020 + post-rebrand 2021+)

9. **KOR's competitive angle**:
   - **Mid-rise concrete healthcare structural depth**
   - **Seismic** for BC retrofit / replacement projects
   - **Mass timber + hybrid** for wellness / cultural / smaller
     scale
   - **BMZ legacy** = 30+ years of BC healthcare structural work
   - Past KOR healthcare references — search KOR's portfolio for
     prior work on hospital / LTC / wellness centre projects

10. **12-month engagement timeline**:
    - Month 1-2: warm intro to health authority CPO or past-
      architect / past-GC relationship-holder
    - Month 3-6: introductory meeting with health authority capital
      planning team
    - Month 6-9: position on health authority's pre-qualified
      structural-eng list
    - Month 9-12: RFP response on next eligible project
    - **Health authority procurement cadence is slow: 18-24 month
      relationship-to-pursuit. Plan for sustained engagement.**

11. **Risk and dropout signals**:
    - Procurement model is Alliance / P3 / captive Stantec — DEAD
    - Health authority has existing locked-in structural firm
      (RJC dominates VCH, Bush Bohlman dominates Island Health)
    - Project type is bundled with capital partner not on KOR's
      target list

### PART C: Revise the verdict

- **PURSUE** — open opportunity, structural slot available, KOR
  competitive, named warm-intro path
- **MONITOR** — current phase locked but health authority has
  more capital plan pipeline (Phase 2/3/4 future)
- **DEAD** — fully locked (Alliance / P3 / Stantec captive).
  Name the incumbent. Identify if future phases exist.
- **DISCOVER** — pre-procurement phase, KOR should build health
  authority relationship before RFP

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
    "description": "verified description + procurement model + named incumbent (if any) + phase scope + KOR pursuit framing. [providerName: ProjectBriefHoning] marker is legacy — root _providerName is authoritative.",
    "schedule": "verified procurement timing with named sources + 12-month KOR engagement timeline",
    "status": "AWARDED ALLIANCE to <firms> | AWARDED P3 to <consortium> | AWARDED PROGRESSIVE DB to <firm + captive structural> | AWARDED PROGRESSIVE DB to <firm + external structural slot status> | RFP OPEN STRUCTURAL <date> | RFP PENDING | UNAWARDED PRE-PROCUREMENT",
    "korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + procurement model identification + named incumbent if DEAD + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move",
    "signals": [
      { "type": "AllianceAward|P3Award|DesignBuildAward|StructuralAward|PhaseLocked|FuturePhaseOpen|CaptiveStructural|HealthAuthorityCapitalPlan|StructuralIncumbent|Other",
        "subject": "...", "detail": "...", "occurredAt": "YYYY-MM",
        "sourceUrl": "MUST be present" }
    ],
    "actions": [
      { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|WarmIntroPath|MonitorPhase|DropPursuit|Other",
        "recommendation": "SPECIFIC named action. Bad: 'reach out to VCH'. Good: 'Email VCH CPO Sharon Petty via prior architect HDR (Anne Phillips) — VCH has Phase 3-4 Richmond Hospital + UBC Renewal + Squamish redevelopment in 5-year capital plan. KOR's BMZ-era VCH relationship from 1995 Lions Gate Tower is the leverage point.'",
        "targetPerson": "...", "targetOrg": "...",
        "timingNotes": "specific window — health authority 18-24 month cadence" }
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
        "notes": "BD-relevant context — decision authority, prior KOR relationship, health authority capital plan visibility" }
    ]
  }]
}
```

Set `_providerName: "ProjectBriefHoning"` at the item root (see schema above). The `[providerName: ProjectBriefHoning]` description marker is legacy — still recognized as a fallback but no longer sufficient. Per HONING-OUTPUT-CONTRACT.md: the ingest whitelists providers and REJECTS files whose `_providerName` is absent, empty, or not in the project whitelist. An unmarked output mis-files as `ProjectBrief` and overwrites the first-pass brief.

## Quality bar — HIGHEST STANDARD

This is the Yurkovich correction pass. The quality bar is the
highest of any honing PROMPT. For every project:

**Procurement model identification is non-negotiable** — every brief
MUST identify the procurement model as Alliance / P3 / Progressive
DB-captive / Progressive DB-external / Traditional DBB / Pre-
procurement. With named source URL.

For every PURSUE or MONITOR project, the brief MUST contain:
- Procurement model confirmed via at least 2 independent sources
- At least 3 named individuals with role + org
- At least 1 individual with direct email OR phone OR LinkedIn URL
- Health authority capital plan context (other projects in pipeline)
- KOR's prior health authority relationship check (including BMZ
  legacy)
- A specific 12-month engagement timeline
- A named competitive angle vs incumbent (if applicable)
- Identified warm-introduction path

For DEAD projects, the brief MUST contain:
- Named procurement model + sources
- Named incumbent structural engineer with source URL
- Named architect + GC + alliance/consortium partners
- Phase scope (this phase DEAD, future phases status)
- 1 sentence rationale for why no future entry exists on THIS phase

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

Tool-call budget: 20-30 per item — this is the highest-rigor pass.
The Yurkovich error cost real BD credibility. Don't under-research.

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
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-hospitals-honing\outputs"`
