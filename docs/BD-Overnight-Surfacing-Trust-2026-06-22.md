# BD Biz-Brain — Overnight Surfacing + Trust Pass (2026-06-22)

Autonomous run against the four agreed targets. **Discipline held: nothing below is called "done" unless a number or a rendered output proves it. I am not claiming the system is perfect or complete.** Each item is split into *proven* vs *needs your hand*.

---

## The one-line summary

The real gap was **surfacing** — we had collected a rich teaming/portfolio graph (IntelWork: ~15.5k edges) but the app's read paths only counted the sparse MPI foreign-key columns (3.4% populated), so most of it was invisible. Four read paths now span the full graph, the brief now tells you how fresh it is, and ~1,944 contact emails + 25 discovered decision-makers were added. Freshness/discovery automation is **designed, not deployed** — by evidence, not laziness (nothing is stale right now).

---

## 1. Surfacing — collected intel now actually shows (PROVEN + committed + adversarially reviewed)

Each change unions the MPI foreign-key rows with the org's IntelWork portfolio edges, deduped by normalized project name (MPI preferred on ties). Each was verified against the live DB *and* passed an independent adversarial review.

| Surface | Before → After (live DB) | Commit |
|---|---|---|
| **Architect leverage** ranking (BD Reports) | architects ranked **182 → 745**; per-org depth e.g. MA+HG 2→34, GEC 4→28, ACI 1→24, Diamond Schmitt 3→24 | `97296704` |
| **Competitor SE footprint** (BD Reports) | DCI 2→7, RJC 14→21, KPFF 1→4, AHBL 0→3, Equilibrium 2→7 (rivals with no IntelWork SE edges correctly stay flat) | `3421efd2` |
| **Org brief "recent work" + owner counts** | **2,694 orgs** with no MPI row now show real portfolio instead of reading "thin"; 493 owner orgs gain project counts | `9f0c51f5` |

Adversarial reviews: architect change = "SOUND — ship it" (all 7 checks pass); brief change = PASS on all 6 (UNION types reconcile, no ordinal/null throws, dedup correct).

**Deferred with evidence (NOT skipped — measured and judged not worth it):**
- *Pursuit-dossier* and *sector-signal* team-edge fills: only **2 / 5 / 1 of 1,484** honed pursuits are fillable for architect/SE/GC by name-match (IntelWork is historical portfolio; it barely overlaps *active* pursuits). 8 cells for an ordinal-risky edit to consumed UI = not worth it.
- *korJoint* brief augmentation: KOR itself has only 55 IntelWork rows — marginal.
- *Region/sector top-org lists*: IntelWork has no province/sector column to scope by.

**The principle established:** footprint-*aggregation* changes (whole-org) have huge yield; per-*active-project* name-match changes do not. That's why the first three shipped and the rest didn't.

---

## 2. Trust — the brief now shows its own freshness (PROVEN in a rendered PDF)

A reader had no way to tell if a brief was current or six months stale. The org-brief header now carries:

> **Intelligence:** refreshed 2026-06-22 (0d ago) · 20 signals, 19 people, 20 portfolio

Derived from `MAX(CanonicalOrgEnrichment.UpdatedAtUtc)` + the Intel bundle counts, via one computed property feeding both the PDF and DOCX renderers. Commit `31a1bdf9`.

**Proven:** rendered the AHS org brief through `BdBriefSmoke` and read the actual PDF — the line is there, page 1. **Needs your eyeball:** the DOCX (.docx) variant uses the identical code path but the smoke tool only renders PDF; and per-fact source/confidence badges in the *WPF* dossier are deferred (I can't drive the WPF UI headless to verify them — that's a you-at-the-screen check).

---

## 3. Contact research — Hunter + web (Apollo evaluated honestly)

You asked to use Apollo and Hunter for people/company info. Outcome:

- **Hunter — productive, ran:** pattern-propagation wrote **1,826 emails** (407 firms, conf 55, flagged unverified); a capped domain-search wrote **118 verified emails** (71 firms, e.g. Beedie execs conf 90–92). ~**1,944 emails** added, NULL-only, source-tagged. Logs: `output/hunter-pattern-2026-06-22.log`, `output/hunter-domain-2026-06-22.log`.
- **Apollo — evidence-based dead-end on the current plan:** the key works and the endpoint responds (org + title come back: "Appia Developments Limited / VP Operations"), but **names and emails are redacted** — free-tier PII masking. Without paid reveal credits it cannot populate `IntelPerson`. I did not build on a paywalled-redacted source. If you get an Apollo seat with credits, the `people/match` reveal path is the place to wire it.
- **Web discovery (Sonnet) — filled the gap Apollo couldn't:** **25 decision-makers across 20 thin-people firms** discovered from firm sites/LinkedIn (Zeidler Architecture 6 incl. a BD Director; Gibraltar, Greyback, DAVA, Appia/Jim Bosa, Path). **Staged, not ingested** → `output/web-people-discovery-2026-06-22.json`. It flagged ~8 Alberta firms (low structural relevance) + a couple geography/surname cautions for your review before ingest.

**Needs your hand:** review `web-people-discovery-2026-06-22.json` and say the word to ingest the clean subset; decide on Apollo credits.

---

## 4. Freshness (Target 3) — designed, not run, *because nothing is stale*

Staleness scan of 11,043 high-value orgs (Architect/GC/Dev/Competitor/KorClient/Buyer): **0 never-enriched, 0 over 60 days, 0 even 30–60 days — all 11,043 enriched within 30 days.** The recent mass-enrichment refreshed everything. Re-enriching now would burn tokens re-processing fresh data.

The actual need is a **recurring** threshold job that re-queues high-value dossiers as they age past ~60 days (using the existing `BdQueueDrainBatchGenerate` → drain → `BdQueueDrainIngest` path). That's a Worker job to deploy with your awareness, not a 3am auto-deploy. The new provenance line (Item 2) is what makes staleness visible when it eventually appears.

## 5. Repeatable discovery (Target 4) — runbook, not new streams

Per the standing rule (don't invent new research streams), repeatable discovery = re-running the **existing** `BdResearchImport` streams + batch generator on a cadence, not new PROMPT.md folders. Tonight's web-people-discovery is an example run of that existing pattern. Cadence scheduling is a you-decision.

---

## What needs you (the honest punch-list)

1. **Publish the WPF app** so the BD Reports + Org Brief changes (Items 1–2) are visible to the firm — these are committed to `develop` but live only after your normal App publish cadence. (I build only, never publish.)
2. **Eyeball a generated org brief** (PDF *and* Word) from the app to confirm the new "recent work" rows + the Intelligence freshness line look right in situ.
3. **Review `output/web-people-discovery-2026-06-22.json`** → approve ingest of the clean subset.
4. **Decide on Apollo** (paid credits → I wire the reveal path) and on scheduling the recurring **freshness** + **discovery** jobs (Items 4–5).

## What I did NOT do (so you're not surprised)
- Did not deploy any Worker job or publish the app.
- Did not ingest the web-discovered people (staged for your review).
- Did not re-run enrichment (nothing was stale).
- Did not build an Apollo writer (plan redacts the data).
- Did not touch the pursuit-dossier / sector-signal / region-list surfaces (measured ~nil yield).

Commits this run: `97296704`, `3421efd2`, `9f0c51f5`, `31a1bdf9` (all on `develop`).
