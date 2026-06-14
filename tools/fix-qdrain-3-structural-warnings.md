# QueueDrain Fix Pass 3 — Org _providerName + Structural WARNs

## False positive to ignore
The audit flagged `{id}` vs `{numeric-id}` in filename patterns across many queues.
**This is a false positive — ignore it entirely.** `{id}` already means a numeric ID.
Do NOT change any filename pattern wording in any PROMPT.md.

## Objective
Two tasks in this pass:

**Task A:** Add missing `_providerName` to 5 org queue schema examples.
**Task B:** Add the four mandatory structural sections to every PROMPT.md that is missing them.

Use only Edit. Do NOT rewrite any PROMPT.md in full. Do NOT run dotnet, git, or shell commands.

---

## Task A — Org queues missing `_providerName` (5 queues)

```
\\KOR-APP01\QueueDrain\honing-architects-tail\PROMPT.md
\\KOR-APP01\QueueDrain\honing-buyers\PROMPT.md
\\KOR-APP01\QueueDrain\honing-competitors\PROMPT.md
\\KOR-APP01\QueueDrain\honing-developers\PROMPT.md
\\KOR-APP01\QueueDrain\honing-gcs\PROMPT.md
```

In the output schema example of each file, find the item object. If `_providerName` is
missing from the item root, add it as the SECOND field (after `displayName`):
```json
{
  "displayName": "<verbatim from input>",
  "_providerName": "FirmNarrativeHoning",
  ...
}
```

These are honing queues so the value is `"FirmNarrativeHoning"` (not `"FirmNarrative"`).

Also add or strengthen the OUTPUT REQUIREMENTS block at the top of each file (see Task B
format below) if it is missing.

---

## Task B — Structural WARNs (all queues listed in the audit)

For EVERY PROMPT.md file that is missing one or more of the four mandatory sections
below, add the missing section(s). Use Edit to insert at the appropriate location.
Do NOT change the surrounding content — only add what is missing.

The full list of queues needing structural fixes (from the audit WARNs):

```
\\KOR-APP01\QueueDrain\ab-projects\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-commercial\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-commercial-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-hospitals\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-hospitals-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-postsecondary\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-postsecondary-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-primes\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-recreational\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-recreational-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-residential\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-residential-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-schools\PROMPT.md
\\KOR-APP01\QueueDrain\bc-ab-schools-honing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-housing\PROMPT.md
\\KOR-APP01\QueueDrain\bc-housing-honing\PROMPT.md
\\KOR-APP01\QueueDrain\contact-enrichment\PROMPT.md
\\KOR-APP01\QueueDrain\contact-finder\PROMPT.md
\\KOR-APP01\QueueDrain\defense-military\PROMPT.md
\\KOR-APP01\QueueDrain\defense-military-honing\PROMPT.md
\\KOR-APP01\QueueDrain\ellisdon-deep-dive\PROMPT.md
\\KOR-APP01\QueueDrain\engagement-plans\PROMPT.md
\\KOR-APP01\QueueDrain\firstpass-buyers\PROMPT.md
\\KOR-APP01\QueueDrain\firstpass-competitors\PROMPT.md
\\KOR-APP01\QueueDrain\firstpass-developers\PROMPT.md
\\KOR-APP01\QueueDrain\firstpass-us-orgs\PROMPT.md
\\KOR-APP01\QueueDrain\gap-fill-orgs\PROMPT.md
\\KOR-APP01\QueueDrain\graph-completion\PROMPT.md
\\KOR-APP01\QueueDrain\graph-completion-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-architects-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-architects-tail\PROMPT.md
\\KOR-APP01\QueueDrain\honing-buyers\PROMPT.md
\\KOR-APP01\QueueDrain\honing-buyers-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-competitors\PROMPT.md
\\KOR-APP01\QueueDrain\honing-competitors-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-developers\PROMPT.md
\\KOR-APP01\QueueDrain\honing-developers-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-gcs\PROMPT.md
\\KOR-APP01\QueueDrain\honing-gcs-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-korclients-deep\PROMPT.md
\\KOR-APP01\QueueDrain\honing-orgs\PROMPT.md
\\KOR-APP01\QueueDrain\honing-projects\PROMPT.md
\\KOR-APP01\QueueDrain\honing-us-deep\PROMPT.md
\\KOR-APP01\QueueDrain\indigenous-projects\PROMPT.md
\\KOR-APP01\QueueDrain\indigenous-projects-honing\PROMPT.md
\\KOR-APP01\QueueDrain\okanagan-orgs\PROMPT.md
\\KOR-APP01\QueueDrain\okanagan-people\PROMPT.md
\\KOR-APP01\QueueDrain\okanagan-projects\PROMPT.md
\\KOR-APP01\QueueDrain\org-name-repair\PROMPT.md
\\KOR-APP01\QueueDrain\orgs\PROMPT.md
\\KOR-APP01\QueueDrain\orgs-architect-scout\PROMPT.md
\\KOR-APP01\QueueDrain\orgs-buyers\PROMPT.md
\\KOR-APP01\QueueDrain\orgs-gcs-partners\PROMPT.md
\\KOR-APP01\QueueDrain\orgs-trip\PROMPT.md
\\KOR-APP01\QueueDrain\people\PROMPT.md
\\KOR-APP01\QueueDrain\projects\PROMPT.md
\\KOR-APP01\QueueDrain\proponents\PROMPT.md
\\KOR-APP01\QueueDrain\us-projects\PROMPT.md
\\KOR-APP01\QueueDrain\us-projects-honing\PROMPT.md
\\KOR-APP01\QueueDrain\vanisland-orgs\PROMPT.md
\\KOR-APP01\QueueDrain\vanisland-people\PROMPT.md
\\KOR-APP01\QueueDrain\vanisland-projects\PROMPT.md
\\KOR-APP01\QueueDrain\verdict-stamp\PROMPT.md
\\KOR-APP01\QueueDrain\verify-flags\PROMPT.md
\\KOR-APP01\QueueDrain\vip-ab-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-architects-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-developers-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-gc-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-island-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-okanagan-deep\PROMPT.md
\\KOR-APP01\QueueDrain\vip-van-deep\PROMPT.md
```

### Section 1 — OUTPUT REQUIREMENTS header
If the file does NOT start with `# OUTPUT REQUIREMENTS`, prepend this block at the
very top of the file (before the existing first line):

```
# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE

One output file per item. Envelope: schemaVersion "1.0", correct kind, items array
with one item. Required fields and _providerName value are defined in the schema
section below. An output that does not match the schema is silently rejected at ingest.

```

### Section 2 — Batch loop
If the file does NOT contain an explicit instruction to re-scan `inputs/` after writing
a SUMMARY, add this block immediately after the SUMMARY section (or at the end of the
execution rules section):

```
## BATCH LOOP (MANDATORY)

One batch is NOT the mission. After writing `outputs/SUMMARY-batch-NNN.txt`, go back
to batch discovery: list `inputs/batch-*.json` again and process the next
lowest-numbered batch with no matching SUMMARY. Only finish the session when NO
un-summarized batch remains. Reset `outputs/_status.json` for each new batch.
```

### Section 3 — No Workflow / Agent prohibition
If the file does NOT contain an explicit `Do NOT call Workflow or Agent tools`
instruction, add this line to the execution rules section:

```
Do NOT call Workflow or Agent tools. Use only web_search, web_fetch, Read, Write.
```

### Section 4 — Per-item bail-out
If the file does NOT contain a bail-out rule (time limit or tool budget per item),
add this to the execution rules section:

```
Bail-out: if 8 tool calls on a single item have not produced a result, write what
you have and move on. Never spend more than 60 seconds of tool time on one item.
```

---

## After editing

Write a log to:
`C:\VIsual Studio Projects\Operations\tools\fix-qdrain-3-results.md`

One line per file:
- `FIXED: <queue-name> — sections added: [A, B1, B2, B3, B4]`
- `SKIPPED: <queue-name> — already had all sections`
