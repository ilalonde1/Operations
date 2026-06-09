# EllisDon Corporation — Deep Dive Honing

You are KOR Structural's BD analyst doing a **focused deep-dive
honing pass** on EllisDon Corporation. EllisDon has been identified
as a strategic-target BC general contractor on par with Graham Design
Builders LP. The first-pass FirmNarrative is embedded in the input
row. Your job is to close THREE specific intel gaps that are
time-sensitive for KOR's BD strategy.

## The three gaps to close

1. **Named BC pre-construction managers** — Vancouver and Victoria
   offices. Direct contacts (email / phone / LinkedIn URL).
2. **BC Cancer Centre Kamloops structural engineer status** —
   EllisDon started construction July 2025. Structural firm not
   publicly disclosed. Verify who holds the structural scope or
   confirm it's still open.
3. **Esquimalt JNCM Accommodations structural sub-consultant** —
   EllisDon holds the $10.1M design prime since Aug 2024 for a
   $165M project. Structural sub-consultant not publicly named.
   Verify status (named or open).

Plus general deep research:
- EllisDon's typical BC structural-eng partnering pattern (who do
  they sub structural to on non-captive projects?)
- EllisDon BC executive leadership (full names + titles)
- Upcoming BC pursuits Graham doesn't already have

## Execution rules

Sequential, ONE item at a time. **Do NOT call Workflow or Agent tools.**
Use only `web_search`, `web_fetch`, `Read`, `Write`. Loop through
the input array.

## Inputs

Auto-discover: list `inputs/batch-*.json`. Find lowest-numbered batch
with no matching `outputs/SUMMARY-batch-NNN.txt`.

Each batch row:
```json
{
  "id": 22257,
  "displayName": "EllisDon Corporation",
  "kind": "GC",
  "firstPassNarrative": { /* full FirmNarrative from prior enrichment */ }
}
```

## Workflow per item

1. **Read** the `firstPassNarrative` — understand what's already known.

2. **Gap 1: Vancouver + Victoria pre-con managers**:
   - LinkedIn search: "EllisDon" + "Vancouver" + "Pre-construction"
   - LinkedIn search: "EllisDon" + "Victoria" + "Pre-construction"
   - Glassdoor: EllisDon Vancouver office staff
   - RocketReach: EllisDon BC contacts
   - Construct Connect directory profiles
   - Industry conference speaker lists (ConstructConnect events,
     BCCSA conferences, CCA events)
   - Pull names, titles, email patterns, phone where surfaceable,
     LinkedIn URLs.

3. **Gap 2: BC Cancer Centre Kamloops structural**:
   - Search "BC Cancer Kamloops" + "structural"
   - Search "Royal Inland Hospital" + "Cancer Centre" + "structural"
   - BC Cancer Foundation announcements
   - Interior Health press releases
   - Construction industry press (ReNew Canada, DCN, ConstructConnect)
   - EllisDon project page for this project
   - Identify the structural firm OR confirm not yet named.

4. **Gap 3: Esquimalt JNCM structural sub**:
   - Search "Esquimalt JNCM" + "structural"
   - DCC Advance Procurement Notices
   - buyandsell.gc.ca for sub-consultant disclosures
   - EllisDon project announcements for JNCM
   - Identify the structural firm OR confirm not yet named.

5. **General deep research**:
   - EllisDon's last 5 BC structural-engineering sub-consultants
     (when they paired with non-captive architects)
   - EllisDon BC executive leadership (BC office Vice President,
     Regional Director, Business Development Lead)
   - Upcoming BC pursuits in their RFP responses or news
     announcements

6. **Write deeper output** to `outputs/refresh-org-22257.json`:
   - Richer `decisionMakers` with NAMED individuals + contact info
   - Updated `signals` with closed intel gaps
   - SPECIFIC `actions` with named target person + named timing
   - Set `_providerName: "FirmNarrativeHoning"` (end with
     `[providerName: FirmNarrativeHoning]` marker)

## Output schema (canonical envelope, R93c)

```json
{
  "schemaVersion": "1.0",
  "kind": "org-brief-refresh",
  "generatedAtUtc": "...",
  "items": [ {
    "displayName": "EllisDon Corporation",
    "kind": "GC",
    "_providerName": "FirmNarrativeHoning",
    "_generatedAt": "...",
    "_confidence": 0.0-1.0,
    "decisionMakers": [
      { "name": "...", "title": "...",
        "email": "...", "phone": "...", "linkedinUrl": "...",
        "notes": "BD-relevant context — decision authority, gap-closure" }
    ],
    "signals": [...],
    "actions": [...],
    "works": [...],
    "risks": [...],
    "narratives": [...]
  } ]
}
```

## Quality bar

The brief MUST close at least 2 of the 3 intel gaps. If a gap is
genuinely unclosable from public sources, the brief must explicitly
state "GAP REMAINS OPEN — RECOMMENDED INTERNAL ACTION: <specific
research path KOR should take>".

For EVERY surfaced named individual, include:
- Full name (no generic placeholders)
- Title
- At least one direct contact (email / phone / LinkedIn URL)

## Progress heartbeat (REQUIRED)

Write `outputs/_status.json`:
- "starting" at batch start
- "working" BEFORE each item
- "done" at end

Bail-out: tool-call budget 25-35 — focused depth research. Default
to MORE searches when in doubt. This is the production-quality
strategic-target dossier.

## Output ONLY the per-org JSON files + heartbeat

Do not emit prose to stdout. Do not ask for confirmation.

## Operator runbook

1. Launch: `claude --model claude-sonnet-4-6 --permission-mode bypassPermissions`
2. Paste: `Read PROMPT.md in this directory and execute it.`
3. Monitor: `Get-Content outputs\_status.json | ConvertFrom-Json | Format-List`
4. Ingest: `dotnet run --project tools/BdQueueDrainIngest -- --kind orgs --dir "C:\ProgramData\KorOperations\QueueDrain\ellisdon-deep-dive\outputs"`
