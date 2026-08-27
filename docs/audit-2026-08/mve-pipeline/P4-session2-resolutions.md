# P4 — Session 2 resolutions, sources and proven workarounds

**Compiled 2026-08-26.** This document exists so nothing below is researched twice. It resolves five
of the seven open questions the first session left, corrects two claims, and records the three
techniques that did the work.

**Prior art read before starting** (repo rule 2): `11-MVE-PIPELINE-BRIEF.md`, `P1-oc-la.md`,
`P2-san-diego.md`, `P3-utah-bay-developers.md`, and `START-HERE-NEXT-SESSION.md`. Those established
the project list, the stage classification and the seven open questions. This document adds only
what changed.

**The absence rule still governs.** Where anything below says *not publicly named*, it means no
public record was found. It never means the seat is open.

---

## 1. The three techniques that did the work

Recorded first, because they are worth more than any single answer here.

### 1.1 City entitlement plan sets carry a full consultant directory

This is the technique that answered the two biggest questions. An entitlement submittal is a plan
set, and a plan set has a project directory sheet listing every consultant by discipline, with
address and phone. It is filed with the city and posted with the agenda packet.

- **Anaheim** — `https://www.anaheim.net/DocumentCenter/View/{id}`. The OCVIBE residential packet
  runs 67575 (staff report) through 67585 (the 130-page plan set, 138 MB). The directory is on sheet
  G-110, and repeated in condensed form on A-100.5.
- **Irvine** — `https://irvine.granicus.com/MetaViewer.php?meta_id={id}`. The Discovery Park packet
  is 164545 (staff report), 164547 (vicinity map), 164549 (information sheet), 164551 (the 36-page
  plan set, 63 MB).

**Where the sheets are raster-only**, as Irvine's are, `pdftotext` returns nothing. Render the
bottom-right title block instead and read it:

    pdftoppm -png -r 150 -f <page> -l <page> -x 1950 -y 1400 -W 600 -H 250 <pdf> <stem>

At 150 dpi a 17×11 sheet renders about 2550×1650, so those offsets land on the consultant stamp.
`tools/titleblocks.py` stacks one crop per page into a single contact sheet, which makes a
36-sheet set readable in one look.

### 1.2 San Diego's permit record is an open dataset, not the Accela portal

The first session's next-step was *"open aca.accela.com/SANDIEGO from a real browser."* **That is not
necessary.** The City publishes the whole approvals table as CSV, **updated daily**:

- Landing page: `https://data.sandiego.gov/datasets/development-permits/`
- Active approvals: `https://seshat.datasd.org/development_permits/approvals_active_datasd.csv`
  (272 MB, ~834,000 rows) — also `approvals_issued_*`, `approvals_created_*`, `approvals_closed_*`,
  per year and cumulative.

Columns include `PROJECT_STATUS`, `APPROVAL_TYPE`, `APPROVAL_STATUS`, `APPROVAL_ISSUE_DATE`,
`APPROVAL_VALUATION`, `APPROVAL_STORIES`, `APPROVAL_FLOOR_AREA`, the full affordable-unit mix, and
`APPROVAL_PERMIT_HOLDER`. `GIS_ADDRESS` is formatted `4002 Park Bl, San Diego, CA 92103` — grep on
the street number and name, not the full string. It covers 2003 to today, so the *"OpenDSD only goes
to 2018"* limitation does not apply. `tools/sdq.py` queries the local copy.

**It does not name design consultants.** `APPROVAL_PERMIT_HOLDER` is the applicant or expediter. It
answers *what stage* definitively and *who designed it* not at all.

### 1.3 A browser User-Agent opens more than planning.lacity.gov

Retested this session. `curl -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36
(KHTML, like Gecko) Chrome/128.0 Safari/537.36"`:

| Source | Result |
|---|---|
| **buildsd.org** | **200.** Previously recorded as 403 |
| **aiaoc.org** | **200** at the root. `/design-awards/` and `/awards` are 404 — the 2026 gala is September 2026 and no winner list is posted |
| **hillcrestbia.org** | **200.** Carries a development map, updated 2026-04-22 |
| **mve-architects.com/connect/** and `/about/blog/` | 200 |
| **riverwalksd.com** | 200, and `wp-sitemap-posts-post-1.xml` enumerates every news post |
| **culvercity.org** | **Still 403** |
| **santaclaraca.gov** | **Still 403** |

**BuildSD needs one extra step.** Project detail is client-rendered — `__NEXT_DATA__` is empty and
there is no reachable API — but each project page's server-rendered `<head>` carries `og:title` and
`og:description` with a one-line status. The 147 project slugs are server-rendered as links on
`/projects`. `tools/buildsd_meta.py` harvests all 147 in about a minute.

---

## 2. Questions resolved

### 2.1 Discovery Park is not MVE's — it is BDE Architecture's ✅

**This was the brief's #1 question and the answer is negative.**

City of Irvine Planning Commission packet, meeting 2025-05-01, Case Nos. 00935798-PMP (master plan),
00938716-PTT (VTTM 19260), 00948850-PDA (development agreement). Plan set dated **10 April 2025**.

| Sheet | Discipline | Firm |
|---|---|---|
| AP0.02, AP1.00, AP1.04, AP1.08, AP1.12, AP2.02 | **Architecture** | **BDE ARCHITECTURE** |
| C-1 | Civil | **Urban Resource**, 2923 Saturn St Unit H, Brea CA 92821 |
| L-1, L-4, L-7 | Landscape | **burton** Landscape Architecture Studio, 307 S Cedros, Solana Beach CA 92075 |

**No structural sheets in the set** — normal for an entitlement submittal.

**BDE Architecture**: in practice since 1988, multifamily specialist, 950 Howard Street, San
Francisco, **with a San Diego office**. So The Irvine Company gave its largest new entitlement to a
firm that is also now a competitor in the market MVE just entered. (REPORTED — firm profile pages.)

The staff report (`meta_id=164545`) names no architect anywhere; it is the plan set that answers it.
Report prepared by Erica S. Hong, Senior Planner. Applicant throughout is Irvine Company.

### 2.2 OCVIBE Residential Phase I — thirteen consultants named, structural not among them ✅

City of Anaheim, **Final Site Plan DEV2025-00019**, Attachment 10, *"OCVIBE Entitlements | October
2025"*, sheet **G-110 PROJECT DIRECTORY**. Applicant Anaheim Real Estate Partners, LLC, 2101 East
Coast Highway Suite 230, Corona del Mar; contact Brian Myers. Site 1500–1600 S. Douglass Road.

| Discipline | Firm |
|---|---|
| Agent | Christine Saunders & Associates, LLC |
| Master plan architect | **smith-clementi**, Venice CA |
| **Architect** | **MVE + Partners**, 1900 Main Street Suite 800, Irvine |
| Landscape architect | Fletcher Studio, San Francisco |
| Civil engineer | Fuscoe Engineering, Irvine |
| Traffic | Pirzadeh & Associates, Irvine |
| Master planning & solid waste | Hunsaker & Associates Irvine, Inc. |
| Fire master plan | Holmes, San Francisco |
| Signage | 3d-identity, Denver |
| Dry utilities | Moran Utility Services, Inc., Irvine |
| **Geotechnical** | **NMG Geotechnical Inc.**, Irvine |
| Irrigation | Water Concern, Rancho Santa Margarita |

**Structural is absent, and John A. Martin & Associates does not appear.** JAMA's credit remains a
district-level, REPORTED-tier claim.

Two things this establishes and one it does not:
- **Establishes** that MVE is architect of the 530-unit residential buildings from a **city record**,
  not from MVE's own site. Previously this rested on MVE's portfolio page.
- **Establishes** that geotechnical was appointed by October 2025. Geotechnical normally lands with
  or just before structural.
- **Does not establish that the seat is open.** A final site plan does not always require the
  structural consultant to be listed. It is the strongest signal in the brief, not a fact about a
  contract.

### 2.3 4002 Park Blvd has not started vertical — and is not stalled ✅

City of San Diego DSD approvals, dataset updated **2026-08-25**, all rows for `4002 Park Bl`
(APN 4454920700, 32.750724 / −117.146706).

| Record | What it is | Status |
|---|---|---|
| **PRJ-1113160** / PMT-3279496 | *"Request for deviation to ACI318-19. New concrete apartment building is proposed."* Alternate Methods and Materials | **Issued 2024-05-06** |
| **PRJ-1121960** | Grading and Public Improvements — storm water, sidewalk, curb, ADA ramp, utility connections, **temporary shoring**, traffic control | Issuance Checklist Requested; agreements **issued 2026-06-30 and 2026-07-09** |
| **PRJ-1127128** / PMT-3325762 | Building Permit — *"21-Level Mixed Use High Rise"*, 5 levels above-grade parking, residential L6–L20, roof amenity | **Opened**, project status **Recheck Required**. Valuation **\$47,132,178.72**, 21 storeys, 212,821 sf |
| PRJ-1127128 | Mechanical, Electrical, Plumbing, Fire Suppression, Fire Underground | All **Opened**, none issued |
| **PRJ-1134449** | ROW — removal of two gas services, Lincoln Ave and Centre St | **Issued 2025-06-10** |
| **PRJ-1137712** | ROW — temporary power system and **undergrounding of power lines** for the new high rise | **Issued 2025-10-10**, status **Inspecting** |
| **PRJ-1148991** | Pool and spa, 20th floor amenity | Created 2025-12-15, Recheck Required |
| **PRJ-1153705** | Sign permit, *"new mixed-used building"* | Created 2026-03-13 |

Unit mix on the building permit: 6 very low + 4 low + 6 moderate + 190 above moderate = **206 units**,
of which **173 are density-bonus**. That matches MVE's live Pali page exactly and closes the
Pali / 4002 Park identity question for good.

**Two conclusions.** The building permit is in plan check, not issued, so there is no vertical
construction. But utilities are being cut and undergrounded, a ROW permit has been in *inspection*
since October 2025, and grading agreements issued six to eight weeks ago — this is a project moving
toward a start, not a dormant one.

**And the structural seat is not open.** An ACI 318-19 code-deviation request is a structural
submittal; one was issued in **May 2024**. An engineer has been engaged for over two years and the
structural drawings are in plan check now. **Remove 4002 Park from any open-seat list.**

### 2.4 Kennedy Wilson / Toll Brothers — the filings restate the number, and the list stays private ✅

| Source | Date | What it says |
|---|---|---|
| Announcement (Toll Brothers IR, Kennedy Wilson) | 2025-09-18 | *"a pipeline of 29 sites in various stages of development which, if completed, would total approximately \$3.6 billion of invested capital"* |
| **KW 10-K, FY2025** (filed 2026-02-27) | 2025-12-31 | *"the acquisition of **24 acquisition purchase agreements for certain land positions development pipeline with potential capitalization of \$2.9 billion**"* |
| **KW 10-Q, Q1 2026** (filed 2026-05-06) | 2026-03-31 | Same 24 / \$2.9B wording. Final tranche closed in Q1 2026 — four completed assets and one development asset for \$68M, KW at 15% weighted-average interest |

Also from the filings: 18 apartment and student-housing properties acquired (KW weighted-average
interest **11%**, \$1.9B AUM), asset management of 21 more that stay with Toll (\$3.4B AUM), and the
Toll apartment-platform leadership joined KW in **December 2025**.

**No site list anywhere.** KW's 10-K development table names exactly two projects — **Cloudveil**
(288 units, Mountain West, under construction, 2026) and **Oxbow Phase II** (132 units, 2027).
Nothing in California. Treat this as answered: the list is not public and there is no filing
obligation that would make it public.

### 2.5 Five offices — Irvine, Los Angeles, San Diego, San Francisco, Guadalajara ✅

Read directly from `mve-architects.com/connect/`, 2026-08-26. **Both earlier readings were partly
wrong.** It is five, not four — and the fifth is Guadalajara, Mexico, not Denver.

| Office | Address | Phone |
|---|---|---|
| Irvine | 1900 Main Street, Suite 800, Irvine, CA 92614 | 949.809.3388 |
| Los Angeles | 888 South Figueroa Street, Suite 2170, Los Angeles, CA 90017 | 213.805.7600 |
| San Diego | 655 West Broadway, Suite 1170, San Diego, CA 92101 | 619.610.2027 |
| San Francisco | 150 Post Street, Suite 750, San Francisco, CA 94108 | **949.809.3388** |
| **Guadalajara** | Av. Pablo Neruda No. 2656 Int 101, Col. Providencia, C.P. 44630, GDL, JAL | +1 949.809.3361 |

Four California studios and one in Mexico — **no US office outside California**. Denver, opened
September 2022, is not listed; **no closure evidence exists and none should be asserted.** Note that
San Francisco publishes the Irvine switchboard number, which is a small tell about its size.

This also reconciles LABJ's "1/5" field: five is right, Guadalajara is the fifth.

---

## 3. AvalonBay — the lead was pulled, and the headline is not the pipeline

The first session flagged AvalonBay's quarterly supplemental as the highest-value untouched lead. It
has been pulled. Here is exactly what it contains.

**Source:** 8-K filed 2026-07-23 (accession 0000915912-26-000018), Exhibit 99.2, **Attachment 8 —
Development Communities**; and 10-Q filed 2026-07-30 (0000915912-26-000020). CIK 0000915912.

### 3.1 The merger is the story

Merger Agreement with **Equity Residential** signed **20 May 2026**. AvalonBay merges into Canopy
Merger Sub, a wholly owned subsidiary of Equity Residential. Each AVB share converts to **2.793** EQR
common shares. Former AvalonBay stockholders end up with **≈51%** of the combined company, legacy
Equity Residential shareholders **≈49%**. Accounted for as a **reverse acquisition** — Equity
Residential is the legal acquirer, AvalonBay the accounting acquirer. Both boards approved
unanimously. **Expected to complete in the second half of 2026.** The combined company **takes a new
name** and keeps **dual headquarters in Chicago and Arlington, Virginia**. Break fees: Equity
Residential ≈\$1.005B, AvalonBay ≈\$1.070B. AvalonBay expensed **\$12,367,000** of merger costs in
Q2 2026 alone.

AvalonBay is a confirmed MVE client (Avalon West Hollywood, Movietown Square). What a merger of that
size does to a development pipeline is a legitimate, public, uncontroversial thing to ask about.

### 3.2 What the pipeline disclosure actually is

**Every community under construction is named** — 27 of them, 9,064 homes, \$3,526M total capital
cost. The Californian ones, all already started and therefore all locked:

| Community | Location | Homes | Capital cost | Start | Complete |
|---|---|---|---|---|---|
| Avalon Pleasanton | Pleasanton, CA | 362 | \$218M | Q2 2024 | Q3 2027 |
| Kanso Hillcrest | **San Diego, CA** | 182 | \$85M | Q4 2024 | Q2 2027 |
| **Avalon Mission Valley** | **San Diego, CA** | **621** | **\$302M** | Q3 2025 | Q1 2029 |
| Avalon San Ramon | San Ramon, CA | 456 | \$250M | Q4 2025 | Q4 2028 |

⚠ **Avalon Mission Valley is at SDSU Mission Valley, not Riverwalk.** BuildSD tracks it as
`sdsu-mission-valley-avalon`. Do not connect it to Hines' Riverwalk master plan.
Avalon Mission Valley also carries 31,000 sf of commercial.

**Nothing that is not yet under construction is named, in any filing.** The forward pipeline is
disclosed as a count only: **31 Development Rights, 9,997 apartment homes** (10-Q, 2026-06-30); it
was 32 / 9,032 at 2025-12-31 in the 10-K. AvalonBay defines Development Rights as options, long-term
conditional purchase contracts, ground leases, owned land, or public-private designations. **There is
no property-level list to find.** That closes this lead — the count is the disclosure.

### 3.3 One AvalonBay forward project surfaced by name, from outside the filings

**AVA Pacific Beach** — an infill addition of **138 apartments** (7 affordable) plus two parking
structures, a surface lot, open space and a linear park along Jewell Street, on AvalonBay's own
564-unit 1969 garden complex at 3883 Ingraham Street. Approved by the Pacific Beach Planning Group
in May 2025, San Diego Planning Commission October 2025, and **San Diego City Council 2026-02-23**.
**Architect not publicly named.** Not in the under-construction table, so it is presumably one of the
31 Development Rights. (REPORTED — Times of San Diego 2026-02-23; BuildSD `ava-pacific-beach`.)

---

## 4. Questions that stay open, and why that is the right answer

### 4.1 Structural on MVE's San Diego work — a third pass, the same absence

No structural engineer is publicly named on **any** MVE San Diego project. This is now three
independent searches across two sessions returning the same result — Riverwalk, The Becker and
4002 Park. On The Becker the developer (Wakeland), architect (MVE), general contractor (Level 10),
programme, cost and completion date are all published; the engineer is not.

The San Diego permit dataset does **not** close this: `APPROVAL_PERMIT_HOLDER` carries applicants and
expediters, not design consultants.

**This is the question to ask in the room, not a research failure.** Note the one asymmetry worth
carrying: at 4002 Park the structure has been engineered since 2024 (§2.3), so the absence of a
public name there says nothing about whether a seat exists.

### 4.2 Riverwalk Phase 2 sequencing — confirmed unpublished

`riverwalksd.com/wp-sitemap-posts-post-1.xml` enumerates every post on the site. The newest is
**`construction-update-q2-2026`**. There is no Q3 update; `/construction-update-q3-2026/` returns
404. Seventeen of twenty parcels remain undesigned and unsequenced in public. The sitemap is the
clean way to prove nothing newer exists rather than inferring it from a failed search.

### 4.3 Santa Clara Park — the trigger is lease expiry

Council-approved 2025-03-25, 1,792 units on 25.74 acres, replacing a dozen office buildings. **The
Irvine Company is prepared to proceed once the leases on those commercial buildings expire; there is
no official start date.** (REPORTED.) That is a specific, askable constraint rather than an unknown.
`santaclaraca.gov` still refuses a browser request, so the city's own project page was not read.

---

## 5. Corrections to carry

1. **Discovery Park belongs to BDE Architecture.** The brief previously led with *"Ask this one
   first."* It should not be asked as an MVE project at all. §2.1.
2. **4002 Park Blvd is not an open structural seat.** §2.3.
3. **Kennedy Wilson's pipeline is 24 land positions / ≈\$2.9B per the filings**, not 29 sites /
   \$3.6B. Both numbers are real; the second is the September 2025 announcement. §2.4.
4. **MVE has five offices and the fifth is Guadalajara.** §2.5.
5. **Park Summit is a different building.** A 21-storey tower at 555 Upas Street on Balboa Park's
   northwest corner, opened July 2026 — **JWDA** architect, **Floit Properties** developer, Suffolk
   Construction, Greystar management, 265 units. Genuinely easy to confuse with 4002 Park Blvd: same
   height, same corner of the city, same period. It is not MVE's. (REPORTED.)
6. **`B-portfolio.md` has been corrected** — the separate "4002 Park" row and open question 5 are
   struck through and closed.

---

## 6. A rendering defect found and fixed, worth not repeating

The 25 August shipped PDF carried literal `**` markers in §1: *"...across Irvine and LA. \*\*Orange
County billings rose 25%..."*

**Cause.** `build_doc.py` calls `pandoc -f gfm`, and this pandoc build treats `$…$` as TeX math. Two
dollar amounts on the same line — `\$28.1M` and `\$24.5M` — opened and closed a math span, and every
`**` between them was emitted verbatim instead of being parsed as emphasis.

**A second, worse fault in the same builder, found the same day.** Its running footer is
`position:fixed; bottom:5mm`, which places it *inside* the text column rather than in the page
margin. On any page whose text fills the column, the footer **overprints body text** — four of five
content pages of the 26 August draft, and the already-shipped
`KOR-MVE-Pipeline-Brief-2026-08-25-web.pdf`, where it strikes through the line about Hines' 276
Riverwalk townhomes. Negative offsets do not fix it; Chrome clips fixed elements to the page area,
so the footer simply disappears.

**Both faults are now moot for this brief**, which was rebuilt on the canonical
`tools/BdDocTemplate/` system — no pandoc, no fixed footer. ⚠ **The rest of the `audit-2026-08`
series is still on `build_doc.py` and still carries the overprinting defect**, including the MVE
company profile and demo dossier PDFs.

**Fix.** Escape every `$` as `\$` in the markdown source. Six occurrences in this brief.

**The general lesson is repo rule 5.** The defect was invisible in the markdown, invisible in the
HTML source at a glance, and only showed up in `pdftotext -enc UTF-8` of the shipped artifact. Any
KOR document rendered through `build_doc.py` that quotes two dollar figures in one paragraph is
exposed to this. **Grep the shipped PDF for `**` as a standing check.**
