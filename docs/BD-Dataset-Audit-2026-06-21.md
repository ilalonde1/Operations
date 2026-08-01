# BD Brain — Full Dataset Audit & Overnight Enrichment Plan (2026-06-21)

Read-only audit of `KorOpportunitiesDb` to ground the overnight enrichment push. Counts are live as of 2026-06-21.

## Scale
| Area | Count |
|---|---|
| CanonicalOrg (active) | ~49,500 |
| IntelPerson (active) | 9,086 |
| MajorProjectsInventory (active) | 10,044 |
| BuildingPermit | 50,811 (**all Vancouver**) |
| HistoricalOpportunities | 9,907 |
| Opportunities (pipeline) | 1,266 |
| KorPursuits (Deltek-linked) | 1,066 |
| IndustryEvents | 80 (many retired) |
| AwardProgram | 15 (refined today) |
| OpportunitySources | 117 |
| IntelSignal / Affiliation / Action | 20,341 / 15,051 / 24,754 |

## 1. Geographic coverage — the big gap (confirmed)
MPI active by province: **BC 1,425 · CA 551 · AB 317 · OR 58 · WA 17 · YT 1 · NWT 0.**
Northern probe (active MPI): **Northern BC 31 · N. Vancouver Island 57 · Northern AB 4 · NWT 0 · Yukon 1.**
Opportunities: BC 691 · AB 128 · CA 49 · (392 null) · AK 3 · WA 2 · HI 1 — **zero NT/YT.**
Building permits: **50,811, 100% Vancouver** (only the CoV permit source is enabled).

→ **Near-zero coverage of NWT, Yukon, most of Northern BC, and Northern Alberta.** No First Nations development-corp sources (a dominant northern driver). Research agents dispatched to seed all of this.

## 2. Enrichment backlog ("deeply enrich existing people/places/things")
Barren BD-relevant orgs (no website / no intel):
- **Developers 2,068 / 2,450 (84%)** · Architects 846 / 1,168 (72%) · GCs 4,069 / 4,251 (96%) · Competitors 611 / 751 (81%) · Buyers 1,214 / 1,818.
- People: **5,840 / 9,086 (64%) have no email and no LinkedIn.**
→ Drive the platform's existing BD research-enrichment queue at MPI-referenced barren orgs (priority targets), overnight.

## 3. Ingest sources (117) — coverage map
Strong: BC Bid (+Engineering/Awards/Unverified), CanadaBuys, Alberta Purchasing (APC), MPI (BC/AB/CA), CivicInfo BC, SAM.gov, CA open-data (CEQA, San Diego, SF, San Jose, LA RAMP). ~30 bids&tenders municipal portals (incl. some northern: Prince George, Wood Buffalo, Grande Prairie, Comox Valley, Nanaimo, Campbell River). ~35 Bonfire portals (health authorities incl. FNHA/Northern via PHSA, universities).
**Gaps:** no NWT (contractsnwt) or Yukon procurement; no most-of-Northern-BC; **only Vancouver building permits** (no Surrey/Burnaby/Victoria/Kelowna/Calgary/Edmonton permit feeds); no Infrastructure BC pipeline / utility / First Nations Major Projects sources. Research agent dispatched.

## 4. Events (80) — stale + narrow
Markets: BC / Alberta / Pacific NW / Metro Van / LA-SoCal. Many **retired** (the retirement lifecycle is working). No northern coverage. Research agent dispatched to hone + expand the events that matter.

## 5. Awards — done today
15 KOR-enterable programs (Structural ×7, Engineering ×5, Mixed ×2, BuilderDeveloper ×1); architect-only awards excluded. Live in BD Reports → Awards.

---
## Overnight plan
1. **Research (running, Sonnet agents):** Northern BC + N. Van Island · Northern AB + NWT + Yukon · events + ingest sources. → seed lists, projects, ingest-source specs.
2. **Enrich (platform queue):** kick the BD research-enrichment queue at barren MPI-referenced orgs.
3. **Stage for review (no auto-merge):** dedup candidates, new ingest-source configs, net-new entity seeds → morning review (we've had wrong dedup survivors before).
4. **Morning briefing:** synthesize audit + research + enrichment results + a prioritized, ready-to-run plan.
