# Step 1 — Discover websites for the bare-orgs backlog

**Purpose:** One-shot AI pass to convert 2,000+ bare canonical orgs into a definitive `discovered-websites.csv` — verified active website per firm OR null (drop). No enrichment payload, no synthesis — just URL discovery.

**Why this exists:** The 2,126-row bare-orgs queue is too expensive to run through full enrichment Sonnet sessions (5+ hours, lots of tokens). Hybrid pipeline is faster + cheaper:
- This step (you, here, now) → AI does the one thing that's hard without web search: matching messy firm names to current websites
- Step 2 (PowerShell) → deep-dives each verified URL, gathers raw evidence (homepage, contact, MX, Wayback, federal contracts)
- Step 3 (Sonnet, later) → polishes raw evidence into structured enrichment

## Inputs

- `KOR-Data-Honing/inputs/bare-orgs-bd-signal-2026-06-03.csv` — 2,126 rows with columns `Id, Kind, DisplayName, Website, Notes, KorProjectsCount, MpiRefs, AwardCount, InterestCount, PermitCount, BdSignalScore`

## Output (write here when done)

`KOR-Data-Honing/outputs/discovered-websites.csv` with columns:

```
Id,DisplayName,Kind,Website,Confidence,Notes
```

- `Website` = verified active URL (homepage) OR empty string if not found
- `Confidence` = `high` / `medium` / `low` / `none`
- `Notes` = optional one-line context (e.g. "rebranded to RMA+SH", "merged into Arcadis", "defunct per BBB", "common-name collision — could be Calgary or LA firm")

## Rules (the part that matters)

1. **Verify before claiming.** A website only counts if you can actually fetch the page (via WebSearch / WebFetch) and the firm name on the page clearly matches the DisplayName. If two firms share a similar name in different markets, return empty Website and note "ambiguous; <reason>".

2. **Confidence guidance:**
   - `high` — website fetched, firm name + Kind + geography match unambiguously
   - `medium` — website found but with caveats (rebrand, abbreviation expansion, slight name variation) — note in Notes
   - `low` — website found but firm identity uncertain (multi-firm name, market unclear)
   - `none` — empty Website; firm appears defunct, ungoogleable, or only exists as a registry shell

3. **Handle these patterns explicitly:**
   - Rebrands (IBI Group → Arcadis, Robertson Martin Architects → RMA+SH) — use the current URL, note the rebrand in Notes
   - Acronyms (HFKS, KWC, MMM) — expand if needed to find the firm
   - Punctuation (RMA+SH, TKA+D) — strip for search, return the actual URL the firm uses
   - Sister firms / branch offices — return the firm-level homepage, not a branch landing page

4. **Drop ruthlessly.** If you can't confidently locate a website, return empty Website. We'd rather drop 1,000 ungoogleable firms than enrich them with hallucinated URLs.

5. **Geographic priors:** Our markets are BC, AB, Vancouver Island, Okanagan, LA/SD, and the US West Coast. If you find two firms with the same name in different markets (e.g. AMRON Construction in LA vs Redcliff AB), pick the one matching the geographic signal from the existing data (we can verify against awards/MPI/permits in PowerShell later). When unclear, mark `low` confidence and note "ambiguous market — verify provenance".

6. **Do NOT enrich.** No sectors, no key people, no notable projects, no KOR-fit. That's Step 3. This step is one column: URL or null.

7. **Performance hint:** Batch firms by Kind. GCs get treated similarly (search firmname + "construction" + province). Architects get treated similarly (search firmname + "architects" + province). Buyers/Developers vary more — do these one at a time.

## Autonomous-operation block

Run autonomously to completion. Do not ask for confirmation on individual rows. WebSearch / WebFetch as much as needed. Write the output CSV when done. Append progress lines to `outputs/discover-websites-progress.log` every 100 rows so a crash mid-run is recoverable. If a row crashes, mark Confidence=`none` and continue.

## Expected output volumes

Based on Session 5's 73-verified / 22-not-found / 5-uncertain split (73% verified rate):
- ~1,400 verified websites (high or medium confidence)
- ~600 no-URL drops
- ~100 ambiguous (low confidence — proceed to Step 2 but with weaker priors)

If your numbers diverge sharply from this, log a note in the progress file and continue.

## When you're done

1. Final `outputs/discovered-websites.csv` written
2. `outputs/discover-websites-summary.md` — one-pager with: total processed, high/medium/low/none counts, top 20 rebrands/notable findings, any patterns worth flagging for me (the Opus orchestrator)
3. Ping the next step manually — Step 2 PowerShell deep-dive against the discovered list
