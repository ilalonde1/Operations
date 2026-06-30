% BC Public-Sector Seismic Upgrade Procurement — Programs, Pipelines & KOR Playbook
% KOR Structural — Business Development
% Prepared 2026-06-17

---

**Prepared for:** Ian Lalonde / KOR Structural BD
**Date:** 2026-06-17
**Basis:** Three deep-research passes + **a primary-source re-verification pass (2026-06-17) in which the load-bearing claims were read directly from the original gov.bc.ca documents** (SPIR Guidelines, SMP Progress Report, gov SMP page, PHSA, e-RISP, CEPF) + a direct audit of the Biz Brain ingestion code. Confidence levels and gaps are flagged throughout. **Status:** headline claims primary-verified; EGBC SRG training now confirmed (**$689, 7 CE hrs, online, SRG2023** — EGBC Knowledge Centre, 2026-06-17). Only minor sub-items remain (whether course completion auto-adds the firm to the access list/roster; recertification cadence).

> **Why this document exists:** Biz Brain flagged that KOR is not registered on BC's school Seismic Upgrade (Seismic Mitigation) Program. This report (1) maps that program and every other public-sector seismic channel, (2) answers whether Biz Brain is already ingesting the project pipelines, (3) gives a requirements matrix, talking points, and actionables.

---

# 1. Executive Summary

**There is no single "BC seismic program" to register for — there are five separate channels**, each with a different owner, a different door, and a different fit for KOR. The naïve thesis ("seismic is KOR's specialty → register once → win schools") fails on two counts: registration is channel-by-channel, and on schools specifically KOR is a *challenger with zero K-12 track record*, not an incumbent.

**The five headline findings:**

1. **Schools (SMP): the structural engineer is the PRIME consultant, not the architect's sub.** On a school seismic *study* (the SPIR) the School District hires the structural engineer directly as prime; the SE retains and coordinates the architectural/mechanical/electrical sub-consultants. The door is **EGBC SRG training** — mandatory, and it gates access to the guidelines, the seismic analyzer, and the province-wide school-risk database. This is *not* a BC Bid signup; it is a firm-level step KOR controls, and it opens a published pipeline of **247 remaining schools**.

2. **Health is the best fit for KOR's real strength** (high-importance / post-disaster) **and it has a joinable roster** — PHSA and Fraser Health both run pre-qualified "Engineers" consultant lists. But Bush Bohlman owns the billion-dollar hospital capital builds (P3 / design-build via Infrastructure BC). KOR's realistic entry is the **roster path** (smaller assessments/upgrades), not the mega-projects.

3. **Bridges (MOTI) is a real, funded, centralized program — but it is NOT KOR's lane.** It demands bridge-specific seismic experience to CSA S6:19. KOR is a buildings firm. Flag and de-prioritize.

4. **Post-disaster buildings (fire halls, EOCs) are KOR's verified sweet spot** — but there is **no central program, no roster, and no dedicated funding stream.** Fragmented across municipalities and provincial Real Property; CEPF funding is flood/emergency-prep-oriented and flows to governments, not engineers. A relationship + BC-Bid-monitoring game.

5. **Post-secondary** is institution-led: register on each institution's portal (UBC = Bonfire; others via BC Bid). No central roster, no named seismic program.

**The single most valuable operational finding:** Biz Brain scrapes **live tenders** broadly — including health-authority and **UBC Bonfire** feeds (~35 Bonfire portals + ~30 municipal bids&tenders) — so a posted RFP from Fraser Health, Island Health or UBC does come in. But it ingests **none of the forward seismic *pipelines*** (see §2): the **247-school SMP list** and MOTI's bridge program have **no source**, so they're invisible until (and unless) something tenders. The highest-leverage build is to feed that 247-school list in directly.

**The one move that matters most this quarter:** enrol a KOR structural P.Eng in **EGBC SRG training** and sign the **school-risk-database Confidentiality Agreement**. It is the only channel where a single firm-level action unlocks a large, published, monthly-updated pipeline *and* positions KOR as prime, not passenger.

---

# 2. Are We Already Ingesting These Pipelines? (Biz Brain audit)

**Short answer: we scrape live *tenders* broadly — but not the forward *seismic pipelines*.** Verified against the live source inventory (`BD-Module-GapAnalysis-2026-06-21`) plus the ingestion code.

### What IS wired and running (BC-relevant)

| Source | What it pulls | Catches seismic work? |
|---|---|---|
| `BcBid` / `BcBid_Engineering` | BC Bid tenders (one stream keyword-filtered to **"engineering"**) | At tender time |
| `BC_MajorProjectsInventory` | DataBC major-projects feed (weekly) | **Large projects only** (big seismic *replacements*, not the retrofit list) |
| **~35 Bonfire portals** | Live tender portals incl. **Fraser Health, Island Health, FNHA, UBC, UVic, BCIT** + municipalities/transit *(SFU feed is dead/zero-rows)* | **Yes — live health-authority + UBC tenders do come in** |
| **~30 bids&tenders portals + CivicInfo BC** | BC municipal tender portals | Municipal building/seismic tenders at tender time |
| `CoV_AwardedContracts` | City of Vancouver **awarded** contracts | Backward-looking awards, not open opportunities |
| `GovCanada` / `CanadaBuys` / `SamGov` | Federal / US tenders | Not BC-seismic-specific |

### What is NOT ingested (the gaps that matter)

- **No SMP / Seismic Mitigation Program source.** The Ministry's monthly Progress Report (the authoritative list of **247 future-priority schools**) is fetched by nothing. The only "schools" code is canonical-org dedup of district *names*, a schools *report builder*, and a research-honing prompt — none of it ingests the project pipeline. **This is the big one.**
- **No school-district capital-pipeline source.** The 60 districts' own forward capital plans aren't monitored — we only see a district's project once it's posted as a tender.
- **No MOTI source** (correct to skip — not KOR's lane).
- **Roster NOIs / prequalification windows aren't flagged as such.** The PHSA annual Notice of Intent posts on BC Bid so it *may* surface in that feed, but the platform doesn't specifically track prequalification windows.
- **City of Vancouver forward opportunities** arrive only via Bonfire/bids&tenders; the dedicated CoV feed is awards-only (who *won*).

### Bottom line

We **do** catch live *tenders* from a broad set of portals — including health-authority and **UBC Bonfire** feeds — at the moment they post. What's missing is the **forward seismic pipeline**: the 247-school SMP list and MOTI have no source. So the single highest-leverage build remains an **SMP Progress Report ingestion source** (the 247 schools) — it would turn a published, monthly pipeline into tracked opportunities *months before* any tender. (Scoping/decision deferred to you — this documents the gap, it doesn't assume the build.)

> **Correction note (2026-06-25):** an earlier draft of this section under-counted our scraping (it checked only the seed migration, not the full live source table) and wrongly stated "five of six health authorities are unwired." Per the 2026-06-21 module audit we scrape ~35 Bonfire portals incl. Fraser Health, Island Health, FNHA, UBC and UVic. Corrected above.

---

# 3. The Five Channels — Detailed

## Channel A — K-12 Schools: Seismic Mitigation Program (SMP)

| Aspect | Finding | Confidence |
|---|---|---|
| Owner / funder | Ministry of Infrastructure (formerly Min. of Education & Child Care, Capital Branch). Fully provincially funded. EGBC is the Ministry's technical advisor. | High |
| Origin / envelope | Launched March 2005, 15-yr / $1.5B; cumulative spend now >$1.9–2B. | High |
| Who implements | Funded *through* the School District Boards — districts own buildings and run projects. | High |
| Scale (May 2026) | **233 completed · 7 under construction · 8 proceeding to construction · 3 business-case · 247 future priorities · 498 total** (verbatim from the May-2026 SMP Progress Report). ~Half the program ahead. | High (primary-verified) |
| Prioritization | UBC performance-based risk assessment; SRG-trained P.Engs assess each school and assign a seismic risk rating (H1/H2/H3 priority blocks). | High |
| SE procurement | **School District engages the SE directly as PRIME** for the Seismic Project Identification Report (SPIR), the mandatory first study — *"The prime consultant for an SPIR shall meet the following requirements: (a) structural engineering consultant."* The SE *"shall retain and coordinate"* the A/M/E + geotechnical + cost-consultant team; A/M/E contributions vary by block size (fees per Appendix D — there is **no fixed % cap**). The prime must also be a *registered user of the BC Seismic Retrofit Program Database*. **Post-SPIR:** at detailed-design/construction the prime typically reverts to the **architect**, SE as structural sub (the "SE-led-through-completion" claim did not hold). | High (primary-verified) |
| **THE GATE** | **EGBC SRG training is mandatory.** Two independent primary confirmations: the SPIR Guidelines require the prime's PM/PE to have completed the SRG training *"as verified by a signed attestation"*; and EGBC states *"Only firms who have had one or more registrants trained on the current SRG are granted access to the guidelines and seismic performance analyzer."* **The course (confirmed in the EGBC Knowledge Centre, 2026-06-17): "Seismic Retrofit Guidelines 2023 Edition (SRG2023)" — online video, 7 CE hours, $689/registrant, tagged Seismic School Program / Structural.** Database access additionally needs a signed Confidentiality Agreement to EGBC. | High (primary-verified) |
| Technical review | **All SPIRs and PDRs are reviewed by the APEGBC/EGBC-administered Technical Review Board (TRB) before Ministry acceptance**; a SPIR is "formally complete" only after TRB cost-reviewer + TRB Panel-Lead sign-off. *(Notable: SPIR submissions are administered via a `bushbohlman.com` desk — Bush Bohlman, the healthcare incumbent, staffs the TRB review.)* | High (primary-verified) |
| Roster | EGBC publishes a "Seismic Retrofit Engineering Firms" / "Seismic Retrofit Companies" list of SRG-trained firms — de facto prequalification roster (incumbents: RJC, Fast+Epp). | Medium |
| SRG | Seismic Retrofit Guidelines, current 3rd-generation edition (the 2016 SPIR Guidelines cite "SRG2"; the *current* edition governs); mandatory on all SMP work; APEGBC–UBC origin, peer-reviewed by BC + California committees. | High |

> **Re-verification note (2026-06-17):** the SE-as-prime, SRG-mandate, SRG-training-attestation, TRB-review-gate, and the 233/247/498 counts were all re-checked **directly against the primary gov.bc.ca source documents**. This corrected two items the automated research pass had wrongly *refuted* (the SRG training/attestation prerequisite and the TRB review gate are **real**), and removed an unverified "25% A/M/E cap."

## Channel B — Health / Hospitals

| Aspect | Finding | Confidence |
|---|---|---|
| Owner / funder | Health authorities (PHSA, Fraser, Vancouver Coastal, Island, Interior, Northern) + Ministry of Health. | High |
| Two procurement paths | (1) **Pre-qualified consultant rosters** for ordinary engagements; (2) **Infrastructure BC two-step RFQ→RFP / P3 / design-build** for major capital, where the SE rides a short-listed prime/design-build team. | High |
| Roster — how to join | **PHSA Supply Chain maintains the pre-qualified consulting-org list on behalf of the health authorities** (i.e. it is effectively *central/shared* — getting on the PHSA list is the main door, not six separate ones); join via an **annual Notice of Intent (NOI) on BC Bid**, then complete the supplier application. **Fraser Health** also refreshes its own lists annually across 16 categories incl. a standalone **"Engineers"** list; monitor BC Bid + Bonfire; **selection by weighted score, not lowest bid.** | High |
| Technical | Hospitals are high-importance / post-disaster category under NBCC/BCBC (elevated importance factor; **BCBC 2024 raised seismic demands**). Established code practice. | Established practice |
| Named seismic program? | No standalone "seismic" program — seismic upgrades are embedded in health capital projects. | — |
| KOR fit | **Strong** (high-importance is KOR's verified strength) — but **Bush Bohlman owns the major capital** ($4B+). Realistic entry = the **PHSA central list** + Fraser **Engineers roster**. | — |

*Closed: PHSA list is central/shared across authorities, so it covers VCH/Island/Interior/Northern in practice. Fraser additionally runs its own.*

## Channel C — Bridges & Highways (MOTI)

| Aspect | Finding | Confidence |
|---|---|---|
| Owner / funder | Ministry of Transportation & Transit (MOTT, formerly MOTI). | High |
| Named program | **Yes** — bridge seismic retrofit program, initiated **1989** (pre-1983 bridges), staged by Seismic Performance Zone, to a 475-yr return event. **~$61M+ invested since 2001** (2016 snapshot; now higher). | High |
| Tendering | Construction on **BC Bid** (MOTT moved to new BC Bid platform Nov 2 2022). **Consultant/engineering services run through the e-RISP registry** (Registry of Indicative Service Providers) — a **pre-qualification roster**: roster-based indicative selection for contracts **under ~$1M**, open **BC Bid** competition **over ~$1M**. Registration is a three-part office profile via BCeID/EGBC, by work category, with an RFEI. | High |
| Technical | BC-registered **P.Eng experienced in seismic bridge design/assessment/retrofit**; design to **CSA S6:19** as amended by **MOTI Vol.1 §4 (Seismic) Supplement**; importance categories **Lifeline / Major-Route / Other** (force multipliers 1.5/1.35/1.25); **Lifeline bridges in SPC 2&3 require independent peer review**; **Seismic Evaluation Report + Seismic Retrofit Strategy Report** required. | High |
| KOR fit | **Weak — not KOR's lane.** De-prioritize unless KOR deliberately builds bridge capability. | — |

## Channel D — Provincial / Municipal / Post-Disaster Buildings

| Aspect | Finding | Confidence |
|---|---|---|
| Owners | Provincial buildings → Min. of Citizens' Services, Real Property Division. Municipal → individual municipalities. Post-disaster (fire halls, police, EOCs, utility) → municipalities / authorities. | High |
| Funding | **No dedicated seismic building-retrofit funding stream.** The **Community Emergency Preparedness Fund (CEPF)** — Province-funded, **UBCM-administered** — is emergency-prep/flood-oriented (8 streams; none seismic retrofit) and grants only to **local governments and First Nations**. Engineers participate as the grantee's consultant. | High (3-0); "no seismic stream" 2-1 (DRR-Climate Adaptation stream being reorganized) |
| Roster / tendering | **Rosters DO exist.** The **City of Vancouver maintains a pre-qualified Professional Engineering Services Consultants roster** (Feb-2024 award, ~57 firms across engineering disciplines, advertised on BC Bid + the CoV portal under Procurement Policy ADMIN-008) — *primary-confirmed the roster exists; a dedicated Structural category / SRG-2023 reference is per the research pass, worth confirming against the category list.* The **Province** uses a **Multi-Use List RFQ (MUL-RFQ)** mechanism to pre-qualify consultants for government work. So Channel D is *not* purely per-project — there are joinable provincial + CoV qualified lists, plus per-project tenders on **BC Bid + municipal portals**. *(Surrey/Victoria current practice not separately confirmed.)* | High (rosters exist); medium (Structural sub-category detail) |
| KOR fit | **Strong capability** (post-disaster fire halls / EOCs = KOR's verified strength) — and now with **concrete roster doors** (CoV engineering-consultant roster + the provincial MUL-RFQ). | — |

*Verified: CoV runs a pre-qualified engineering-consultant roster (Feb 2024, ~57 firms, via BC Bid + CoV); Province uses MUL-RFQ. To confirm: the Structural sub-category + any SRG-2023 reference. Remaining gaps: Surrey/Victoria specifics; any *named* provincial-building seismic program (none surfaced — seismic rides general capital).*

## Channel E — Post-Secondary

| Aspect | Finding | Confidence |
|---|---|---|
| Owner / funder | **Institution-led** (UBC, SFU, UVic, BCIT procure their own). Ministry funds renewal via **five capital programs** (seismic/asset-renewal under **New Priority Investments** + **Routine Capital**). | High |
| Registration | **No central roster, no named seismic program.** **UBC** — register free on **Bonfire** to bid (also on BC Bid). **SFU** — open two-stage competitive bidding via BC Bid/MERX, **no published roster.** | High |
| KOR fit | **Moderate** — relationship-driven, institution-by-institution. UBC Bonfire registration is a low-cost baseline. | — |

---

# 4. Master Requirements Matrix — What KOR Must Fulfill

| # | Channel | Procuring authority | How KOR registers / prequalifies | Where tendered | Hard technical prerequisites | KOR fit | Priority |
|---|---|---|---|---|---|---|---|
| A | **K-12 Schools (SMP)** | School District (funded by Min. Infrastructure) | **EGBC SRG training** → access guidelines + analyzer → sign **Confidentiality Agreement** (risk DB) → EGBC "Seismic Retrofit Companies" list | District engagement of SRG-trained pool (BC Bid posting unconfirmed) | SRG-trained P.Eng; **SE is prime on SPIR** | Challenger (zero K-12 credit), but SE-as-prime entry | **#1** |
| B | **Health / Hospitals** | Health authorities + Min. Health | Join **PHSA** list (annual **NOI on BC Bid**) + **Fraser Health "Engineers"** list (annual; monitor BC Bid + Bonfire) | BC Bid / Bonfire (rosters); Infrastructure BC RFQ→RFP (major capital) | P.Eng; high-importance/post-disaster; weighted-score selection | **Strong** (Bush Bohlman owns mega-builds) | **#2** |
| C | **Bridges (MOTI)** | Min. Transportation & Transit | Register on the **e-RISP roster** (BCeID/EGBC, by category, RFEI) — roster selection <~$1M, BC Bid >~$1M | e-RISP roster + BC Bid | BC P.Eng **bridge-seismic experienced**; CSA S6:19 + Vol.1 §4; peer review (Lifeline); Eval + Strategy reports | **Weak — not KOR's lane** | **Skip / Watch** |
| D | **Provincial / Municipal / Post-Disaster** | Citizens' Services (provincial); municipalities | **Get on the provincial MUL-RFQ qualified list + the City of Vancouver pre-qualified engineering-consultant roster**; register on municipal portals + BC Bid; ride CEPF grantees as consultant | Provincial/CoV rosters; BC Bid + municipal portals | Post-disaster importance category | **Strong capability + concrete roster doors** | **#3** |
| E | **Post-Secondary** | Each institution | Register **UBC Bonfire** (free); monitor BC Bid for others | Institution portals + BC Bid | P.Eng; institution standards | Moderate | **#4 (cheap baseline)** |

**Cross-cutting baseline (all channels):** register as a supplier on **BC Bid** (free) and on **Bonfire**; maintain EGBC **Permit to Practice** + **Designated Structural Engineer** designation where required for high-importance/post-disaster structures.

---

# 5. KOR Fit & Reality Check (internal — load-bearing)

- **SRG training is necessary, not sufficient.** KOR has **zero K-12 school-seismic track record**; districts shortlist on demonstrated school experience. Incumbents on the EGBC roster are **RJC and Fast+Epp**. SRG training is the *entry ticket*; the first reference is best earned via low-stakes **SPIR study work** (SE-as-prime, smaller fee).
- **Health mega-capital is Bush Bohlman's fortress** ($4B+ — Cowichan, Royal Columbian, New Surrey). Compete on the **roster path**, not the P3 mega-builds.
- **KOR's verified strength = heritage + high-importance / post-disaster** (fire halls, EOCs, hospital-adjacent) — mapping to Channels A (challenger), B (roster), D (fragmented). It does **not** map to C (bridges).
- **Founder disclosure** (Bryson / 2019 EGBC consent order over a *seismic* failure) must be managed **proactively** on any seismic-credibility pursuit — counter with current Markulin (CSA A23.3 Cl.21 committee) / Beirne (IStructE Seismic Panel) / Atkinson (PBSD/NEHRP) depth + OQM peer-check.

---

# 6. Talking Points — Friday Meeting

1. **"There's no single seismic program — there are five, and we now know the door to each."**
2. **"On schools, the structural engineer is the *prime* — unusual, and in our favour."** We lead the study, not ride the architect.
3. **"The one immediate move is EGBC SRG training"** — a firm-level step we control; unlocks the guidelines, the analyzer, the province-wide school-risk database, and a pipeline of 247 remaining schools.
4. **"Clear-eyed: on schools we're a challenger, not an incumbent."** RJC and Fast+Epp hold the K-12 record. Earn a first reference via lower-stakes study work; don't over-claim.
5. **"Health fits what we're actually great at"** — high-importance / post-disaster. PHSA and Fraser run joinable engineer rosters. We won't unseat Bush Bohlman on the billion-dollar hospitals, but the roster path gets us in.
6. **"Fire halls and EOCs are our sweet spot — but no tidy program."** Relationship + monitoring game; Biz Brain is the tool to systematize it.
7. **"Bridges we're consciously skipping"** — real MOTI program, different specialty.
8. **"Biz Brain isn't yet watching these pipelines."** Today we only catch a fraction (BC Bid 'engineering' + the biggest Major Projects). The highest-leverage build is to ingest the 247-school SMP list and wire the PHSA/Fraser feeds.

---

# 7. Actionables

**Immediate (firm-level, this quarter):**

- **A1 — Enrol a KOR structural P.Eng in EGBC SRG training** — the **"Seismic Retrofit Guidelines 2023 Edition (SRG2023)"** course in the EGBC Knowledge Centre (**$689, 7 CE hrs, online**). *The #1 move; a same-day decision at that price.* **Note: the training attaches to the individual registrant who takes it (CE hours + signed attestation), so it must be an actual structural P.Eng — ideally the intended SPIR lead — taken on their own EGBC login, not a non-registrant/firm account. Confirm with EGBC whether a firm can assign a seat vs. self-purchase, and whether completion auto-adds KOR to the firm-access list + Seismic Retrofit Companies roster.**
- **A2 — Sign the EGBC school-risk-database Confidentiality Agreement** (email EGBC Manager, Built Environment & Seismic Initiatives) once trained.
- **A3 — Confirm KOR's BC Bid + Bonfire supplier registration** is live and current.
- **A4 — Apply to the PHSA pre-qualified consultant list** (annual NOI on BC Bid) **and the Fraser Health "Engineers" prequalification** (monitor BC Bid + Bonfire).
- **A5 — Register on UBC Bonfire** (free, post-secondary baseline).
- **A5b — Get on the provincial Multi-Use List (MUL-RFQ) qualified list + the City of Vancouver pre-qualified engineering-consultant roster** (the joinable doors for Channel D provincial/municipal post-disaster work). Watch for the re-opening windows on BC Bid / CoV procurement (CoV last awarded Feb 2024).

**Strategy:**

- **A6 — Decide the school-credential path:** solo SPIR-as-prime vs. team with an incumbent for a first reference (tie to founder-disclosure messaging).
- **A7 — Formally de-prioritize bridges (Channel C)** unless a deliberate capability decision is made.

**Biz Brain (on Ian's go-ahead — audit prior art first):**

- **A8 — Build an SMP Progress Report ingestion source** (the 247 future-priority schools → tracked opportunities with district, risk rating, stage). *Highest-leverage data build.*
- **A9 — Wire PHSA + Fraser Health feeds** (and evaluate the other four authorities) so roster NOIs and health seismic tenders are captured, not missed.
- **A10 — Roster-window tracker** for recurring annual application windows (PHSA NOI, Fraser Health prequal) so KOR never misses a re-application.
- **A11 — Deltek cross-reference:** has KOR billed any of the 60 districts / 6 health authorities? Warm relationships = best first targets.
- **A12 — Pursuit briefs** per district / per future-priority school via the existing BD brief feature.

**Follow-up research:**

- **A13 — Gap-closing + primary re-verification COMPLETE (2026-06-17).** Confirmed against source docs: SE-as-prime on SPIR, SRG mandate, SRG training/attestation, TRB review gate, 233/247/498 counts, PHSA central list, e-RISP (<$1M), CEPF (no seismic stream), CoV engineering-consultant roster, **and the SRG training course itself ($689 / 7 CE hrs / online / SRG2023, via EGBC Knowledge Centre).** Minor sub-items remain: whether course completion auto-adds the firm to the access list/roster + recert cadence; CoV Structural sub-category; Surrey/Victoria specifics.

---

# 8. Confidence, Gaps & Open Questions

**Solid (official sources, 2–3 vote confirmed):** SMP governance/funding/scale; SE-is-prime on the SPIR; SRG mandatory + EGBC training gate + Confidentiality Agreement; PHSA & Fraser Health rosters; Infrastructure BC two-step capital path; MOTI named program + technical standards; CEPF authority/eligibility + no seismic stream; post-secondary institution-led + UBC Bonfire / SFU open-bid. **Ingestion audit:** verified directly against the codebase.

**Closed / confirmed (2026-06-17):** post-SPIR prime = **architect-led** (the SE-led-through-completion claim was refuted); MOTI consultant vehicle = **e-RISP pre-qualification roster** (<~$1M roster / >~$1M BC Bid); **PHSA list is central** across the health authorities; **City of Vancouver runs a pre-qualified engineering-consultant roster** (Feb 2024, ~57 firms) and the **Province uses a MUL-RFQ**.

**Corrected on primary re-verification (2026-06-17):** the automated research wrongly *refuted* two claims that the primary SPIR Guidelines in fact **confirm** — (a) the **SRG training + signed-attestation prerequisite** for the prime's PM/PE, and (b) the **TRB review gate** (all SPIRs/PDRs reviewed before Ministry acceptance). Both are real and now relied upon. An unverified **"25% A/M/E fee cap"** was removed (the source says contributions vary by block size, fees per Appendix D).

**Genuinely refuted / not relied on:** SE-prime-*through-completion* on SMP (prime reverts to architect post-SPIR); "MOTI posts all consultant services on BC Bid" (it's the e-RISP roster under $1M); Infrastructure BC "$32.5B/84 projects"; a dedicated RISP bridge category (01-10); a specific MOTI code order-of-precedence stack (superseded by CSA S6:19 + Vol.1 §4).

**Remaining open:** EGBC SRG training **confirmed** ($689, 7 CE hrs, online, SRG2023) — only sub-items left: whether course completion auto-adds the firm to the access list/Seismic Retrofit Companies roster, and recertification cadence; plus Surrey/Victoria current specifics; whether any *named* provincial-building seismic program exists (none surfaced — seismic rides general capital).

---

# 9. Key Sources

- BC SMP program page — gov.bc.ca/.../capital/seismic-mitigation
- SPIR Guidelines (SE-as-prime) — gov.bc.ca capital-planning/seismic-mitigation
- EGBC Seismic Retrofit Guidance / SRG training gate — egbc.ca/registrants/registrant-programs/seismic-retrofit-guidance
- PHSA Becoming a Vendor — phsa.ca/.../supply-chain/information-for-vendors/becoming-a-vendor
- Fraser Health Business Opportunities — fraserhealth.ca/about-us/business-opportunities
- Infrastructure BC Projects (RFQ→RFP) — infrastructurebc.com/projects
- MOTI Bridge Vol.4 Seismic Retrofit Design Criteria + Vol.1 §4 Supplement (CSA S6:19) — gov.bc.ca bridge standards
- MOTI $61M program release — news.gov.bc.ca 2016TRAN0008-000057
- UBCM CEPF — ubcm.ca/cepf
- UBC Doing Business / Bonfire — finance.ubc.ca/doing-business-with-ubc; SFU competitive bidding — sfu.ca/finance/services/procurement
- Post-secondary capital planning (5 programs) — gov.bc.ca post-secondary capital-planning
- EGBC Designated Structural Engineer requirements — egbc.ca/how-to-apply/.../designated-structural-engineer
- **Biz Brain ingestion audit (internal):** `Kor.Opportunities.Data\Ingestion\Providers\BcMajorProjectsInventoryProvider.cs`; `Kor.Opportunities.Core\Ingestion\StructuralRelevanceGate.cs`; `OpportunitySources` migrations 34/63/171; `Kor.Opportunities.Worker\Services\ScheduledJobDefinition.cs`

*Internal cross-references: project_kor_seismic_credential_reality, project_bc_competitor_dominance_map, project_prime_consultant_strategy, project_deltek_bd_fusion.*
