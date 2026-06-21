# AEC Events + Ingest Sources — Research (2026-06-21)

Overnight research to hone/expand the 80-event table and close ingest-source gaps. Dates verified against primary sources where marked.

## Part A — Events that matter (TOP 12 = KOR must-attend)
| ★ | Event | Organizer | Type | 2026 date | Location | Market | Why KOR |
|---|---|---|---|---|---|---|---|
| ★1 | **BUILDEX Vancouver** | Informa | Trade show+conf | Feb 11–12 | Vancouver CC | BC | Largest W. Canada AEC gathering; architects+developers |
| ★2 | **Vancouver Real Estate Forum** | Informa | Developer conf | Mar 31–Apr 1 | Vancouver CC | BC | Highest developer density in Canada; pre-RFP |
| ★3 | **UDI BC** (luncheons/socials) | UDI Pacific | Networking | year-round (Apr 16/28, May 21, Jun 17…) | Van/Surrey/Vic | BC | THE BC developer network; monthly touchpoints |
| ★4 | **AIBC Conference** | AIBC | Conf+awards | May 25–27 | Vancouver CC | BC | Every BC architect-client (prime-consultant referrals) |
| ★5 | **HAVAN Awards Gala** | HAVAN | Gala | Apr 18 | Westin Bayshore | BC | Metro Van homebuilders + residential developers |
| ★6 | **SEABC Annual Dinner + Pinnacle Lecture** | SEABC | Gala+lecture | May 13 (Eric Karsh, mass timber) | Vancouver | BC | Structural peer reputation + talent |
| ★7 | **ULI BC / Cascadia Regional** | ULI BC | Conf+events | May 12–14 + year-round | Vancouver | BC/PNW | Developer/investor net; cross-border WA/OR play |
| ★8 | **BUILDEX Alberta** | Informa | Trade show | Oct 21–22 | BMO Centre Calgary | AB | Edmonton/Calgary expansion in one room |
| ★9 | **SEAOC Convention** | SEAOC | Conf+awards | Aug 26–28 | Scottsdale AZ | CA | CA SE peer credibility for JV/stamping |
| ★10 | **ACEC-BC Awards Gala** (+Jul Summer Connections) | ACEC-BC | Gala | May 8 | Vancouver Playhouse | BC | Institutional owner/client network |
| ★11 | **NAIOP Vancouver CRE Awards** | NAIOP Van | Gala | May 13 | Vancouver | BC | ICI developer principals (office/mixed-use) |
| ★12 | **FNMPC Conference** | FNMPC | Conf | Apr 29–May 1 | Toronto | National (Indigenous) | Indigenous infrastructure pipeline (growing BC/AB) |

Secondary (ingest, not must-attend): EGBC Annual Conf (Oct 15–16, Victoria) · BOMA BC Awards (seismic retrofit owners) · ULI Alberta · AAA Banff Session (date TBD) · SEA Northwest Conf (Sep, rotating) · AIA Seattle · ULI SoCal · The Buildings Show/Construct Canada (Dec 2–4, Toronto) · ACEC Canada (Oct 28–29, Ottawa) · CHBA National Awards (May 8, Québec City).

→ **Action:** ingest the TOP 12 (+secondaries) into IndustryEvents with SourceKey, dedup vs existing 80, retire stale/past. Staged for review.

## Part B — Ingest sources to add (prioritized)
**The #1 gap: municipal building permits — we ingest only Vancouver.** Surrey/Calgary/Edmonton/Victoria all expose open-data permit APIs.

| Pri | Source | Type | URL | Surfaces | API? |
|---|---|---|---|---|---|
| ★HIGH | **Surrey Building Permits** | Muni permits | data.surrey.ca/dataset/building-permits | Metro Van's highest-volume market | API (ArcGIS/Socrata) |
| ★HIGH | **Calgary Building Permits** | Muni permits | data.calgary.ca/resource/c2es-76ed.json | AB's largest city | **Socrata API** |
| ★HIGH | **Edmonton Building Permits** | Muni permits | data.edmonton.ca/…/24uj-dj8v | KOR's primary AB market (+dev permits = upstream) | **Socrata API** |
| ★HIGH | **Alberta Major Projects Inventory** | Prov pipeline | majorprojects.alberta.ca / open.canada.ca (dataset 3e4efd44) | All AB ≥$5M; monthly | **Open CSV/XLS/KML** |
| ★HIGH | **Infrastructure BC Projects** | P3 pipeline | infrastructurebc.com/projects | BC's largest health/edu/transit P3s by phase | Scrape+PDF |
| ★HIGH | **BC Health Authority Capital pages** | Health pipeline | fraserhealth.ca/capital-projects/projects (+VCH/Island/Northern/Interior) | 12+ active Fraser Health projects etc. | Scrape |
| HIGH | Victoria Building Permits | Muni permits | opendata.victoria.ca (60-day & all-time feeds) | Capital region | API (ArcGIS) |
| HIGH | BC K–12 Major Capital list | Edu pipeline | gov.bc.ca/…/major-capital-projects | New schools + seismic upgrades | Scrape |
| HIGH | UBC/SFU/BCIT capital plans | Univ pipeline | infrastructuredevelopment.ubc.ca + bcit.ca/facilities | $2.1B UBC; SFU med school; BCIT mass-timber | Scrape |
| HIGH | **California DSA projects** | CA schools | dgs.ca.gov/dsa | Every CA K-12/CC seismic-structural review (KOR seismic fit) | Scrape |
| MED | Kelowna / Burnaby / New West permits | Muni permits | opendata.* | Interior + TOD markets | API (verify dataset) |
| MED | CMHC Housing Accelerator Fund tracker | Fed housing | cmhc-schl.gc.ca/…/housing-accelerator-fund | Muni mid-rise demand surge | Portal |
| MED | Open Canada permits by municipality | Macro stats | open.canada.ca (dataset 45a00be0) | National permit trend signal | CKAN API |
| MED | WA State WEBS | US proc | des.wa.gov | WA A/E architect-team awards | Portal |
| LOW-MED | FNMPC Projects · OregonBuys | Indigenous/US | fnmpc.ca · oregonbuys.gov | Indigenous infra; OR state | Scrape/portal |
| — | BC MPI (gov) — **DISCONTINUED after Q3 2025** | — | — | use Infra BC + health pages going forward | Excel archive |

→ **Action (staged):** add the ★HIGH municipal-permit + pipeline sources as `OpportunitySources`/`PermitSource` configs (they trigger crawls — gated on review). The Socrata/ArcGIS ones are clean API ingests; the scrape ones need a Playwright provider (we have the pattern).

*Full per-event/per-source detail + all citations live in the agent transcript; this is the actionable distillation.*
