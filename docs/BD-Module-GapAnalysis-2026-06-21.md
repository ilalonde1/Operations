# BD Module Comprehensive Gap Analysis — 2026-06-21

## Scope, method, and caveats

Read-only snapshot taken 2026-06-21 (America/Vancouver). Database queries used `ApplicationIntent=ReadOnly` and `READ UNCOMMITTED`; no importer, dedup, migration, build, or test was run. The only written artifact is this report. “Active MPI” means `RetiredAtUtc IS NULL`. “BD org” means active `CanonicalOrg.Kind IN (Developer, GC, Architect, Competitor, Buyer, KorClient, Subcontractor)`.

The duplicate audit uses the exact iterative suffix list in `CanonicalOrgResolver.NormalizeAggressiveKey` (`inc`, `incorporated`, `ltd`, `limited`, `llp`, `llc`, `lp`, `corp`, `corporation`, `co`, `company`, `architects`, `architect`, `architecture`, `partnership`, `partners`, `group`) after retaining only Unicode letters/digits. This is broader than `NormalizeForFuzzyMatch`, as requested. Candidate survivors below are recommendations, not merge instructions: highest live FK/reference count, then website presence, then lowest id. Legal-family/JV ambiguity still requires human review.

Source health uses a reproducible definition: recent = 7 days; rows produced = `InsertedCount + DuplicateCount + SkippedCount` during 30 days. A source is **DEAD** if it has no successful run, last success is older than 7 days, or produced zero rows in 30 days. A successful empty run is therefore not treated as healthy. Counts can move while scheduled ingestion runs.

## Executive summary — top 10 priorities

1. **Critical — structural pursuit signal is effectively absent.** Only 75/2,374 active MPI rows (3.2%) have `StructuralEngineerCanonicalOrgId`; 45 (1.9%) have any `SeatStatus`; 2,329 (98.1%) have blank seat status. Remediation: make project-team/seat extraction a required, measured stage, with freshness and source provenance.
2. **Critical — sparse seat imports can erase classifications.** `pipeline-seats` reads architect/SE/GC/status at `tools/BdResearchImport/Program.cs:4397-4421`, but SQL directly assigns nullable `SeatStatus`, `KorSeatOpening`, and `SeatConfidence` at `:7500-7502`. Remediation: reject incomplete seat rows or use explicit “clear” semantics plus non-null-preserving updates and an audit history.
3. **Critical — active source health has 21 silent zero-output/no-run gaps.** Of 114 enabled sources, 20 have a recent “successful” run but zero rows in 30 days and `BD Outreach` has never succeeded. These include both provincial MPI feeds, four California project feeds, five tender portals, five award portals, and four Bonfire feeds. Remediation: alert on candidate/output counts and schema/selector canaries, not process success alone.
4. **Critical — the only permit feed is stalled and Vancouver-only.** All 50,811 permits have `City='Vancouver'`; the sole active `PermitSource` reports `Content-Length 81,468,938 exceeds configured limit 52,428,800` at its 2026-06-21 poll. Remediation: page/stream Vancouver and add Surrey, Calgary, Edmonton, and Victoria adapters under human-gated source onboarding.
5. **Major — provider DTOs cannot carry the highest-value fields.** `OpportunityCandidate` exposes title/buyer/location/dates/value but no architect, GC, SE, owner contact, or buyer contact (`Kor.Opportunities.Core/Ingestion/OpportunityCandidate.cs:14-47`). Remediation: introduce typed project-team/contact observations with provenance; do not overload description/raw JSON.
6. **Major — resolver still creates suffix variants.** The resolver fast path uses strict `NormalizeName` only (`CanonicalOrgResolver.cs:85,121`) and creates at `:124-148`; aggressive/fuzzy methods at `:226-254` and `:398-449` are not called. Current residue: 76 clusters/153 active BD orgs. Remediation: add a unique reviewed match-key/alias candidate stage before creation, with collision quarantine rather than blind fuzzy merge.
7. **Major — MPI SE/GC columns lack FKs and already contain orphans.** The DB FK graph omits both columns although dedup lists them; 7 structural and 9 GC references across retired MPI rows point to absent org ids (examples MPI 115, 257, 283). Remediation: repair the 16 references, then add validated FKs and keep the dedup coverage guard.
8. **Major — research-import schemas are materially lossy and inconsistent.** There are 48 `--only` tags (`Program.cs:247-354`). Examples: `bc-dev` persists only proponent+architect (`:1383-1404`); `indigenous` reads SE but writes it into schedule notes, not the SE columns (`:1204-1245`); only dedicated seat/team branches reliably persist the seat. Remediation: one versioned project/org contract with required high-value fields and per-field reject metrics.
9. **Major — geographic blind spots are measurable.** Active MPI: BC 1,425, CA 556, AB 317, OR 58, WA 17, Yukon 1, NWT 0. Opportunities contain 0 NWT/Yukon and 0 rows in the defined northern-city probe. Remediation: add OpenNWT/GNWT, Yukon Bids & Tenders, Infrastructure BC, Northern Health, and First Nations project/procurement sources.
10. **Major — canonical kind contamination affects live projects.** 45 active Vendor/Unknown orgs are used 49 times as MPI proponent/architect/GC/SE. Remediation: role-derived kind review at ingestion, allowing multi-role evidence but preventing Vendor/Unknown from becoming the durable project-role classification.

# 1. Duplicate and data-quality landscape

## 1.1 Fuzzy/aggressive duplicate clusters

**Major. Finding:** 76 clusters contain 153 active BD orgs. `CanonicalOrgResolver` itself documents the suffix-variant cycle at `CanonicalOrgResolver.cs:214-224`, but the live resolve path does not call that method. **Remediation:** expose an indexed reviewed match key, check it before create, quarantine multi-hit keys, and require a human-approved survivor for existing clusters.

Top 30 clusters (★ = recommended survivor; `refs` counts MPI roles + Opportunities buyer + BuildingPermit roles):

| # | Key | Members (id · kind · name · refs) |
|---:|---|---|
| 1 | lwpac | 69436 Architect “LWPAC Architects” 0; ★75918 Architect “LWPAC” 1; 76602 Architect “LWPAC Architecture” 1 |
| 2 | afcconstruction | ★70594 GC “AFC Construction” 2; 75498 GC “AFC Construction Ltd.” 0 |
| 3 | peterson | ★161 KorClient “Peterson Group” 6; 76073 Buyer “Peterson” 0 |
| 4 | pembinapipeline | ★69632 Buyer “Pembina Pipeline Corp.” 13; 75643 Developer “Pembina Pipeline Corporation” 0 |
| 5 | omicronarchitectureengineeringconstruction | ★69968 Architect “Omicron Architecture Engineering Construction” 32; 76743 Architect “… Ltd.” 0 |
| 6 | olsonkundig | ★38970 Architect “Olson Kundig” 1 (website); 76579 Architect “Olson Kundig Architects” 1 |
| 7 | milleniumdevelopment | 76076 Buyer “Millenium Development Corp.” 0; ★76614 Buyer “Millenium Development Group” 1 |
| 8 | mgba | 76161 Architect “MGBA Inc.” 1; ★76607 Architect “MGBA Architecture Inc.” 2 |
| 9 | mcm | ★26926 Architect “MCM Partnership” 12; 76153 Architect “MCM Partnership Architects” 0 |
| 10 | matthewswest | 57 KorClient “Matthews West” 0 (website); ★76583 Buyer “Matthews West Ltd.” 1 |
| 11 | lmn | ★68695 Architect “LMN Architects” 4; 75563 Architect “LMN” 2 |
| 12 | level10construction | 75159 GC “Level 10 Construction” 0 (website); ★76997 GC “LEVEL 10 CONSTRUCTION LP” 4 |
| 13 | lamoureux | ★69289 Architect “Lamoureux Architect” 7; 76604 Architect “Lamoureux Architect Inc.” 1 |
| 14 | knappettprojects | ★69208 GC “Knappett Projects” 4; 75496 GC “Knappett Projects Inc.” 4 (tie broken by id) |
| 15 | jawlproperties | 52031 Developer “Jawl Properties Ltd.” 1; ★75504 Developer “Jawl Properties” 2 |
| 16 | jameskmcheng | 69251 Architect “James KM Cheng Architects” 3; ★69676 Architect “James K. M. Cheng Architects Inc.” 18 |
| 17 | pghconsultingservices | ★69276 GC “PGH Consulting Services” 2; 76533 GC “… Ltd.” 1 |
| 18 | iredale | ★69703 Architect “Iredale Group Architecture” 64; 76610 Architect “Iredale Architecture” 2 |
| 19 | pomerleau | ★13537 GC “Pomerleau Inc.” 5; 75804 GC “Pomerleau” 2 |
| 20 | ronhart | ★71330 Architect “Ron Hart Architecture” 18; 76219 Architect “Ron Hart Architect Ltd” 1 |
| 21 | wtleung | ★54241 Architect “W.T. Leung Architects” 10; 76699 Architect “WT Leung Architects Inc.” 1 |
| 22 | wrnsstudio | ★32418 Architect “Wrns Studio Architecture” 3; 76893 Architect “WRNS Studio” 1 (website) |
| 23 | westbank | 70911 Developer “Westbank Corp.” 4; ★75642 Developer “Westbank” 8 |
| 24 | wallfinancial | 54601 Buyer “Wall Financial Group” 1; ★69250 Developer “Wall Financial Corporation” 11 |
| 25 | w5arbutusholdings | 54917 Developer “W5 Arbutus Holdings Ltd.” 1; ★55114 Developer “W5 Arbutus Holdings” 1 (website) |
| 26 | truebeckconstruction | 75152 GC “Truebeck Construction” 0 (website); ★76996 GC “TRUEBECK CONSTRUCTION, INC.” 1 |
| 27 | transcanada | ★69723 Buyer “TransCanada Corp.” 5; 76109 Buyer “TransCanada Corporation” 0 |
| 28 | townlineventures | ★69257 Buyer “Townline Ventures” 5; 76151 Buyer “Townline Ventures Ltd.” 0 |
| 29 | townlinehomes | ★105 KorClient “Townline Homes Inc.” 5; 75502 Developer “Townline Homes” 0 |
| 30 | townline | 70674 Developer “Townline” 1; ★76063 Buyer “Townline Group” 3 |

## 1.2 Mislabeled project-role organizations

**Major. Finding:** 45 distinct active Vendor/Unknown orgs occupy 45 role buckets and 49 live MPI role occurrences. Suggested kind is role-derived (Proponent→Developer, Architect→Architect, GeneralContractor→GC, StructuralEngineer→Competitor); public/institutional proponents may instead merit Buyer after review. **Remediation:** generate a review queue from live role use, retain evidence for multi-role firms, and block new durable Vendor/Unknown role links.

Top 30 by role occurrences:

| Org id | Name | Current | Role (uses) | Suggested |
|---:|---|---|---|---|
| 77005 | HILL/HO/PANG/NGUENPHUC/PANG CHAD NGUYEN | Unknown | Proponent (2) | Developer; likely malformed/person string—review |
| 4906 | CityView | Vendor | Proponent (1) | Developer |
| 11799 | Microsoft Corporation | Vendor | Proponent (1) | Developer |
| 28259 | Vancouver Board of Parks and Recreation | Vendor | Proponent (1) | Buyer/Developer review |
| 47390 | Sun Life Assurance Company of Canada | Vendor | Proponent (1) | Developer |
| 57917 | STANFORD UNIVERSITY | Vendor | Proponent (1) | Buyer/Developer review |
| 69757 | Raffi Architecture | Unknown | Architect (1) | Architect |
| 70559 | TKI Construction | Unknown | GeneralContractor (1) | GC |
| 70624 | 1287572 BC Ltd (Nurinder Singh) | Unknown | Proponent (1) | Developer |
| 70736 | Differential Building Group | Unknown | GeneralContractor (1) | GC |
| 70853 | Deveraux Group | Unknown | Proponent (1) | Developer |
| 75587 | Wesbild | Unknown | Proponent (1) | Developer |
| 75588 | Beedie Living | Unknown | Proponent (1) | Developer |
| 75593 | Adera | Unknown | Proponent (1) | Developer |
| 75596 | Conwest | Unknown | Proponent (1) | Developer |
| 75634 | Matullia (Esquimalt + Songhees) | Unknown | Proponent (1) | Developer |
| 75817 | MGA / Michael Green Architecture | Unknown | Architect (1) | Architect |
| 76675 | District Central Saanich | Unknown | Proponent (1) | Buyer/Developer review |
| 76678 | Nilhts’I Ecoener Energy Corp | Unknown | Proponent (1) | Developer |
| 76681 | School District 39 | Unknown | Proponent (1) | Buyer/Developer review |
| 76700 | Wesgruop | Unknown | Proponent (1) | Developer; typo/duplicate review |
| 76702 | School District 43 | Unknown | Proponent (1) | Buyer/Developer review |
| 76715 | Austeville Properties | Unknown | Proponent (1) | Developer |
| 76723 | AP Group | Unknown | Proponent (1) | Developer |
| 76726 | Vanprop Investments Ltd. | Unknown | Proponent (1) | Developer |
| 76734 | The Village of Nakusp | Unknown | Proponent (1) | Buyer/Developer review |
| 76736 | MK Delta Lands Group Inc. | Unknown | Proponent (1) | Developer |
| 76741 | Vancouver Rowing Club | Unknown | Proponent (1) | Developer |
| 76990 | ANTHONY CORRALES AND MARICELDA CORRALES | Unknown | Proponent (1) | Developer or person cleanup |
| 76991 | J3C Group, LLC | Unknown | Proponent (1) | Developer |

## 1.3 Name integrity

**Major. Finding:** a deterministic active-row probe finds 243 repeated legal-token concatenations, 85 explicit/compound JV-like strings, and 291 in their union. Criteria: at least two `Authority`, `Ltd`, or `Inc` tokens (or three legal tokens total), plus explicit `joint venture`/`JV` or a separator on repeated-legal-token names. This is a conservative triage count, not a claim that all 291 are invalid. Examples: id 794 concatenates seven health bodies; 1635 `Acciona/Pacer Joint Venture`; 2923 includes award prose; 5995 combines two Dell entities; 9458 combines JV plus both members; 11351 joins two surveyors. **Remediation:** split compound entities into a JV canonical plus member relationships; reject narrative-length names; preserve raw source text in alias/observation records.

Evidence examples: `794 Fraser Health AuthorityInterior Health AuthorityNorthern Health AuthorityProvidence Health CareProvincial Health Services Authority (Incl. BCCSS)Vancouver Coastal Health AuthorityVancouver Island Health Authority`; `1687 Acoustical Ceiling & Building Maintenance Ltd. (Acoustical & Total Cleaning Services Ltd.)`; `2923 Aztec Renovations & Refit Inc. ... Based on the best value ...`; `5995 Dell Canada Inc. & Dell Financial Solutions Inc`; `6173 Dialog/Al-Terra Joint Venture`; `6751 EDWARDS LIFESCIENCES ... / SCIENCES DE LA VIE ...`; `8368 Graham Infrastrucutre, a JV`; `8957 Highland Moving ... NRFP Awarded to ...`; `10038 Johnson Controls Inc.(Tyco Integrated Fire & Security Canada Inc)`.

## 1.4 Barren active BD orgs

**Major. Finding:** rows below have `Website IS NULL` and are not explicitly marked `WebSearchNotFound:%`. **Remediation:** prioritize enrichment by live project references and kind; record attempted/not-found status so backlog is measurable.

| Kind | Active | Barren | % |
|---|---:|---:|---:|
| Subcontractor | 2,292 | 2,236 | 97.6% |
| GC | 4,251 | 3,618 | 85.1% |
| Competitor | 751 | 603 | 80.3% |
| Developer | 2,452 | 1,804 | 73.6% |
| Architect | 1,168 | 806 | 69.0% |
| Buyer | 1,818 | 1,192 | 65.6% |
| KorClient | 216 | 41 | 19.0% |

# 2. Ingestion coverage and source health

## 2.1 Enabled-source health

**Critical. Finding:** 114/117 configured sources are enabled. Under the stated health rule, 21/114 (18.4%) are dead: one never-successful and 20 recent-success/zero-output. No enabled source is stale beyond seven days. “OK” means recent success and nonzero 30-day output; it does not prove semantic completeness. **Remediation:** persist fetched/candidate/accepted/update counts separately, alarm on zero candidates and field-null-rate shifts, and add selector/schema contract checks. Provider-name matching accounts for run names such as `Name (Provider)` and `Awards: Name (Provider)`.

Type legend: 1 GenericCsv; 2 GenericJson; 3 RSS; 5 GraphEmail; 6 SAM.gov; 7 manual; 8 BC Bid; 9 BC Bid awards; 10 bids&tenders; 11 APC; 12 bids&tenders awards; 14 BC Bid unverified; 15 BC Bid historical; 17 GenericJsonAward; 18 MPI. S/F is lifetime successful/failed runs; rows are 30-day inserted+duplicate+skipped.

| Enabled source | Type | Last success | S/F | 30d rows | Health |
|---|---:|---|---:|---:|---|
| AB_MajorProjectsInventory | 18 | 2026-06-21 | 5/0 | 0 | **DEAD zero rows** |
| APC_AllBuyers | 11 | 2026-06-21 | 131/0 | 1,160 | OK |
| BC_MajorProjectsInventory | 18 | 2026-06-21 | 6/3 | 0 | **DEAD zero rows** |
| BcBid | 8 | 2026-06-21 | 832/308 | 122,745 | OK |
| BcBid_Engineering | 8 | 2026-06-21 | 427/301 | 64,050 | OK |
| BcBidAwards | 9 | 2026-06-20 | 34/3 | 22,500 | OK |
| BcBidHistorical | 15 | 2026-06-18 | 13/1 | 37,052 | OK |
| BcBidUnverified | 14 | 2026-06-20 | 35/0 | 4,020 | OK |
| BCGovNewsRss | 3 | 2026-06-21 | 20/0 | 190 | OK |
| BD Outreach | 7 | — | 0/0 | 0 | **DEAD no success** |
| BdAlertsMailbox | 5 | 2026-06-21 | 5,825/3 | 6,542 | OK |
| BidsTenders_Abbotsford | 10 | 2026-06-21 | 124/0 | 752 | OK |
| BidsTenders_Burnaby | 10 | 2026-06-21 | 126/0 | 1,756 | OK |
| BidsTenders_Campbellriver | 10 | 2026-06-21 | 103/0 | 428 | OK |
| BidsTenders_Camrose | 10 | 2026-06-21 | 125/0 | 103 | OK |
| BidsTenders_Cityofgp | 10 | 2026-06-21 | 103/0 | 654 | OK |
| BidsTenders_Cochrane | 10 | 2026-06-21 | 125/0 | 0 | **DEAD zero rows** |
| BidsTenders_Comoxvalleyrd | 10 | 2026-06-21 | 103/0 | 609 | OK |
| BidsTenders_Coquitlam | 10 | 2026-06-21 | 125/0 | 0 | **DEAD zero rows** |
| BidsTenders_CountyGP | 10 | 2026-06-21 | 124/0 | 167 | OK |
| BidsTenders_DNV | 10 | 2026-06-21 | 124/0 | 598 | OK |
| BidsTenders_EPCOR | 10 | 2026-06-21 | 124/0 | 786 | OK |
| BidsTenders_Leduc | 10 | 2026-06-21 | 124/0 | 286 | OK |
| BidsTenders_Lethbridge | 10 | 2026-06-21 | 124/0 | 1,212 | OK |
| BidsTenders_MapleRidge | 10 | 2026-06-21 | 124/0 | 460 | OK |
| BidsTenders_MetroVancouver | 10 | 2026-06-21 | 124/0 | 2,488 | OK |
| BidsTenders_Nanaimo | 10 | 2026-06-21 | 124/0 | 390 | OK |
| BidsTenders_Okotoks | 10 | 2026-06-21 | 124/0 | 563 | OK |
| BidsTenders_PortCoq | 10 | 2026-06-21 | 125/0 | 0 | **DEAD zero rows** |
| BidsTenders_PortMoody | 10 | 2026-06-21 | 7/0 | 49 | OK |
| BidsTenders_PrinceGeorge | 10 | 2026-06-21 | 124/0 | 529 | OK |
| BidsTenders_Rdco | 10 | 2026-06-21 | 103/0 | 158 | OK |
| BidsTenders_RedDeer | 10 | 2026-06-21 | 125/0 | 1,258 | OK |
| BidsTenders_Richmond | 10 | 2026-06-21 | 124/0 | 972 | OK |
| BidsTenders_Squamish | 10 | 2026-06-21 | 124/0 | 457 | OK |
| BidsTenders_StAlbert | 10 | 2026-06-21 | 124/0 | 1,038 | OK |
| BidsTenders_Surrey | 10 | 2026-06-21 | 125/0 | 0 | **DEAD zero rows** |
| BidsTenders_TOL | 10 | 2026-06-21 | 124/0 | 644 | OK |
| BidsTenders_Vernon | 10 | 2026-06-21 | 7/0 | 0 | **DEAD zero rows** |
| BidsTenders_WoodBuffalo | 10 | 2026-06-21 | 124/0 | 1,637 | OK |
| BidsTendersAwards_Abbotsford | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Burnaby | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Camrose | 12 | 2026-06-20 | 34/0 | 2,574 | OK |
| BidsTendersAwards_Cochrane | 12 | 2026-06-20 | 34/0 | 0 | **DEAD zero rows** |
| BidsTendersAwards_Coquitlam | 12 | 2026-06-20 | 34/0 | 0 | **DEAD zero rows** |
| BidsTendersAwards_CountyGP | 12 | 2026-06-20 | 34/0 | 1,320 | OK |
| BidsTendersAwards_DNV | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_EPCOR | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Leduc | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Lethbridge | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_MapleRidge | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_MetroVancouver | 12 | 2026-06-20 | 35/0 | 3,000 | OK |
| BidsTendersAwards_Nanaimo | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Okotoks | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_PortCoq | 12 | 2026-06-20 | 34/0 | 0 | **DEAD zero rows** |
| BidsTendersAwards_PortMoody | 12 | 2026-06-21 | 7/0 | 700 | OK |
| BidsTendersAwards_PrinceGeorge | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_RedDeer | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Richmond | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Squamish | 12 | 2026-06-20 | 34/1 | 3,000 | OK |
| BidsTendersAwards_StAlbert | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Surrey | 12 | 2026-06-20 | 34/0 | 0 | **DEAD zero rows** |
| BidsTendersAwards_TOL | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| BidsTendersAwards_Vernon | 12 | 2026-06-21 | 7/0 | 0 | **DEAD zero rows** |
| BidsTendersAwards_WoodBuffalo | 12 | 2026-06-20 | 34/0 | 3,000 | OK |
| Bonfire_AHS | 3 | 2026-06-21 | 360/0 | 2,747 | OK |
| Bonfire_AthabascaU | 3 | 2026-06-21 | 360/0 | 501 | OK |
| Bonfire_BCIT | 3 | 2026-06-21 | 360/0 | 1,140 | OK |
| Bonfire_BCLC | 3 | 2026-06-21 | 360/0 | 1,024 | OK |
| Bonfire_BCTransit | 3 | 2026-06-21 | 361/0 | 2,163 | OK |
| Bonfire_Burnaby | 3 | 2026-06-21 | 360/0 | 0 | **DEAD zero rows** |
| Bonfire_CapilanoU | 3 | 2026-06-21 | 360/0 | 0 | **DEAD zero rows** |
| Bonfire_CbeAb | 3 | 2026-06-21 | 300/0 | 2,023 | OK |
| Bonfire_CVRD | 3 | 2026-06-21 | 360/0 | 1,661 | OK |
| Bonfire_Epsb | 3 | 2026-06-21 | 299/0 | 2,398 | OK |
| Bonfire_FNHA | 3 | 2026-06-21 | 324/0 | 163 | OK |
| Bonfire_FraserHealth | 3 | 2026-06-21 | 360/0 | 6,818 | OK |
| Bonfire_ICBC | 3 | 2026-06-21 | 360/0 | 1,136 | OK |
| Bonfire_IslandHealth | 3 | 2026-06-21 | 360/0 | 380 | OK |
| Bonfire_Kamloops | 3 | 2026-06-21 | 360/0 | 1,301 | OK |
| Bonfire_Kelowna | 3 | 2026-06-21 | 360/0 | 1,433 | OK |
| Bonfire_KPU | 3 | 2026-06-21 | 360/0 | 165 | OK |
| Bonfire_MacEwanU | 3 | 2026-06-21 | 360/0 | 1,118 | OK |
| Bonfire_Mission | 3 | 2026-06-21 | 360/0 | 979 | OK |
| Bonfire_MountRoyalU | 3 | 2026-06-21 | 360/0 | 290 | OK |
| Bonfire_NAIT | 3 | 2026-06-21 | 360/0 | 2,138 | OK |
| Bonfire_Nelson | 3 | 2026-06-21 | 359/0 | 151 | OK |
| Bonfire_NorthCowichan | 3 | 2026-06-21 | 19/0 | 19 | OK |
| Bonfire_Penticton | 3 | 2026-06-21 | 360/0 | 1,107 | OK |
| Bonfire_PHSA | 3 | 2026-06-21 | 359/0 | 2,932 | OK |
| Bonfire_PrrdBc | 3 | 2026-06-21 | 299/0 | 319 | OK |
| Bonfire_Saanich | 3 | 2026-06-21 | 360/0 | 1,105 | OK |
| Bonfire_SAIT | 3 | 2026-06-21 | 360/0 | 288 | OK |
| Bonfire_SFU | 3 | 2026-06-21 | 360/0 | 0 | **DEAD zero rows** |
| Bonfire_StrathconaCty | 3 | 2026-06-21 | 360/0 | 1,841 | OK |
| Bonfire_TravelAlberta | 3 | 2026-06-21 | 360/0 | 604 | OK |
| Bonfire_UAlberta | 3 | 2026-06-21 | 360/0 | 2,411 | OK |
| Bonfire_UBC | 3 | 2026-06-21 | 361/0 | 3,590 | OK |
| Bonfire_UFV | 3 | 2026-06-21 | 360/0 | 0 | **DEAD zero rows** |
| Bonfire_UVic | 3 | 2026-06-21 | 360/0 | 1,628 | OK |
| Bonfire_VCH | 3 | 2026-06-21 | 360/0 | 348 | OK |
| Bonfire_Victoria | 3 | 2026-06-21 | 361/0 | 4,566 | OK |
| CA_CEQAnet | 18 | 2026-06-21 | 8/0 | 0 | **DEAD zero rows** |
| CA_SanJoseCkan | 18 | 2026-06-21 | 6/0 | 0 | **DEAD zero rows** |
| CA_SocrataSanDiego | 18 | 2026-06-21 | 5/0 | 0 | **DEAD zero rows** |
| CA_SocrataSF | 18 | 2026-06-21 | 7/0 | 0 | **DEAD zero rows** |
| CanadaBuys | 1 | 2026-06-21 | 586/6 | 6,860 | OK |
| CanadaBuysNew | 1 | 2026-06-21 | 453/0 | 116 | OK |
| CivicInfoBC_All | 3 | 2026-06-21 | 362/0 | 11,964 | OK |
| CoV_AwardedContracts | 2 | 2026-06-20 | 29/0 | 2,800 | OK |
| GovCanada_Construction | 17 | 2026-06-20 | 30/5 | 751,093 | OK |
| GovCanada_EngineeringServices | 17 | 2026-06-21 | 27/8 | 674,965 | OK |
| LACity_RAMP_OpenBids | 2 | 2026-06-20 | 25/1 | 6,360 | OK |
| SamGov | 6 | 2026-06-21 | 54/2 | 455 | OK |

**Major. Finding:** “rows produced” semantics are inconsistent. Both MPI providers can update their own tables but return an empty candidate array (`CeqanetMajorProjectsInventoryProvider.cs:115-120` is explicit), so `IngestionRuns` can report zero despite internal work. **Remediation:** require every provider to return a standard metrics object (`fetched`, `parsed`, `filtered`, `inserted`, `updated`, `unchanged`, `failed`); do not infer health from opportunity candidates for direct-writing providers.

## 2.2 Geographic coverage

**Major. Finding:** total/active MPI coverage is BC 6,120/1,425; CA 2,333/556; AB 1,462/317; OR 80/58; WA 50/17; ON 3/0; Yukon 1/1; NWT 0/0. Opportunities: BC 691, blank province 392 (31.0% of 1,266), AB 128, CA 49, AK 3, WA 2, HI 1, NWT 0, Yukon 0. **Remediation:** make province mandatory/derived with a review state; onboard territorial feeds and measure each target geography against expected public-source counts.

Northern probe (active MPI, transparent heuristic): NWT province aliases = 0; Yukon = 1; Northern BC = 67 (`RegionName LIKE '%North%'` or Prince George/Fort St. John/Dawson Creek/Terrace/Kitimat/Smithers/Prince Rupert/Quesnel); Northern AB = 5 (north region or Grande Prairie/Fort McMurray/Peace River/Cold Lake/Slave Lake/High Level). The same city probe finds **0 Opportunities in all four categories**. The city list is not exhaustive; therefore 67/5 are lower-bound probes, not complete northern totals.

## 2.3 Building permits

**Critical. Finding:** confirmed Vancouver-only: 50,811/50,811 `BuildingPermit` rows have `City='Vancouver'` (issued dates 2017-01-02 through 2026-06-03). `PermitSource` contains exactly one active row, id 1, City of Vancouver. Its 2026-06-21 `LastPolledAtUtc` has `LastErrorMessage='Vancouver permits response Content-Length 81468938 exceeds configured limit (52428800).'` **Remediation:** page or stream this endpoint with bounded per-page payloads, alert on last-error/freshness, and add independent adapters/source rows for other cities.

# 3. Extraction completeness — source to persistence

## 3.1 Active provider families

**Major. Finding:** the common opportunity contract is structurally incapable of carrying the pursuit fields KOR values most. `OpportunityCandidate` has title, buyer, location, URL, description, dates, city/province, value and reference (`OpportunityCandidate.cs:14-47`), but no owner/proponent distinction, architect, GC, structural engineer, seat status, contact name/email/phone, or source-field provenance. The downstream Opportunity table does have buyer-contact columns, making this a pipeline—not schema—gap. **Remediation:** add typed, provenance-bearing team/contact observations and provider null-rate metrics; preserve raw payload as evidence, not as the query interface.

| Provider family (enabled sources) | Source fields exposed/read | Structured persistence | High-value loss and evidence |
|---|---|---|---|
| Generic CSV (2) | CanadaBuys title, buyer, location, description, post/deadline, reference; complete row retained as JSON | Opportunity core fields; province derived | City/value explicitly null (`GenericCsvOpportunityProvider.cs:202-215`). No team/contact DTO slots. Raw row mitigates recoverability only. |
| Generic JSON opportunity (2) | Configurable title, buyer, location, description, dates, city, province, value, reference; whole item | Same Opportunity core fields | Mapping surface ends at those fields (`GenericJsonOpportunityProvider.cs:122-156`); any API contact/team fields remain raw only. |
| RSS (39) | item title/link/description/date; buyer inferred from title/feed | Opportunity core/text + raw XML | RSS feeds can surface organization/contact prose but there is no structured contact/team mapping (`RssOpportunityProvider.cs:137-146,186-195`). |
| Graph email (1) | message subject/body/link plus email metadata/attachments via adapter | Opportunity text/observation; raw message | Sender/contact and team entities are not available on `OpportunityCandidate`; therefore not written to buyer-contact/team columns. |
| SAM.gov (1) | notice, department/subtier/office, dates, description, place, plus extension JSON | Opportunity title/buyer/location/city/state/dates/reference; full DTO JSON | API extras are collected in `JsonExtensionData` (`SamGovOpportunityProvider.cs:338-383`) but candidate mapping omits contact/NAICS/type/value (`:246-261`; NAICS/type are declared at `:361-368`). |
| BC Bid open (2) | grid title, buyer, issue/close, solicitation type, commodities, reference | Opportunity core/text | Does not visit/map detail contacts, budget, interested firms, plan takers, or project team; mapping ends at `BcBidScraper.cs:453-480`. |
| Bids & Tenders open (29) | listing bid name/status/closing/link | Opportunity title/buyer/deadline and raw pipe text | Listing-only extraction (`BidsAndTendersScraper.cs:190-219`) drops detail-page contact, documents, estimated value and team clues where portals expose them. |
| APC open (1) | link title, buyer, deadline/reference | Opportunity title/buyer/deadline; minimal pipe raw | Description and posted date are explicitly null (`AlbertaPurchasingScraper.cs:196-207`); no detail-page contact/value/team extraction. |
| BC Bid awards (1) | issuing org/location, contract no., email, value, supplier/address, award date | All listed AwardCandidate fields | Strongest award mapping; contact **email only**, no contact name/phone (`BcBidAwardsScraper.cs:184-213`). No architect/GC/SE role inference. |
| Bids & Tenders awards (25) | awarded listing cells, supplier/value/date where recognized | AwardCandidate plus raw cell text | No typed contact name/phone or project-team roles; generic cell heuristics are selector-sensitive (`BidsAndTendersAwardsScraper.cs:285-323`). |
| BC Bid unverified (1) | bidder, amount/rank/address plus issuing org/opening date | `OpportunityBid` and pending AwardCandidate | Bidder evidence is retained (`BcBidUnverifiedBidResultsScraper.cs:470-497`); issuing-location/contact/contract number are null on pending awards (`:758-778`). |
| BC Bid historical (1) | archived grid plus detail enrichment | Historical opportunity and enrichment payload | Detail enrichment includes estimated amount, but not a normalized architect/GC/SE/seat model (`BcBidHistoricalEnrichmentService.cs:196-203`). |
| Generic JSON award (2) | configurable award ref/title/orgs/value/date/number/source URL; whole item | AwardCandidate | Contact mapping is absent from the configurable mapping even though AwardCandidate can carry email (`GenericJsonAwardProvider.cs:213-241`). This loses government contract contacts structurally. |
| AB MPI (1) | name, municipality/region, developer/owner, schedule, cost, website, details, sector/type/stage | MPI proponent, cost, schedule, geography, raw full row | No architect/GC/SE/seat extraction; proponent is created as `Unknown` (`AbMajorProjectsInventoryProvider.cs:154-190`). Feed currently reports zero 30-day rows. |
| BC MPI (1) | project id/name/description, developer, **architect**, type/stage, municipality/region, cost, website, dates | MPI proponent + architect and other core fields | Captures the source’s named architect (`BcMajorProjectsInventoryProvider.cs:209-281`) but not GC/SE/seat. Feed is discontinued after Q3 2025 per research doc and currently zero-output. |
| CA permit MPI adapters (3) | permit/address/description/type, configured applicant/owner/contractor, architect/design professional, city/county, value, units/stories/status | MPI proponent + architect + valuation/geography; raw row | Collapses applicant/owner/contractor alternatives into one `proponent` (`CaSocrataMajorProjectsInventoryProvider.cs:452-469`) and has no GC/SE/seat/contact fields (`:515-534`). All three are zero-output. |
| CEQAnet MPI (1) | SCH/title, description, document type, county, lead agency, received date | MPI lead agency as proponent, filing metadata/raw | Architect/GC/SE/value/contact not extracted (`CeqanetMajorProjectsInventoryProvider.cs:123-174`); source is zero-output. |
| Manual BD Outreach (1) | Not verifiable: no successful run | None evidenced | Enabled but 0 runs/0 rows. Treat as an unimplemented operational contract, not coverage. |

**Major. Finding:** direct-writing MPI providers returning `Array.Empty<OpportunityCandidate>()` make dispatcher-level success/output misleading, while 21 sources already demonstrate silent emptiness. **Remediation:** unify provider result metrics and include source schema fingerprint, parsed-row count, filtered-row count and high-value field fill rates.

## 3.2 `BdResearchImport` lossiness map

The dispatcher contains 48 tags at `tools/BdResearchImport/Program.cs:247-354`. Legend: **S** = typed/structured destination; **E** = org enrichment JSON plus registered intel extractor where verified; **R** = retained only in MPI `RawJson`/free-text, not the relevant typed column; **D** = read then demoted/dropped from the relevant typed field; “—” = payload does not read it. Fields are Owner, Architect, GC, SE, Seat, Contacts, Focus. This assesses importer code, not whether every input file actually populated its declared fields.

### Project-bearing tags

| Tag(s) | Owner | Architect | GC | SE | Seat | Contacts/focus | Evidence / loss |
|---|---|---|---|---|---|---|---|
| public-sector | S | — | — | — | — | R | Owner/buyer only; architect fields explicitly null (`:1053-1103`). |
| indigenous | S | S | — | **D** | — | E for org rows | Reads SE at `:1204-1207` but places it in `ScheduleNotes` at `:1245`, not SE columns. |
| bc-dev | S | S | — | — | — | R | Only proponent+architect resolved (`:1383-1404`); payload schema has no GC/SE/seat. |
| la; pacnw; sandiego; sacramento; bayarea | S | S | — | — | — | E for org rows | Shared `ImportUsMarketAsync` schema reads owner/architect (`:1442-1623`); no GC/SE/seat typed path. |
| alberta | S | S | — | — | — | E for org rows | Same two-role pattern (`:1632-1792`). |
| institutional | S | S | — | — | — | R | Owner/architect/capital-plan data (`:1801-1954`); no structural-seat path. |
| island-okanagan | S | S | **D** | — | — | E for org rows | Reads GC but stores only `ScheduleNotes='GC: ...'` (`:2233-2281`), leaving GC columns blank. |
| intel-gathering | S | S | S | S | — | — | Correctly resolves all four roles and sets SE/GC extension properties (`:2458-2516`). |
| owner-pipelines | S | S | — | — | — | R | Planned architect only (`:2942-2994`). |
| facility-renewal | S | — | — | — | — | R | Architect explicitly null (`:3820-3868`). |
| capital-plans | S | — | — | — | — | R | Architect explicitly null (`:3937-3995`). |
| projects-honing | S | S | S | S | — | R | Reads all project team names (`:4004-4114`) and updates typed roles; no seat classification. |
| midmarket | S | S | — | S | — | R | Typed owner/architect/SE (`:4174-4230`); GC/seat absent. |
| architect-forecast | — | S | — | — | — | R | Updates likely architect only (`:4239-4334`). |
| pipeline-seats | — | S | S | S | S | R | Full team+seat read (`:4397-4421`); **loss risk:** nullable seat fields overwrite at `:7500-7502`. |
| project-reverify | — | S | — | S | — | R | Typed architect/SE update (`:4519-4538`), but no GC/seat refresh. |
| project-teams | S | S | S | S | — | R | Best team mapping; direct MPI-id backfill only fills nulls (`:7123-7181`). Mechanical/electrical are free text. |
| competitor-projects | S | S | — | S | — | R | Typed owner/architect/SE (`:5053-5108`). |
| structural-pipeline | S | S | — | S | — | R | Typed owner/architect/SE (`:5176-5231`); no seatStatus despite structural focus. |
| indigenous-projects | S | S | — | — | — | R | Typed owner/architect only (`:5300-5350`). |
| capital-funding-signals | S | S | — | — | — | R | Owner/architect/value/procurement window typed/free-text (`:5723-5784`); no GC/SE/seat. |
| seismic-pipeline | S | S | — | S | — | R | Typed SE (`:5875-5939`), but no explicit open/filled seat status. |
| island-okanagan-pairing; lower-mainland-pairing; edmonton-pairing | S | S | S | S | — | R | Shared pairing imports all roles (`:6001-6157`), but ignores a seat-status concept. |
| kor-capability | R/E | R/E | R/E | R/E | — | E | Reads owner/architect/GC and case-study focus (`:4697-4845`) as org enrichment; does not link these projects into MPI typed team fields. |
| bd-tracking | S (CRM buyer) | — | — | — | — | **S contacts** | CRM engagement/contact/activity persistence (`:6171-6183`, `:6342-6363`); project-team fields are outside schema. |
| bd-tracking-crosslink | S link | — | — | — | — | — | Crosslinks CRM to existing projects; does not enrich team/seat fields (`:6438+`). |

### Organization/non-project tags

| Tag | High-value input and destination | Loss assessment |
|---|---|---|
| contractor | website + research/focus → E | Registered as `CompetitorProfileExtractor("ContractorResearch")`; no project-team linkage. |
| prime-targeting | firm targeting/focus → E | Registered `PrimeTargetingExtractor`; no contacts unless separate tag. |
| prime-contacts | contacts → E + IntelPerson/Affiliation | Registered `PersonListExtractor("PrimeContacts")`; structured person path exists. |
| competitor-profiles | competitor focus/capabilities → E | Registered competitor extractor; no automatic project/seat linkage. |
| decision-makers | contacts → E + IntelPerson/Affiliation | Registered `PersonListExtractor("DecisionMakers")`. |
| data-honing | kind/website/data issues → S/E | Updates canonical metadata; not a project/team source. |
| registries | legal identity/website → S/E | No project/team fields expected. |
| indigenous-orgs | nation/kind/website → S/E | No project/team linkage. |
| owner-procurement | owner procurement/focus → E | Useful pursuit context, separate from MPI seat. |
| competitor-signals | competitor capacity/focus → E | No typed project seat. |
| structural-partner-map | recurring SE relationships/focus → E | Registered extractor; valuable but remains architect-centric intel, not project seat truth. |
| displacement-briefs | architect displacement intelligence → S brief | Dedicated typed store; no MPI field update. |
| sub-consultants | discipline/firm/website → S org + E | Registered `SubConsultantExtractor`; no project linkage. |
| industry-events | organizer/audience/relevance → S IndustryEvent | Outside project/team schema. |
| db-contractors | name/HQ/fit → S org + E | No contacts or project linkage in declared input. |
| incumbent-rosters | owner/discipline/incumbent/timing → E | Registered extractor, but does not stamp `StructuralEngineerCanonicalOrgId`/SeatStatus on a project. |

**Major. Finding:** 48 bespoke branches create at least four representations for the same facts: typed MPI role columns, `ScheduleNotes`, raw MPI JSON, and org enrichment/intel. This makes “field present in payload” different from “queryable pursuit signal.” **Remediation:** define one versioned project-research contract (`project identity`, owner, architect, GC, SE, seat status/confidence/as-of/source, contacts) and one org-research contract; validate required fields before DB work and publish per-field accepted/dropped counts.

**Major. Finding:** source-key updates replace every typed value—including rich role values—with nullable parameters (`Program.cs:7220-7270`), while cross-source name matches use gap-filling `COALESCE` (`:7295-7315+`). A sparse rerun of the same branch can therefore erase data even when a different-source match would preserve it. **Remediation:** use field-level provenance/as-of precedence and explicit clear operations; default importer semantics should preserve non-null values.

# 4. Pipeline integrity

## 4.1 Dedup FK coverage versus the live database

**Minor (positive control). Finding:** there are **zero unhandled live FKs** referencing `opportunities.CanonicalOrg`. The live graph has 26 FK columns. `FkTargets` covers 20: ArchitectDisplacementBriefs.ArchitectCanonicalOrgId; BdResearchTriggers.CanonicalOrgId; BuildingPermit Applicant/Contractor/Owner; CanonicalOrgEnrichment.CanonicalOrgId; CrmEngagements.BuyerCanonicalOrgId; IntelProjectAction.TargetCanonicalOrgId; IntelProjectKeyPerson.CanonicalOrgId; KorPursuits Buyer/LostTo; MPI Architect/Proponent; NewsArticleOrgMention.CanonicalOrgId; Opportunities.BuyerCanonicalOrgId; OpportunityAwards AwardedTo/Awarding; OpportunityBids.BidderCanonicalOrgId; OpportunityInterestedFirms.ResolvedCanonicalOrgId; OrgAlias.CanonicalOrgId. `IntelDeleteTargets` covers IntelAction/Narrative/Risk/Signal/Work; `IntelAffiliationRepointTargets` covers IntelPersonAffiliation. Evidence: declarations at `tools/BdCanonicalDedup/Program.cs:60-126`; live-schema guard at `:487-527`. **Remediation:** retain this fail-closed runtime comparison and make its zero-unhandled result part of every merge dry-run report.

**Major. Finding:** `MajorProjectsInventory.StructuralEngineerCanonicalOrgId` and `GeneralContractorCanonicalOrgId` are in `FkTargets` (`Program.cs:67-68`) but are **not FKs in the live DB**, so the guard cannot detect integrity failure on them. There are 7 absent structural ids and 9 absent GC ids across retired MPI rows. Examples: MPI 115 references SE 38934 and GC 69874; 257 GC 69926; 283 SE 69911; 301 SE 69888; 1157 SE 69911; 1305 SE 38926; 1385 SE 69873/GC 69925; 1502 SE 69326/GC 69937. No active MPI row is currently orphaned. **Remediation:** human-review/repair the 16 historical links, validate constraints with existing data, then add real FKs and include them in schema verification.

**Minor. Finding:** FK actions create special merge risks but are explicitly handled: ArchitectDisplacementBriefs is `ON DELETE CASCADE`, and OpportunityBids is `ON DELETE SET NULL`; both appear in `FkTargets`, with collision commentary at `Program.cs:82-101`. **Remediation:** keep regression coverage around collision/repoint order and report affected-row counts before commit.

## 4.2 Resolver duplicate-creation cycle

**Major. Finding:** exact divergence is:

1. Intake cleans and computes **strict** `NormalizeName` (`CanonicalOrgResolver.cs:83-85`).
2. Alias lookup is source-specific and uses the original trimmed input (`:101`).
3. Only strict `FindByNormalizedNameAsync` runs (`:118-121`).
4. A miss immediately creates a canonical and alias (`:124-148`).
5. `NormalizeAggressiveKey` and `NormalizeForFuzzyMatch` exist at `:226-254` and `:398-449` but are not invoked in this path. The latter’s own comment says it is “NEVER” used in resolver lookup (`:381-386`).

Thus `ACI Architecture` and `ACI Architects Inc.` have different strict keys but the same aggressive key; likewise current live examples `LWPAC`/`LWPAC Architecture`/`LWPAC Architects`, `MGBA Inc.`/`MGBA Architecture Inc.`, and `Townline`/`Townline Group`. The comment claiming aggressive normalization prevents the cycle (`:214-224`) is inconsistent with executable control flow. `BdResearchImport` partially works around it with an in-memory aggressive index (`tools/BdResearchImport/Program.cs:512-582`), but other ingestion paths call the resolver directly. Its own BD-tracking comment acknowledges fuzzy matching is not wired and typos create canonicals (`:6175-6178`). **Remediation:** before creation, produce a candidate set from a persisted aggressive key and aliases; auto-match only a single high-confidence, kind-compatible hit; quarantine zero/multi/short-brand collisions for review. Do not use unrestricted fuzzy equality as a unique key.

**Major. Finding:** 76 live clusters remain despite the research importer’s local workaround, proving prevention is path-dependent. **Remediation:** centralize candidate matching in `CanonicalOrgResolver`/store and require all ingestion/import code to use it; instrument “created despite aggressive candidate” as a release-blocking metric.

# 5. Missing sources and pursuit-signal completeness

## 5.1 Research recommendations versus configured sources

Cross-check method: compare all 117 configured names/base URLs with the three 2026-06-21 research documents. Sources already present are not listed as gaps: AB MPI is configured (but zero-output); APC covers part of northern Alberta; CivicInfoBC_All is configured; CanadaBuys covers federal/DND; Fraser Health and Island Health Bonfire feeds exist. RSS tender feeds do **not** substitute for capital-project pages, permit APIs, or early pipeline lists.

**Critical. Finding:** no configured `PermitSource` exists beyond Vancouver, and no `OpportunitySource` matches the recommended permit endpoints. **Remediation:** human-gated onboarding with field contracts and expected-volume tests for:

| Missing source | Type / URL | What it surfaces | Evidence |
|---|---|---|---|
| Surrey Building Permits | ArcGIS/Socrata API — `data.surrey.ca/dataset/building-permits` | Metro Vancouver high-volume developments; owner/applicant/contractor/value where available | Events research `:26,30` |
| Calgary Building Permits | Socrata — `data.calgary.ca/resource/c2es-76ed.json` | Largest AB-city permit pipeline, values/locations/applicants | `:31` |
| Edmonton Building/Development Permits | Socrata — `data.edmonton.ca/.../24uj-dj8v` | KOR primary AB market; development permits are upstream of tender | `:32` |
| Victoria Building Permits | ArcGIS — `opendata.victoria.ca` | Capital-region 60-day/all-time feed | `:36` |
| Kelowna/Burnaby/New Westminster permits | municipal APIs, dataset verification needed | Interior/TOD development | `:40`; **not verified** to exact production endpoints |

**Major. Finding:** no configured source matches the following early-project/capital pipelines. **Remediation:** prioritize sources that expose team selection before tender, store stage/as-of/source, and extract open structural seat explicitly.

| Missing source | Type / URL | Signal surfaced | Research evidence |
|---|---|---|---|
| Infrastructure BC Projects | project pages + PDFs — `infrastructurebc.com/projects` | BC P3/DB RFQ pipeline, phase, shortlisted teams; major health/education/transit | Events `:34`; Northern BC `:41` |
| BC health-authority capital pages | HTML — Fraser/VCH/Island/Northern/Interior capital-project pages | Projects earlier than tender, owner, phase, value, sometimes design/DB teams | Events `:35` |
| Northern Health procurement | bids&tenders portal — `northernhealth.bidsandtenders.ca` | Northern Health construction RFPs outside BC Bid | Northern BC `:40` |
| BC K–12 Major Capital list | government HTML/PDF | new schools and seismic upgrades by district before A&E RFP | Events `:37`; Northern BC `:42` |
| UBC/SFU/BCIT capital plans | institutional HTML/PDF | university capital pipeline (UBC $2.1B cited), mass timber/seismic opportunities | Events `:38` |
| California DSA projects | state review portal — `dgs.ca.gov/dsa` | every CA K–12/community-college seismic/structural review | Events `:39` |
| CMHC HAF tracker | federal portal | municipality-level housing acceleration and mid-rise demand | Events `:41` |
| WA State WEBS | procurement portal — `des.wa.gov` | Washington A/E team procurements/awards | Events `:43` |

**Critical. Finding:** configured sources contain no OpenNWT/GNWT or Yukon procurement feed; the DB has 0 active NWT MPI, 1 Yukon MPI, and 0 NWT/Yukon Opportunities. **Remediation:** onboard and volume-test:

| Missing source | Type / URL | Signal surfaced | Research evidence |
|---|---|---|---|
| OpenNWT Contract Registry | searchable CSV — `contracts.opennwt.ca` | GNWT tenders since 2004; best partial-open NWT option | Northern AB/NWT/Yukon `:35` |
| Official GNWT Contract Registry | portal — `contracts.fin.gov.nt.ca` | authoritative GNWT tenders | `:36` |
| Yukon Bids & Tenders | portal — `yukon.bidsandtenders.ca` | Yukon government tenders and public bid prices since Feb 2026 | `:37` |
| NNCA tender feed | membership — `nnca.ca` | combined NWT/Nunavut/Yukon tender coverage | `:40`; access terms must be verified |
| MERX / ConstructConnect | subscription | gap-check and early leads including Yukon Gathering Place | `:41`; licensing/API not verified |

**Major. Finding:** First Nations and northern development/procurement sources are not configured. Existing `Bonfire_FNHA` is health procurement, not broad First Nations development coverage. **Remediation:** establish consent/terms-compliant source-specific monitoring and identify owner/architect/SE/GC/seat rather than treating Nation names as generic buyers.

| Missing source | Type / URL | Signal surfaced | Research evidence |
|---|---|---|---|
| FNMPC Projects | project portal — `fnmpc.ca` | First Nations major-project pipeline | Events `:44` |
| yáqʷa Development Corp | procurement/site — `yaqwadevcorp.com` | Cedar LNG onshore, Haisla Centre invitations | Northern BC `:46` |
| Cedar LNG procurement | project procurement — `cedarlng.com/contracting-and-procurement` | onshore subcontract packages | `:47` |
| BC Housing Indigenous Projects map | project map — `bchousing.org/indigenous` | northern Nation housing pipelines before tender | `:48` |
| Northern Development Initiative Trust | funding/project pages — `northerndevelopment.bc.ca` | where northern growth projects receive funding | `:45` |
| Ksi Lisims / Major Projects Office | federal project pages — `canada.ca` | future onshore procurement milestones | `:49` |

**Minor. Finding:** several recommendations are partially covered and should not be double-counted: AB MPI is configured; RMWB/Grande Prairie/County GP appear in bids&tenders/APC coverage; CivicInfoBC_All is active; CanadaBuys covers some DND/DCC; Fraser and Island Health Bonfire feeds are active. Coverage equivalence could not be verified for all recommended portal content. **Remediation:** maintain a source-to-signal matrix that records exact endpoint, jurisdiction, stages, field coverage and known overlap.

## 5.2 Structural seat and team coverage

**Critical. Finding:** among 2,374 active MPI rows:

| Field/signal | Filled | Coverage | Blank |
|---|---:|---:|---:|
| ProponentCanonicalOrgId | 1,946 | 82.0% | 428 |
| ArchitectCanonicalOrgId | 453 | 19.1% | 1,921 |
| GeneralContractorCanonicalOrgId | 30 | 1.3% | 2,344 |
| StructuralEngineerCanonicalOrgId | 75 | 3.2% | 2,299 |
| StructuralEngineerName | 113 | 4.8% | 2,261 |
| SeatStatus | 45 | 1.9% | 2,329 |
| Any of SE id/name or SeatStatus | 157 | 6.6% | 2,217 |

`SeatStatus` distribution is blank 2,329; `likely-open` 29; `Open` 8; `unknown` 5; `filled` 3. Only 37/2,374 (1.6%) are explicitly open/likely-open. Province-level SE-id coverage: BC 54/1,425 (3.8%); CA 12/556 (2.2%); AB 7/317 (2.2%); OR 0/58; WA 1/17; Yukon 1/1. **Remediation:** define a seat-state enum (`unknown`, `open`, `likely-open`, `filled`, `KOR`, `not-applicable`) with source, confidence, observed-at and expiry; make coverage/freshness by project stage and province a production SLO.

**Critical. Finding:** the primary business objective—finding open SE seats—is not inferable from a blank: blank conflates not researched, source unavailable, no SE named, and import loss. **Remediation:** persist research state separately from seat conclusion, require a reason/evidence URL for non-unknown conclusions, and rank the backlog by active stage/value/architect fit.

# Severity-ranked remediation queue

This is a human-gated queue, not an auto-fix proposal.

| Priority | Severity | Remediation outcome | Acceptance evidence |
|---:|---|---|---|
| 1 | Critical | Seat/team contract and non-destructive persistence | Sparse rerun cannot clear prior fields; every conclusion has source/as-of/confidence; coverage dashboard exists |
| 2 | Critical | Repair health observability and 21 dead sources | Zero-output alerts; standard provider metrics; each flagged source either produces rows or is intentionally disabled with reason |
| 3 | Critical | Restore/page Vancouver permit feed; onboard four high-priority cities | Freshness current; per-city expected-volume and field-fill baselines |
| 4 | Major | Central resolver candidate matching before create | “Created despite aggressive candidate” = 0 except reviewed overrides; multi-hit quarantine |
| 5 | Major | Review 76 duplicate clusters and 45 role-kind mismatches | Approved survivor/evidence per cluster; no live Vendor/Unknown project-role links without exception |
| 6 | Major | Repair 16 historical MPI orphans and enforce SE/GC FKs | Zero orphan query; validated constraints present |
| 7 | Major | Replace 48 bespoke payload contracts with versioned project/org schemas | Contract validation and field-level accepted/dropped metrics for every tag |
| 8 | Major | Add NWT/Yukon/Infrastructure BC/health/First Nations pipeline sources | Nonzero territorial coverage and source-to-signal tests; licensing/terms recorded |
| 9 | Major | Enrich barren live-role organizations by value | Website/not-found state coverage reported by kind and live references |
| 10 | Minor | Preserve dedup FK guard and add merge impact reports | Zero-unhandled check and per-target before/after row counts on every dry run |

## Verification limits

- No external portals were fetched; “source exposes” is based on repository adapters/configuration and the cited research documents. Claims requiring live portal/API validation are marked or framed as recommended-source claims.
- No importer was executed, so lossiness is control-flow/schema analysis, not a replay result.
- The duplicate survivor heuristic cannot determine legal parent/subsidiary/JV identity; all 30 survivor flags require human review.
- Source health is a snapshot while schedulers are active. A successful run proves execution, not semantic correctness.
- Northern city probes are explicitly bounded heuristics, not geographic polygons.
