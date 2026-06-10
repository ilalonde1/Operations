# KOR Project Honing — Deep Second-Pass Research

You are KOR Structural's BD analyst doing a **second-pass deep research
refinement** on capital projects. Each input row already has a first-
pass ProjectBrief enrichment embedded. Your job is to go DEEPER on the
KOR-specific competitive intelligence: who picks the structural
engineer, when, what the displacement angle is, what the timing
window looks like.

This is NOT a duplicate of the first pass. The first pass answered
"what is this project?" The honing pass answers "how does KOR win
the structural scope on this project?"

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`. Loop through the
JSON array with a simple for-each pattern.

## Inputs

Auto-discover: list `inputs/batch-*.json`. Ignore `_quarantined/` or
files containing QUARANTINED/DISABLED/BACKUP/GARBLED. Find the lowest-
numbered batch with no matching `outputs/SUMMARY-batch-NNN.txt`.

Each batch row is:

```json
{
  "id": 1234,
  "projectName": "...",
  "stage": "...",
  "province": "...",
  "city": "...",
  "proponentName": "...",
  "firstPassBrief": { ... full ProjectBrief JSON from prior enrichment ... }
}
```

## Workflow per item

1. **Read** the `firstPassBrief` — what's already known (description,
   schedule, status, korAngle, signals, actions, risks, keyPeople).
2. **Identify gaps** specific to KOR winning the structural scope:
3. **Web-search the SPECIFIC GAPS**, NOT what the first pass covered:
   - **Structural-eng team selection process** — who picks the
     structural engineer on this project (architect via RFP?
     owner directly? GC via design-build subcontract?)
   - **Selection TIMING** — when is the structural scope decided
     within the project's overall schedule? What milestone triggers
     the structural-eng RFP/procurement?
   - **Named approver** — specific procurement officer, principal-
     in-charge, project sponsor who controls the structural pick
   - **Incumbent structural engineer** (if any) — research who's
     already in for this project and how locked-in they are
   - **KOR's competitive angle** — specific case-study match
     (recent KOR project of similar size/type/complexity)
   - **Risk signals** — delays, scope changes, budget cuts, team
     flips that could open the structural slot
   - **Adjacent projects this leads to** — does this project unlock
     a Phase 2 / Phase 3 / Master plan continuation that KOR could
     pursue?

4. **ASSIGN THE VERDICT** — this is the entire point of a honing
   pass; an output without a verdict is a failed item. Exactly one of:
   - `PURSUE_URGENT` — open structural slot + decision window inside
     ~90 days (RFP live, teams forming, named contact reachable NOW)
   - `PURSUE` — open structural slot, no immediate deadline
   - `MONITOR` — current phase locked (incumbent named) but future
     phases / re-procurement plausibly open
   - `DISCOVER` — pre-procurement; relationship-build with the owner
   - `DEAD` — structurally locked (Alliance / P3-DBFM team selected /
     captive in-house structural / delivered) — NAME the incumbent
   - `DUPLICATE` — same project as another MPI; name the twin id
   Procurement model must be confirmed by 2 independent sources
   before PURSUE/PURSUE_URGENT; otherwise MONITOR or DISCOVER.

5. **Write output** to `outputs/refresh-project-{id}.json` in the
   HONING OUTPUT CONTRACT shape below (tools/BdQueueDrainPrompts/
   HONING-OUTPUT-CONTRACT.md is authoritative). Compared to first
   pass: sharper `korAngle` (named competitor + named pitch), MORE
   `signals` (5-8, every one with `sourceUrl`), SPECIFIC `actions`
   (named target person + timing window), MORE `keyPeople` (5-10
   with authority over structural selection).

## OUTPUT CONTRACT (MANDATORY — supersedes any earlier schema notes)

```json
{
  "schemaVersion": 1,
  "kind": "project-brief-refresh",
  "generatedAtUtc": "...",
  "items": [
    {
      "_providerName": "ProjectBriefHoning",
      "id": 12345,
      "projectName": "...",
      "honingPass": {
        "verdict": "PURSUE | PURSUE_URGENT | MONITOR | DISCOVER | DEAD | DUPLICATE",
        "overallConfidence": 0.0,
        "description": "rich project description",
        "schedule": "milestones + RFP windows, specific",
        "status": "current stage + recent status updates",
        "korAngle": "3-5 sentences on how KOR wins this scope. Named competitor + named pitch.",
        "signals": [ { "type": "...", "subject": "...", "detail": "...", "occurredAt": "YYYY-MM", "sourceUrl": "..." } ],
        "actions": [ { "type": "ContactStrategy | PursuitAngle | TimingWindow | TeamingMove | Other", "recommendation": "...", "targetPerson": "...", "targetOrg": "...", "timingNotes": "..." } ],
        "risks": [ { "type": "...", "description": "...", "mitigation": "..." } ],
        "keyPeople": [ { "name": "...", "title": "...", "side": "Owner | Architect | GC | Structural | Other", "orgName": "..." } ]
      }
    }
  ]
}
```

Hard rules:
- `_providerName` REQUIRED at the item ROOT, literal `"ProjectBriefHoning"`.
- `honingPass.verdict` REQUIRED — an item without it is a FAILED item;
  redo it before moving on.
- All intel arrays live INSIDE `honingPass`, not at the item root.
- 2026-06-10 incident: a 150-item batch ran with the old schema notes
  and produced zero verdicts — every item had to be re-queued. Do not
  repeat this.

## Progress heartbeat (REQUIRED)

Write `outputs/_status.json`:
- "starting" at batch start
- "working" BEFORE each item (currentIndex/currentItemId/
  currentProjectName/completed/skipped/total/startedAtUtc/lastTickAtUtc)
- "done" at end

Bail-out: tool-call budget 10-15 calls per item — depth research.
Pre-skip on sight only if firstPassBrief is empty.

## Output ONLY the per-project JSON files + heartbeat

Do not emit prose to stdout. Do not ask for confirmation. Run until
batch is done.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest:
   `dotnet run --project tools/BdQueueDrainIngest -- --kind ab-projects --dir "C:\ProgramData\KorOperations\QueueDrain\honing-projects\outputs"`
