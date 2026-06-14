# QueueDrain Fix Pass 2 — Person Queue displayName Placement

## False positive to ignore
The audit flagged `{id}` vs `{numeric-id}` in filename patterns.
**This is a false positive — ignore it entirely.** Do NOT change filename pattern wording.

## Objective
Fix the output schema examples in 6 person queue PROMPT.md files. The defect: these
PROMPT.md files show `displayName` in the wrong location (at the envelope root, or in
wrong order inside the item), which tells Sonnet to put it in the wrong place, causing
every output file to be rejected at ingest.

The ingest resolves the person by reading `displayName` from INSIDE `items[0]` — if it
is anywhere else, the record is silently skipped (ok=0).

Use only Edit. Do NOT rewrite any PROMPT.md. Do NOT touch files outside the 6 listed.
Do NOT run dotnet, git, or any shell command.

---

## The 6 queues to fix

```
\\KOR-APP01\QueueDrain\contact-finder\PROMPT.md
\\KOR-APP01\QueueDrain\engagement-plans\PROMPT.md
\\KOR-APP01\QueueDrain\okanagan-people\PROMPT.md
\\KOR-APP01\QueueDrain\people\PROMPT.md
\\KOR-APP01\QueueDrain\vanisland-people\PROMPT.md
\\KOR-APP01\QueueDrain\honing-people\PROMPT.md
```

---

## What to fix in each PROMPT.md

Read each file. Find the output schema / envelope example. Apply these edits.

### Fix 1 — displayName must be FIRST field INSIDE items[0]

The correct envelope looks like this:
```json
{
  "schemaVersion": "1.0",
  "kind": "person-brief-refresh",
  "generatedAtUtc": "2026-06-01T00:00:00Z",
  "items": [
    {
      "displayName": "Jane Smith",
      "_providerName": "PersonBriefHoning",
      "overallConfidence": 0.88,
      ...
    }
  ]
}
```

**`displayName` is the FIRST field inside `items[0]`.**
**`_providerName` is the SECOND field inside `items[0]`.**
**Neither field appears at the envelope root.**

If the PROMPT.md shows `displayName` at the envelope root (same level as `schemaVersion`),
move it to be the first field inside `items[0]`.

If the PROMPT.md shows `_providerName` before `displayName` inside the item, swap them.

If the PROMPT.md shows the item starting with `"overallConfidence"` before `displayName`,
reorder so `displayName` is first.

### Fix 2 — _providerName accepted values

In the schema example, `_providerName` must be exactly one of:
- `"PersonBrief"` — for first-pass and contact-enrichment queues
- `"PersonBriefHoning"` — for honing and deep-research queues

If the PROMPT.md shows any other value (e.g. `"KOR BD Research Agent"`, `"PersonRefresh"`,
or a blank), change it to the correct value for that queue's purpose:
- contact-finder, people, okanagan-people, vanisland-people → `"PersonBrief"`
- honing-people, engagement-plans → `"PersonBriefHoning"`

### Fix 3 — Add or strengthen the IDENTITY RULE

After the schema example, ensure there is an explicit rule block that says:

```
## IDENTITY RULE (MANDATORY)
`"displayName"` MUST be the FIRST FIELD INSIDE `items[0]` — not at the envelope
root, not anywhere else. The ingest resolves the person by reading displayName
from inside the item object only. An output without displayName inside the item
is silently skipped (ok=0).
`"_providerName"` MUST be exactly "PersonBrief" or "PersonBriefHoning".
```

If the rule already exists but is weaker (e.g. doesn't say "first field"), strengthen it.

---

## After editing

Write a one-line log per file to:
`C:\VIsual Studio Projects\Operations\tools\fix-qdrain-2-results.md`

Format:
- `FIXED: <queue-name> — <fixes applied>`
- `SKIPPED: <queue-name> — already correct`
