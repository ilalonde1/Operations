% California Pipeline Funnel — Provider Build Plan
% KOR Structural — BD Platform (INTERNAL)
% 2026-06-17

> KOR's own focused California major-projects feed, built off free/public sources — the CA equivalent of the BC providers (CanadaBuys/BcBid/CKAN). Goal: a `CaMajorProjectsInventoryJob` in `Kor.Opportunities.Worker` (alongside `Ab`/`BcMajorProjectsInventoryJob`) on a cron, feeding `MajorProjectsInventory`. No Dodge/ConstructConnect rental. Per [[feedback_use_platform_not_oneshots]]: build providers, not one-shots.

## Source matrix (verified endpoints)
| Source | Method | Endpoint | Auth | Key fields | Cost | Diff |
|---|---|---|---|---|---|---|
| **SF building permits** | Socrata SODA | `data.sfgov.org/resource/k2ra-p3nq.json` | free app token | proposed_units, proposed_construction_type, revised_cost, **structural_notification**, status, description | free | 2 |
| **San Diego County permits** | Socrata SODA | `data.sandiegocounty.gov/resource/dyzh-7eat.json` | free app token | record_type/subtype/category, use, valuation, floor_area, issued_date | free | 2 |
| **LA City permits** | Socrata SODA | `data.lacity.org/resource/hbkd-qubn.json` | free app token | permit_sub_type, work_description, valuation, dwelling_units, stories | free | 2* |
| **San Diego City permits** | CSV file | `seshat.datasd.org/development_permits/…received_datasd.csv` | none | project_id, valuation, units, status, address | free | 2 |
| **CEQAnet** (earliest signal) | HTML scrape | `ceqanet.lci.ca.gov/Search/Recent` + per-SCH pages | none (pace) | SCH#, type (MND/EIR/NOD), lead agency, received, title, description, county | free | 4 |
| **Sacramento City permits** | ArcGIS FeatureServer | via `data.cityofsacramento.org` Hub → API tab | none | permit fields | free | 3 |
| **San Jose permits** | CKAN | `data.sanjoseca.gov/api/3/action/datastore_search?resource_id=761b7ae8-…` | none | permit#, work class, description, address | free | 3 |
| **CA Housing APR** | CKAN | `data.ca.gov/api/3/action/datastore_search?resource_id=fe505d9b-…` | none | jurisdiction, units permitted, year (aggregate) | free | 2 |
| **US Census BPS** | XLS file | `census.gov/construction/bps/xls/cbsamonthly_{YYYYMM}.xls` | none | CBSA, structure type, count, valuation | free | 1 |
| Accela direct API | — | gated (agency registration) | — | — | — | 5 — **SKIP** (use SD CSV) |
| HCAI hospitals | scrape | esp.hcai.ca.gov (no bulk export) | — | project#, status, county | free | 5 — defer |
| DSA schools | form scrape | apps2.dgs.ca.gov/dsa/tracker | — | app#, district, status | free | 5 — defer |

\*LA City dataset last refreshed Jan 2025 — verify cadence before relying.

## Build order
**Tier 1 (build first — API-clean, highest value):**
1. **SF (k2ra-p3nq)** — best field coverage in CA (has a `structural_notification` flag). Same SODA pattern as KOR's Canadian providers → ~1 day.
2. **San Diego County (dyzh-7eat)** — clean Socrata, good valuation → ~1 day.
3. **LA City (hbkd-qubn)** — same pattern; flag the Jan-2025 staleness, monitor → ~1 day.
4. **San Diego City CSV** (seshat.datasd.org) — trivial file ingest → ~½ day.
5. **CEQAnet scraper** — *highest strategic value* (MND/EIR filings 12–36 mo upstream of permits = the earliest pipeline signal). Daily poll of /Search/Recent + keyword filter (keep residential/mixed-use/hotel/commercial/school/hospital; drop road/utility/trail/pipeline) → ~2 days.

**Tier 2:** Sacramento (ArcGIS), San Jose (CKAN), CA APR (data.ca.gov). **Tier 3/defer:** Oakland (confirm dataset ID), OC (fragmented), HCAI + DSA (scrape-only, high value/high effort — defer until Tier 1–2 stable).

## Filtering to KOR's building/SE lane
SODA `$where`: multifamily/commercial + valuation threshold (e.g. SF `proposed_units>4 OR revised_cost>1000000`). All feed the StructuralRelevanceGate at intake (the same always-reject heavy-industrial tier already live) before landing in MajorProjectsInventory with Province='CA'.

## Why this beats Dodge for KOR
Dodge/ConstructConnect are broad + paid. This is **focused** (building/SE-relevant, KOR's metros, CEQA's earliest signal) + **free** + **owned**. And — per the SD Loop-2 finding — **SEAOSD/AIA award feeds + permit `structural_notification` flags are how we'd have caught the VCA/Accolade and Glotman/Andia seat losses earlier**; the funnel is also a competitor-seat early-warning system, not just a project list.

---
*Source: 2026-06-17 integration research. Endpoints verified where marked; [Unverified] dataset IDs (Oakland, OC, Sacramento FeatureServer URL) need a portal lookup at build time. Recommended first build: the SF + SD-County SODA providers + a CaMajorProjectsInventoryJob shell, then the CEQAnet poller.*
