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

1. ✅ **Kamloops** — verified, pre-filtered to active, one field-list problem away.
   *Wired; 54 observations as of 2026-10-08.*
2. ✅ **Port Moody** — the adapter already exists; this is a config row.
   *Wired 2026-10-08, migration 334. 17 applications ingested on the first run.*
3. ✅ **Richmond, New Westminster, Langley Township, North Vancouver** — deeper probe.
   Population 480,000 between them and all four are plausible.
   *Probed 2026-10-08. See the second pass below — the answer is not ArcGIS.*
4. ✅ **Kelowna** — 144,576 people and a primary market with nothing. The ArcGIS search
   returned only overlays; its applications are likely on a different platform.
   *Settled: this is a finding, not a search failure. Recorded in migration 333 so nobody
   re-hunts it on ArcGIS.*
5. ⛔ **Re-enable View Royal and Parksville**, both of which exist and are switched off.
   *View Royal CANNOT simply be enabled — see the second pass. Parksville is a manual PDF
   source (SourceType 99) and still owed.*

⭐ **The measurement is no longer only in this document.** §K of the weekly Data Health
Audit now prints it — "N of 56 in-footprint municipalities have an early-signal feed" with
the uncovered population per region — off the roster in migration 333, deriving "held" from
live sources rather than a copied literal. That is the fix for why this file's own numbers
went stale: `coverage_gap.py` hardcoded `HAVE_APPLICATIONS` as a python list, so it could
not notice Surrey and Kamloops being wired. **A gap in a document gets rediscovered; a gap
in the weekly report gets closed.**

## Second pass — 8 October 2026

Prompted by Ian: *"I'm POSITIVE we looked at this - we HAD to have? If not it was a major
miss obviously."* He was right, and this document was the proof. It was not found before a
whole wiring plan had been rewritten around questions it had already answered. The miss was
never the analysis; it was that **items 2–5 above were never executed.**

### What was wired

| Municipality | Population | How | Result |
|---|---:|---|---|
| **Port Moody** | 33,535 | EngagementHQ, migration 334 | **17 applications, first run clean** |

⚠ **The hostname in this document redirects.** It recorded `engageportmoody.ca`; the live
host is **engage.portmoody.ca**. A feed that depends on a redirect fails in a way that looks
like the publisher went quiet.

⚠ **Port Moody names no applicant.** 14 of its 17 descriptions contain the *word* applicant
— as in *"The applicant would like to rezone…"* — and never a firm. Same shape as Langford.
It does publish file numbers (`REZ00058`, `DP000104`), which `engagementhq.fileNumberRegex`
could capture; untested, so not configured.

### Their own ArcGIS servers — found by schema, and the answer is no

Host-guessing per step 3 of the hunt order found **3 confirmed ArcGIS roots of 7 tried**:

| Municipality | Root | Layers |
|---|---|---:|
| North Vancouver (District) | `geoweb.dnv.org/arcgis/rest/services` | 261 |
| **Burnaby** | `gis.burnaby.ca/arcgis/rest/services` | 892 |
| Delta | `maps.delta.ca/arcgis/rest/services` | 314 |

`tools/ArcGisFingerprint` across all three: **0 Applications**, 2 IssuedPermits, 10
ZoningOverlay, 8 NotApplications. The only candidate is Delta's **Building Permits**
(1,903 rows) — issued, so late signal, and the tool says so itself.

⚠ **"0 applications" needed scoping before it could be believed.** The fingerprint judges
by schema, but `Interesting(name)` gates *which* layers get schema-checked, on eight words
(develop, permit, applic, rezon, planning, subdiv, project, zoning). So it inspected
**20 of 1,467 layers**. The other 1,447 were filtered for plausible application words:
14 candidates, all OCP/zoning/heritage overlays. The two strongest — Delta's
`ProposedLandUse / Land Use Proposed` and `- Other` — were hand-checked: **6 fields,
no file number, no status, no date.** Overlays, exactly as the fingerprint's own reasoning
predicts: *an overlay has no file number, because an overlay is not a case.*

Richmond, New Westminster, Langley Township and North Vancouver (City) have **no ArcGIS
root at any guessed hostname**. That is step 3 of eight, not a conclusion.

### Engagement platforms — 0 of 10, and the control is why that is trustworthy

Step 5 found both Vancouver and Port Moody, so it was run across Burnaby, Richmond, New
Westminster, Langley Township, both North Vancouvers, West Vancouver, Delta, Abbotsford and
Chilliwack. **No EngagementHQ tenant at any guessed hostname.**

⚠ **Read this only because the control passed.** The probe ran Vancouver and Port Moody as
live positives first. On its first run Vancouver came back *"not JSON (soft 200)"* — because
a 4,000,000-byte read cap truncated `shapeyourcity.ca/projects.json`, which is 4,767,875
bytes. The control refused to report. **Three false absences in one day came from limits
the probe's own author set**: three AND-ed search terms, a filter requiring the city name in
an item title, and that read cap. A negative from a scope you chose is not a finding.

**Lead: New Westminster runs an engagement platform that is not EngagementHQ.**
`beheardnewwest.ca` → `www.beheardnewwest.ca/en-CA/`, title "Citizen engagement platform",
and `/projects.json` returns the same 8,108-byte HTML. The `/en-CA/` locale path is not
EngagementHQ's shape. 78,916 people; needs a browser look and possibly a new adapter.

**Lead: Delta runs Tempest.** Its ArcGIS carries a `Planning / Tempest Heritage Sites`
layer, and Tempest OurCity/Prospero is a platform we already have an adapter for
(Victoria, Saanich, Colwood). No Prospero instance answered at five guessed hostnames —
`www.delta.ca/ourcity/prospero/search.aspx` returns an honest 404 — so the instance name
has to come from Delta's own planning pages.

### ⛔ View Royal is not the easy win it looks like

Recommendation 5 above says to re-enable it. **It cannot be re-enabled as configured.**
`ScraperProbe --prospero` against its BaseUrl returns **0 applications, "no result rows
rendered"**, because `www.viewroyal.ca/webapps/ourcity/prospero/search.aspx` is a **soft
404** — HTTP 200 serving `<title>View Royal | Page Not Found</title>` with no Prospero
markers at all. Six host variants were tried; Saanich uses `/Tempest/` and Colwood
`/TLive/`, and View Royal's instance was not found.

⚠ **A soft 404 is its own fault class, and three sources shared it on 2026-10-08:**
Coquitlam replaying duplicates, BC MPI's silently removed CSV, and this. **A BaseUrl that
soft-404s looks perfectly alive to any fetch-success check.** Enabling View Royal would have
produced a permanently NEVER-PRODUCED source — noise in the sentinel that §I and §J were
just built to make meaningful.

### Okanagan, Island and the regional districts — 9 October

Asked directly: *"What about the Okanagan??? And Alberta? And Vancouver Island?"*

**Every uncovered Okanagan municipality has its own ArcGIS server, and none publishes
applications.** All seven were found by host-guessing, which is itself the finding — the
September pass cleared this region on an ArcGIS *Online* search:

| Municipality | Pop | Root | Layers | Applications |
|---|---:|---|---:|---:|
| Kelowna | 144,576 | `maps.kelowna.ca/arcgis` | 818 | **0** |
| Vernon | 44,519 | `maps.vernon.ca/server` | 1,155 | **0** |
| Penticton | 36,885 | `maps.penticton.ca/server` | 116 | **0** |
| West Kelowna | 36,078 | `gis.westkelownacity.ca/arcgis` | 411 | **0** |
| Salmon Arm | 19,432 | `gis.salmonarm.ca/server` | 0 | — |
| Lake Country | 15,817 | `gis.lakecountry.bc.ca/arcgis` | 124 | **0** |
| Summerland | 12,042 | `gis.summerland.ca/server` | 151 | **0** |

⭐ **Kelowna's self-hosted server was never checked before.** September settled Kelowna on
`opendata.kelowna.ca`; `maps.kelowna.ca` is a different machine with 818 layers. The verdict
survives the better look — but it was reached the right way this time.

⚠ **A naming false friend worth knowing: Kelowna's `Kelowna_Applications_MapServices`
service means SOFTWARE applications.** It contains Snow Removal Priority, Street Lights and
"Staff Area - Plumbing Inspector". Anything keying on the word "applications" will chase it.

**Vancouver Island: two more found, one wired.**

| Municipality | Pop | Root | Outcome |
|---|---:|---|---|
| **Esquimalt** | 17,533 | `gis.esquimalt.ca/arcgis` | ⭐ **WIRED** — migration 336, 29 of 43 kept |
| Sooke | 15,086 | `gis.sooke.ca/arcgis` | ⛔ not wireable, see below |

Esquimalt runs the **same Tempest DevTracker as Victoria**, with `SUBJECT` and `PURPOSE`
fields, so Victoria's mapping applies directly. Its content is real structural signal —
*"Proposing a 4-story wood frame building with a 1-level underground parkade"*. Dated
2017-07-19 → 2026-08-18, 43 of 43.

⛔ **Sooke has an applications layer and it cannot be used.** `ArcGisProbe` rejected
**18 of 18**: every row is `PLANNING — <address>` with an **empty description**, so the gate
has nothing to tell a house from a tower and correctly drops them all. Wiring it would
manufacture a `source_everything_filtered_out` alert. This is the Saanich problem in the
Island view's own header — *"the source gives nothing to tell them apart"*.

Eight Island municipalities returned nothing by either method: North Cowichan, Port Alberni,
Oak Bay, Central Saanich, Powell River, Sidney, North Saanich, Duncan.

**Regional districts — the "one wiring job instead of sixteen" idea, tested and mostly no.**
Metro Vancouver (`gis.metrovancouver.org`, 883 layers) and Fraser Valley RD
(`maps.fvrd.ca`, 218) both have ArcGIS roots; Central Okanagan, North Okanagan, Cowichan
Valley and Capital RD have none at any guessed host. ⚠ The fingerprint reported three
"Applications" on Metro Vancouver — **Liquid Waste Permits (Industrial / Groundwater) and
Air Quality Permits/Approvals.** Those are environmental permits, not development
applications: they carry a permit number, address and type, so the schema fingerprint cannot
tell them apart. **A schema test is better than a name test and still not a judgment.**

**Alberta: Calgary is live.** `Calgary_LandUseRedesignation`, migration 335 — first run
864 inserted, 0 failed, **712 of 864 naming an applicant (82%)**, e.g. *"Land Use Amendment
to accommodate MU-1 — HORIZON LAND SURVEYS"*. §K does not count it; that roster is BC.

⚠ **It took two runs, and the first looked fine.** Esquimalt's first run returned
**Success=1, 0 inserted, 43 skipped, and zero gate-reject rows** — because
`IngestionService.cs:123` drops a candidate with no Title **or no Url** *before* the
relevance gate, so nothing is recorded anywhere. The cause was a missing
`arcgis.detailUrlTemplate`. Same fault class as Calgary's url-less feed. **Any new source's
first run must be read for INSERTED, not for Success.**

### Where that leaves the four that matter

Burnaby (249,125), Richmond (209,937), Langley Township (132,603) and Delta (108,455) are
**600,120 people with no application feed**, and ArcGIS and EngagementHQ are both now
excluded for them by measurement rather than by assumption. The remaining steps of the hunt
order are 2 (read the city's map page for an app id, then its item data), 4 (VertiGIS
Studio / Geocortex internal portals) and 7 (quarterly PDFs) — plus Delta's Tempest instance
and New Westminster's unidentified platform. That is browser work, not a sweep.
