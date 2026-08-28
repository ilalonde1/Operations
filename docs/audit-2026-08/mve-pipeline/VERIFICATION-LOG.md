# Verification log — primary-source checks

Standard: a claim PASSES only when the **named firm's own page**, or the record itself,
states it. A search-engine summary is never a source; it is a lead to a source.
BLOCKED means the page could not be read — it is *not* a pass and *not* a refutation.

Dated 27 August 2026.

## Method defect found and fixed

`verify.py` returned BLOCKED for Thornton Tomasetti because its URL was
`thorntontomasetti.com/projects/las-vegas-ballpark`. The real path is `/project/`,
singular. The URL had been constructed by pattern, not derived from a search.

**A BLOCKED verdict against a guessed URL is evidence of nothing.** Every URL must be
derived from a domain-restricted search first, then fetched. Re-derive before re-running.

## Confirmed

| Claim | Verdict | Source |
|---|---|---|
| Thornton Tomasetti = structural, A's Las Vegas Ballpark | **CONFIRMED** | TT's own project page. Role stated as structural design, construction engineering, façade engineering, waterproofing, façade access — five services, not one. Owner: The Athletics. 33,000 seats, completion 2028. Architects BIG + HNTB. |
| Thornton Tomasetti = structural, Bally's Integrated Resort LV | **CONFIRMED** | TT's own project page. Client Bally's Corporation/JLL. 26 acres on the former Tropicana site. Architect named as **Marnell Howryla Architecture** — the fuller firm name; earlier note said "Marnell Architecture". |
| MVE offices = five, including Guadalajara | **CONFIRMED** | mve-architects.com/connect — Irvine, Los Angeles, San Diego, San Francisco, Guadalajara. |

## Refuted

| Claim | Verdict | Correction |
|---|---|---|
| "TSK" = structural engineer, Las Vegas Convention Center | **REFUTED** | **Magnusson Klemencic Associates (MKA)** is the structural engineer — their own stats block reads "MKA ROLE: Structural Engineer". TSK Architects is a *consulting architect*, alongside tvsdesign (design architect) and Carpenter Sellers Del Gatto. The claim confused a role. Note also: **completed 2021** — history, not a pursuit. |
| MVE has Denver / Salt Lake City / Washington DC studios | **REFUTED** | A search summary asserted this. MVE's own contact page lists five offices and none of them. |

## Open — not proven either way

| Item | State |
|---|---|
| MVE work in Houston / Texas | **UNPROVEN.** No Texas office on their contact page, and the portfolio page is a JS shell that returns no project text to a plain fetch. Absence of an office is not absence of a project. Do not assert it in either direction. |
| Silverado (Clark County) structural engineer | **NOT PUBLICLY NAMED.** Developer Ochoa Development Corporation; architects **Gensler + Yihong Liu + Associates**; civil **RCI Engineering**; representation LAS Consulting. Clark County Zoning Commission hears it **2 September 2026**. Not publicly named ≠ seat open. |

## Arizona — the 50-project set, recounted from source

Source: AZ Big Media, *"50 commercial real estate projects to know in 2026"*. A trade
listicle, not a municipal record — the document already says so, and should keep saying so.

**The five sample rows are all correct.** Address, size, developer, architect and
contractor for 5550 E. Crown Place, 217 E. 7th St., 19360 N. 73rd St.,
10050 N. Scottsdale Rd. and 95 Arroyo Pinon Dr. each match the source verbatim.

**The intermediate parser was the unreliable artefact, not the document.** `ptk_rows.txt`
reported `*** NOT NAMED ***` for architects the page names plainly, and dropped
Layton Construction. Two causes: the source writes `General contractor :` with a space
before the colon on some records, and the block boundaries drifted. Do not use that file
as a checking baseline.

The recount now reconciles against the source's own label counts exactly —
50 blocks / 50 `Address:`, 49 architects / 49 `Architect:`, 26 firms holding 51 slots
(two projects name two firms each).

### League-table errors found and fixed

| Firm | Was published | Actually | Evidence |
|---|---|---|---|
| Butler Design Group | 5 | **6** | Six named addresses: Cotton Ln, Morelos Pl, Loop 202/Hawes, Civic Square, Washington St, Northern Ave |
| Gensler | 2 | **3** | 217 E. 7th St. Tempe, 1 E. Adams St., 1500 N. Central Ave. |

The table also listed firms with one project while omitting four with two — Phoenix Design
One, ALINE Architecture Concepts, Deutsch Architecture Group and Soaring Eight — and had
those four in a "present once each" sentence. Presented as a ranking, it had holes in it.
All four are now in the table and the sentence is corrected.

The "Type" column now says what it is: how the firm appears *in this set*, not an audited
national footprint.

## Hawaii — from empty to the second-strongest region

The Honolulu permit feed is frozen at 1 July 2025, so the feed audit had written Hawaii
off. It should not have: **the regulator publishes a monthly PDF that names what is under
construction.** The HCDA Executive Director's Kaka'ako report is the workaround.

From the reports of 6 May and 1 July 2026, cross-checked against each other:

| Tower | Developer | Architect | Confirmed by |
|---|---|---|---|
| **Kalae** | Howard Hughes / Victoria Ward Ltd | **Solomon Cordwell Buenz** | The developer's own groundbreaking release, which also names Layton Construction (GC), Vita (landscape), Nicole Hollis (interiors). 330 homes |
| **Ālia** | **Kobayashi Group** | **WRNS Studio** | Both the official project team page and WRNS's own project page. 457 units, 39 storeys over a 5-storey podium, 1,075,981 sf, completion 2027 |
| **Launiu** | Howard Hughes | **Arquitectonica** — their first Hawaii project | 486 homes, 40 storeys, ground broken 21 Oct 2025 |

**No structural engineer is named on any of the three.** That is three separate primary
sources checked — the developer's project-team page, the architect's own page, and the
groundbreaking release — not one search that came back empty. Still: not publicly named
is not the same as an open seat.

Two further findings:

- **The Park Ward Village appears as under-construction in the May report and is absent
  from the July one.** The monthly cadence gives a completion signal for free.
- **Ālia is an early project under Kamehameha Schools' Kaia'ulu 'o Kaka'ako master plan**,
  so the same landowner has more towers behind it.

Unit-count discrepancy noted: trade press reports Ālia at 477 units; WRNS Studio's own
page says 457 with a full bedroom-by-bedroom breakdown. The architect's figure is used.

## North Carolina — half a region, and say which half

Raleigh/Wake resolves 2026 projects with owner and contractor from the permit feed
(Omni Hotel Raleigh $308.2M / Brasfield & Gorrie; Block 4 Midrise $69.6M / Tributary SPV
LLC / John Moriarty; 5000 Louisburg Rd $77.8M / Halle; West South Street $47.3M /
W.M. Jordan). **Charlotte does not** — its entitlement feed is frozen at 23 May 2022.

⛔ Neither feed carries an architect or an engineer, ever. Owner and contractor only.
The design-team layer still has to come from somewhere else.

## Wide pass, 27 August 2026 — Houston, Dallas, Miami, Charlotte, Nevada

### Confirmed from the firm's or developer's own page

| Claim | Source |
|---|---|
| **DeSimone Consulting Engineering = structural**, Southern Land Upper Kirby (Houston) | Developer's own groundbreaking release, verbatim: *"DeSimone Consulting Engineering for structural engineering"*. Also corrects the GC to **ANDRES Construction**. SCB architect + interiors, Kimley-Horn civil. 953,000 sf, 38-storey/331 units + 10-storey/107,000 sf office |
| **SCA Design = architect**, Hylo Park (North Las Vegas) | SCA's own project page |
| **GRAEF = civil/site + CEI, NOT structural**, Waldorf Astoria Miami | GRAEF's own page: *"site/civil engineering services"* |

### Refuted, corrected, or downgraded

- ⛔ **GRAEF is not the structural engineer on the Waldorf Astoria.** Two engineering firms appear in coverage of that tower and reading GRAEF as structural would have repeated the TSK/LVCC error exactly. Their own page settles it: civil.
- ⚠ **CHM Structural on the Waldorf is secondary-sourced only** — reported in trade coverage, not carried on CHM's own site. Marked unverified in the document.
- ⚠ **MKA on Queensbridge Collective is unverified.** One thin source called them a "consultant"; MKA's own site does not carry the project, and Riverside's own project page names **no firms at all**.
- ⚠ **Clark Construction's Queensbridge page returns 403.** BLOCKED, not absent.

### Where the primary source disagrees with the trade press — primary wins

| Item | Trade press | The owner's or engineer's own page |
|---|---|---|
| Ālia, Honolulu | 477 units | **457** (WRNS, with a bedroom-by-bedroom breakdown) |
| Waldorf Astoria, Miami | 1,049 ft, 387 residences | **1,046 ft, 360 residences** (GRAEF) |
| Queensbridge, Charlotte | 600,000 sf office | **356,000 sf** office, 755 residential units total (Riverside) |
| Hylo Park, Las Vegas | 700 units on 73 acres | **393 single-family homes** plus a lifestyle centre (SCA Design), developer not named |

Four for four. **Where a number matters, take it from the party that has to be right about it.**

### The Phoenix correction — a shipped document was wrong

The feed audit judged Phoenix on its open-data portal (23 rows, yearly totals) and wrote the city
off. Phoenix runs a separate service, `maps.phoenix.gov/pub/rest/services/Public/Planning_Permit`,
carrying **68,292 permits and 13,022 plan reviews**, row-level, **current to 27 August 2026**. It
includes the case type **FINAL SITE PLAN – MAJOR** — the submittal whose plan set carries the
consultant directory. That is the Arizona submitted-projects search Ian promised Dan.

⚠ **`PROFESS_NAME` is a trap.** 90.6% populated, and the name promises the design professional.
The values are the licensed **contractor**: *TO BE BID*, *OWNER*, Valley Fire Sprinkler, solar
installers, Sundt, Austin Commercial, TSMC. Never present it as an architect.

Why the first pass missed it: the host path is `/pub/rest/services`. Every probe of the conventional
`/arcgis/rest/services` returned 404 — **and a 404 against a guessed host proves nothing**, the same
rule the Thornton Tomasetti miss produced. It was found by searching the ArcGIS Online organisation
catalogue: derived, not guessed.

**Scottsdale is the mirror image.** `Active_CDS_Cases` is flawless — 82 cases, all 8 fields on all
82 rows — and frozen at 25 Feb 2026 because the city moved to the SPUR portal in January. SPUR's
search API exists (`GET` returns 405, so the method is right) but every anonymous `POST` returns 500:
it wants an OAuth token from Tyler's identity service. **A perfect feed can still be a closed one**,
and as cities migrate onto these platforms this blocker grows while the open ArcGIS ones shrink.

## The Arizona 50, interrogated rather than listed — 27 August 2026

The set had only ever been *read*. Counting it changes the conclusion, and two of the
document's own claims were wrong.

**Sector mix** — civic/education 17 (34%), **multifamily 12 (24%)**, industrial 10 (20%),
healthcare 6, hospitality 5. Three-quarters of an "Arizona projects" list is not MVE's market.

### ⭐ The finding: Arizona multifamily has no incumbent

Twelve multifamily projects went to **eleven different architects**. Gensler has two; nobody
has three. The top firm holds **17% of multifamily slots**. KTGY, Niles Bolton and ESG — the
national multifamily names — hold **one each**. There is no incumbent to displace because
there is no incumbent.

### ⛔ Two claims in our own document were wrong

- **"Butler Design Group is a design-build outfit"** — false. Six projects, **six different
  contractors** (Stevens-Leinweber, Ryan ×3, Willmeng, Layton). A conventional architecture
  practice whose *name* reads like a design-builder. Sector split: 4 industrial, 1 healthcare,
  1 civic — **zero multifamily**, so not an MVE competitor either.
- **"That is the competitive pressure in Arizona"** — misplaced. Design-build is **50% of
  industrial** (5 of 10) and **8% of multifamily** (1 of 12), 0% of civic, healthcare and
  hospitality. Every design-build project in the set is LGE, five of six industrial.

### ⭐ Four structurally closed developers — the don't-call list

| Developer | Evidence |
|---|---|
| **Creation Equity** (6 projects — most active in the set) | LGE Design Build on **all six**; no outside architect on any |
| **Ryan Companies US** (4) | Developer *and* GC on all four, brings Butler on three, Deutsch on one |
| **Statesman Group** | Developer and GC; **no architect named at all** |
| **StreetLights Residential** (Houston) | Architect, interiors and GC all in-house |

**Butler–Ryan is the only architect-to-contractor pairing that repeats** anywhere in the fifty
(3 projects). Everything else is a one-off — the same unconsolidated picture as the headline.

## Phoenix forward pipeline

**373 open** site-plan/rezoning cases; **280** submitted since 1 Jan 2025; **41 distinct
residential** by project name (42 rows — Sagewood Phase 4 spans two parcels).

⚠ **Every row carries status OPEN.** Counting by year would show 2024 below 2025 and it would
mean nothing — closed cases leave the layer, so older years shrink by survivorship. It is a
snapshot, not a time series, and the document says so.

## Verification-checker defect, fixed

Two "MISS" results today were the checker, not the document: a box label rendered **uppercase**
by CSS, and "Creation Equity" **wrapping across a line**. Both were reported as missing content
that was present. `checkpdf.py` now normalises whitespace and compares case-insensitively.
**A checker that cries wolf gets ignored, which is worse than no checker.**

## ⭐⭐ The Arizona finding TESTED in Raleigh — 27 August 2026

**"No incumbent" does NOT generalise. Arizona is the outlier, and that is what makes
the Arizona number worth stating.**

| Measure | Arizona multifamily | Raleigh multifamily |
|---|---|---|
| Projects with a confirmed architect | 12 | 11 |
| Distinct firms | **11** | **7** |
| Largest holder | Gensler, 2 | **JDAVIS, 4** |
| **Top firm share** | **17%** | **36%** |
| Firms per project | 0.92 | 0.64 |

Raleigh's leader holds more than double Arizona's, from a field half as wide. Cline
Design Associates second with 2; Perkins Eastman, Tightlines Design, Studio M, Foley
Design and Iwan Architecture & Engineering Consultants one each.

⭐ **JDAVIS was acquired by ISG on 14 May 2025** (Raleigh HQ, 89 staff). All four of its
recovered submittals were filed in 2024 — designed pre-deal. The Raleigh incumbent is
fifteen months into an ownership change.

### How the Raleigh architects were obtained — a reusable chain

Architects are **not** in the permit feed and **not** on the site-review application form
(that form has owner + applicant, and the applicant is normally the civil engineer). They
are on the **drawing set**, which Raleigh publishes:
`development-plan record → case number → .../COR15/<plan_number>.pdf → the firm on the drawings`

Two signals inside the PDF, precision first:
1. ⭐ **The sheet COPYRIGHT BLOCK** — *"(C) 2024 JDAVIS ARCHITECTS EXPRESSLY RESERVES ITS
   COMMON LAW COPYRIGHT…"*. Repeats on every sheet, names the firm unambiguously. This is
   the strong signal; the cover-sheet directory is vector layout, so `pdftotext` separates
   the label `ARCHITECT` from the firm name beside it.
2. The directory line, **only** when a street address follows the firm name.

Coverage: 92 multifamily plans submitted since Jan 2024 → **71 published as form only**
(no drawings) → **21 full drawing sets** → **12 name a building architect**, the other 9
being civil/landscape submittals that name none. So recovery is near-complete *on the sets
that can answer*, which is what makes the percentages safe.

### ⛔ Three extraction errors caught before they shipped

- **A loose `ARCHITECT` regex returned construction notes as firm names** — "PRIOR TO ANY
  CONSTRUCTION ACTIVITIES", "OF ANY DISCREPANCIES BETWEEN THE NOTES", "OWNER". About 4 of 23
  were real. On a drawing set the token appears in general notes far more than in the
  directory. **Never match a bare label on a plan set.**
- **Bass, Nixon & Kennedy** was captured as an architect. They are consulting engineers
  (civil/MEP/survey) and it came from the *Applicant* field. Removed — 12 → 11.
- **"IWAN IWAN ARCHITECTURE CONSULTANTS"** was a parse artifact; the firm is
  **Iwan Architecture and Engineering Consultants, LLC**. Kept, name corrected.
- Also correctly rejected: **"Southeastern Architectural Systems"** — a screen-system
  *manufacturer* named in a materials schedule, not a project architect.

### ⛔ Charlotte cannot be tested — three closed routes

Its "Committed Development Entitlement" dataset (richest schema found anywhere) **stops at
23 May 2022**. Its live rezoning layer holds 86 pending petitions with a petitioner but **no
project name and no design team**. And the city's own rezoning documents return **HTTP 403**.
The Raleigh chain would work the moment those documents are reachable. A Charlotte figure
today would be invented.

## ⭐⭐⭐ WHAT REPLACED THE DEAD FEEDS — 27 August 2026

The feeds did not die because the cities stopped building. **They migrated systems.** Every
"dead feed" verdict in the audit was really an un-followed migration, and the successors are
live. Three of them are the SAME PLATFORM.

| Jurisdiction | Old feed died | Replaced by | Reachable? |
|---|---|---|---|
| **Clark County** (the Strip) | Feb 2021 | **Accela Citizen Access** `aca-prod.accela.com/CLARKCO` | ✔ HTTP 200 |
| **Dallas** (MVE is hiring here) | Mar 2020 | **DallasNow** — Accela, live 5 May 2025, replaced POSSE + ProjectDox | ✔ HTTP 200 |
| **Charlotte** | May 2022 | **Accela Citizen Access** `aca-prod.accela.com/CHARLOTTE` | ✔ HTTP 200 |
| Honolulu | Jul 2025 | **HNL Build** — Clariti on Salesforce, by Speridian, live 4 Aug 2025 | untested |
| Scottsdale | Jan 2026 | Tyler EnerGov **SPUR** | OAuth-gated |

⭐⭐ **THREE OF SIX ARE ACCELA. One integration opens Clark County, Dallas and Charlotte.**
- Clark County = **the Las Vegas Strip**: Bally's, the Riviera site, Durango, Silverado.
- Dallas = the market MVE advertised two roles in on 21 Aug with no Texas office.
- Charlotte = currently our weakest region.

⭐ **The Accela API is a DOOR, not a wall.** `apis.accela.com/v4/agencies` returns
`{"status":400,"message":"App ID or access token is required."}` — that is a **registration**,
not a block. Accela runs a self-service developer program. **Next action is human: register an
App ID.**

⭐ **And a route around the Charlotte 403.** `charlottenc.gov` returns 403 to us, but
`aca-prod.accela.com/CHARLOTTE/Default.aspx` returns **200**. The city website blocks us; the
city's permitting platform does not. ⚠ ACA is ASP.NET WebForms (`__VIEWSTATE`), so it needs a
session + postback handler, not a plain GET.

### Corrections to the feed audit produced by this pass

- ⛔ **"Clark County — no bulk feed" was WRONG.** `maps.clarkcountynv.gov/arcgis/rest/services/
  CompPlanning/Accela_DocRef` holds **876,120 application records** — 12,895 in 2019, 14,337 in
  2020, 1,302 in 2021, **and zero from 2022 onward**. Not "no feed": a **dead** feed, stopping
  Feb 2021, exactly when the county moved to ACA.
- ⭐ **Miami is LIVE and was never tested.** City of Miami `Building_Permits_Since_2014`
  (`services1.arcgis.com/CvuPhqcTQpZPT9qY`) — **230,545 permits, current to 25 Aug 2026**.
  ⚠ `CompanyName` is the **contractor** (Power Design, electrical subs), not an architect.
- ⛔ **Dallas open data still has no successor dataset.** `e7gq-4sah` advertises an update of
  5 Jun 2026 and is the *same* 126,840 rows ending March 2020 — a metadata touch, not data.
  **The live Dallas record exists only inside Accela.**
- ⛔ **Phoenix does not publish reachable plan sets.** Its PDD project search is a JS app with
  no forms, no document links. So Arizona's design teams still rest on a trade listicle while
  Raleigh's come from primary records — the reverse of what you would expect.
- Houston's `Planning_and_Development` MapServer is zoning overlays only — no projects.

### The honest capability map, by region Dan named

| Region | Project pipeline | Design teams | Grade |
|---|---|---|---|
| **Raleigh** | live | ⭐⭐ **primary record** (plan-set copyright block) | **A** |
| Arizona | ⭐ Phoenix live, 373 open site plans | ⚠ trade listicle only, annual | B |
| Hawaii | ⭐ HCDA monthly PDF | ⚠ hand-researched, 3 towers | C+ |
| Miami | ⭐ live, 230k permits | ⚠ trade press only | C |
| Nevada | ✔ LV + Henderson live; ⛔ Strip dead since 2021 | ⛔ none (NVBEX paywalled) | D |
| Houston | ⛔ aggregate only | ⛔ trade press only | D |
| Charlotte | ⛔ dead 2022 | ⛔ 403 on city site | F → **D via ACA** |

**Only ONE of seven is at Dodge standard today.** The gap is not analysis, it is access.

## ⭐⭐⭐ ALL THREE "DEAD" JURISDICTIONS ARE OPEN — and no registration was needed

Asked to register an Accela App ID. **Registration would not have worked, and it was not
required.**

⛔ **Why registration fails.** An App ID gets a *sandbox* token. Production access to a given
agency's records needs that agency to enable the developer in its own Admin portal — Accela's
own docs: *"agencies must enable developers through the Admin portal for them to have access."*
So Clark County, Dallas and Charlotte would each have had to authorise KOR separately. My
"it's a registration, not a block" line the turn before was too optimistic; this corrects it.

⭐ **What works instead — ACA's public search, no account at all.** It is the surface citizens
use without logging in, it takes a plain GET, and it renders results **server-side** into an
ASP.NET GridView that is present in the returned HTML:

    https://aca-prod.accela.com/<AGENCY>/Cap/GlobalSearchResults.aspx?QueryText=<term>

Columns: **Date · Record Number · Record Type · Module · Short Notes · Project Name · Status**

| Agency | Verdict | Newest record |
|---|---|---|
| **CLARKCO** — the Las Vegas Strip | ⭐ LIVE | **27 Aug 2026 (same day)** |
| **dallastx** — DallasNow | ⭐ LIVE | **27 Aug 2026 (same day)** |
| **CHARLOTTE** | ⭐ LIVE | **20 Aug 2026** |

⭐ **It resolves the projects that matter**, not just generic terms. `athletics ballpark` →
**79 records**, including `BD26-34988` and `BD26-33952` *Commercial Grading — ATHLETICS LAS
VEGAS BALLPARK* (Aug 2026), `BD26-30668` *Commercial Building New — PHASED PROJECT* (Jul),
`BD26-26432` *Commercial Mechanical* (Jun). Permit-by-permit progress on the largest project
on the Strip — the Thornton Tomasetti job. `tropicana` 96 · `riviera` 94 · `durango` 94 ·
`silverado` and `ballys` 100+.

⚠ **Known limits, so nobody overstates it.** The GET returns **page 1 only (10 rows)**; full
sets need pagination or the page's own *"Download results"*, both `__doPostBack` on
`gdvPermitList` — an ASP.NET postback needing session + `__VIEWSTATE`. And these are
**permits, not design teams**: Record Type and Project Name, no architect.

⭐ **Playwright is already in this repo** — .NET, `Kor.Opportunities.Data/Ingestion/Scraping/`
(BcBid, Alberta Purchasing, APC scrapers) plus the headed `Kor.Opportunities.Capture` session
harness. That is the browser-rendering capability named three times today as the top blocker.
It would unlock ACA pagination/export, Phoenix's JS project search, Scottsdale SPUR and Austin.
⚠ Adding a scraper to that production project is a real code change and has not been made.
