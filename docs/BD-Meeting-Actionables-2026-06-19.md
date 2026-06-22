# BD Meeting — Vancouver Lower Mainland — Actionables & Verified Targets

**Source:** "Business Development – Vancouver Lower Mainland" meeting recording, 19 Jun 2026 (2h 35m).
**Prepared by:** Ian — names cross-referenced against the BD brain (canonical orgs + Deltek client IDs + KOR project counts) and web-verified where internal data was thin.
**Verified & updated 2026-06-20** — all canonical org IDs, KOR project counts, CRM engagement numbers and the De Cotiis family structure re-confirmed against the live DB (post the De Cotiis enrichment + dedup merges + CRM remap). Changes flagged inline.

**Brain hardened + re-verified 2026-06-21** — a full module clean-up ran since the meeting; the graph these targets live in is now production-grade:
- **Culled 50k → 17.8k warm orgs** (32k orphan commodity-vendor rows cold-archived; fully reversible — any resurfaces on a real pursuit). The pursuit targets below were untouched (all have KOR projects/relationships).
- **100+ duplicate clusters merged** (incl. the BC health-authority cluster 32→9 clean entities), **81 mis-classified Kinds corrected** (e.g. AECOM/RJC/SMP now correctly tagged **Competitor**), MPI structural-engineer/GC foreign keys completed, and the **duplicate-creation cycle closed** at the resolver (safe fuzzy-match before create) so the brain stays clean going forward.
- **Every org + person ID in this doc re-verified against the cleaned graph 2026-06-21 — all resolve correctly** (Pinnacle #53665, Onni #38949, Amacon #169/#71060, Emerge #111, Evantra #203, RBI #14, Lark #53694/#72260, Kerkhoff, Yellowridge, + the full De Cotiis person directory #1399/#1406/#3757/#9303-9305/#901/#902/#903/#1940/#1941). The Lark "Unknown" stray rows (#55097/#27243) are confirmed already-merged.
- **Full deep re-enrichment now running** across all 6,401 + 4,211 BD-relevant orgs (Developers/Architects/GCs/Competitors/Buyers/KorClients) — so by the Tuesday intel deliverable, every target in §5 will carry fresh, web-verified narrative + people + signals on the clean graph, not the pre-cull state.

**How to read this:**
- **kp=N** — number of KOR projects on record with that org (warmth signal). ✅Deltek = linked Deltek client.
- **[conf: High/Med/Low]** — confidence in the *name resolution* (the meeting audio garbled many names).
- Names in **bold** are the corrected/confirmed spellings; the transcript's garbled version is shown in *(quotes)* where useful.
- ⚠️ = an intel flag to read before acting.

> **Already actioned & verified (live CRM engagement IDs as of 2026-06-20):** the meeting's pursuits are **live in the CRM** — RBI **#4**, Pinnacle International **#11**, M'Akola **#12/#19**, Purpose Driven **#15**, Mundi **#61**, Bosa Development **#70**, Graham/Richmond Hospital **#91**, Open Form **#92**, Squamish/Nch'ḵay̓ **#93**, plus Rory's modular pair **Emerge Modular (Greg Zemrau) #95** and **Evantra Developments #97**. *(RBI moved from #90 → #4 when the session's dedup remapped the org to canonical #14.)* Names were also cleaned in the Biz Brain (dedup merges, reclassify, aliases, contacts). Full detail in **§10 (Brain)** and **§11 (CRM)**.

---

## 1. Firm-level actionables (lists, qualifications, proposals)

| # | Action | Owner | Notes |
|---|--------|-------|-------|
| 1 | **Get on the K-12 School Seismic (SMP) qualified-suppliers list** — requires **EGBC SRG (Seismic Retrofit Guidelines) training** by a registered engineer | **JM** takes the course; **Ian** confirms details | 412 seismic-retrofit gov't jobs / ~400 projects in their DB. Course offered ~once a year; EGBC mails continuing-ed notices (Kevin: no need to phone EGBC). **Kevin declined** to be the engineer. Was supposed to be done last year (Chelsea). On qualifying → sign NDA → ingest their project DB into the brain. |
| 2 | **Get on the Fraser Health list** | John (has a Fraser Health contact to phone) | One opportunity/year, window-based; they send a "list is coming" notice. |
| 3 | **Get on the BC Ferries list** | TBD | Continuous workload, strong gov't fees, lots of seismic-retrofit work. Open Q: marine qualification required? |
| 4 | **TransLink introductions** | TBD | "Huge, right in our backyard." Same marine/qualification question. |
| 5 | **Ministry / big design-build (Graham-style) qualification requirements** | **Ian** to research | Rory's Q: what list/quals to play on big-fee design-build? Can a client/ministry contact sponsor us in? |
| 6 | **Add an "additional services" appendix to Canadian proposals** (already on US proposals) | Jim/John | Out-of-scope items (crane footings, water-meter vaults, core-hole review, seismic instrumentation, supplementary schedules) at pre-agreed fees → negotiating leverage + upsell. Maintain two templates (US/Canada). |

## 2. The "Biz Brain" + CRM + process

- **Name the tool** — undecided (candidates: "Biz Brain," KIA, KSA, ICE, Frank).
- **Ian to deliver:** meeting synopsis + an **action sheet broken into 3–4 "packages"** (e.g. *reconnect existing clients*, *think-outside-the-box new sectors*, *regional pushes*); assign 2–3 people per package. Intel promised "by Tuesday."
- **CRM build-out over the weekend;** assign each lead to an owner and **lock it** so two people don't call the same contact.
- **Plug into the brain:** the GIS contact's company (Omar → Ian), the Broadway/SkyTrain rezoning+DP list, and all corrected companies/people below.

## 3. Website overhaul — decision at Monday partnership meeting

- Make it **less people-centric, more work/projects/breadth**.
- **Address the "JB issue"** that surfaces in web searches and is hurting pursuits (came up with Shape Properties + others) — likely remove some/all partners; decided at partnership level.
- **A dedicated page per region** (Vancouver LM, Island, Okanagan, Alberta, US) highlighting that region's experience.
- Reframe "Partners/Principals" → **"Leadership,"** featuring **client-facing/regional leaders** (Omar, Conor, Islam, Jason, Kevin, Rory…) rather than every partner.
- Lots of project images; populate it; speed it up; map at the bottom. **Staff section still live — needs fixing.**

## 4. Marketing / promotion

- **Social media automation (Ian to build):** LinkedIn auto-post a project every ~2 days (give Ian access); **get on Instagram** (architects/developers are active there — KOR has none). Benchmark: *Toshiro*, head of BD at WSP, who posts constantly.
- **Awards:** have the brain find awards to apply for and auto-apply (Henry used to win awards).
- **Lunch-and-learns** at developers/architects — most effective name-recognition tool (Henry ran these).
- **Client appreciation events** — consider.
- **Marketing capacity:** 1-day course, or hire a **BCIT marketing intern** (2-week placement); or use the brain as "VP of Marketing."
- **Signage — Griffin owns it** (with Tyler/Kenny). New-site sign opportunities: **West 10th "Nexus"** (UBC corridor, starts next week) and **10th & Highbury "Sightline"** (if Haber agrees). Rory wants roll-ups (fabric ones exist; Rory also has a sign-maker buddy) → coordinate with Griffin. Field crews + contractors are happy with Griffin's field system — keep it responsive.

---

## 5. Target lists by region (corrected names → owner → action)

### Vancouver Lower Mainland — Omar (lead), John, Jim
- **Pinnacle International** [conf: High] — kp=8, ✅CL00333. Work in Alberta, new San Diego properties, Vancouver business; warm but stale. → John + Omar meet **Michael De Cotiis** *("Mike Dakotis")*, President/owner. ⚠️ See Pinnacle/Onni note in §7.
- **Bosa Development** [High] — kp=78, ✅CL00047. Reconnect **Matt Patzer** (VP Construction, mpatzer@thinkbosa.com); meet **Paul Lamme** *("Paul LeMay")*, VP Development **Bosa San Diego**, in California.
- **Bosa Properties** [High] — kp=44, ✅CL00048 (separate entity). **JB** to set up lunch ("we got zero from Bosa Properties").
- **Intergulf Development Group** [High] — kp=8, ✅CL00204. One job under construction; John knows the "top dog"; an **Axiom** contact intro'd a PM.
- **Ledcor** [High] — kp=10. Soft intro via existing **TELUS** work (the **Telus-via-Ledcor** link is a real record in our data); backdoor connection in Edmonton; partner up.
- **Omicron Development Inc.** [High] — kp=4, ✅CL00305. Soft intro, already doing TI; chase light-civil/infrastructure (Conor via **Bruno**).
- **Graham (Construction)** [High] — **AJ** (rebar) intro'd; Graham is GC on **Richmond Hospital** (likely phase 2 & 3). Set up meeting w/ Graham + **Bert**. Graham knows someone at **Ellison** who'll intro to **Bird**.
- **"BD"** (developer w/ a farm/light-industrial division; did light industrial on "Ramer," TI at Station Square) — via **Rick**; pursue another division (leasing/light industrial). *[name not yet resolved — verify with Omar]*
- **UBC ecosystem** — **Manit's** wife teaches a UBC course; ~100 UBC buildings coming; Omar meeting **Emma** next week. **JM** to work the UBC development side (an engineer there had major issues → opening). **Jack** (from Potem) intro to **SFU** and **UBC Trust** — lunch w/ UBC Trust in July.
- **ANH** — Omar teaming ANH with **AJ** (a placer w/ his own company) so ANH can compete with **LMS** on development work.
- **Mundi Hotel Enterprises** *("Mundai Group")* [High company / Low person] — Kamloops; President **Ron Mundi**. Not yet in our DB. **Rory Allen** there (ex-"CDA" → Tim Properties [folded] → Mundi). KOR did a hotel for them; hotels across the interior, expanding to Vancouver/Burnaby, lots in Alberta; **big downtown Vancouver tower** floated. Omar intro'd **Adrian (EGC)**. Sent a rental/wood-frame proposal (with Conor). **Alan** willing to share competitor fee data (KOR fees ran low in Edmonton).
- **Squamish Nation → Nch'ḵay̓ Development Corp** [High] — **Jacob Lewis III (Xayil)**, Director of Community Development. Omar coffee **July 8** (relationship only, no work talk); then meet the dev team.
- **M'akola Development Services** *("Makola/Mákola")* [High] — KorClient, ✅Deltek (Indigenous housing, Victoria, office here). Target the dev lead (no "VP Development" title; closest is **Kaela Schramm**, Dir. Projects & Planning — the *"McCullough girl"* Omar met at ULI). Also a **City Spaces** contact (didn't return call).
- **GIS consultant** (met at ULI) — does property/"open quake" studies for City of Vancouver + City of Victoria; can tip where cities plan to build (residential/industrial/schools/hospital/fire/police). → Omar to give Ian the contact; plug into brain.
- **PCI Developments** [High] — Cambie & Broadway tower. Omar has contact via **Brad Howard** + a PM + site super + VP-Dev phone (via **Jacob** of **Urban One**); door not shut, not answering → phone Brad again.
- **Onni Group** [High] — #38949. **Jason** setting up a meeting; reconnect with **Mr. Morris / the Morrises** = **Morris De Cotiis** (Onni principal, construction & property mgmt; #9303). Onni is run by the late founder Inno's sons — **Rossano** (President, #1406), **Morris**, **Giulio**, **Paolo**. ⚠️ *Distinct from "Bonnis Properties" (#70958, kp=30) — don't conflate.* (Onni is the De Cotiis-family sibling firm to Pinnacle — see §7.)
- **Shape Properties** [High] — ✅Deltek. "Nothing moving, don't want to meet"; Omar will phone anyway. (JB issue surfaced here.)
- **RBI Group of Companies** [High] — kp=6, ✅Deltek. New-ish client, cash-rich, buying property, heavy **WSP** user. KOR building **Richmond Hotel**; got **137th St**; likely **Cobalt Hotel**; **Edmonton hotel** drawings coming; lots in interior. → pitch re-engineering his WSP projects for profitability.
- **VGM Group / BMZ / Gary / Suki** *(spellings unverified)* — cash-rich developer network bidding low on receivership deals; **Bonnie** bought Hudson's Bay + Atmosphere. Relationship intel.
- **McAllister** ("Let Mac") — tougher revisit. **Beto** — go meet (Resident is their biggest tower). **Charmaine** — arrange a visit re: their biggest tower. *[Beto/Charmaine unresolved — verify with Omar]*
- **Axiom Builders** [High] — kp=12, ✅CL00527. Argentina contact helping; set up Axiom Calgary meeting; team on Vancouver Island work.
- Omar in contact with many **unknown Vancouver-office architects** — broaden the net.

### Vancouver Island — Rory (lead)
- **Merrick Architecture** [High] — ✅Deltek; has an Island office. John knows someone; get warm intro to Victoria; Rory + John go.
- **Wensley Architecture** [High] — Victoria office; principal **Neil Jacobsen** *(verify — not in DB; Neil Banik retired, was him + Barry Way)*. Rory did a Wensley job in Duncan (**Paddle Road**).
- **Kerkhoff Construction** (kerkhoff.ca) [High] — already in brain: KorClient #186 (Kerkhoff Development) + Developer #70544 (Kerkhoff Construction). Was GC on the Paddle Road job; KOR got paid out when Kerkhoff didn't pay. The new owner Rory called *"Ridge/Bridge North America"* = **North American Development Group** (#54449, already linked to Kerkhoff in the brain).
- **The Lark Group** [High] — Surrey; CEO **Larry Fisher**. ⚠️ **In the brain but duplicated** — #53694 "The Lark Group" (Developer), #72260 "Lark Projects Ltd." (GC, BC Housing-confirmed), plus #55097/#27243 ("Unknown"). Needs **merge + reclassify**, not a new record. Reconnect; **Jason** works with them (Abbotsford); KOR did **Legion Veterans Village** (2nd tower "Lucent" sold to **Landa Global**). The founder-death/sale Rory recalled is **unconfirmed** — verify before referencing.
- **City of Nanaimo** — municipal-direct work (steady stream).
- **Farmer Construction Ltd.** (Victoria, since 1951) [High] — biggest Southern-Island GC; approach. (**Campbell Construction Ltd.** also big.)
- **Dino** *(co. = "Moniz"? — JB has long Moniz relationship; John designed Dino's brother **Gab Carrie**'s house)* — flagship tower (architect **Perkins + Will**) being sold; selling Granville + Kingsway sites; reducing portfolio; seeking partners. Keep warm. *[Dino's surname/company unresolved — verify with Rory/JB]*
- **Modular push (Rory's headline):**
  - **Greg Zemrau — Emerge Modular** [High] *("Gregg")* — **existing KOR client (#111)**; KOR ≈ his prime consultant. **(Rory's update — now in CRM as engagement #95.)** Live: White Rock Marine Dr (2 fourplexes), Vancouver fourplex (in shop), **Duncan ~4-storey by the university (proposal owed)**, retrofit Greg's warehouse, plant on mainland/island, upgrade Alberta plant. **Simon** learning **Autodesk Advanced Steel** → charge for shop drawings. Greg wants developer partners with sites+funding; he supplies modules. CMHC change: **5% down** on modular (even >$1M, transfers to homeowner) — lean in.
    - **Evantra Developments** [High] — #203, **existing KOR client** (eng #97). The Langford/Victoria developer that several of Greg's KOR jobs come in through — the developer-partner side of the modular pipeline. Rory's lead; keep Emerge ↔ Evantra linked.
  - **Yellowridge Construction Ltd.** *("Yellow Ridge")* [High] — **existing KOR client (#115)** + Yellowridge Design Build (#70585); Port Moody design-builder, leading the **Defence Canada** design-build bid (off-site/modular/panelized). (We have a relationship here already — not a cold approach.) Rory met twice; rubric rewards 2 projects where the whole team worked together; Defence Canada prioritizes off-site methods.
  - **WHB Group** *("WWBH")* [Med] — New Westminster formwork/rebar; principal **John Wu** (Chinese-group modular connections; ≠ the BC Housing "John Wu" in our DB).
  - **Wilson Chang** *(vs. "Wilson Shen")* [Low] — Island modular operator. ⚠️ Our DB has a **Wilson Chang (architect)** — may be a *different* person; don't merge. The **Thind** developers project could be a large modular tower in Burnaby (steel modules from China, Canada Builds funding) — see ⚠️ Thind note in §7.
- **Website:** add modular capability (carefully — proprietary).
- **Island sites:** Parksville (out of ground), Duncan (pouring L1 next week), **Langford** (keep KOR name off until it's a go — Denbrook/liability sensitivity).

### Okanagan & Interior — Conor (lead)
- Most active BD; big pipeline if it moves. Goes to Kelowna (took Simon last time).
- **Orchard Park Properties** *("Apriano")* [High] — principal **Apriano Meola** (ex-Pinnacle/Onni; co-founder with Anthony Beyrouti). Built **Water Street by the Park**, Kelowna (3 towers). John met him pre-Jeremy → introduce Conor.
- **Losing the Penticton ("Politano") office space** — decided to let it go (confirmed).
- Conor to give Ian his Vancouver/Interior contact list; co-pitched the Mundi/RBI rental with Omar.

### Andrea — get more front-facing (explicit ask)
- **Arno Matis Architecture (AMA)** [High] — ⚠️ the transcript's *"Amy/Aromatis/Larno/Mattis"* are **all one firm / one person: Arno Matis** (founder; ex-Bing Thom). Does heavy rezoning + some design/construction; works with clients who also use **RJC**. Amy/AMA gave KOR's name to such a client → go meet AMA. (Andrea building rapport — helped Arno with a kitchen reno.)
- **"Griffin" (architect)** — *distinct from Griffin the field lead.* Wanted to wait (Marcelo left, overloaded) but would meet; Andrea + others. *[surname unresolved]*
- **Marcela/Marsa** — left, "resurrected herself," from Chile; may bring something.
- **Purpose Driven Development** [High] — ✅Deltek; founder **Carla Guerrera**; contact **Annelise van der Veen** *("Dan Van Mueller/King")*. All-women consultant teams. Reviewed/awarded KOR the **Church of Epiphany** project (KOR has it per the meeting; not externally findable — internal record stands).

### USA / California — John (lead), Jim
- John to California in ~2 weeks; gave Ian **5 existing-relationship developer companies** to scout for pipeline before meetings (only **Greystar** named — San Diego, key people **Bob Faith** [Founder/CEO], **John Wilbeck** [MD Development, Canada]).
- Jim → a **California qualified-leads report** from the brain.

---

## 6. New-sector / "outside the box" ideas to chase
- **Light industrial, light/small civil infrastructure** (KOR has the technical chops).
- **Steel-stud / suspended-ceiling / TI add-on engineering** — high margin, "rinse & repeat"; offer as a proposal tick-box. JB, Kevin, Rory already do some. Garth at **Anthem** → TI in RJC's building (~$10k, ~$8k profit). **Decide who owns this line.**
- **Water features / pools** — underserved niche (old **Rockingham** did the whole package incl. Dept. of Health approvals); good fees.
- **Utilities/telecom:** **GS / Jerry Sayers** (cell-site work for Rogers); **BC Gas / Fortis** ministry work.
- **Marine:** BC Ferries, TransLink (qualification TBD).
- **Modular/off-site** (Defence Canada signal via Yellowridge).

---

## 7. Intel flags (read before acting)

1. **Pinnacle vs Onni vs Amacon — the boardroom confusion explained (verified, now fully in the brain).** Three De Cotiis brothers split from the family holding company (**Viam Holdings**) in the 1990s into **three sibling firms**:
   - **Pinnacle International** (#53665) — **Michael De Cotiis** (President & CEO, #1399), the last surviving founding brother, **alive/active**. *(Also the BC-licence "Person Responsible" at Mondiale Development #20962.)*
   - **Onni Group** (#38949) — founded by **Innocenzo "Inno" De Cotiis (1937–2020)** (#3757); now run by **his sons**: **Rossano** (President, #1406), **Morris** (#9303 = the meeting's *"Mr. Morris"*), **Giulio** (#9304), **Paolo** (#9305).
   - **Amacon** (Developments #169 / Construction #71060) — the third brother (**Amalio**) branch: **Marcello** (CEO/President, #901), **Marc** (President, #1941), **Donato** (Owner/VP, #902), **Italo** (Chairman, #1940), **Lilliana** (Hospitality, #903).
   Michael is **uncle** to the Onni/Amacon next-gen. That's why the room said *"his dad died… that's his brother… it's not Pinnacle"* — they were half-remembering Inno's 2020 death and the cousin firms. **No recent (2024–26) De Cotiis death.** Treat all three as adjacent — a strong Pinnacle (Michael) relationship sits next to Onni + Amacon decision-makers.
2. **Thind Properties is distressed.** kp=17 historically, but **Eclipse Brentwood is under creditor protection (~$189M owed to KingSett), 2024-25.** The "biggest modular tower in Vancouver" floated with the *"old Thind developers"* is real but **high-risk** — pursue with eyes open; the steel-modules-from-China / Canada Builds angle is unverified.
3. **"Amy at AMA / Aromatis / Larno / Mattis" is one person** — **Arno Matis**. Don't create four contacts.
4. **Lark Group is already in the brain — and duplicated** (Developer #53694 + GC #72260 + two "Unknown" rows). This is a **merge/reclassify** job, not an add. (Earlier draft wrongly called it net-new.)
5. **Onni ≠ Bonnis Properties** (kp=30) — two different orgs in our data; keep separate.
6. **Yellowridge & Kerkhoff are existing KOR clients** (#115, #186) — the Defence Canada modular bid and the Paddle Road story both run through relationships we already have, not cold contacts.

---

## 8. Confirmed people directory

| Person | Role / org | Status |
|---|---|---|
| **Michael De Cotiis** | President/owner, **Pinnacle International** | web-confirmed |
| **Matt Patzer** | VP Construction, **Bosa Development** — mpatzer@thinkbosa.com | in DB |
| **Paul Lamme** | VP Development, **Bosa San Diego** (@thinkbosa.com) | web-confirmed |
| **Jason Turcotte** | VP Dev Cressey / Pres Darwin / Principal **OpenForm** — jturcotte@cressey.com | in DB |
| **Chris Wilkinson** | left Darwin (~1 mo ago) → development at **OpenForm** | transcript/web |
| **Bob Faith** | Founder/CEO, **Greystar** | in DB |
| **John Wilbeck** | MD Development (Canada), **Greystar** | in DB |
| **Annelise van der Veen** | Mgr Strategic Projects, **Purpose Driven Development** (founder Carla Guerrera) | web-confirmed |
| **Jacob Lewis III (Xayil)** | Director of Community Development, **Nch'ḵay̓ Development Corp** (Squamish) | web-confirmed |
| **Apriano Meola** | Co-founder, **Orchard Park Properties** | web-confirmed |
| **John Wu** | **WHB Group** / Lower Mainland Forming (formwork/rebar) | web (Med) |
| **Kaela Schramm** | Dir. Projects & Planning, **M'akola Development Services** | web (Med) |
| **Greg Zemrau** | **Emerge Modular** (modular fabricator, KOR client #111) — *the meeting's "Gregg"* | in DB (eng #95) |
| **Morris De Cotiis** | Principal, **Onni Group** (#9303) — *the meeting's "Mr. Morris"* | in DB |
| **Rossano De Cotiis** | President, **Onni Group** (#1406) | in DB |
| **Marcello De Cotiis** | CEO/President, **Amacon** (#901) | in DB |

## 9. Verify directly (don't guess further)

- **Chris Hall** (Polygon) — Rory's real retiring contact, but no public footprint (Polygon CEO is Neil Chrystal). Confirm with Rory.
- **Rory Allen** (Mundi Hotel Enterprises) — no public trace; confirm with Omar.
- **Neil Jacobsen / Neil Banik / Barry Way** (Wensley Victoria) — not in DB; confirm with Rory.
- **"BD" developer**, **Beto**, **Charmaine**, **Dino**'s surname/company, the **GIS consultant**'s firm, **VGM/BMZ/Gary/Suki** — confirm spellings/identities with Omar/Rory before brain ingest.
- ~~**Gregg** (modular)~~ — **RESOLVED 2026-06-20: Greg Zemrau, Emerge Modular (#111)**; developer side = Evantra Developments (#203). Both live in CRM (eng #95/#97).
- **Wilson Chang** — confirm whether the Island modular operator is the same as our DB architect (likely not).

---

## 10. Biz Brain ingest (applied & verified 2026-06-19)

The brain already held ~all of these orgs (often duplicated), so the meeting yielded little net-new. Batch **applied & verified** (details + audit trail in `output/brain-ingest-2026-06-19/`):
- ✅ **3 dedup merges** — Lark ×2 (stray "Unknown" rows folded into Developer #53694 / GC #72260, arms kept separate), Nch'ḵay̓ #70915 → #54983. Allowlist override + post-audit done.
- ✅ **1 reclassify** — Mundi #70864 `Unknown → Developer`.
- ✅ **4 aliases** — "Mundai Group", "Yellow Ridge", "Open Forum", "Open RD" → their canonicals (all resolve).
- ✅ **2 contacts** — Annelise van der Veen (#13816 → Purpose Driven), Jacob Lewis III (#13817 → Nch'ḵay̓), affiliations wired.
- ✅ **De Cotiis family fully enriched (2026-06-20)** — Michael (#1399, Pinnacle) plus the Onni branch (Inno 1937–2020 #3757; sons Rossano #1406, Morris #9303, Giulio #9304, Paolo #9305) and the Amacon branch (Marcello #901, Marc #1941, Donato #902, Italo #1940, Lilliana #903) are all in the brain with affiliations wired; duplicate person rows (Don/Mike/Donato dups) retired. See §7 #1.
- ⏭️ **WHB Group / John Wu — skipped** (IntelPerson key is name-only → would fuse with the existing BC-Housing "John Wu"). *(Note: the M1 identity-anchor re-key shipped 2026-06-20 now keys people by email→LinkedIn→name+org, so this is safer to revisit.)*

**Brain-wide hardening since the meeting (2026-06-21):** beyond these meeting-specific writes, the whole module was cleaned — 50k→17.8k warm orgs (commodity-vendor cull, reversible), 100+ dup clusters merged incl. the BC health-authority cluster (32→9), 81 Kind corrections, MPI FK completion, and the resolver dup-creation cycle closed (fuzzy pre-create match). Full deep re-enrichment of all BD-relevant orgs is now running. Net effect for these pursuits: the targets sit in a deduped, correctly-typed, FK-sound graph and will carry fresh verified intel by the Tuesday deliverable. All IDs in this doc re-verified 2026-06-21.

---

## 11. CRM — pursuits loaded this session (applied & verified 2026-06-19)

The meeting's active pursuits were loaded into the CRM, **deduped against the existing engagements** already imported from Jim's tracking spreadsheet (so nothing was duplicated). Stage 1 = *Drafting/active*. These are live records now — pick them up under each owner.

**New engagements created (org had none):**

| Eng # | Org | Owner | Region | What / next step |
|---|---|---|---|---|
| **#4** | RBI Group of Companies (org #14) | Omar | Van/LM | *(was #90; org remapped to canonical #14 in the dedup.)* Hot active client, kp=6 (last 2026-03-03). Richmond Hotel (u/c), 137th St (awarded), Cobalt Hotel (likely), Edmonton hotel (drawings incoming). Pitch re-engineering their WSP work. |
| **#11** | Pinnacle International (org #53665) | Jim | Van/LM | kp=8 (last 2026-01-27), CL00333. John + Omar to meet **Michael De Cotiis** (President & CEO). De Cotiis family fully mapped — see §7 #1. |
| **#91** | Graham Construction | Omar | Van/LM | Intro via AJ (rebar). Pursue **Richmond Hospital Ph2 & Ph3**; meet Graham + Bert; Ellison → Bird intro. |
| **#92** | Open Form Properties | Jim | Van/LM | 3 existing projects (kp=3, last 2026-04-13). Jim → lunch **Jason Turcotte** (next month), Kevin to join; chase the Open RD (auto/commercial) division. *Contact: Jason Turcotte, jturcotte@cressey.com.* |
| **#93** | Nch'ḵay̓ Development Corp (Squamish) | Omar | Van/LM | Coffee with **Jacob Lewis III** on **2026-07-08** (relationship-build), then meet dev team. *Contact: Jacob Lewis III, Dir. Community Development.* |
| **#95** | Emerge Modular (org #111) | Rory | Island | **Rory's update — "Gregg" = Greg Zemrau, Emerge Modular** (KOR client). Modular pipeline: White Rock, Vancouver fourplex, Duncan 4-storey (proposal owed), warehouse retrofit, plant upgrades. Simon on Advanced Steel shop drawings. |
| **#97** | Evantra Developments (org #203) | Rory | Island | Langford/Victoria developer; the developer partner several of Greg Zemrau's KOR jobs come in through. Keep Emerge ↔ Evantra linked. |

**Activities logged on existing engagements (no duplicates created):**

| Eng # | Org | Owner | Meeting note added |
|---|---|---|---|
| **#61** | Mundi Hotel Enterprises | Omar | Rental wood-frame proposal sent (with Conor); expanding to Vancouver/Burnaby; contact Rory Allen; Alan to share competitor fees. |
| **#15** | Purpose Driven Development | Conor | **Church of Epiphany awarded to KOR** (Andrea); more in pipeline (all-women teams). |
| **#70** | Bosa Development | Jim | Reconnect Matt Patzer (VP Construction); meet Paul Lamme (VP Dev, San Diego). |

**Not yet loaded (need an owner/decision first):** the broader reach-out list in §5 (Merrick, Greystar/California, Polygon/Chris Hall, Apriano/Orchard Park, Lark, BC Ferries/TransLink lists, etc.) and the Duncan modular proposal (no clear buyer canonical org — it's Gregg's fabrication shop). Say the word and I'll stage these as engagements too.

---

*Generated from the meeting recording; corrected names sourced from the BD brain (canonical orgs / Deltek) and web verification. Brain + CRM writes applied 2026-06-19 with post-audit; low-confidence names flagged for human confirmation rather than auto-ingested.*
