# QueueDrain PROMPT.md Audit + Fix

## Objective

Audit every PROMPT.md under `\\KOR-APP01\QueueDrain\` against the ingest
tool contracts below. **REPORT ONLY — do not modify any file.**
Write a results file at the end.

Do NOT call Workflow or Agent tools. Do NOT run dotnet build or dotnet test.
Use only Read, Glob, Grep, Write.

---

## Step 1 — Discover all queues

Glob `\\KOR-APP01\QueueDrain\**\PROMPT.md`. Process every match.

---

## Step 2 — Ingest contracts (authoritative)

### org-brief-refresh queues
Applies to: honing-orgs, gap-fill-orgs, vip-van-deep, vip-ab-deep,
vip-architects-deep, vip-developers-deep, vip-island-deep, vip-okanagan-deep,
vip-gc-deep, and any other queue whose PROMPT.md specifies `kind: org-brief-refresh`.

**Filename:** `refresh-org-{numeric-id}.json` — digits only after the prefix, no slugs.
**Envelope:**
```json
{
  "schemaVersion": "1.0",
  "kind": "org-brief-refresh",
  "generatedAtUtc": "...",
  "items": [ { ... } ]
}
```
**Item root MUST contain:**
- `"displayName"` — verbatim from input
- `"_providerName"` — EXACTLY `"FirmNarrative"` OR `"FirmNarrativeHoning"` (no other values accepted)

### person-brief-refresh queues
Applies to: honing-people, contact-enrichment, and any queue whose
PROMPT.md specifies `kind: person-brief-refresh`.

**Filename:** `refresh-person-{numeric-id}.json`
**Envelope:**
```json
{
  "schemaVersion": "1.0",
  "kind": "person-brief-refresh",
  "generatedAtUtc": "...",
  "items": [ { "displayName": "...", "_providerName": "...", ... } ]
}
```
**CRITICAL:** `"displayName"` MUST be the FIRST field INSIDE `items[0]`.
NOT at the envelope root. NOT anywhere else. INSIDE the item object.
**`_providerName`** MUST be exactly `"PersonBrief"` OR `"PersonBriefHoning"`.

### project-brief-refresh queues
Applies to: honing-projects and any queue whose PROMPT.md specifies
`kind: project-brief-refresh`.

**Filename:** `refresh-project-{numeric-id}.json`
**Item MUST contain at root:** `"_providerName": "ProjectBriefHoning"`, `"id": <number>`
**Item MUST contain:** `honingPass.verdict` — one of:
`PURSUE_URGENT | PURSUE | MONITOR | DISCOVER | DEAD | DUPLICATE`

### org-name-repair queues
Applies to: org-name-repair.

**Filename:** `refresh-orgname-{numeric-id}.json`
**Item MUST contain:** `"canonicalOrgId"` (echoed number), `"garbledName"` (echoed string),
`"correctedName"`, `"confidence"` (float >= 0.7), `"sourceUrl"`, `"_providerName": "OrgNameRepair"`

---

## Step 3 — Checks per PROMPT.md

For each PROMPT.md, check ALL of the following:

### 3A — Output schema correctness
- Does the example JSON in the PROMPT.md show `displayName` in the CORRECT location?
  - org queues: at the item root ✓
  - person queues: as FIRST FIELD inside items[0] ✓ (if it shows it at the envelope root → DEFECT)
- Does `_providerName` in the example match an accepted value exactly?
  - Check for typos: "FirmNavrativeHoning", "FirmNarrativehoining", "KOR BD Research Agent", etc. → DEFECT
- Does the filename pattern in the PROMPT.md say `{numeric-id}`? If it says a name/slug → DEFECT
- Does `"kind"` in the output example match the correct ingest kind?

### 3B — Mandatory sections present
- [ ] OUTPUT REQUIREMENTS block at the very top of the file (before mission text)
- [ ] Heartbeat instructions: `outputs/_status.json` with "starting"/"working"/"done" states
- [ ] Batch loop: explicit instruction to re-scan inputs/ after each SUMMARY and continue
- [ ] No-Workflow/Agent prohibition: explicit "Do NOT call Workflow or Agent tools"
- [ ] Bail-out: tool budget or time limit per item

### 3C — Document the defect
For every defect found, record:
1. Queue name
2. Exactly what is wrong (quote the offending text)
3. Exactly what it should say (the correct replacement text)

---

## Step 4 — Write results

After all queues are audited, write:
`C:\VIsual Studio Projects\Operations\tools\audit-qdrain-2026-06-13-results.md`

```markdown
# QueueDrain Audit Results — 2026-06-13

## Summary
N queues audited. X PASS, Y WARN, Z FAIL.

## FAILs (ingest will break)
### queue-name
- Defect: [exact quoted text that is wrong]
- Must become: [exact replacement text]

## WARNs (won't fail today but will eventually)
### queue-name
- Defect: ...
- Must become: ...

## PASSes
- queue-name
- queue-name
```

---

## Rules

- Read each PROMPT.md fully before checking it.
- **Do NOT modify any file — report only.**
- Do not run dotnet, git, or any shell command.
