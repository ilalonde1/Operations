# START HERE — MVE design-team work, state at 28 August 2026

Supersedes the earlier version of this file. Read this before touching anything.

---

## 1. The deliverable

**`docs/KOR-MVE-Design-Team-Dossier-2026-08-28-web.pdf`** — 7pp, built on `tools/BdDocTemplate`,
assembled from `docs/audit-2026-08/mve-designteam-{head,print,body}.html` (build command is in the
head fragment). **This is the document that goes to Dan Gura at MVE.**

⛔ **`docs/KOR-MVE-Regional-Intel-2026-08-27-web.pdf` is INTERNAL and must never be sent** — it
frames the same research around where KOR wins a structural seat.

### Two rules the client document must keep obeying
1. **It answers MVE's commercial question, not KOR's.** MVE is an *architect*. A "structural
   engineer" column was cut for exactly this reason. See
   `feedback_client_doc_takes_the_client_pov` in memory.
2. **It states findings, never our method.** A whole methodology section and the endpoint list were
   removed. Sweep the *rendered PDF* for leaks (`drawing set`, `plan set`, `copyright block`,
   endpoint names) — two survived the section deletion and were only caught that way. See
   `feedback_never_hand_the_client_our_method`.

Verify with `scratchpad/checkpdf.py <pdf> --must ... --gone ... --forbid Glotman "open seat"`.
It normalises whitespace and case; **table cells can still split a phrase across columns in
`pdftotext`, so a MISS on a table string may be an extraction artefact — look at the page.**

---

## 2. What the dossier says (all verified)

- **Arizona multifamily has no incumbent architect** — 12 projects, 11 firms, top firm 17%.
  ⭐ **Independently corroborated** against Phoenix PUD narratives: 16 projects, ~14 firms, top
  firm 2. Two unrelated sources, same dispersion.
- **Raleigh is the opposite** — 11 projects, 7 firms, **JDAVIS 36%**, and JDAVIS was **acquired by
  ISG on 14 May 2025**.
- **Design-build is 50% of Arizona industrial, 8% of multifamily.** ⛔ Butler Design Group is *not*
  a design-build firm and has **zero** multifamily — do not repeat that error.
- **Four closed developers**: Creation Equity (LGE on all six), Ryan Companies, Statesman,
  StreetLights.
- **Market tempo** on permits ≥$10M: Las Vegas flat five years (3–7/yr); Raleigh 3 → 15 → ~11.
  ⚠ Counts and values disagree; the table uses only whole-project permits.
- **41 open Phoenix residential submittals** — the thing Dan actually asked for.

---

## 3. ⭐ THE SOURCE CHAINS — the real asset

Full detail in memory: `reference_entitlement_and_permit_research` §9b–§9l. Short form:

| Region | Design-team source | Status |
|---|---|---|
| **Phoenix / AZ** | **PUD rezoning narratives** — PROJECT TEAM block with emails + phones. `tools/phx_pud_teams.py`, 53% recovery | ✅ free |
| **Raleigh** | Plan sets → **copyright block on every sheet** | ✅ free |
| **Nevada / the Strip** | Accela record № → `docimgsrch.clarkcountynv.gov` → **PLAN sheets → title block** | ✅ free, proven |
| **Charlotte** | Design team not filed at the stage the city publishes | ⛔ not yet exists |
| **Miami** | **UDRB agenda packets** (IQM2 board 1096) — letter of intent + signed drawing set. `tools/udrb_teams.py` | ✅ free, proven |
| **Houston** | **TDLR TABS** — statewide registry, every commercial project >$50k names the design firm. `tools/tabs_projects.py` | ✅ free, **a census** |

⭐ **The general law**: the design team is named on the document written **by** the design team —
the drawing set, the PUD narrative, the contractor's project page. **No permit feed anywhere names
an architect**; 14 tested, and every field that looks like it does holds the *contractor*.

### Traps that cost hours — do not re-learn
- ⛔ **A 403/500 to `urllib` is usually a bot filter or a bad payload, not a wall.** Three
  "blockers" were false: Clark Construction, charlottenc.gov, Scottsdale SPUR. **Retry in
  Playwright before recording a negative.**
- ⛔ **Blazor/Telerik portals**: `fill()` does not bind — searches run EMPTY and look like no data.
  Use `click()` → `type(delay=90)` → `Tab`. Cost four failed Clark County attempts.
- ⛔ **Never flatten a table to pipes and collapse runs** — it deletes empty cells and shifts every
  later column (`project_name` read 0/93 and was merely misaligned).
- ⛔ **Case-sensitive label matching** returned 0 of 18 PUD narratives and looked like a source with
  no architects. Always `re.IGNORECASE`.
- ⚠ **Sanity-check every recovered firm against what it actually does.** Bass Nixon & Kennedy
  (engineers), Kimley-Horn (civil) and a golf-course architect all surfaced as "architects".
- ⚠ **Permit name ≠ marketing name.** Collegeview = *Signature at Varsity*; "veteran housing" =
  *Patriot Apartments*.

---

## 4. ✅ ALL SIX REGIONS ARE COVERED (28 Aug)

Miami and Houston were closed on 28 August. Full detail in `VERIFICATION-LOG.md` §"Session 3" and
in memory `reference_entitlement_and_permit_research` §9m (Miami) and §9n (Houston).

**Houston — TDLR TABS is the strongest source in the whole report.** Texas requires every
commercial project over $50,000 to be registered for accessibility review and the registration
names the design firm. It is a **census, not a sample** — the denominator is the law's, not an
editor's — and it is statewide, so **Dallas, Austin and San Antonio come free**.
Harris County 2024–2026: 11,790 registrations, 4,087 new construction, 358 at $10M+, of which
**314 (88%) name a design firm across 157 distinct firms**.

⭐ **The Arizona pattern repeated on unrelated data: industrial concentrates (top 3 = 52%),
multifamily disperses (20 firms over 28 projects).** Two markets, two independent source types.

**Miami — UDRB agenda packets.** 28 published packets, Jan 2024 → Jul 2026, 6.69 GB. Each is the
applicant's own submittal: letter of intent plus the signed drawing set, with a team directory
carrying phone numbers.

### ⬜ What is left
1. **Finish the two harvests** (both were running at the end of the session; both resume — the
   scripts skip what is already done). Houston: `tabs_projects.py detail`, ~4,087 records.
   Miami: 28 packets at roughly 1.5 MB/s.
2. **Write the Miami and Houston sections into the client dossier** — they are not in the PDF yet.
3. Sanity-check every firm before it is quoted. Non-architects legitimately appear in both sources.

---

## 5. Spending — current answer

**⭐ All six regions are now covered free — buy nothing.** BEX
(`docs/audit-2026-08/BEX-quote-request.md`, AZBEX $575/yr at KOR's tier) and BLDUP
(`BLDUP-trial-walkthrough.md`, free account created) were the fallback for Miami and Houston, and
both regions were solved on public records instead. Houston in particular is a *census*, which no
paid product can match. ⚠ BLDUP free shows address/sector only — design team is Pro.
⚠ DATABEX is **Arizona-only**; NVBEX is a magazine, not a database.
⛔ Do **not** buy anything for Arizona, Raleigh or Nevada — we already beat it, free.

⚠ **Clark County's search button carries a non-commercial-use declaration.** Ian's decision on
28 Aug was to proceed. See `reference_public_record_commercial_use_limits` — the line held is
*extract the fact, never redistribute the file*.

---

## 6. Tools built this session

- `tools/aca_permit_probe.py` — Accela public search, paginated, reconciles against the portal's
  own counter. Clark County / Dallas / Charlotte, no account.
- `tools/phx_pud_teams.py` — Phoenix PUD narratives → design team.
- `scratchpad/checkpdf.py` — verify a shipped PDF's text (whitespace/case normalised).
- Playwright (Python) is installed and drives the **installed Edge** via `channel="msedge"` — no
  browser download. ⚠ Keep separate from the .NET Playwright in `Kor.Opportunities.Data`.
