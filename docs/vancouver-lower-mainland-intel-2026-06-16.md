# Lower Mainland — BD Intelligence Report
*Generated 2026-06-16 from the KOR BD Brain (KorOpportunitiesDb). For the Friday BD meeting.*
*Refreshed after a system-wide data-hygiene pass (migrations 146–150 + canonical dedup) — counts below run on consolidated, de-duplicated data.*

> Scope: `MajorProjectsInventory` where Province=BC, Region=Lower Mainland (canonical, post-141/150), active (not retired).
> The earlier ~510 unregioned-BC caveat is largely resolved: region was back-filled from municipality
> (migration 150), pulling Metro Van / Fraser Valley projects that were previously invisible into scope.

---

## 1. Executive summary
- **785 active Lower Mainland projects** (up from 566 pre-backfill), **~$76B** estimated capital value,
  across **318 owners**, **120 architect firms**, and a still-thin set of structural engineers.
- The market is **public-sector-dominated**: school districts, UBC/SFU, and the health authorities
  are the highest-frequency, highest-value owners. KOR's win-path here is **getting onto the
  architect/Prime teams** that these owners select.
- **The competitive position is concentrated:** Fast + Epp and Glotman Simpson are the incumbent
  structural engineers on most of the marquee architect relationships — the displacement targets.
- **KOR's own foot-in is now visible** (it was previously hidden under a competitor-mislabeled row):
  KOR is the structural engineer on **3 active LM pursuits** (with DIALOG and Chris Dikeakos) and has a
  **~95-project historical LM track record** now correctly attributed to the KOR anchor.
- **Biggest intelligence gap:** the structural-engineer-per-project edge is only **3.3% populated**
  (26 of 785) — so the displacement map below is directional, not complete. Buffing this is the #1
  enrichment priority (see §8).

## 2. Market overview
| Stage | Projects |
|---|---:|
| (unstaged) | 298 |
| Capital plan | 204 |
| Procurement | 163 |
| Permitting | 76 |
| Concept | 29 |
| Planned | 11 |
| Approved / Design | 4 |

Procurement + Permitting (239 projects) are the **near-term actionable** pipeline; Capital-plan (204)
is the **pre-positioning** pipeline (get on teams before the RFP).

## 3. Owners / Buyers landscape (who controls the work)
*Consolidated — the Vancouver SB, Richmond SD, North Van SD, and Fraser Health splits that previously
fragmented these counts have been merged.*

| Owner | LM Projects | Est $M |
|---|---:|---:|
| Vancouver School Board (SD39) | 86 | 1,020 |
| Richmond School District (SD38) | 40 | — |
| Fraser Health Authority | 20 | 2,065 |
| Surrey School District No. 36 | 18 | 6,217 |
| North Vancouver School District (SD44) | 16 | 771 |
| University of British Columbia | 16 | 5,147 |
| City of Vancouver | 15 | 306 |
| Vancouver Coastal Health | 14 | 4,956 |
| Simon Fraser University | 12 | 1,439 |
| Infrastructure BC | 9 | 4,080 |

**Read:** the K-12 school districts are the *volume* engine; UBC/SFU + the health authorities + Infrastructure BC
are the *value* engine. School-district cost fields are still largely empty ($0) — a value-backfill gap.

## 4. Architects / Primes (the teams to get on)
| Architect | LM Projects | KOR foot-in* |
|---|---:|---:|
| Chris Dikeakos Architects | 15 | 1 |
| GBL Architects | 8 | 0 |
| Revery (Bing Thom) | 7 | 0 |
| Arcadis IBI Group | 6 | 0 |
| DIALOG | 6 | 1 |
| Perkins + Will | 6 | 0 |
| hcma architecture + design | 5 | 0 |
| Henriquez Partners | 5 | 0 |
| Studio One Architecture | 5 | 0 |
| Francl Architecture | 4 | 0 |

\* KOR's existing warm relationships (DIALOG, Chris Dikeakos) are now correctly surfaced after the
mislabeled "KOR Structural Engineers" competitor row was merged into the KOR anchor (migration 146).

## 5. Competitors (structural engineering — who we displace)
*De-duplicated — Glotman Simpson (6 split rows), WHM (5), and the KOR-own competitor mislabel are now merged.*

| Competitor SE | LM projects |
|---|---:|
| **Fast + Epp** | 6 |
| **Glotman Simpson** | 5 |
| **WHM Structural** | 4 |
| **KOR Structural** (us) | 3 |
| RJC Engineers | 2 |
| Stantec / StructureCraft / Weiler Smith Bowers / Bush, Bohlman / Miskimmin | 1 each |

**Read:** Fast + Epp is the market leader and is embedded with the highest-value Primes — the primary
displacement target. Glotman Simpson owns a cluster of the marquee architect relationships. The SE edge
is only 3.3% populated, so these counts are the visible tip; deepening §8's SE research will complete
the displacement map. KOR's 3 active foot-ins (DIALOG, Chris Dikeakos) are defensible talking points.

## 6. Live pursuits (PURSUE / PURSUE_URGENT — the meeting targets)
| Project | Verdict | $M | Architect |
|---|---|---:|---|
| Roberts Bank Terminal 2 (RBT2) | **PURSUE_URGENT** | 3,000 | — |
| Operations & Maintenance Centre #5 | PURSUE | 1,000 | — |
| UBC Faculty of Medicine — Medicine One | PURSUE | 680 | — |
| UBC Lower Mall Student Housing Redevelopment | **PURSUE_URGENT** | 560 | Ryder / 3XN |
| SFU School of Medicine (Surrey) | PURSUE | 520 | Stantec |
| VGH West 12th Ave Tower | PURSUE | 400 | Musson Cattell Mackey |
| Plant & Animal Health Centre Replacement | PURSUE | 400 | — |
| Brentwood Block Condominium | PURSUE | 300 | Perkins + Will |
| Olympic Village Elementary — New School | **PURSUE_URGENT** | 150 | McFarland Marceau |

## 7. Key contacts (the actionable layer — who to call)
After this week's enrichment + the hygiene pass (17 fabricated firm/role "contacts" cleared):
- **Hunter (verified, ~95% conf):** use directly for cold outreach.
- **asis (pre-existing clean):** use directly.
- **PatternInferred (constructed, conf 55):** treat as "likely — verify before cold outreach."

**Known contact-quality caveat (in progress):** ~20 high-confidence contacts are still mis-affiliated to
the wrong firm (e.g. Concord Pacific staff filed under W.T. Leung; Grosvenor staff under Hariri Pontarini).
These are queued for a verified re-homing pass and should be spot-checked before using a contact whose
email domain doesn't match the firm.

## 8. Data depth & what to buff up (the enrichment roadmap)
This report doubled as the system gap-map. The hygiene items are now **fixed**; the enrichment items remain:

| Item | Status | Detail |
|---|---|---|
| **Canonical dedup** | ✅ **Fixed** | VSB / Richmond SD / North Van SD / Fraser Health / Glotman / WHM splits merged. |
| **KOR foot-in mislabel** | ✅ **Fixed** | "KOR Structural Engineers" competitor row merged into the KOR anchor — surfaces KOR's real relationships + 95-project history. |
| **Region tagging** | ✅ **Fixed** | 1,938 BC/AB rows back-filled from municipality (migration 150); LM scope now complete. |
| **Org-name contamination** | ✅ **Fixed** | 39 award-letter-boilerplate names cleaned (migration 148). |
| **Fabricated contacts** | ✅ **Fixed** | 17 firm/role records' fabricated emails cleared (migration 149). |
| **Structural-engineer edge (§5)** | 🔲 **#1 priority** | Only **3.3%** of LM projects have an SE. Research the SE per project (project-teams stream). Unlocks the full displacement map. |
| **Composite team rows** | 🔲 Open | ~51 multi-firm rows ("design; AOR; SE") hold team intel in one field — decompose into per-firm role edges (also feeds the SE edge). |
| **Wrong-firm contacts** | 🔲 Open | ~20 high-confidence contacts mis-affiliated; verified re-homing pass. |
| **Owner project value** | 🔲 Open | School-district $ fields empty; backfill estimated value. |
| **US-tagged rows** | 🔲 Open | ~100 US-west-coast rows mis-tagged Province=BC; correct after a US-format decision. |

**Bottom line for Friday:** the market map, owner landscape, Prime targets, competitor incumbency, and
live pursuits now run on **clean, de-duplicated data** — safe to present. The two things that would make
this *best-in-class* are the **SE-per-project edge** (completes the displacement story) and
**owner-side decision-maker contacts** (makes every pursuit actionable).
