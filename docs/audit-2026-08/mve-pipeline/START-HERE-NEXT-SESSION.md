# START HERE — MVE pipeline research

**Status after session 2 (2026-08-26): the brief is current and shipped. Five of the seven open
questions are closed. Do not restart the research, and do not re-ask the closed questions.**

---

## 1 · Read these first, in this order

| File | What it holds |
|---|---|
| `../mve-pipeline-brief.html` | **The deliverable, and now the single meeting document.** Shipped as `docs/KOR-MVE-Pipeline-Brief-2026-08-26-web.pdf` (11 pp). Built on the canonical `tools/BdDocTemplate/`. Edit `../mve-pipeline-body.html`, not the assembled file. `../11-MVE-PIPELINE-BRIEF.md` is RETIRED |
| **`P4-session2-resolutions.md`** | **Read this second.** What session 2 resolved, with sources — and the three techniques that did it. It will save you a day |
| `P1-oc-la.md` | Orange County + LA — forward projects, SE field, the "getting busier" cadence |
| `P2-san-diego.md` | San Diego — the studio's real workload |
| `P3-utah-bay-developers.md` | Utah, Bay Area, Arizona + the developer-side view |
| `../10-MVE-COMPANY-DOSSIER.md` | ~155 built projects. **Reference only — do not re-research the portfolio** |
| `../09-MVE-DEMO-DOSSIER.md` | Who is in the room; David Arnold as likely evaluator |

**The one rule that governs all of it:** where a document says a structural engineer is *"not
publicly named,"* that means no public record names one. **It does not mean the seat is open.**
Never upgrade that phrasing.

---

## 2 · Closed — do not spend a minute on these again

Full sourcing in `P4-session2-resolutions.md`.

- **Discovery Park is BDE Architecture's, not MVE's.** Read off the plan set filed with the City of
  Irvine. It was the brief's #1 question; the answer is negative.
- **4002 Park Blvd (Pali) has not started vertical, and its structural seat is not open.** An
  ACI 318-19 deviation request was issued in May 2024; the building permit is in plan check now.
- **OCVIBE Residential Phase I names thirteen consultants and no structural engineer**, and John A.
  Martin is not among them. This is now the strongest lead in the brief.
- **Kennedy Wilson / Toll Brothers: 24 land positions, ≈\$2.9B per the SEC filings.** The site list
  is not published and there is no filing obligation that would make it so.
- **MVE has five offices** — Irvine, Los Angeles, San Diego, San Francisco, **Guadalajara**. No US
  office outside California. Denver is absent with no closure evidence.
- **AvalonBay's supplemental has been pulled.** It names every community under construction and
  nothing that is not. The forward pipeline is a count only — 31 Development Rights, 9,997 homes.
  **The real finding is that AvalonBay is merging into Equity Residential**, closing H2 2026.
- **Riverwalk Phase 2 is genuinely unsequenced in public** — proven from the site's own sitemap, not
  inferred from a failed search.

---

## 3 · What actually remains

1. **Who does structural for MVE in San Diego?** Three independent passes, same absence. This is
   the question to ask in the room, not a research task. Do not report it as "missing" — report it
   as unpublished.
2. **6201 Residences, Culver City** — 846 units, approved October 2025, start targeted 2026, so the
   shortest fuse in the brief. `culvercity.org` refuses even a browser User-Agent. The plan-set
   technique in `P4 §1.1` would answer it if the packet can be reached another way.
3. **Santa Clara Park** — the trigger is lease expiry on the offices it replaces. `santaclaraca.gov`
   also still 403s. Santa Clara uses Legistar; that was not tried.
4. **Riverwalk at Studio City and 5350 Wilshire** — both LA cases, both with plan sets that would
   carry a consultant directory. `planning.lacity.gov` answers a browser User-Agent. **This is the
   most promising untried work.**
5. **Apollo, on Ian's say-so only.** 2 credits converts the ~20-open-roles figure from a
   ZipRecruiter count to a dated primary one. Org record already pulled
   (`5d0a96e4a3ae61c6bf265b06`). **Get sign-off before any spend.**

---

## 4 · Techniques — read `P4 §1` before searching

Three things carried this session, and they generalise:

- **City entitlement plan sets carry a full consultant directory** — the single highest-yield
  artifact for "who is on this project."
- **San Diego's permit record is a daily CSV at `seshat.datasd.org`**, not the Accela portal.
- **A browser User-Agent now opens buildsd.org, aiaoc.org and hillcrestbia.org**, not just
  `planning.lacity.gov`. Culver City and Santa Clara still refuse.

**CEQAnet** keyword search is still broken — it returns all 444,560 documents regardless of query.
Reach records by direct SCH-number URL only.

---

## 5 · Producing the brief

**Edit `../mve-pipeline-body.html`** — the content fragment, not the assembled
`mve-pipeline-brief.html`. Jim's feedback on the first two dossiers was *"lots of info there, too
much to go through."* It is now the single meeting document, so it carries more than the pipeline —
but every addition has to earn its place. Add rows, resolve questions, tighten, re-rank.

The brief is built on **the canonical KOR design system**, `tools/BdDocTemplate/`. Its README is the
authority: **copy the style block verbatim, do not restyle per document.** The exact rebuild command
is in the comment at the top of `mve-pipeline-brief.html`; then:

```
pwsh -NoProfile -File tools/Format-BdWebPdf.ps1 -Html docs\audit-2026-08\mve-pipeline-brief.html \
  -Pdf docs\KOR-MVE-Pipeline-Brief-<date>-web.pdf
```

**Verify the shipped PDF as text, never the HTML** — `pdftotext -enc UTF-8` — and render a few pages
and *look* at them. Both faults found on 26 August were invisible in the source: a footer
overprinting body text, and a swallowed bold span. See `P4 §6`.

⚠ **Do not go back to `build_doc.py`.** It lives in no repo — only in a session scratchpad — and its
`position:fixed` running footer overprints body text on most pages of every document it has
produced. The rest of the `audit-2026-08` series is still on it and still carries that defect.

---

## 6 · Standing context

- **Ian has been right every time he has corrected this research.** If something looks absent, ask
  him what it is called before reporting it missing.
- The wider audit this sits inside is at `../START-HERE.md` — unrelated to the MVE work, but it is
  the same directory and worth not confusing.
