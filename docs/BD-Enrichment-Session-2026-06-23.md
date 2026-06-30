# BD Enrichment + Visuals Session — 2026-06-23

Autonomous run while Ian was away. Two workstreams: (1) the in-app BD visuals, (2) a highest-ROI enrichment cycle.

---

## 1. In-app BD visuals (built, render-verified, PENDING COMMIT)

Three live visuals now render inside the app under **BD Reports → Analytical reports**, with Save PDF / Save DOCX:

- **Teaming Attack-Graph** — orgs on live pursuits; heat = pursuit $ × urgency; **green ring = warm (we know people there)**; edges = co-teaming.
- **Priority Treemap** — tiles sized by $ at stake; green = warm, red = no contacts yet.
- **Opportunity Attack Cards** — one card per pursuit: team + KOR's path-in.

Key decisions made mid-build:
- **Heat was wrong at first**: it read `vDossierCompleteness.Score` (= dossier *incompleteness*), which (a) took **122s and timed out** and (b) colored well-known orgs cold. Repointed heat to a **cheap attack-value query (0.7s)** = pursuit $ × urgency. Warmth is now its own channel. The snapshot table we discussed is therefore **not needed** for the visuals (would only help Generate-Batch).
- **Light KOR theme** (was black-on-black), standardized readable tooltips on graph + treemap.
- Render verified by launching the app + screenshotting (`docs/_app-v2-graph.png`, `_app-v2-treemap.png`, `_app-v2-cards.png`).

**Not yet committed** — waiting on your go. Files: `BdVisualHtml.cs`, 3 service queries in `SqlBdReportService.cs`, `IBdReportService.cs`, `BdReportsViewModel.cs`, `BdReportsWindow.xaml(.cs)`, `DocxBuilder.RenderImage`.

---

## 2. Enrichment cycle — 21 highest-attack orgs (DONE, in DB)

Targeted the orgs sitting on KOR's **biggest live pursuits that were missing contacts** (the attack-value lens — NOT the Score rubric, which surfaced Deltek garbage like "Devlin Construction Ltd. Award amount GST NOT included"). Full loop: research → ingest → contact-enrich → dedup-plan.

**Result (verified in DB):**
- 21 orgs: full FirmNarrativeHoning intel ingested (decision-makers, signals, plays, Current+Action narratives).
- 21 websites backfilled (were blank → blocked Apollo/Hunter).
- **116 emailed contacts now** on these orgs (were ~0). Sources: 46 Apollo verified + 13 Hunter verified + 73 pattern-inferred.
- All 21 now show **warm** on the attack-graph.

### Act-on-this-now intel the agents surfaced
- **YMCA Calgary** — GEC Architecture just named prime (Jun 2026) on the **$120M West District** facility. Structural sub picked within weeks of prime → **contact GEC Calgary now.**
- **SD42 (Maple Ridge)** — Pitt Meadows Secondary replacement **in design phase now**; SE window open. Call **Louie Girotto 604.463.8918**.
- **Palliser Regional Schools** — Coalhurst school architectural RFP **live now**.
- **Uchucklesaht Tribe** — housing SE selection window **Q3 2026**; incumbent Buepoint (Duncan) — KOR challenger.
- **ACRD (Alberni-Clayoquot)** — seismic-trigger new office building; low-competition geography, exactly KOR's mandate. Confirmed email: dsailland@acrd.bc.ca.
- **Competitor flag:** **Glotman Simpson** is incumbent SE on the Kengo Kuma Banff Visitor Centre (won May 2026) AND was on Heatherwick's 1700 Alberni — they own the starchitect-Vancouver structural lane.
- **Stale pursuit:** Heatherwick's 1700 Alberni was **withdrawn Aug 2024** — no active Canadian project; down-weight.

---

## 3. Dedup — COMMITTED + audited (83 merges)

`BdCanonicalDedup --commit`: **83 groups merged, 0 failed.** Tool hard-deletes losers + repoints all FKs — verified **0 orphans** (affiliations/enrichments/MPI links). Mostly recent `77xxx` bare-name dups folding into established canonicals (Perkins+Will, ZGF, UBC, City-of-*, GEC, etc.). Wright Runstad loser #77320 (which I'd enriched) folded into survivor #68713 — contacts preserved.

The 2 I'd flagged, resolved:
- **Stantec Architecture → Stantec Inc. (Competitor)** — allowed. Stantec self-performs structural, so they're a competitor to KOR in every scenario (never a team-with architect). One Stantec, kind Competitor = correct + clean.
- **TransLink** — merged, then fixed survivor #69680 to `Kind=Buyer`, `DisplayName='TransLink'`.

## 4. Messy-name hygiene — done what's real; the rest was a false alarm

- **Retired 12** disconnected Deltek bid-line artifacts (0 project links, junk names like "Devlin Construction Ltd. GST not included in Bid Amount", "Design-build consortium (awarded)").
- **Lesson:** crude substring filters are dangerous — "Kin**gst**on", "Livin**gst**one", "Flag**st**aff", "**Award** Construction Mgmt" all matched a junk filter as false positives. Always preview + word-boundary patterns.
- **The "207 multi-entity" backlog was mostly a false alarm:** of 206, **125 have intel/people** and **27 are on live projects** — they're real JVs/partnerships ("BC Housing / Archway", "Arthur Erickson / Musson Cattell Mackey") that the JV-string policy keeps. The safe-retire filter matched 1 (itself a real named consortium). **Nothing to bulk-clean — retiring would destroy intel.**

## 5. Remaining (your call — not done)
- **353 MPIs with NULL ProponentName** — the proponents drain. Moderate ROI (fills who owns each project); big Sonnet run.
- **12 PURSUE/URGENT MPIs missing ProjectBrief** — low marginal (already honed). Recommend skip.

## Saturation note
The org-contact vector is exhausted: only 1 more domained contact-gap org existed on live pursuits (Amanat — no Apollo coverage), and **zero** high-attack briefed orgs are stale.

## Housekeeping
- Removed the garbage Score-ranked `batch-036.json` I generated on the share.
- Working files in `_drain36/` (envelopes + verified domains) — kept for traceability; safe to delete.
