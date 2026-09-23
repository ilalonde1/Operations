# Early-signal coverage: what we hold, and what we are missing

**Measured 2026-09-10.** Prompted by the City of Vancouver turning out to be absent
from fourteen municipal application feeds. The question that followed — *what else?* —
is answered here by measurement rather than by hunting one municipality at a time.

## The headline

| | |
|---|---|
| In-footprint municipalities | **56** |
| Development-application feed held | **11** |
| Feed held, but disabled or not an application feed | 3 |
| **No early signal at all** | **42** |
| People living where we hold no application feed | **2,583,534 of 3,973,480** |

We hold an application feed for **11 of 56** municipalities we sell into, covering
**35%** of the footprint's population. Metro Vancouver — the home market — was
**3 of 21** before today.

Municipality list from the provincial dataset
(`WHSE_LEGAL_ADMIN_BOUNDARIES.ABMS_MUNICIPALITIES_SP`, 160 municipalities), filtered to
the four regions KOR sells into. Populations are 2021 census.

## By region

| Region | Held | Of | Population with no feed |
|---|---:|---:|---:|
| Metro Vancouver | 3 | 21 | 1,711,487 |
| Fraser Valley | 0 | 6 | 303,151 |
| Vancouver Island | 8 | 21 | 161,645 |
| Okanagan / Interior | 0 | 8 | 407,251 |

Two things stand out. **The Island is the best-covered region and the smallest
opportunity** — it is where the effort went, because that is where a specific dossier
was being built. **The Okanagan is 0 of 8** despite being named a primary market.

## Found and wired today

| Municipality | Population | Source | Volume |
|---|---:|---|---|
| **Vancouver** | 662,248 | shapeyourcity.ca (EngagementHQ) | 88 live, 20 name the architect |
| **Surrey** | 568,322 | ArcGIS Online, city org | 13,848 total, 1,267 active, **891 pass the gate, 890 held** |
| **Kamloops** | 97,902 | maps.kamloops.ca (Tempest behind ArcGIS) | 151 rows -> 115 applications, **42 held**, 2018-11-08 to 2026-09-21 |

**Updated 2026-09-23.** Coverage is now **13 of 56**. Kamloops was found by
`tools/ArcGisFingerprint`, which classified 137 layers across five municipal
servers and returned exactly one genuine application table. Its layer cannot
page (`supportsPagination: false`), which the adapter could not express until
`arcgis.supportsPagination` was added — before that it read zero rows from a
live layer of 151 and reported a clean partial.

**Kelowna has no public development-application layer.** Its twelve keyword hits
are zoning overlays and Cityworks ROAD USE permits. That is a finding, not a
search failure.

Surrey is the largest single addition we have made. For scale, the entire Vancouver
Island programme is ~3,000 applications across fourteen feeds.

⚠ Surrey's service name contains a space and its directory advertises `/ArcGIS/`
capitalised; the obvious URL returns `{"error":{"code":400}}` inside an HTTP 200.
It has **no date field and no address field** — `PROJECT_NO` carries the year for a
human to read, and no date is invented from it.

⚠ The first Surrey run read 5 pages of 2,000 and reported DEGRADED, because the status
filter was being applied after the fetch. Pushed to the server with `arcgis.where`;
one page, no degradation. **Filters belong at the source.**

## Found, verified, not yet wired

| Municipality | Population | Source | Evidence |
|---|---:|---|---|
| Kamloops | 97,902 | `maps.kamloops.ca` CityMap_PlanningDevelopment | "Planning Applications (Active Only)" 145 rows; "Subdivision Application (Active Only)" 60 rows |
| Port Moody | 33,535 | `engageportmoody.ca` (EngagementHQ) | 66 projects, 23 applications, **17 live** |
| Abbotsford | 153,524 | ArcGIS Online org `ZYlQy38aWlfDG1Qh` | "Development Permit Properties" — needs opening before trusting |
| Delta | 108,455 | `maps.delta.ca` | Building Permits 1,919 (issued — late signal) |

Kamloops is the strongest of these: the layer is **pre-filtered to active** by the city
itself. Its query interface returned 0 rows for `outFields=*` and needs a field list
worked out before wiring.

## Probed and nothing found *by this method*

Richmond · Langley Township · Chilliwack · North Vancouver District · New Westminster ·
Port Coquitlam · North Vancouver City · West Vancouver · Nanaimo · Vernon · Mission ·
Langley City · White Rock · North Cowichan · Oak Bay · Central Saanich · Sidney ·
North Saanich · Port Alberni · Duncan · Powell River · Salmon Arm · Bowen Island · Hope

⛔ **This list is "not found by an ArcGIS Online item search plus an EngagementHQ probe",
not "no feed exists".** The standing note
`reference_island_permit_feed_hunting` records that "no feed found" was wrong on 2 of 3
municipalities last time. Richmond and New Westminster in particular publish
development-application maps that a deeper probe should reach. The remaining hunting
order is: their own `/server/` **and** `/arcgis/rest/services` roots, VertiGIS portals,
Tempest/Prospero, then PDFs.

## Why the gap existed

The false positive that makes this hard: **"Development Permit Area" is a zoning overlay,
not an application.** It matches every keyword, returns perfectly valid features, and
would ingest cleanly as nonsense. Ten of Kelowna's twelve hits and Burnaby's only hit
were overlays. The hunt tool now filters them by name and the survivors are still opened
and looked at before anything is wired.

The second reason: **the platform, not the municipality, is the unit.** Vancouver and
Port Moody were both found on EngagementHQ — a consultation platform nobody thinks to
check for permits — using an adapter built for the Regional District of Nanaimo. Two
municipalities were missed for months because the search was for *a Vancouver feed*
rather than *which platforms carry applications*.

## The tools

Both live in the session scratchpad and should be promoted into `tools/` if this is run
again:

- `coverage_gap.py` — the measurement above. Provincial municipality list vs. our live
  sources, ranked by population.
- `feed_hunt.py` — ArcGIS Online item search plus EngagementHQ tenant probe across every
  municipality in one run. Reports candidates, never conclusions.
- `walk_arcgis.py` — walks a server root through its folders, filters overlays out by
  name, and reports row counts per candidate layer.

## Recommended order

1. **Kamloops** — verified, pre-filtered to active, one field-list problem away.
2. **Port Moody** — the adapter already exists; this is a config row.
3. **Richmond, New Westminster, Langley Township, North Vancouver** — deeper probe.
   Population 480,000 between them and all four are plausible.
4. **Kelowna** — 144,576 people and a primary market with nothing. The ArcGIS search
   returned only overlays; its applications are likely on a different platform.
5. **Re-enable View Royal and Parksville**, both of which exist and are switched off.
