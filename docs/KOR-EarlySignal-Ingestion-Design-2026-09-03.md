# Early-Signal Ingestion — Design

**2026-09-03. Replaces the discontinued BC Major Projects Inventory with something better than the paid services.**

## Why

BC Stats killed the MPI (last issue Q3 2025, page removed 30 June 2026). That was our only
forward-pipeline feed for BC. Tenders are not a substitute — by the time a project is tendered the
structural engineer was chosen months ago. What we lost was the *early* signal, and what we should
build back is earlier still.

Two things this design refuses to repeat:

- **Per-source scrapers.** 118 sources exist today and 32 of them contribute nothing. Another 30
  hand-rolled municipal scrapers is not a system, it is a maintenance liability.
- **Fetch-success as health.** MPI reported `Success = true` weekly for two months against a dead
  publisher. Every source added here is covered by the `source_went_silent` /
  `source_never_delivered_anything` / `source_everything_filtered_out` checks already in
  `BdIntegrityCheck`.

## The core insight: adapt to PLATFORMS, not municipalities

Almost every Canadian public body publishes through one of five platforms, each with a documented,
stable API. Build one provider per platform and onboarding a new city becomes **a config row, not
code** — the same pattern that already works for Bonfire and bids&tenders tenants.

| Adapter | Platform | Confirmed users |
|---|---|---|
| **A1** | **ArcGIS Hub / ArcGIS Open Data** (REST) | Metro Vancouver, Burnaby, Columbia Shuswap RD, BC Stats building permits |
| **A2** | **Opendatasoft** (Explore API v2) | City of Vancouver (`vancouver.opendatasoft.com`) |
| **A3** | **CKAN** (`package_search` / `datastore_search`) | BC Data Catalogue, open.canada.ca, EAO EPIC, IAAC registry |
| **A4** | **Socrata** | already built — `CaSocrataMajorProjectsInventoryProvider` |
| **A5** | **Civic agenda platforms** (eScribe, CivicWeb, iCompass) | council + committee agendas, the earliest signal of all |

⚠ **A3 already exists in part and is BROKEN — fix it first.** `GovCanada_Construction` calls
`datastore_search` with a filter but **no `sort` and no offset paging**, so it re-reads the same
24,999 of 71,408 records every run, in insertion order, and has never ingested a federal award newer
than 2022. The live corpus runs to 2026-07-08. Fixing paging in A3 is the single highest-value
change in this document and it unlocks EAO EPIC and the IAAC registry at the same time.

## The signal ladder — earliest to latest

The whole point is to move UP this ladder. MPI sat around rung 3. Paid services resell rungs 4-6.

| # | Signal | Lead time | Where |
|---|---|---|---|
| 1 | **Capital plans and budgets** — school district, health authority, post-secondary, municipal 5-year financial plans | 1–3 years | PDFs and board agendas; needs extraction |
| 2 | **Council and committee agendas, public hearing notices** | 3–18 months | A5 platforms |
| 3 | **OCP amendments, rezoning applications** | 6–18 months | A1/A2 datasets, A5 agendas |
| 4 | **Development permit applications** | 3–12 months | A1/A2 |
| 5 | **Subdivision applications** | 3–12 months | A1/A2 |
| 6 | **Building permits** | at construction | A1/A2, BC Stats BPER |
| 7 | Tenders | too late for design | already ingested |
| 8 | Awards | retrospective + competitive intel | A3 (fix paging) |

**Rungs 1–3 are where a structural engineer gets chosen, and no paid service sells them well.** That
is the whole competitive argument for building rather than renting.

## Provincial and federal registries worth wiring

- **EAO EPIC** — BC Environmental Assessment Office project registry. Project description, **phase**,
  **decision**, **proponent name**, **updated daily**. The best single provincial early signal for
  major projects. Open source at `bcgov/esm-server`; also mirrored as an open-data dataset.
- **Canadian Impact Assessment Registry (IAAC)** — federal equivalent, assessment inventory published
  as a dataset. Covers federal-lands and designated projects.
- **BC Stats BPER** — building permits by municipality / regional district, on ArcGIS Hub. Statistical
  rather than project-level, but it is the trend layer MPI used to provide.
- **BC Housing** project pages, **Infrastructure BC** (P3/alliance pipeline), health authority and
  post-secondary capital plans.

## BC's special cases — do not model the province as "cities"

This is where a generic Canadian product gets it wrong and we do not have to.

- **28 regional districts.** Metro Vancouver (MVRD), Capital (CRD), Nanaimo (RDN) and the rest hold
  land-use authority for **electoral areas** — everything outside municipal boundaries. A city-only
  model is blind there.
- **Islands Trust** — a separate land-use authority over the Gulf and Howe Sound islands. Neither
  municipal nor regional district.
- **First Nations lands** — reserve and treaty lands are outside municipal process entirely. The
  te'tuxwtun development (102 acres, Snuneymuxw, approved by member vote 9 May 2026) never touches a
  municipal DP feed. These surface through Nation announcements, economic-development arms and
  federal registries — and they are exactly the projects where KOR's Indigenous-participation card
  matters. **Model them explicitly or miss them entirely.**
- **Improvement districts** and unincorporated communities.
- **Provincial land** — Crown tenures, and school/health/post-secondary sites that bypass municipal
  approval.

## Build order

1. **Fix A3 paging** (`sort` + offset walk). Unlocks 46,409 unread federal award records, plus EPIC
   and IAAC. Smallest change, largest immediate return.
2. **A1 ArcGIS Hub adapter** + config rows for Metro Vancouver, CRD, RDN, Burnaby, Surrey, and BC
   Stats BPER. Widest coverage per unit of work.
3. **A2 Opendatasoft adapter** + City of Vancouver.
4. **EAO EPIC** as a first-class source (rung 3, daily, province-wide).
5. **A5 agenda adapter** — the earliest rung, and the hardest; agendas are PDFs. Do it last, once the
   structured layers are paying.

Each source lands with a config row, a canonical-org resolution path, and coverage by the existing
freshness checks on day one. No source is "done" until a deliberate silence would raise.

## What this does not do

- It does not replace relationships. Rungs 1–3 tell you a project exists; getting the seat is still
  a call to the architect or the developer.
- It does not cover private work with no public process — which on the Island is a real share of
  multi-family. Those still arrive via tenders, press and the network.
- Agenda mining (rung 1–2) is genuinely hard and is scheduled last for that reason, not first.
