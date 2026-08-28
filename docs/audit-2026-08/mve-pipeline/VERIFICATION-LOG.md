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
