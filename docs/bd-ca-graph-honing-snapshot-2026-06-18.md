# California BD Graph — Honing Snapshot (2026-06-18)

State of the CA intelligence graph after the funnel deploy + honing pass. Companion to the field guide / statewide synthesis; this captures *graph health* and the actionable layers.

## Live funnel (4 streams, deployed v1.0.9665.1195)
| Source | Type | Rows |
|---|---|---|
| SF (`data.sfgov.org` Socrata) | building permits | 1,084 |
| San Jose (CKAN datastore) | building permits | 54 |
| City of San Diego (DataSD CSV) | approvals | 29 |
| CEQAnet (statewide HTML) | CEQA filings | 5 |
| Curated/ecosystem (research) | major projects | 209 |

Note: SF and SD permit feeds carry no developer/applicant field at source, so those rows are projects-without-proponent by design — the "who" comes from the curated developer graph + research, not the permit feed.

## Org graph — 237 CA funnel orgs, classified & structurally clean
Developer · Architect · Buyer · GC · KorClient · Competitor · Vendor — **0 JV-strings, 0 contaminated contact-names, 0 junk placeholders remain.**

- **Name hygiene fixed:** the ecosystem ingest had concatenated contacts and JV teams into org names ("Holland Partner Group  Kevin Willis", "Steinberg Hart / Studio Gang / …"). 46 contaminated names found; 27 contact-bearing ones cleaned + their people extracted.
- **Fragments deduped:** Nabih Youssef ×3, Saiful Bouquet, DCI, Chris Dikeakos, Bosa Development → single canonicals.
- **JV-strings decomposed (all 20):** merged into the operating lead where one existed (Hines, Tishman Speyer, BRIDGE Housing, Strada, SKK, DGS, SJSU, Steinberg Hart, Trammell Crow, Wexford) or renamed in-place (Abode Services, Wexford, LBNL, Jamison Properties, SF Mayor's Office of Housing); 5 junk placeholders retired. Held only Platt/Whitelaw (real firm) and Bosa Properties (distinct Deltek client from Bosa Development).
- **~18 org merges total, every one repoint-clean** (affiliation-repoint tool fix): 0 new orphans, 0 hand-curated contacts lost.

## Contact layer — 619 CA contacts (252 email, 183+ LinkedIn)
This is the layer that brings CA toward Lower-Mainland parity. This pass (a) extracted 27 contacts buried in org names and (b) ingested 23 named SE-decision-makers at the most-active developers from targeted research — **20 now carry Apollo-verified emails**.

### Decision-makers at the top developers (Apollo-verified emails)
Holland Partner Group: Tom Warren (Pres) twarren@hollandpartnergroup.com · Greg Thomas (Pres, Holland Construction) gthomas@hollandpartnergroup.com · John Wayland (Exec MD NorCal) jwayland@hollandpartnergroup.com.
Related California: Ann Silverberg (Pres & CEO, CA + NW Affordable — **PNW mandate**) asilverberg@related.com · Phoebe Yee (EVP Design) pyee@related.com.
Brookfield: Adrian Foley (Pres & CEO) adrian.foley@brookfieldrp.com · Josh Roden (Pres NorCal Land & Housing) josh.roden@brookfieldpropertiesdevelopment.com · Nicole Burdette (Reg Pres US Land CA/AZ) nicole.burdette@brookfieldrp.com.
Carmel Partners: Adam Mayer (VP Dev) amayer@carmelpartners.com · Will Cipes (SVP Dev SoCal) wcipes@carmelpartners.com.
Sutter Health: Warner Thomas (Pres & CEO) warner.thomas@sutterhealth.org. Crescent Heights: Bruce Menin (Principal) bam@crescentheights.com.
Onni Group (De Cotiis family — Rossano/Morris/Giulio + Beau Jarvis VP Dev LA): on file without email (not in Apollo; LinkedIn/direct outreach). Full detail: `docs/ca-research-raw/ca-developer-se-selection-2026-06-18.md`.

### From the org-name cleanup — 10 Apollo-verified contacts

| Contact | Title | Firm | Email |
|---|---|---|---|
| Robert Emami | CEO | ROEM | remami@roemcorp.com |
| Mark Pilarczyk | President of Development | Swenson | mark@swenson.com |
| Colin Epperson | Principal | ARCO/Murray | cepperson@arcomurray.com |
| Kate Conley | Principal | Architects FORA | kate@architectsfora.com |
| Vince O'Driscoll | Co-Owner | Oarcon | vince@oarcon.com |
| Tobias Yuen | Sr Project Manager | Level 10 Construction | tyuen@level10gc.com |
| Anthony Bonasera | Project Manager | Devcon Construction | abonasera@devcon-const.com |
| Matt Lindsay | Sr Project Manager | Premier Design+Build | matt.lindsay@pdbgroup.com |
| Tom Bliska | Architect | David Baker Architects | thomasbliska@dbarchitect.com |
| Nicole Olaes | Associate | Arup | nicole.olaes@arup.com |

(Apollo org-match verification rejected 17 — incl. false matches like "Kevin Willis → Swinerton" — so no wrong emails entered the graph.)

## Most active proponents (by project count)
UC system (10 / $1.34B) · CSU (9 / $1.14B) · **Onni Group (9 / $163M)** · Holland Partner Group (7) · TMHCO (6 / $384M) · Brookfield Properties (5 / $1.78B) · Crescent Heights (5) · Level 10 (4) · Related California (4) · Sutter Health (3 / $5.17B healthcare).

## Largest pipeline
SD Civic Center Redevelopment $4.49B · Sutter Santa Clara Medical Center $3.81B · Brookfield Gas Company Tower / 777 Tower $1.50B · Sutter Emeryville $1.36B · UC San Diego La Jolla Medical Center Tower 2 $1.36B.

## Strategic finding — mostly challenger, but two warm incumbents
The Deltek-link pass over 9,139 Developer/Architect/GC/Competitor orgs produced zero reliable *new* links among the unlinked funnel firms — so KOR is broadly a challenger in CA. **But that scan missed already-linked entities of other kinds, and verification found two existing relationships with top-active developers:**

- **Onni Group (9 CA projects) is an existing KOR client** — `Onni Contracting (California), Inc.` (KorClient, Deltek-linked) and `Nonni Property Group` (KorClient, linked; likely an "Onni" typo to verify). Onni is Vancouver-HQ and its own GC, so it controls SE selection directly. This is the **strongest warm bridge** — an actual CA client relationship, not just a BC connection. (Org graph splits Onni across 5 rows — Developer 38949, KorClient 167/151, Buyer 76124, Vendor 75008 — a dedup/relationship-mapping task, but do NOT blind-merge KorClient rows.)
- **Holland Partner Group (68644) is also Deltek-linked** — existing relationship; KOR already has contact John Wayland (Exec MD NorCal Dev). Live target: 540-unit Stevens Creek San Jose (pre-construction, SE slot open).

So the accurate picture: challenger overall, **warm incumbent with Onni and HPG**. Win the rest via the architect's-structural-sub / warm-team path. Incumbent to beat at the high-rise tier: **MKA (Seattle)**.

## Open / next
- Decompose the ~20 JV credit-line orgs (Hines/Affinius, Strada/Trammell Crow, UC Davis/Wexford, …); retire junk placeholders.
- Deferred tool fix: **BdCanonicalDedup does not repoint `IntelPersonAffiliation`** — every honing merge with loser-side affiliations leaks orphans (11 pre-existing BC orphans outstanding). Fix the tool, then sweep.
- In-flight: Sonnet research agent on top-developer pipelines + SE-decision contacts + warm paths (Onni focus).
