# BdGatherIntel — info gathering pipeline

Hybrid AI + PowerShell pipeline for converting bare canonical orgs into rich BD intelligence. AI does only what it's uniquely good at; PowerShell does everything else.

## Why this exists

Sonnet enrichment sessions (Session 5 pattern) take ~10–15 min per 100 firms and burn lots of tokens because each row does its own web search + extraction. For the 2,000+ bare-orgs backlog that's ~5+ hours and a high token bill.

This pipeline splits the work:

| Step | Tool | Job |
|---|---|---|
| 1 | **Sonnet (you, one terminal run)** | Discover websites — return verified URL per firm or null |
| 2 | **PowerShell (no tokens)** | Deep-dive each URL: homepage + about/contact/projects/team + MX + Wayback + OpenCorporates + federal contracts |
| 3 | **Sonnet (focused, on rich raw evidence)** | Polish: structured enrichment payload per firm, ready for `BdResearchImport` |
| 3' | **PowerShell (drops)** | For null-website rows, mark `Notes='WebSearchNotFound:<today>'` in CanonicalOrg so the bare-org export skips them next time |

Result: same enrichment quality, ~10× faster, fraction of the token cost.

## Files

- `Step1-DiscoverWebsites-Prompt.md` — paste into a Sonnet terminal session (run with `claude --model sonnet --dangerously-skip-permissions` from `KOR-Data-Honing/`). One-shot pass. Writes `discovered-websites.csv`.
- `Step2-DeepDive.ps1` — reads the Step 1 CSV. For each row with a website, fetches homepage + follow-on pages + MX + Wayback + OpenCorporates + govcanadacontracts.ca. Writes one `evidence-<id>.json` per firm to `outputs/gathered-evidence-<date>/evidence/`.
- `Step3-MarkDrops.ps1` — reads the Step 1 CSV. For every empty-Website row, sets `Notes='WebSearchNotFound:<today>'` on the matching `CanonicalOrg` row. Reversible.

## Typical run

```powershell
# 1. (One Sonnet terminal session — manual, in KOR-Data-Honing/)
#    Paste tools\BdGatherIntel\Step1-DiscoverWebsites-Prompt.md, let it complete.
#    Output: KOR-Data-Honing\outputs\discovered-websites.csv

# 2. Deep-dive every URL (no tokens; PowerShell only)
&  tools\BdGatherIntel\Step2-DeepDive.ps1 -OnlyConfidence 'high,medium'

# 3a. Mark drops (no-URL rows) so they're skipped next time
&  tools\BdGatherIntel\Step3-MarkDrops.ps1

# 3b. (Optional Sonnet polish, batched) — TBD, not yet implemented
```

## Notes / future work

- Step 2's PowerShell deep-dive currently fetches up to 5 follow-on pages per firm (About / Contact / Projects / Team). Adjust the loop in `Get-FollowonPages` to tune depth.
- BC Registry lookup is not yet wired (OpenCorporates covers most Canadian firms but BC Registry would add status + directors). Add when needed.
- Step 3 (Sonnet polish) is not yet implemented. The pattern would be: read each `evidence-<id>.json`, hand to Sonnet with a structured-output prompt, get back the same JSON shape Session 5 used (with `_providerName` routing by Kind), then run `BdResearchImport --only data-honing` or the custom ingest at `C:\Users\ilalonde\AppData\Local\Temp\ingest_bare_orgs.ps1`.
