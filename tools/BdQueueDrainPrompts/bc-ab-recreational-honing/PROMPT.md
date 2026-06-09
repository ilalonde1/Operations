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

Auto-discover: `inputs/batch-*.json`. Find lowest-numbered batch with
no matching `outputs/SUMMARY-batch-NNN.txt`.

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

## Output schema (canonical envelope, R93c)

Write to `outputs/refresh-project-{id}.json`. End `description`
with `[providerName: ProjectBriefHoning]`.

## Quality bar

For every PURSUE or MONITOR:
- At least 3 named individuals with role + org
- At least 1 individual with direct email / phone / LinkedIn
- Named procurement model + incumbent if applicable
- Named long-span / pool / arena / field-house typology match
- Specific 12-month engagement timeline
- Named warm-intro path

## Progress heartbeat (REQUIRED)

Write `outputs/_status.json` (starting/working/done).
Bail-out: 15-22 tool calls per item.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\bc-ab-recreational-honing\outputs"`
