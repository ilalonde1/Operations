# Overnight Enrichment — Morning Briefing (2026-06-20)

Four Sonnet research agents ran overnight, keyed off the **live graph gap** (high-footprint orgs with zero contacts). Research-only — **nothing was written to the DB.** Review below, then say go and I'll apply clean enrichment migrations keyed by `orgId`.

## Haul
| Category | Orgs | People | With email | Hunter-verified |
|---|---|---|---|---|
| Architects | 10 | 36 | 33 | 30 |
| Competitors | 5 | 24 | 22 | 15 |
| Developers | 11 | 42 | 34 | 29 |
| Public-sector | 9 | 54 | 47 | 11 |
| **Total** | **35** | **156** | **136** | **85** |

Files in this folder: `<category>.json` (structured, ingest-ready by orgId) + `<category>-report.md` (narrative).

## ⏰ Time-sensitive — surfaced, act this week
- **UC Berkeley RSSP — Emeryville Structural Repairs RFQ**: prequalification **due July 15, 2026**. A live, open structural solicitation KOR can pursue now (contacts Wendy Hillis, Todd Henry captured).

## Highlights by category
- **Architects** — Acton Ostry (7, incl. Mark Simpson P.Eng — the in-house structural bridge), ThinkSpace (6 + BD director), KMBR, Parkin, Rositch Hemphill, Low Hammond Rowe all verified. *Parkin: Cameron Shantz retired Oct 2025 → Vancouver leadership gap (opening).*
- **Developers** — Cadillac Fairview strongest (7, Hunter 98–99; SVP Josh Thomson has a structural background = warm angle). Highstreet (VP Construction Tony Kudryk), Heidelberg (Ignacio Cariaga P.Eng, low-carbon concrete), Westbank, Kind, Bold, JTA, Maskeen all returned contacts.
- **Public-sector** — Alberta Infrastructure ($28.3B pipeline, 5 verified), ECSD (3 Hunter-97), County of San Diego (QBS via BuyNet), VCC, BC MoTI (e-RISP registry is the gate; Kevin Volk P.Eng ADM). Interior Health = $1.5B+ hospital pipeline (Brian Miller gatekeeper).
- **Competitors** — Bush Bohlman, HDR, Sorensen Trilogy, Aspect deepened with leadership + vulnerabilities.

## Dispositions BEFORE ingest (judgment calls — don't auto-load)
- **SMP Engineering (15548)** → **reclassify TeamingPartner, NOT Competitor.** It's a Calgary electrical/MEP firm, already suppressed via migration m132 ("allied-discipline, misclassified Competitor"). Potential MEP sub on KOR teams. **Exclude from competitor ingest; reclassify.**
- **Hotson Bakker (54300)** → **defunct; merged into DIALOG ~2009.** Joost Bakker is now a DIALOG Vancouver principal. Recommend merge `54300 → DIALOG (6154)` and re-home contacts. (Joins the dedup follow-ups below.)
- **Keyara Corp. (53419)** → **no discoverable footprint** (domains dead, no registry hit). Verify the source data (shell/DBA/data-entry error) before trusting; exclude for now.
- **Maskeen Development (54841)** → **in receivership** (KSV/FTI, ~$25M defaults, early 2026). Ingest contacts but flag — low pursuit priority.
- **Westbank (70911)** → financial pressure (Mabberley lawsuit); VP Development seat appears unfilled.
- **PatternInferred emails (conf 55)** — ingest at low confidence, clearly flagged; not for cold outreach without a second check.

## Dedup leftovers I flagged while building the lists (fold into morning cleanup)
- `hcma` (75897) → merge into the real **hcma (8799)** [already enriched]
- `ZGF` (75556) → merge into **ZGF Architects (38975)**
- `Fast` (75803) → likely a **Fast + Epp (7345)** fragment — verify + merge
- `Hotson Bakker (54300)` → DIALOG (above)

## Proposed morning sequence (on your go)
1. Apply enrichment migration(s) — architects + developers + public-sector + the real competitors, keyed by orgId, **excluding** SMP/Keyara and the defunct/dup orgs.
2. Reclassify SMP → TeamingPartner; merge the dedup leftovers (hcma/ZGF/Fast/Hotson Bakker).
3. Stand up the UC Berkeley RFQ as a tracked pursuit (July 15 deadline).
