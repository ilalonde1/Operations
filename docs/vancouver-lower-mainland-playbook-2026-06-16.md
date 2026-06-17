% Lower Mainland — BD Operating Playbook
% CONFIDENTIAL — Ian Lalonde / KOR leadership only
% 2026-06-16

> **Not for the field-guide audience.** This is the management layer behind the Lower Mainland BD Field Guide — how the pipeline is scored, where the firm deliberately concedes, the competitor-displacement plays, and the account move-sequences. Pair it with the Field Guide (the shareable pursue-and-call version); this doc holds what stays inside.

---

## 1. HOW THE PIPELINE IS SCORED (the EV math)
The Field Guide presents a *verified* shortlist, not a raw ranking — because the raw EV ranking is only a candidate-*generator*. Run blind it surfaces duplicates, distressed/cancelled projects, and seats whose stored stage is stale (a project reading "Capital Plan" that's actually alliance-locked). The scoring chain behind the numbers:

- **Fee model:** structural fee ≈ 1.0% of construction value; verified cost where available, else a flagged sector-median model (don't present the modeled values as bankable — they're order-of-magnitude).
- **Win-probability weighting:** each warm-path fee × (relationship strength × ~69% base close rate). Relationship strength is tiered off warm-path depth — owner/architect/GC shared-project count from Deltek.
- **SOM (~$183M)** is the win-prob-weighted expected capture across the 247 warm set. **$340M** is the un-weighted warm-serviceable. **$950M** is the whole-pipeline upper bound — context only, never a number to manage to. When anyone quotes "$950M," correct it to the warm $340M / expected $183M.
- **The gate** (`tools/ReportIntegrityCheck.sql`) is what makes the shortlist trustworthy: in-lane + early-stage + de-duplicated + live-source-confirmed. Every region report must pass it before it ships.

## 2. COVERAGE SCORECARD — can we actually reach the buyer? (internal QA)
**26 of the top 30 EV targets (87%) now have a named, emailable decision-maker** — up from a baseline where the core public buyers (VSB, Richmond SD, VCH, PCL) had **zero** emailable contacts before this cycle. The 4 uncovered are the industrial/marine seats KOR doesn't pursue. This cycle's Apollo pass closed the building-sector gaps (District of Mission, ZGF, Baptist Housing). This is the internal readiness metric — track it per region; the field guide just gets the finished call sheet.

**Still unverified (the data ceiling):** Anita Leonoff (Arcadis — not in Apollo; IBI→Arcadis rebrand likely moved her email), Sangeetha Ramanan (HDR — no Apollo record), Jason Turcotte (Cressey — email withheld), Shelley Rid (SFU — extrapolated only). Hunter is the next lever for these four.

## 3. ARCHITECT DISPLACEMENT — the competitor-wedge plays
The Field Guide ranks architects to *deepen*; this is who they're locked to and how to pry the seat:

- **hcma — the #1 displacement target.** 5 pursuable seats, no KOR relationship, currently defaults to **Fast+Epp**. 18 contacts already on file. This is the highest-value *conversion* in the book — going after it means taking structural work Fast+Epp considers theirs.
- **DIALOG (Fast+Epp foot-in)** — DIALOG is a KOR foot-in but routes its structural to Fast+Epp; wedge their **non-timber** work where Fast+Epp's mass-timber edge doesn't apply.
- **GBL / Francl (Glotman dual-wedge)** — both also work with other SEs; KOR's existing GBL relationship is the opening to displace Glotman on their non-high-rise work.
- **Revery (convert)** — warm client relationship but **0 shared projects** — pure conversion target.

## 4. COMPETITIVE INTELLIGENCE — verified dominance + where we concede
**Why this stays internal:** it names where KOR *cannot* win and why, which is strategy, not a sales sheet.

**Verified rival dominance (2024–26 awards/portfolios, sourced):**
- **Bush, Bohlman & Partners — owns BC healthcare.** Structural EOR on Cowichan Hospital ($1.45B), Royal Columbian redevelopment, New Surrey Hospital ($2.88B) — **$4B+ of health infrastructure.** *(Our graph showed them at "1 seat / cultural" — badly understated; corrected.)*
- **Glotman Simpson — owns Vancouver high-rise residential/mixed-use.** Senákw (6,000 units), Fifteen Fifteen, Canada's Earth Tower, M5, City of Lougheed; recurring developers Westbank/Bosa/Shape/Delta Land.
- **RJC Engineers — owns public civic/institutional + is a K-12 seismic incumbent.** UBC Gateway ($180M), BCIT Tall Timber, Capstan Station, Burnaby Fire Halls, Quw'utsun school; partner HCMA.
- **Fast + Epp — owns mass timber + award-winning civic/cultural** (PNE Amphitheatre, Rosemary Brown, Eric Hamber) and is the **other K-12 seismic incumbent** (Begbie, Bayview, Brighouse). Partners HCMA/GBL/Perkins&Will/DIALOG/Revery.
- StructureCraft (design-build timber niche) · WHM (high-rise residential, publishes little) · Entuitive/Aspect (emerging) · WSP/Stantec (supporting roles in LM).

**The strategic read (do not broadcast):** the institutional/healthcare megaprojects are **incumbent fortresses** — Bush Bohlman in health, RJC in civic/institutional. KOR's two *realistic* lanes are **(1) developer-residential**, won on **relationship** (Anthem/Wesgroup/Reliance/Bosa) not incumbency, against Glotman/WHM; and **(2) heritage + high-importance seismic**, KOR's verified strength, where only RJC competes. **Don't burn pursuit budget fighting Bush Bohlman for hospitals — that is why the verified-open hospital seats (Burnaby / Surrey Memorial / VGH) are *watch*, not *chase*.** Surrey Memorial is the one exception worth a real run because it's genuinely pre-team.

## 5. ACCOUNT MOVE-SEQUENCES (the next-plays)
Field guide gives the team *who*; this is *the move*. Validated against KOR's Deltek pursuit record: **KOR has zero pure-loss client relationships** — it loses individual pursuits, never whole clients — and deep repeat clients convert **~85–95%** (Wesgroup 78 won / 6 lost, Intracorp 45 / 3, Ledingham McAllister 40 / 2). That's the measured basis for weighting the pipeline toward relationship-rich seats.

- **Graham Construction** — one call covers two of the top-5 EV pursuits (Richmond Hospital + Annacis); get KOR on the structural team before award. KOR's single highest-leverage GC relationship.
- **Vancouver Coastal Health** — Gage is the decision-maker behind two EV-list seats; relationship call now, ahead of the RFPs.
- **Arcadis IBI** — deepen the existing client tie and get on the International Plaza + NW Towers teams; being on the Prime's team *is* the structural seat.
- **UBC** — recurs more than any other owner (23 seats); pursue the Medicine + housing cluster as a **campus account**, not one-off bids.
- **Wesgroup / Anthem** — principal-to-principal calls on active seats KOR isn't on; pure cross-sell on proven trust.
- **NSDA Architects** — 28 shared projects (more than most named Primes) **but KOR's highest loss count (7)**. Treat as a core architect account: deepen with Ken Wong **and diagnose the 7 losses** — are they defaulting to another SE on certain building types? Plug the leak.

## 6. SEISMIC — the credential-handling note
KOR's seismic positioning is **strong heritage/high-importance, zero K-12 school credit** (challenger, not incumbent, on SMP). Lead the heritage/high-importance pursuits. **Manage the founder (Bryson) EGBC-seismic disciplinary disclosure (2019) proactively** — have the framing ready before it surfaces in a pursuit, rather than reacting to it. The ~2019 rebrand distanced the firm; keep the narrative forward-looking (current credentials, named credits) and don't volunteer the history unprompted, but be prepared.

---

*Source of truth: `docs/vancouver-lower-mainland-intel-2026-06-16.md` (the full integrity-gated master). Shareable cut: `docs/vancouver-lower-mainland-field-guide-2026-06-16.md`.*
