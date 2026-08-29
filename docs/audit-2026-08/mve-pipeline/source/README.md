# Source data for the MVE six-market claims

Every headline in `KOR-MVE-Market-Snapshot` and its companion re-derives from
what is in this directory or its parent. **It is here because it was very nearly
lost.**

## Why this exists

On 28 August 2026 `tools/verify_dossier_claims.py` was run to re-check the
Houston figures and answered *"Houston data not found."* The Houston TDLR
extract, the Miami UDRB packets and the agendas were all sitting in a **session
scratchpad**, which is temporary. The Miami packets — 46–505 MB each, hours of
downloading — survived only because their `pdftotext` output happened not to
have been cleaned up yet.

A claim that cannot be re-derived is a claim that cannot be defended. Anything
a shipped number rests on lives in the repo from now on.

## What is here

| File | Feeds | Claim it supports |
|---|---|---|
| `houston-tdlr-harris.jsonl.gz` | `verify_dossier_claims.py` | 4,087 projects · 3,085 named · 806 firms · top 3% |
| `houston-tdlr-harris-detail.jsonl.gz` | same | 79 multifamily · 45 firms · top 10% · 390 since 1 Jun |
| `miami-udrb-agendas/` (28 files) | `udrb_agendas.py` | **66 distinct PZ cases heard** — the figure that ships |
| `miami-udrb-teams.tsv` | `verify_miami_concentration.py` | the design-team extraction, per board appearance |
| `miami-udrb-meetings.tsv` | — | the meeting index the packets came from |

The parent directory holds the per-market datasets: Arizona, Charlotte, Clark
County, Hawaii, Houston plats, Miami PZAB, and MVE's published client list.

## ⛔ What is deliberately NOT here

**The source PDFs.** Roughly 140 MB of UDRB packets, Phoenix PUD narratives,
Charlotte site plans, Hawaii EAs and Houston plat spreadsheets. Two reasons, and
the second matters more than the first:

1. Size — they are an order of magnitude larger than everything else combined.
2. **Redistribution.** Clark County's portal bans commercial use of its hosted
   documents without the owner's permission, and plan sets are copyrighted.
   The standing rule in this work is **extract the fact, never redistribute the
   file**. That applies to the repo as much as to a client.

Each tool's docstring names the URL its inputs came from, so any of them can be
re-fetched.

## ⚠ The one figure that does not fully re-derive

Miami's **35 projects with an architect confirmed, 13 firms, top 17%** — which
appears in the companion, not in the document that ships.

`udrb_teams.py` emits `(packet, item, firm)` and never carries the PZ case
number onto the item row, so `miami-udrb-teams.tsv` can be counted by **board
appearance** but not deduped to **project**. The companion's own limits
paragraph says the figure is per project and warns that counting appearances
double-credits a firm — ten of Miami's cases were heard at more than one
meeting.

Two separate attempts to "correct" that number were both measurement errors:
counting appearances (47) against a project figure (35), then counting anchors
in the packets (33) against a figure derived from the agendas (66). **Match the
population before comparing a number.** Closing this properly means adding the
case number to `udrb_teams.py`'s output; until then the figure rests on its
original run and stays out of the client document.

## Banked to the graph, 28 Aug 2026

This research lives in `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS`, not only
in these files. Before it ran there were **zero OrgFacts** from any of this work.

- `../decompose-to-brain.sql` — 20 typed facts across 15 orgs. Re-runnable;
  NaturalKey upserts, so a second run stays at 20.
- `../decompose-touchpoint.sql` — the 27 Aug call with Dan Gura as a
  CrmTouchpoint. MVE now reads **Warm** in `vw_OrgWarmth`.

Orgs were created through the dup-safe path (`BdResearchImport
--ingest-canonical`, dry-run then live): **886811–886823**, thirteen creates,
one match (Ovation to existing 54091), zero duplicate fuzzy keys introduced.

The graph now answers these without opening a PDF:

    -- who has an open seat
    SELECT c.DisplayName FROM opportunities.OrgFact f
      JOIN opportunities.CanonicalOrg c ON c.Id = f.CanonicalOrgId
     WHERE f.Body LIKE 'OPEN SEAT%' AND f.RetiredAtUtc IS NULL;

    -- who will never hire an outside architect
    SELECT c.DisplayName FROM opportunities.OrgFact f
      JOIN opportunities.CanonicalOrg c ON c.Id = f.CanonicalOrgId
     WHERE f.Body LIKE 'WILL NOT HIRE%' AND f.RetiredAtUtc IS NULL;
