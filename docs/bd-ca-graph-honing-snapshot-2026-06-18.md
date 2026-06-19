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

## Org graph — 251 CA funnel orgs, classified
Developer 97 · Architect 61 · Buyer 42 · GC 20 · KorClient 4 · Competitor 4 · Vendor 3 · Unknown 20 (JV credit-line strings + junk placeholders, held for triage).

- **Name hygiene fixed:** the ecosystem ingest had concatenated contacts and JV teams into org names ("Holland Partner Group  Kevin Willis", "Steinberg Hart / Studio Gang / …"). 46 contaminated names found; 27 contact-bearing ones cleaned + their people extracted; 11 duplicate variants merged (orphan-audited clean).

## Contact layer — 591 CA contacts (242 email, 183 LinkedIn)
This is the layer that brings CA toward Lower-Mainland parity. This pass added 27 contacts buried in org names; **10 now carry Apollo-verified emails + titles + LinkedIn**:

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

## Strategic finding — CA is greenfield for KOR
Deltek-link pass over 9,139 Developer/Architect/GC/Competitor orgs produced **zero reliable CA client links** (the one fuzzy hit, "Holland Construction", is an unverified name coincidence and was not committed). The CA funnel firms are not existing KOR clients — confirming KOR enters CA as a challenger and must win via the architect's-structural-sub / warm-team path, not incumbency.

- **Warmest bridge: Onni Group** — Vancouver-HQ developer with 9 active CA projects. KOR's BC relationship is the most direct cross-market intro path. (Under research.)

## Open / next
- Decompose the ~20 JV credit-line orgs (Hines/Affinius, Strada/Trammell Crow, UC Davis/Wexford, …); retire junk placeholders.
- Deferred tool fix: **BdCanonicalDedup does not repoint `IntelPersonAffiliation`** — every honing merge with loser-side affiliations leaks orphans (11 pre-existing BC orphans outstanding). Fix the tool, then sweep.
- In-flight: Sonnet research agent on top-developer pipelines + SE-decision contacts + warm paths (Onni focus).
