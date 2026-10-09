# Early-signal wiring plan — ten verified feeds, 2026-10-08

Ten municipal feeds were **queried live on 8 October 2026** and all ten returned current
data. None is wired. The adapters they need already exist. This is the plan to connect
them, in the order that closes the biggest gaps first.

Every row below was measured, not read off a portal description. The endpoints are
recorded here because the predecessor document did not keep its own — see *What went
wrong with the last audit*.

## What is already covered

| Region | Early-signal feeds | State |
|---|---|---|
| Vancouver / Lower Mainland | Surrey, Coquitlam, Maple Ridge, Vancouver | live |
| Vancouver Island | Saanich, Langford, Courtenay, Victoria, Qualicum, Comox ×2, Campbell River, Colwood, RDN | live |
| US — California | CA_CEQAnet, CA_SocrataSanDiego, CA_SanJoseCkan, CA_SocrataSF | live, 94 new rows in 30 days |
| **Alberta** | **none** — APC is tenders, AB_MajorProjectsInventory is quarterly forward pipeline | **gap** |
| **Okanagan** | Kamloops only (and Kamloops is Thompson) | **gap** |
| **US — outside California** | none | **gap** |

## The ten verified feeds

### Alberta — the biggest gap, and the easiest to close

| Feed | Endpoint | Rows | Newest | Applicant? |
|---|---|---|---|---|
| Calgary Development Permits | `data.calgary.ca/resource/6933-unw5.json` | 193,127 | 2026-10-08 | ⭐ `applicant` |
| Calgary Land Use Redesignation | `data.calgary.ca/resource/33vi-ew4s.json` | 8,998 | 2026-10-08 | ⭐ `applicant` |
| Calgary DP Public Notices | `data.calgary.ca/resource/8rd9-gix2.json` | — | 2026-10-08 | — |
| Edmonton Development Permits | `data.edmonton.ca/resource/2ccn-pwtu.json` | 120,420 | 2026-10-08 | no |

Calgary's **Land Use Redesignation** is rezoning — earlier in the cycle than a development
permit — and carries `fromlud` → `proposedlud`, i.e. the density change being sought, with
the applicant named. That is the single richest early signal found anywhere in this pass.

### US — all five re-verified from the 2026-08-27 audit

| Feed | Endpoint | Rows | Newest | Useful fields |
|---|---|---|---|---|
| Austin | `data.austintexas.gov/resource/3syk-w9eu.json` | 2,379,364 | 2026-10-07 | `issued_date`, cost |
| Phoenix | `maps.phoenix.gov/pub/rest/services/Public/Planning_Permit/MapServer` | 65,820 + 12,062 | **2026-10-08** | `PROFESS_NAME`, `SCOPE_DESC` |
| Raleigh / Wake | `services.arcgis.com/v400IkDOw1ad7Yad/arcgis/rest/services/Building_Permits/FeatureServer/0` | 184,498 | 2026-10-06 | `estprojectcost`, `contractorcompanyname` |
| Las Vegas | `services1.arcgis.com/F1v0ufATbBQScMtY/arcgis/rest/services/OpenData_Building_Permits_/FeatureServer/0` | 439,877 | 2026-10-02 | `APPLICANT`, `LEGALOWNER`, `DECLVLTN` |
| Henderson | `services2.arcgis.com/naGsY5NZWVbd6bwD/arcgis/rest/services/DSC_Permits/FeatureServer/0` | 274,930 | 2026-10-02 | `ApplyDate`, `CommercialValue`, `MultiFamilyValue` |

Phoenix's service also carries **Proposed Zoning, Approved Zoning, Zoning Adjustments and
General Plan Amendments** (layers 2–5) alongside permits. Those are earlier signal than
permits and were not mentioned in the August audit.

## Which adapter each feed needs

**No new provider is required for any of the ten.**

- **ArcGIS** → `ArcGisFeatureOpportunityProvider`, SourceType 20. Config only.
  Phoenix, Raleigh, Las Vegas, Henderson.
- **Socrata (JSON)** → `GenericJsonOpportunityProvider`, SourceType GenericJson. Config only
  **once one small change lands** — see below. Calgary ×3, Edmonton, Austin.

### ⛔ The one code change that gates the Socrata half

`GenericJsonOpportunityProvider` exposes sixteen `json.*` paths — `titlePath`, `buyerPath`,
`descriptionPath`, `postedDatePath`, `externalReferencePath` and so on — **but has no
applicant path and never sets `BuyerContactName`.** `ArcGisFeatureOpportunityProvider` does
(`BuyerContactName = Trim(ContactName ?? Applicant, 200)`).

Without it Calgary ingests as address + description with **nobody's name on it**, which is
the difference between a permit notice and a relationship lead. Brief:
`docs/codex/CODEX-genericjson-applicant-path.md`.

## Two traps, both already paid for elsewhere

**⚠ Nevada exposes `tables`, not `layers`.** Both `OpenData_Building_Permits_` and
`DSC_Permits` return `"layers": []` with the data in `"tables": [{"id": 0, …}]`.
`FeatureServer/0` queries correctly, but an adapter that enumerates layers sees an empty
service and reports nothing — healthy-looking and silent. Check this before wiring either.

**⛔ Kelowna's "Development Permit Area" layers are zoning overlays, NOT applications.**
`opendata.kelowna.ca` carries 282 datasets; the permit-shaped hits are all *2040 OCP
Development Permit Area* — Form and Character, Natural Environment, Hazardous Condition,
Wildfire. The module guidance already names this trap: *"A 'Development Permit AREA' layer
is a zoning overlay, not applications. It returns valid features and ingests cleanly as
nonsense. Abbotsford's was rejected for exactly this."* Do not wire them.

## Order of work

1. **`json.applicantPath`** — one provider change, gates everything Socrata. Codex brief written.
2. **Calgary** ×2 (Development Permits + Land Use Redesignation). Closes the Alberta gap with
   a named applicant. Prove with a probe before enabling, per the module rule: seed
   `IsEnabled = 0`, enable after the Worker deploy.
3. **Edmonton** Development Permits.
4. **Phoenix** — ArcGIS, config only, richest US signal (permits + zoning + GPA).
5. **Raleigh, Austin** — config only.
6. **Las Vegas, Henderson** — config only, after the `tables` behaviour is confirmed.
7. **Okanagan** — unfinished discovery. Kelowna needs a real *applications* layer (not DPA);
   Vernon and Penticton hub domains were not found. This is research, not wiring.

Each new source gets `source_went_silent` coverage on day one through the weekly audit —
with the caveat that SourceType 18 is excluded from that sentinel, so these should be
SourceType 20 / GenericJson rather than MPI if they are to be monitored.

## What went wrong with the last audit, so it is not repeated

`docs/audit-2026-08/mve-feed-audit` (27 August 2026) is rigorous — fourteen feeds, every one
queried, eight of them shown to advertise data they do not deliver. **It records row counts,
newest records and verdicts, but not the URLs it queried.** Six weeks later, re-running its
own tests meant re-deriving every endpoint by hand — several hours for five feeds, and two
of them only after DNS failures on the obvious hostnames.

A document whose thesis is *"tested rather than trusted"* must carry the test. Every endpoint
in this plan is written down above.

Two of its figures also moved on re-measurement: **Henderson is 274,930 rows, not 28,137** —
ten times what was recorded, with four separate valuation fields including
`MultiFamilyValue` — and Austin is 2.38M rather than 287,044. Both were almost certainly
filtered slices recorded as totals.
