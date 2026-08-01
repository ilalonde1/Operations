# Kor BD Module — Executive Summary

**As of 2026-05-30 · Branch: `develop` · Round 37 audit-clean**

---

## What it is, in one line

A **first-class business-development platform** sitting natively inside Kor.Operations.App that fuses (a) every public-sector RFP in BC + Alberta, (b) every major project the construction press has reported on, (c) every architectural prime KOR could partner with, (d) every BD touchpoint the partners have logged in 2026 — and lets us **generate a one-page pursuit brief on any of them in seconds**.

## What we now have under the hood

| Asset | Count | Source |
|---|---:|---|
| **Live opportunities** (active RFPs) | **794** | BC Bid, CanadaBuys, Bids&Tenders, Bonfire, APC, MERX, SAM.gov, GraphEmail (8 live ingestion pipelines) |
| **Major-projects inventory** (live, not retired) | **2,302** | AB MPI feed + BC MPI bulk import + KOR research scrapers (LA, PacNW, Indigenous-dev, Institutional, Prime-consultant, Island-Okanagan ecosystem) |
| **Canonical organizations** | **56,154** | De-duplicated across all sources (7,953 dupes merged in Round 26 alone) |
| **Historical awards** | **136,093** | Drives win-probability scoring + competitor profiling |
| **BD engagements** (partner touchpoints) | **70** | Just imported from `KOR Structural BD Tracking 2026.xlsx` — first time this lives in the system, not a spreadsheet |
| **BD contacts** (real people with emails) | **74** | Same source — every touchpoint has a person, a firm, and an outcome |
| **Distinct cities with project activity** | **365** | BC + AB combined |
| **MPI projects cross-linked to BD touchpoints** | live | Round 36's research-agent fuzzy-match wires "potential projects" text → MajorProjectsInventory |

## What changed in the last 4 hours

- **BD-Tracking spreadsheet → first-class data.** All 6 regional sheets (Vancouver/LM, Island, Alberta, Okanagan, USA, Eastern Canada) extracted into 83 normalized rows, grouped into 70 engagements, with the freeform "Potential Projects" column auto-cross-linked against the MPI by region/province.
- **Schema relaxed for BD-tracking reality.** Migration 48 lets engagements live without a parent RFP; migration 49 wires the many-to-many between an engagement and the projects it touches. Added `BuyerCanonicalOrgId / Region / ProposalsSubmittedCad / ProposalsAcceptedCad / PotentialProjects` to the engagement model.
- **New BD-Tracking screen** inside the BD workspace — region tabs (Vancouver/LM / Island / Alberta / Okanagan / USA / Eastern Canada), per-initiator filter (Omar / Conor / Islam / Jim / Rory / John / Ian), rollup card (count + submitted$ + accepted$ + capture rate), engagement grid, and drill-detail panel with Activities + Contacts + Linked MPI Projects.
- **Codex audit twice.** Round 1 found 13 issues — all closed. Round 2 found 13 more — all closed. Round 37 commit `6a139c5` closes the second batch plus 3 analyzer-test offences inherited from prior rounds.
- **Test suite: 325 / 325 green** (modulo 4 env-var-bound integration tests that need Deltek credentials, by design).

## Why partners care

| Pain | What we used to do | What we do now |
|---|---|---|
| "Who's doing the new Surrey hospital — and do we know anyone there?" | Open spreadsheet, search PDFs, ask around | Click the project in the MPI grid → Org Dossier shows architect, owner, GC, our touchpoints, our awards history with that owner |
| "How is the Alberta market doing — projects, dollars, partners?" | Manual roll-up from emails | Click Alberta tab → 32 active BD touchpoints, $X submitted YTD, top primes (Stantec / DIALOG / GEC), top owners (Alberta Health Services / UofC) |
| "Generate a one-pager on Bosa Development for tomorrow's meeting" | 90 minutes in Word | One click → brand-matched PDF (Round 36's brief feature) |
| "Did we ever submit to Kennedy Wilson?" | Tribal memory | Filter engagements by canonical org → all touchpoints + outcomes |
| "Show me the most active architects we should be partnering with" | Gut feel | Top primes ranked by MPI project count: Stantec (23), Chris Dikeakos (18), HCMA (15), KMBR (13), DIALOG (11), Perkins+Will (11) |

## What's next on this track

- **Generate Brief** feature already designed (`project_bd_brief_feature`) — opp / region / org briefs, PDF default + DOCX right-click
- **BD AI layer (Phase 4)** deferred until data is clean and flowing — then layer text-to-SQL Q&A and proactive insights on top of everything above
- **Deltek fusion** — link `Clendor` rows to canonical orgs to unify external (BD) and internal (financial) views of every client
- **Prime-consultant strategy** — KOR wins public AEC work as the architect's structural sub; deep-research session running to identify which primes we should be on slate with first

## What's still in motion right now

- Lower Mainland Pairing R3 (Sonnet) and Edmonton Pairing (Sonnet) — autonomous research sessions filling the architect↔structural-sub relationship graph
- Honing Pass 14 — queued behind those two; canonical hygiene + alias consolidation
