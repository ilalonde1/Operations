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
