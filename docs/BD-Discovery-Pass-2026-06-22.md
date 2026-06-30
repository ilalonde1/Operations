# BD Discovery Pass — Results
### 2026-06-22 · find the AEC firms KOR was *missing*, add them live, enrich them

**What ran.** A full-footprint discovery pass: research agents compiled the active AEC firms/buyers in each KOR market and fed them through the existing market-research streams (`KOR-{LA,SanDiego,Sacramento,BayArea,PacNW,Alberta}-Market` + `KOR-Institutional-Pipeline`). The `CanonicalOrgResolver` dedups on normalized name, so only the genuinely-missing firms were created (born **live**), each with a market-research note; then the free QueueDrain roll gave every one a full FirmNarrative dossier. **$0 API** (subscription agents only).

## Yield
- **487 net-new canonical orgs** created (live CanonicalOrg 17,751 → 18,238).
  - Buyers 190 · Architects 97 · Developers 68 · GCs 58 · Competitor SEs 52 · Unknown 13.
- **~71% net-new rate** on the market firm-lists (the growth-market org graph really was thin); ~29% deduped to firms already held.
- **Enrichment:** net-new enrichable barren 475 → **0**; orgs with FirmNarrative **6,194 → 6,658**.
- Markets: BC (buyers) + Alberta (Edmonton/Calgary) + SoCal (LA/SD) + NorCal (Bay/Sacramento) + PacNW (Seattle/Portland).
- **BC firms deliberately deferred** (deepest existing coverage; no clean firms-stream) — BC *buyers* were covered.

## Dominant theme: the seismic pipeline (KOR's core lane)
Discovery surfaced a coordinated, multi-year seismic-retrofit wave across the US West that maps exactly to KOR's specialization:
- **California hospitals — SB 1953 / HCAI 2030 deadline:** Scripps ($2.6B), Sharp ($2B+, $100M seismic), Cedars-Sinai, Children's Hospital LA, Adventist Health (19+ hospitals). **Suffolk Construction** is opening a Newport Beach office *specifically* for this wave.
- **UC system — 2030 seismic compliance:** all campuses on retrofit programs (UC Berkeley alone 133+ buildings). Single registration key = UC Plan Room.
- **CSU system:** $3.1B+ five-year plan with explicit seismic priority list, procured campus-by-campus (Northridge = highest risk).
- **K-12:** LAUSD ($9B bond, **525 schools need seismic retrofit**, ASCE 41 RFQ active), SF ESER 2026 bond ($535M, ~3,700 structural assessments), Seattle PS, Tacoma PS, Spokane PS.
- **Infrastructure/water:** Metropolitan Water District SoCal (multi-year seismic SE pipeline).
- **BC:** BC Infrastructure ($45.9B), the SMP school-district pipeline (verified in the prior summary).

## Top time-sensitive targets (this week / this month)
- **DIALOG BC** — architect on *both* UHNBC Acute Care Tower ($1.579B) and NAIT Advanced Skills Centre ($384M). One relationship, two major projects, SE window open now.
- **Ware Malcomb** — opened a Vancouver BC office (May 2026) and *needs local SE subs now*; Glotman is the incumbent to displace.
- **ZGF** — Vancouver office; WSU ISB ($60M) SE not yet named.
- **Safdie Rabines** (SD) — Midway Rising (4,000 units + 16,000-seat arena); SD Best Architect two years running.
- **MVE+Partners San Diego** — new office, no SE vendor lock-in yet.
- **Hensel Phelps SD** — SDCCD Mid-City $100M design-build, structural sub selection now.
- **Gilbane / Suffolk / Chandos** — actively building/​resetting their subconsultant rosters (open windows).

## Competitor displacement map (now in the brain)
- **BC:** Glotman Simpson (hi-rise resi), RJC + Fast+Epp (civic/institutional), Entuitive, Krahn, Pacific JCK.
- **CA/US West:** Englekirk/WSP (CSU seismic peer-review contract), Degenkolb, KPFF, DCI Engineers, Coffman, Walter P Moore, Nabih Youssef (founder d. 2024 — succession flux), Saiful Bouquet, Miyamoto, Holmes, Rutherford+Chekene, PBA, Buehler.
- **KOR's moat (verified positioning):** developer mid-rise residential + heritage/seismic — the segments WPM/WSP/the trophy-project firms won't chase.

## Morning cleanup worklist (tagged in DB, all reversible)
Query `opportunities.CanonicalOrg` by `EnrichmentSuppressedReason`:
- **Office/name dedups (merge pending):** Carrier Johnson (77207→77164), ZGF (75737→77104), CKC (77297→77125), Protostatix/Englobe (75674/76639), Aspect (76499→75826), MGBA (76161→75878), Bow Transit (76236→75858), HDR variants, Michael Green (75834). Plus the office-suffix fragments (HOK Seattle, DCI San Diego, etc.) → nightly dedup/honing.
- **Name-cleanup (truncated):** DBRDS, LARGE Architecture, OHSU, Teeple, SD62/SD63 (re-enriching).
- **Data-hygiene flags:** TJZ Structural (Calgary AB, not US — review/retire), Westbank Corp Seattle (receivership — do-not-engage), Triumph/Wexford (US-only, no BC).
- **JV-strings / multi-entity:** suppressed (do-not-enrich) — candidates for the JV decomposition program.

## Recommended next step
**Resynthesize** — the region/displacement/funnel views and pursuit briefs now read a much fuller org graph (6,658 enriched, the full US-West + Alberta footprint added). That's where this discovery pays off.
