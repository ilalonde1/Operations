# Developer BD Enrichment Report - 2026-06-20

Mission: identify the people who engage/select the structural engineer at 11 target real-estate developers, verify emails via Hunter, and capture each developer's active BC/AB/CA pipeline.

## Summary

- 11 developers researched; 42 people captured; 34 with an email (24 Hunter-verified, 8 PatternInferred, 2 web).
- 1 developer (Keyara Corp.) has no discoverable web/registry footprint - flagged for source-data verification.
- 2 developers are in financial distress as of early 2026 (Westbank under lawsuit pressure; Maskeen in active receivership) - treat outreach accordingly.

## Per-developer findings

### 70911 Westbank Corp. (westbankcorp.com)
- High-rise resi / mixed-use, Vancouver. Domain is accept_all (first-name-only pattern: troy@, brad@, danny@), so Hunter scores are moderated.
- Top contact: Troy Petry, VP Construction (91). Also COO Brad Jones, Sr Dev Mgr Danny Ross.
- Pipeline: Oakridge Park, 1684 Alberni, Joyce (5050 Joyce), Magnolia (North Van). Exited Senakw. VP Development seat appears unfilled; firm under financial pressure (Mabberley lawsuit, Feb 2026).

### 70099 Cadillac Fairview (cadillacfairview.com)
- Strongest result. first.last pattern, hard-verified Hunter scores 98-99; 7 contacts.
- Highest-value SE selectors: David Lee (Sr Director PM Development, LEED AP/PMP, 99) and Ryan Blatt (VP PM Development, 98). Josh Thomson (SVP Dev) has a structural-engineering background - warm angle.
- Pipeline: BC = 555 W Cordova office tower proposal; AB = 490-unit Calgary rental towers (under construction, 2028). Lou Ficocelli (VP Leasing, BC-based) is a relationship entry point.

### 71236 Highstreet Ventures (gohighstreet.ca)
- Kelowna multi-family, wood-frame / net-zero focus. Pattern {f}{last}; 7 contacts, mostly verified.
- Top contact: Tony Kudryk, VP Construction (PatternInferred 55 - ZoomInfo-confirmed prefix but not returned directly by Hunter). Verified emails: Pino Mancuso (President, 98), Jeff Wilkins (Regional Mgr BC, 98), Scott Butler (CEO, 97), Mike Kristiansen (Precon Mgr, 94), Eric Delorme (Dev Mgr, 93), Sean Price (Field Ops, 91).
- Pipeline: Ascent (Kelowna), City Gate (Langford). Strong structural-engagement profile.

### 53419 Keyara Corp. (NO DATA)
- Exhaustive search (web, LinkedIn, OpenCorporates BC, Hunter domain variants, REW/Livabl/BuzzBuzzHome, direct domain fetches) found no public footprint. keyaracorp.ca/.com unreachable; keyara.ca is an unrelated beauty brand.
- ACTION: verify orgId 53419 source data - possible shell/dormant entity, different DBA, or data-entry error. Needs BC Registry lookup by incorporation number.

### 54841 Maskeen Development (maskeen.ca)
- Surrey/Fraser Valley multi-family. Only verified email: Pritpal Sivia, Development Coordinator (99). Owner Jagdip Sivia and two site/project staff found but no confirmed emails.
- CRITICAL: firm in active receivership as of early 2026 (KSV over Maskeen 177 / Bentley Rd after 13.7M MCAP default; FTI over Langley high-rise; Victory project also in receivership). Outreach caution warranted.

### 69692 Bold Properties (bold.ca)
- Vancouver resi developer. Pattern {first}{l}. Tommy He (President, 99, verified); Hao Min (CEO, PatternInferred 55).
- Pipeline: portfolio 435M+ across 9+ Metro Van projects (Synchro, Edgestone, Larchwood, Galt, Anchor) - all shown completed/past; no announced new pipeline found.

### 55107 JTA Development Consultants (jtadevco.com)
- Vancouver development-management firm (not a principal developer - a dev manager/PM consultant). Pattern {first}; 5 contacts.
- Verified: James Tod (Founder, 95), Helen Williams (Asst PM, 98). PatternInferred: Breanna Martin (Sr Dev Mgr), Nixon Tsang (Dev Mgr), Andrew Hawryluk (Associate).
- Pipeline: 11+ Broadway Plan sites plus numerous Vancouver mixed-use rental projects; also New West and Alberta. High SE-engagement frequency.

### 54861 Jayen Properties (jayenproperties.com)
- Metro Van multi-family, part of RBI Group. Hunter returned no personal emails / no determinable pattern.
- Primary SE selector: Trevor Massey, Director of Construction (LinkedIn found, no email). Also Sukhi Rai (President), Krinder Rai (VP), Raj Khangura (CFO).
- Pipeline: Park and Maven (Coquitlam), LEVEL (Burquitlam, sold out); future 24-storey condo+hotel (Vancouver) and 36-storey office+student housing (Surrey).

### 54793 Landvision Group (landvisiongroup.ca)
- Surrey family-owned multi-family, very staff-opaque (no team page). Only Roch Chevrier (VP Construction) surfaced, from secondary sources, unconfirmed; no Hunter emails.
- Pipeline: broad BC footprint - Millstream (Langford), Francis Collection (Richmond), Croydon 28 (S Surrey), plus Coming Soon across Langford/Victoria/Delta/West Kelowna/Richmond.

### 54691 Kind Development Group (kinddevelopments.ca)
- Vancouver boutique/luxury resi (formerly TCD / Trasolini Chetner). Pattern {first}; 3 verified emails.
- Rob Chetner (Founding Partner, 95) primary contact; Kerri Chetner (Partner, 91); Ben Bee (Partner, 84). Rob also a partner at Trasolini Chetner Construction Corp.
- Pipeline: EagleView Heights (Gibsons), McCleery and MaGee (Vancouver Southlands), Lakeview Village (West Kelowna); Hudson 8 coming soon.

### 54853 Heidelberg Materials Ltd. (heidelbergmaterials.com)
- Building-materials/cement company (formerly Lehigh Hanson), not a developer. Targeted for spec-influence roles. first.last pattern; 6 contacts, all verified.
- KOR angle: Ignacio Cariaga (Commercial Sustainability Director, P.Eng., 96) directly engages structural engineers on low-carbon concrete spec - primary relationship target. Tyler Thorson (GM BC Ready-Mix, 96) controls BC supply. Oliver Patsch (President/CEO NW Region, 95) is the top-level partnership target. Granit Gasi (Technical Sales Sustainability, 98), Jessica Pepin (Sales Ops, legacy lehighhanson.com 98), Azunna Adibe (Lab Supervisor P.Eng., 98).
- Initiatives: Edmonton CCUS plant (1.4B, federal funding, late-2026 target, first full-scale net-zero cement plant); EcoCem PLC rollout across BC; active BCRMCA/Concrete BC member.

## Method notes
- Emails verified via Hunter.io domain-search; confidence recorded as returned. PatternInferred = 55 where the domain pattern was confirmed but the specific person was not returned by Hunter. email=null where no pattern was determinable. No emails fabricated.
- Output JSON: developers.json (ASCII-only, validated, 11 orgs / 42 people).
