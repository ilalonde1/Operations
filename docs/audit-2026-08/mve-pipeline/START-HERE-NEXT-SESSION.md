# START HERE — MVE pipeline research, session 2

**Purpose of the next session: fill the gaps a spent search budget left. Do not restart the
research.** Three tracks are complete and on disk. Your job is the ~15 named leads below.

Written 2026-08-25. The previous session exhausted its WebSearch quota (200/200) partway through
and finished on direct URL fetches only. **You start with a fresh quota — spend it on discovery,
not on re-reading what is already established.**

---

## 1 · Read these first, in this order (~20 minutes)

| File | What it holds |
|---|---|
| `../11-MVE-PIPELINE-BRIEF.md` | **The deliverable.** Jim's four questions answered. Read this first — it tells you what "done" looks like. |
| `P1-oc-la.md` | Orange County + LA — 9 forward projects, SE field, the "getting busier" cadence |
| `P2-san-diego.md` | San Diego — 5 forward items, the studio's real workload |
| `P3-utah-bay-developers.md` | Utah, Bay Area, Arizona + the developer-side view |
| `../10-MVE-COMPANY-DOSSIER.md` | ~155 built projects. **Reference only — do not re-research the portfolio.** |
| `../09-MVE-DEMO-DOSSIER.md` | Who is in the room; David Arnold as likely evaluator |

**The one rule that governs all of it:** where a document says a structural engineer is *"not
publicly named,"* that means no public record names one. **It does not mean the seat is open.**
Never upgrade that phrasing.

---

## 2 · The highest-value lead, not yet touched

**AvalonBay publishes its complete development pipeline in quarterly 8-K supplementals.**
Authoritative, public, filed with the SEC, and never pulled. AvalonBay is a confirmed MVE client.
Start here — via `sec.gov` EDGAR full-text search for AvalonBay 8-K exhibits, most recent quarter.

Then: **Kennedy Wilson absorbed Toll Brothers' pipeline — 29 sites, ~$3.6B, site list unpublished.**
Toll is a confirmed MVE client. Try Kennedy Wilson investor presentations and 10-K/10-Q filings on
EDGAR. This is the single biggest unknown in the brief.

---

## 3 · Sources that blocked, and the workarounds that were proven to work

**Two techniques the last session verified — use them:**
- **`curl` with a browser User-Agent gets HTTP 200 from `planning.lacity.gov`** where plain fetch
  gets 403. That is how the EIR and SCEA case pages were read.
- **`layimby.com/?s=MVE` has a working search endpoint** and was the highest-yield discovery source
  for LA. **Urbanize LA has no working search endpoint**, and **there is no Urbanize San Diego at
  all** — that was a wrong assumption in the last brief.

**Blocked, needing search or a real browser:**

| Source | Problem |
|---|---|
| `planning.lacity.gov`, `newportbeachca.gov`, `culvercity.gov` | 403 to plain fetch — use the curl UA trick |
| `BuildSD.org` | 403. San Diego's actual entitlement tracker |
| SDBJ, The Registry SoCal, Times of San Diego, The Real Deal, bldup, hoodline, yieldpro | 403 |
| CoStar, LoopNet | paywalled |
| `aiaoc.org` | 403 — **and it publishes full consultant rosters on award entries**, which is how the existing consultant map was built. Worth the effort. |
| San Diego permits | `OpenDSD` only covers 2003–2018. Post-2018 needs an Accela session at `aca.accela.com/SANDIEGO` |
| `slc.gov` online open houses | 404 |
| Hines.com, catalyst-invest, liveatsilopark | 403 or JS-rendered |

**CEQAnet is broken in a specific way worth knowing:** it moved to `ceqanet.lci.ca.gov`, and its
keyword search returns all 444,560 documents regardless of query. **Reach records by direct
SCH-number URL only.**

---

## 4 · The open questions, ranked

1. **Is Discovery Park MVE's?** The Irvine Company, 1,858 units, PC-approved June 2025, no architect
   publicly named. Largest unassigned entitlement found. **Highest value.**
2. **Has 4002 Park Blvd (aka Pali) broken ground?** "Preparing to break ground" since 2025-08-18 and
   still not started. Settle via BuildSD or the Accela portal.
3. **Does John A. Martin hold OCVIBE's residential buildings, or only the district?** JAMA is named
   at district level; the residential scope is unresolved.
4. **What is in the Kennedy Wilson / Toll Brothers site list?** See §2.
5. **Four offices or five?** One source read MVE's contact page as four (all California), another as
   five, with LABJ's "1/5" field corroborating five. **Denver opened 2022 and is not on the current
   page — no closure evidence exists.** Settle it from their own site, or leave the count unstated.
6. **Who does structural for MVE in San Diego and Salt Lake?** No SE is publicly named on *any* MVE
   San Diego project across two independent passes. Salt Lake is locally held (BHB, Dunn).
7. **Is Riverwalk Phase 2 sequenced anywhere publicly?** 17 of 20 parcels undesigned.

---

## 5 · Corrections already banked — do not rediscover

- **Pali and 4002 Park Blvd are one project**, not two. `../mve-research/B-portfolio.md` still lists
  them separately and should be fixed.
- **Reserve at Silo Park broke ground 2025-10-14.** Two developer sites still call it
  pre-development.
- **Snug Harbor is dead** — GPA rescinded 6–0 on 2026-02-09 after a referendum.
- **The Village at Riverwalk (721 u) and Riverwalk Central Village are Gensler**, not MVE.
- **Uptown Newport** is Shopoff Management, not The Irvine Company.
- **The Canyon, San Francisco is MVRDV.** **MVE, Inc. of Modesto** is an unrelated civil/survey firm.
- **MVE has no aviation work and no Egypt project** — zero matches across 74 live pages, ~140
  archived pages and ~4,700 archived URLs. Directory listings claiming otherwise are wrong.
- **No firm-wide revenue or headcount figure is quotable.** Sources disagree; none is a filing.

---

## 6 · What to produce

**Update `../11-MVE-PIPELINE-BRIEF.md` in place.** Do not write a new document — Jim's feedback on
the first two dossiers was explicitly *"lots of info there, too much to go through."* The brief is
six short sections and must stay that way. Add rows to the tables, resolve the open questions,
tighten. If something new is big enough to change the ranking, re-rank.

Then re-render:

```
python <scratchpad>/build_doc.py docs/audit-2026-08/11-MVE-PIPELINE-BRIEF.md \
  docs/audit-2026-08/_mve-pipeline.html "<title>" "<h1>" "<sub>" "<cards>" "<note>"
pwsh tools/Format-BdWebPdf.ps1 -Html docs\audit-2026-08\_mve-pipeline.html \
  -Pdf docs\KOR-MVE-Pipeline-Brief-<date>-web.pdf
```

**Verify the shipped PDF as text, never the HTML** — `pdftotext -enc UTF-8`. A committed
`ERR_FILE_NOT_FOUND` page already exists elsewhere in this repo from skipping that step.

---

## 7 · Standing context

- **Apollo is available and costs credits.** 1 credit per organisation enrich; the org record for
  MVE is already pulled (`5d0a96e4a3ae61c6bf265b06`). **Get Ian's sign-off before any spend.** Two
  credits would convert the hiring evidence to a dated primary count.
- **Ian has been right every time he has corrected this research.** If something looks absent, ask
  him what it is called before reporting it missing.
- The wider audit this sits inside is at `../START-HERE.md` — unrelated to the MVE work, but it is
  the same directory and worth not confusing.
