# Early-signal wiring plan — ten verified feeds

Ten municipal feeds were **queried live on 8 October 2026** and all ten returned current data.
The adapters they need already exist, so most of what remains is config. This is the plan to
connect them, in the order the dependencies allow.

Every row below was measured, not read off a portal description. The endpoints are recorded
here because the predecessor document did not keep its own — see *What went wrong with the last
audit*.

## What is already covered

| Region | Early-signal feeds | State |
|---|---|---|
| Vancouver / Lower Mainland | Surrey, Coquitlam, Maple Ridge, Vancouver | live |
| Vancouver Island | Saanich, Langford, Courtenay, Victoria, Qualicum, Comox ×2, Campbell River, Colwood, RDN | live |
| US — California | CA_CEQAnet, CA_SocrataSanDiego, CA_SanJoseCkan, CA_SocrataSF | live, 94 new rows in 30 days |
| **Alberta** | **none** — APC is tenders, AB_MajorProjectsInventory is a quarterly forward pipeline | **gap** |
| **Okanagan** | Kamloops only (and Kamloops is Thompson) | **gap** |
| **US — outside California** | none | **gap** |

## ⛔ Two standing constraints on anything wired from here

**1. Never seed a new source as SourceType 18.** Q9's staleness sentinel excludes types
0, 7, 18 and 99, and for type 18 nothing else checks freshness — 0 of the 9 audit report
sections reads it. Measured 2026-10-08: `125 enabled = 115 evaluated + 3 historical + 1 type-7
+ 6 type-18`, and those six are the entire US footprint plus both forward pipelines. A source
filed as 18 is a source that can die quietly. New sources are **GenericJson (2)** or
**ArcGisFeatureService (20)**, both of which the sentinel covers.

**2. A high-volume permit feed needs a read-layer filter before it is useful**, not just a
relevance gate. See *The big-ticket problem* below. Land-use *rezoning* feeds do not — rezoning
is inherently a buildable-form change.

## The ten verified feeds

### Alberta — the biggest gap, and the easiest to close

| Feed | Endpoint | Rows | Newest | Applicant? |
|---|---|---:|---|---|
| Calgary Land Use Redesignation | `data.calgary.ca/resource/33vi-ew4s.json` | 8,998 | 2026-10-08 | ⭐ `applicant` |
| Calgary Development Permits | `data.calgary.ca/resource/6933-unw5.json` | 193,127 | 2026-10-08 | ⭐ `applicant` |
| Calgary DP Public Notices | `data.calgary.ca/resource/8rd9-gix2.json` | 94 | 2026-10-08 | — |
| Edmonton Development Permits | `data.edmonton.ca/resource/2ccn-pwtu.json` | 120,420 | 2026-10-08 | no |

**Land Use Redesignation is the one to wire first.** It is rezoning — earlier in the cycle than
a development permit — carries `fromlud` → `proposedlud` (the density change being sought) with
the applicant named, and every row is a buildable-form change, so it needs no big-ticket
filtering. The relevance gate learned Calgary's district codes on 2026-10-08 and now keeps
238 of 400 recent rows, up from 6.

⚠ **Its remaining ceiling is 33.7%.** The gate reads title + description, and 202 of 600 recent
coded rows do not contain the district code in their description. `proposedlud` must be
composed into the description at ingest — the way `ArcGisFeatureOpportunityProvider` prefixes
`"Applicant: {X}. "` — before those rows can be read at all. The Redesignation feed has no
plain-English district field to fall back on; its fields are `address, applicant, applieddate,
description, fromlud, latitude, locationaddresses, locationcount, longitude, multipoint,
permitnum, permittype, proposedlud, statuscurrent`.

**Development Permits needs a date filter and a big-ticket filter.** Unfiltered it is 193,127
rows; `$where=applieddate > '<90 days ago>'` brings that to **1,490**, measured. It carries a
plain-English `landusedistrictdescription` the existing gate vocabulary already reads
("Mixed Use - Active Frontage", "Commercial - Community 1") — but also
"Residential - Contextual One Dwelling", and bare `"residential"` is a building signal
(`StructuralRelevanceGate.cs:53`), so composing that field in admits every single dwelling.
`proposedusedescription` adds the small-permit noise: "SIGN - CLASS B",
"HOME OCCUPATION - CLASS 2", "CONVENIENCE FOOD STORE".

**DP Public Notices is low priority.** Only 94 rows — a rolling window of permits currently
advertised for public notice — and it uses an entirely different field vocabulary
(`job_dscrn`, `lud_cd`, `com_nm`, `dp_ad_dt`, `xtrnl_file_no`), so it needs its own mapping
rather than a copy of the DP one. Potentially high signal per row; small enough to defer.

### US — all five re-verified from the 2026-08-27 audit

| Feed | Endpoint | Rows | Newest | Useful fields |
|---|---|---:|---|---|
| Austin | `data.austintexas.gov/resource/3syk-w9eu.json` | 2,379,364 | 2026-10-07 | `issued_date`, cost |
| Phoenix | `maps.phoenix.gov/pub/rest/services/Public/Planning_Permit/MapServer` | 65,820 + 12,062 | 2026-10-08 | `PROFESS_NAME`, `SCOPE_DESC` |
| Raleigh / Wake | `services.arcgis.com/v400IkDOw1ad7Yad/arcgis/rest/services/Building_Permits/FeatureServer/0` | 184,498 | 2026-10-06 | `estprojectcost`, `contractorcompanyname` |
| Las Vegas | `services1.arcgis.com/F1v0ufATbBQScMtY/arcgis/rest/services/OpenData_Building_Permits_/FeatureServer/0` | 439,877 | 2026-10-02 | `APPLICANT`, `LEGALOWNER`, `DECLVLTN` |
| Henderson | `services2.arcgis.com/naGsY5NZWVbd6bwD/arcgis/rest/services/DSC_Permits/FeatureServer/0` | 274,930 | 2026-10-02 | `ApplyDate`, `CommercialValue`, `MultiFamilyValue` |

Phoenix's service also carries **Proposed Zoning, Approved Zoning, Zoning Adjustments and
General Plan Amendments** (layers 2–5) alongside permits. Those are earlier signal than permits
and were not mentioned in the August audit. Phoenix is the richest US feed for that reason.

## The big-ticket problem — one piece of work, not three

Three feeds share a single shape: high volume, dominated by single-family dwellings, tenant
improvements, signs and home occupations, where the relevance gate correctly admits them (they
*are* building work) but they are not structural engagements.

| Feed | Gate keeps | Of which noise |
|---|---|---|
| Edmonton Development Permits | 344 of 400 | 222 single detached houses |
| Calgary Development Permits | not yet measured | signs, home occupations, R-C1 dwellings |
| Vancouver Island (12 feeds) | — | **already solved** |

The Island solved it at the read layer, not the gate: `opportunities.vIslandApplications`
(migration 316) exposes an `IsBigTicket` bit over a single shared predicate, written once
precisely because the same filter had been written twice in one session and the two disagreed
by 26 rows. Its own header states what it does **not** cover, which is the pattern to keep.

**This generalises to Alberta rather than being rewritten per city.** Doing it once unblocks
Edmonton and Calgary DP together. Keep the gate permissive — it is a radar, recall over
precision — and let the read layer decide what reaches a person.

## Which adapter each feed needs

**No new provider is required for any of the ten.**

- **ArcGIS** → `ArcGisFeatureOpportunityProvider`, SourceType 20. Config only.
  Phoenix, Raleigh, Las Vegas, Henderson.
- **Socrata (JSON)** → `GenericJsonOpportunityProvider`, SourceType 2. Config only.
  Calgary ×3, Edmonton, Austin. `json.applicantPath` shipped 2026-10-08, so these now carry
  the developer's name rather than an address with nobody on it. Precedent for a Socrata
  BaseUrl carrying its own `$select`/`$where`: the live `LACity_RAMP_OpenBids` source.

## Two traps, both already paid for elsewhere

**⚠ Nevada exposes `tables`, not `layers`.** Both `OpenData_Building_Permits_` and `DSC_Permits`
return `"layers": []` with the data in `"tables": [{"id": 0, …}]`. `FeatureServer/0` queries
correctly, but an adapter that enumerates layers sees an empty service and reports nothing —
healthy-looking and silent. Confirm this before wiring either.

**⛔ Kelowna's "Development Permit Area" layers are zoning overlays, NOT applications.**
`opendata.kelowna.ca` carries 282 datasets; the permit-shaped hits are all *2040 OCP Development
Permit Area* — Form and Character, Natural Environment, Hazardous Condition, Wildfire. The
module guidance already names this trap: *"A 'Development Permit AREA' layer is a zoning
overlay, not applications. It returns valid features and ingests cleanly as nonsense.
Abbotsford's was rejected for exactly this."* Do not wire them.

## Order of work

1. **Calgary Land Use Redesignation** — `OpportunitySources` row plus `json.*` mappings, seeded
   `IsEnabled = 0`, proven with a probe, enabled after a Worker deploy. Needs nothing else; the
   gate already reads it.
2. **Compose `proposedlud` into the description** at ingest. Lifts Redesignation's ceiling from
   66% to full coverage.
3. **The big-ticket read layer for Alberta**, generalising `vIslandApplications`. Gates both of
   the next two items.
4. **Calgary Development Permits** — 90-day `$where`, plus `landusedistrictdescription` and
   `proposedusedescription` composed in.
5. **Edmonton Development Permits.**
6. **Phoenix** — ArcGIS, config only, richest US signal (permits + zoning + GPA).
7. **Raleigh, Austin** — config only.
8. **Las Vegas, Henderson** — config only, after the `tables` behaviour is confirmed.
9. **Calgary DP Public Notices** — 94 rows, its own field mapping.
10. **Okanagan** — unfinished discovery, not wiring. Kelowna needs a real *applications* layer
    (not DPA); Vernon and Penticton hub domains were not found.

Each new source gets `source_went_silent` coverage on day one through the weekly audit, provided
constraint 1 above is honoured.

## Known defects against this plan

| Defect | State |
|---|---|
| Type-18 sources have no freshness check; §I prints "All enabled sources ✔" having evaluated 115 of 125 | brief written, `docs/codex/CODEX-mpi-sources-have-no-freshness-check.md` |
| `CA_SocrataSanDiego` is wired as `approvals_created_2026_datasd.csv` — year is a literal in `BaseUrl`, nothing rewrites it. 2024/2025/2026 return 200; 2027 returns 403. Breaks 1 January 2027, silently, being type 18 | brief owed |
| 9 `AB_MajorProjectsInventory` rows carry a `RetiredAtUtc` with no `RetiredReason` | noted, low |
| OR (80 rows) and WA (50) in `MajorProjectsInventory` have had no new row in 132 days and were last seen 109 days ago — probably one-off imports that should be retired rather than left looking live | needs a decision |

## What went wrong with the last audit, so it is not repeated

`docs/audit-2026-08/mve-feed-audit` (27 August 2026) is rigorous — fourteen feeds, every one
queried, eight of them shown to advertise data they do not deliver. **It records row counts,
newest records and verdicts, but not the URLs it queried.** Six weeks later, re-running its own
tests meant re-deriving every endpoint by hand — several hours for five feeds, and two of them
only after DNS failures on the obvious hostnames.

A document whose thesis is *"tested rather than trusted"* must carry the test. Every endpoint in
this plan is written down above.

Two of its figures also moved on re-measurement: **Henderson is 274,930 rows, not 28,137** — ten
times what was recorded, with four separate valuation fields including `MultiFamilyValue` — and
Austin is 2.38M rather than 287,044. Both were almost certainly filtered slices recorded as
totals.
