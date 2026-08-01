# QueueDrain Fix Pass 1 — Project Queue Schema Drift

## False positive to ignore
The audit flagged `{id}` vs `{numeric-id}` in filename patterns across many queues.
**This is a false positive — ignore it entirely.** `{id}` in PROMPT.md wording already
means a numeric ID. Do NOT change any filename pattern wording.

## Objective
Fix the output schema in 14 project queue PROMPT.md files. These queues have the OLD
schema — `korAngle` as a flat string, no `honingPass` wrapper, no `verdict` field, some
missing `_providerName`, some missing `id` at item root. Any batch run from these queues
produces output the ingest tool rejects.

Use only Edit. Do NOT rewrite any PROMPT.md. Do NOT touch any file outside the 14 listed.
Do NOT run dotnet, git, or any shell command.

---

## The 14 queues to fix

```
\\KOR-APP01\QueueDrain\ab-projects\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-commercial\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-commercial-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-hospitals\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-hospitals-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-postsecondary\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-postsecondary-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-recreational\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-recreational-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-residential\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-residential-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-schools\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-schools-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-housing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-housing-honing\PROMPT.md
\\KOR-APP01\QueueDrain\defense-military\PROMPT.md
\\KOR-APP01\QueueDrain\defense-military-honing\PROMPT.md
\\KOR-APP01\QueueDrain\indigenous-projects\PROMPT.md
\\KOR-APP01\QueueDrain\indigenous-projects-honing\PROMPT.md
\\KOR-APP01\QueueDrain\okanagan-projects\PROMPT.md
\\KOR-APP01\QueueDrain\projects\PROMPT.md
\\KOR-APP01\QueueDrain\us-projects\PROMPT.md
\\KOR-APP01\QueueDrain\us-projects-honing\PROMPT.md
\\KOR-APP01\QueueDrain\vanisland-projects\PROMPT.md
```

---

## What to fix in each PROMPT.md

Read each file. Apply ONLY the edits below that are needed (skip any that are already correct).

### Fix 1 — `_providerName` value
Find any line in the output schema example that says:
```
"_providerName": "ProjectBrief"
```
Change it to:
```
"_providerName": "ProjectBriefHoning"
```

### Fix 2 — Add `"id"` field at item root
In the output schema example, find the items array. The item object must have
`"id": <numeric MPI id>` as its FIRST field (before `_providerName`). If `"id"` is
missing from the item root, add it. Example of correct item opening:
```json
{
  "_providerName": "ProjectBriefHoning",
  "id": 12345,
```
If both are missing, add them. If `"id"` is present but `_providerName` is wrong, fix
`_providerName` only.

### Fix 3 — Replace flat `korAngle` with `honingPass` wrapper
Find the output schema section. If the item has `"korAngle": "..."` as a flat field
(NOT nested inside a `honingPass` object), replace the entire korAngle field (and any
adjacent flat fields like `description`, `schedule`, `status`, `signals`, `actions`,
`risks`, `keyPeople` that are at the item root) with the canonical honingPass block:

```json
"honingPass": {
  "verdict": "PURSUE_URGENT | PURSUE | MONITOR | DISCOVER | DEAD | DUPLICATE",
  "overallConfidence": 0.0,
  "description": "rich project description",
  "schedule": "milestones + RFP windows",
  "status": "current stage + recent updates",
  "korAngle": "3-5 sentences: how KOR wins this scope, named competitor, named pitch",
  "signals": [
    { "type": "...", "subject": "...", "detail": "...", "occurredAt": "YYYY-MM", "sourceUrl": "..." }
  ],
  "actions": [
    { "type": "ContactStrategy|PursuitAngle|TimingWindow|TeamingMove|Other",
      "recommendation": "...", "targetPerson": "...", "targetOrg": "...", "timingNotes": "..." }
  ],
  "risks": [ { "type": "...", "description": "...", "mitigation": "..." } ],
  "keyPeople": [
    { "name": "...", "title": "...", "side": "Owner|Architect|GC|Structural|Other", "orgName": "..." }
  ]
}
```

Also add or update the HARD RULE near the output schema to say:
```
`honingPass.verdict` is REQUIRED on every item. An item without a verdict is a failed
item — redo it before moving on.
```

### Fix 4 — `_providerName` in honing queues that have `overallConfidence` at item root
Some honing PROMPT.md files show `"_providerName": "ProjectBriefHoning", "overallConfidence"`
as consecutive fields at the item root. The `overallConfidence` should be INSIDE `honingPass`,
not at the item root. If you see `"overallConfidence"` at the item root (outside `honingPass`),
move it inside the `honingPass` block as part of Fix 3.

---

## After editing

For each file, log one line:
- `FIXED: <queue-name> — <list of fix numbers applied>`
- `SKIPPED: <queue-name> — already correct`

Write the log to:
`C:\VIsual Studio Projects\Operations\tools\fix-qdrain-1-results.md`
