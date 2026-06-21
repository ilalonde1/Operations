# BD Brain — Overnight Enrichment Briefing (2026-06-21)

Read this first. Synthesis of the overnight audit + three deep-research streams + dedup-checks. Detail docs linked at the bottom. **All writes are STAGED for your review** — nothing was auto-merged, auto-seeded, or drained (per the dedup-burn + qdrain-audit-pending cautions).

## TL;DR
1. **Geographic gap confirmed & now fillable.** NWT/Yukon/Northern-BC/Northern-AB had ~0 coverage. Research seeded **~50 named orgs, ~55 named projects, 25+ ingest sources, 22 events** — all sourced.
2. **The enrichment win is mostly RECLASSIFY + ENRICH, not seed.** Most northern players already exist in the brain as **barren/mislabeled Vendor rows** (Deltek-AP seeding). The job is to fix labels, enrich, and merge dups — higher-value and lower-risk than mass seeding.
3. **#1 ingest gap: municipal building permits.** We ingest **only Vancouver** (50,811 permits). Surrey, Calgary, Edmonton, Victoria all expose **open-data permit APIs** — straight ingests.
4. **Real open SE seats found** in the North (below) — time-sensitive pursuits, not just data.
5. **Data-hygiene bugs surfaced** (concatenated names, mislabeled GCs, dup clusters) — staged fixes.

## A. Northern orgs — seed vs enrich vs reclassify (dedup-checked)
**NET-NEW → seed (clean, low dup risk):** Broadstreet Properties · Northern Dynamic Designs · yáqʷa Development Corporation · Housing NWT · NT Health & Social Services Authority (NTHSSA) · Taylor Architecture Group · NNCA · Gwich'in Tribal Council · Yukon Housing Corporation · Yukon Hospital Corporation · s.no architecture · Kwanlin Dün First Nation · Chu Níikwán LP · Ta'an Kwäch'än Council.

**EXISTS but MISLABELED → reclassify:** Graham Design Builders #8360 (Vendor→**GC**) · IDL Projects #44647 (Vendor→**GC**) · GNWT #52210 (Vendor→**Government**) · Yukon University #58668 (Vendor→**Owner**) · NIC #44026 / ARCAN #43852 (GC, barren) · Tlicho #42547 (Vendor→Government). *(DIALOG #6154 & Stantec #38934 are tagged "Competitor" — defensible since they self-perform engineering; leave, but they're also architect-targets.)*

**EXISTS but BARREN → enrich:** Northern Health #54976 · Clark Builders #4922 (already enriched ✓) · Kobayashi+Zedda #58416 · Da Daghay #40986 · Peace Wapiti SD #603 · NW Polytechnic #892 · Providence Living #76391 · MGA Architecture #70763 · Mikisew Cree #11839 · Athabasca Chipewyan #42171 · Wood Buffalo Housing #643 · many more.

**DUP CLUSTERS → merge (staged, careful):** Northern Health (#54976 / #75655 / **#794 concatenated-name mess**) · M'akola (#52 / #69999 / #75546) · Lax Kw'alaams (#55005 / #75636) · Defence Construction Canada (#47044 / #76469) · Athabasca Chipewyan (#42171 / #58800).

⚠️ **Data-hygiene bug:** CanonicalOrg **#794** has DisplayName = seven health authorities concatenated ("Fraser Health AuthorityInterior Health Authority…"). Needs cleanup at source.

## B. Open SE seats — the real pursuits (time-sensitive)
| Project | Where | Owner / team | Why now |
|---|---|---|---|
| **FSJ Peace Villa Expansion (84 LTC)** | Fort St. John | Northern Health / Infra BC | **Procurement on BC Bid NOW** — catch the 3 DB shortlist, pitch all 3 architects |
| **UHNBC Acute Care Tower** ($1.58B) | Prince George | **DIALOG** / EllisDon | Pre-con begins early 2027; SE not named — approach DIALOG |
| **Yukon Gathering Place** ($75M) | Whitehorse | **Chu Níikwán** (KDFN) | Schematic now, SE open; DB RFP posted; contact Chu Níikwán + KZA/Taylor |
| **Carson (SD28) + Gitwinshilkw (SD92) elementaries** | Quesnel / Nass | School districts | Active major-capital, no design team yet — watch BC Bid for A&E RFP |
| **Campbell River "Reimagine the Row"** (200 u) | Campbell River | **Seymour Pacific** #53416 | Design permit Apr 2026; approach Seymour Pacific directly |
| **Haisla Centre Ph2 + Cedar LNG buildings** | Kitimat | **yáqʷa Dev Corp** | Master-plan RFP out; sets up Ksi Lisims later |
| **CFB Cold Lake DCC pipeline** ($272M + tail) | Cold Lake | EllisDon / DCC | 5-yr defence pipeline; CanadaBuys DCC filter |
**Proof point:** Fast+Epp (Vancouver SE) already holds the Tulita Health Centre, NWT — a BC firm *can* win territorial work.

## C. Ingest sources to add (prioritized, staged — they trigger crawls)
★HIGH: **Surrey / Calgary / Edmonton / Victoria building-permit APIs** (Socrata/ArcGIS — clean ingests) · **Alberta Major Projects Inventory** (open CSV) · **Infrastructure BC** project pipeline (scrape) · **BC health-authority capital pages** (scrape) · **OpenNWT** (CSV) · **Yukon Bids&Tenders** · **Northern Health Bonfire** · **CivicInfo BC bids**.
HIGH: BC K-12 capital list · UBC/SFU/BCIT capital plans · California DSA (CA school seismic — KOR fit) · NNCA (territories feed).
*Note: KOR already has APC (covers many N.AB municipalities) + CanadaBuys (covers DCC — verify filters).*

## D. Events — hone the 80, add the TOP 12
Add/refresh: BUILDEX Vancouver (Feb 11–12) · Vancouver RE Forum (Mar 31–Apr 1) · UDI BC (year-round) · AIBC Conf (May 25–27) · HAVAN Gala (Apr 18) · SEABC Dinner (May 13) · ULI BC/Cascadia (May 12–14) · BUILDEX Alberta (Oct 21–22) · SEAOC (Aug 26–28) · ACEC-BC Gala (May 8) · NAIOP CRE Awards (May 13) · **FNMPC (Apr 29–May 1)** — the Indigenous-infrastructure anchor for all the northern First Nations work above. Retire stale/past entries.

## Proposed morning execution order (all gated on your OK)
1. **Reclassify + enrich** the mislabeled/barren northern orgs that already exist (Graham, IDL, GNWT, Yukon U, NIC, ARCAN…) — highest value, lowest risk; via the existing enrichment pipeline.
2. **Seed** the ~14 net-new northern orgs (dedup-checked clean).
3. **Add the ★HIGH ingest sources** — start with the 4 municipal-permit APIs (clean) + Alberta Major Projects (CSV).
4. **Ingest the TOP 12 events**; retire stale.
5. **Resolve the dup clusters** (Northern Health #794 mess first) — careful, one at a time, post-audit (our standing rule).
6. **Work the open SE seats** — FSJ Peace Villa + Yukon Gathering Place are the most time-sensitive.

## Detail docs
- `BD-Dataset-Audit-2026-06-21.md` — full dataset audit + counts
- `KOR-Northern-BC-VanIsland-Research-2026-06-21.md`
- `KOR-Northern-AB-NWT-Yukon-Research-2026-06-21.md`
- `KOR-Events-IngestSources-Research-2026-06-21.md`
- Dedup-check scripts: `output/check-northern-seeds.ps1`, `output/check-north2-seeds.ps1`

**Nothing applied to the DB.** Awaiting your direction in the morning on the execution order above.
