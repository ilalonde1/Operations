# QueueDrain Audit Results — 2026-06-13

## Summary
72 queues audited. 0 PASS, 2 WARN, 70 FAIL.

## FAILs (ingest will break)
### ab-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `The ProjectBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"description": "long-form description of the project scope, drivers, owner intent"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-commercial-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + procurement model + named incumbent if DEAD + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-commercial
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBrief"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"_providerName": "ProjectBrief",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named developer + tenant + structural typology + KOR's competitive angle"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-hospitals-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + procurement model identification + named incumbent if DEAD + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-hospitals
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "3-5 sentences. PURSUE/MONITOR/DEAD/DISCOVER verdict + reasoning + named competitor incumbent if applicable."`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-postsecondary-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent structural if DEAD (REQUIRED) + institution capital plan pipeline + KOR competitive angle (mass timber / lab / seismic) + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-postsecondary
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBrief"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"_providerName": "ProjectBrief",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named institution + 5-year capital plan context + structural typology + KOR's competitive angle"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-primes
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "PrimeConsultantResearch"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"description": "Prime consultant for {ProjectName}: {ArchitectFirmName}. {1-2 paragraphs explaining who, why this matters, KOR relationship status}. [providerName: PrimeConsultantResearch] marker is legacy — root _providerName is authoritative."`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-recreational-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER/DUPLICATE verdict + named incumbent structural if DEAD (REQUIRED) + procurement model + typology (pool/arena/field-house) KOR specialty match + phase scope (current locked vs future open) + KOR competitive angle + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-recreational
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBrief"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"_providerName": "ProjectBrief",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "3-5 sentences. PURSUE/MONITOR/DEAD/DISCOVER verdict + named architect + long-span / pool / arena / field-house specialty match + competitive angle vs incumbent if applicable."`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-residential-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER/DUPLICATE verdict + named incumbent structural if DEAD (REQUIRED) + KOR developer relationship status (existing client or BMZ legacy = call this out) + typology match (high-rise/mid-rise/mass-timber) + phase pipeline (Phase 1 DEAD may open Phase 2) + KOR competitive angle + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-residential
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBrief"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"_providerName": "ProjectBrief",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named developer + BMZ legacy reference if applicable + structural typology match + competitive angle"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-schools-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent if DEAD + KOR's prior SD relationship if any + BMZ legacy reference if applicable + competitive angle + warm-intro path + named target + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-ab-schools
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER verdict + reasoning + KOR's prior SD relationship if any + competitive position"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-housing-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent if DEAD + BC Housing partnership model + KOR competitive angle + warm-intro path + named target decision-maker + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### bc-housing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + named operator + KOR fit"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### contact-enrichment
- Defect: `outputs/refresh-person-{id}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `"_providerName": "PersonBrief",   "displayName": "<verbatim from input — the person's name>"`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### contact-finder
- Defect: `outputs/refresh-person-{personId}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `"_providerName": "PersonBrief",   "displayName": "<verbatim>"`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### defense-military-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent if DEAD + KOR competitive angle + warm-intro path + named target decision-maker + first move"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### defense-military
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + DCC prime path + security clearance considerations"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### ellisdon-deep-dive
- Defect: `outputs/refresh-org-22257.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### engagement-plans
- Defect: `outputs/refresh-person-{personId}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `"_providerName": "PersonBriefHoning",       "displayName": "<verbatim from input>"`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### firstpass-buyers
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### firstpass-competitors
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### firstpass-developers
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### firstpass-us-orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### gap-fill-orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### graph-completion-deep
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.

### graph-completion
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.

### honing-architects-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-architects-tail
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.
- Defect: `No ``_providerName`` field appears in the org item example.`
- Must become: Add `"_providerName": "FirmNarrative"` or `"_providerName": "FirmNarrativeHoning"` at the item root.

### honing-buyers-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-buyers
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.
- Defect: `No ``_providerName`` field appears in the org item example.`
- Must become: Add `"_providerName": "FirmNarrative"` or `"_providerName": "FirmNarrativeHoning"` at the item root.

### honing-competitors-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-competitors
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.
- Defect: `No ``_providerName`` field appears in the org item example.`
- Must become: Add `"_providerName": "FirmNarrative"` or `"_providerName": "FirmNarrativeHoning"` at the item root.

### honing-developers-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-developers
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.
- Defect: `No ``_providerName`` field appears in the org item example.`
- Must become: Add `"_providerName": "FirmNarrative"` or `"_providerName": "FirmNarrativeHoning"` at the item root.

### honing-gcs-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-gcs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.
- Defect: `No ``_providerName`` field appears in the org item example.`
- Must become: Add `"_providerName": "FirmNarrative"` or `"_providerName": "FirmNarrativeHoning"` at the item root.

### honing-korclients-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### honing-people
- Defect: `outputs/refresh-person-{id}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.

### honing-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.

### honing-us-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### indigenous-projects-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBriefHoning",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "5-7 sentences: PURSUE/MONITOR/DEAD/DISCOVER verdict + named incumbent if DEAD + KOR competitive angle + warm-intro path + named target decision-maker + first move + respect for protocols"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### indigenous-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"korAngle": "PURSUE/MONITOR/DEAD/DISCOVER + warm-intro path + KOR fit"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### okanagan-orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### okanagan-people
- Defect: `outputs/refresh-person-{id}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `The PersonBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "PersonBrief"` or `"_providerName": "PersonBriefHoning"` inside `items[0]`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### okanagan-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `The ProjectBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"description": "long-form description of the project scope, drivers, owner intent"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### org-name-repair
- Defect: `outputs/refresh-orgname-{id}.json`
- Must become: Use `outputs/refresh-orgname-{numeric-id}.json`.

### orgs-architect-scout
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### orgs-buyers
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### orgs-gcs-partners
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### orgs-trip
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### people
- Defect: `outputs/refresh-person-{id}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `The PersonBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "PersonBrief"` or `"_providerName": "PersonBriefHoning"` inside `items[0]`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `The ProjectBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"description": "long-form description of the project scope, drivers, owner intent"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### us-projects-honing
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.

### us-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `"_providerName": "ProjectBrief"`
- Must become: Use `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"_providerName": "ProjectBrief",     "overallConfidence"`
- Must become: Add `"id": <number>` at the project item root.
- Defect: `"description": "<what the project is, owner, size, status — 3-6 sentences>"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### vanisland-orgs
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vanisland-people
- Defect: `outputs/refresh-person-{id}.json`
- Must become: Use `outputs/refresh-person-{numeric-id}.json`.
- Defect: `The PersonBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "PersonBrief"` or `"_providerName": "PersonBriefHoning"` inside `items[0]`.
- Defect: `"overallConfidence": 0.0-1.0,`
- Must become: Inside `items[0]`, make the first two fields `"displayName": "<verbatim from input>"` then `"_providerName": "PersonBrief"` or `"PersonBriefHoning"`.

### vanisland-projects
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.
- Defect: `The ProjectBrief record inside ``items[0]`` MUST be:`
- Must become: Add `"_providerName": "ProjectBriefHoning"` at the item root.
- Defect: `"description": "long-form description of the project scope, drivers, owner intent"`
- Must become: Add `"honingPass": { "verdict": "PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE", ... }`.

### verdict-stamp
- Defect: `outputs/refresh-project-{id}.json`
- Must become: Use `outputs/refresh-project-{numeric-id}.json`.

### vip-ab-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-architects-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-developers-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-gc-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-island-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-okanagan-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

### vip-van-deep
- Defect: `outputs/refresh-org-{id}.json`
- Must become: Use `outputs/refresh-org-{numeric-id}.json`.

## WARNs (won't fail today but will eventually)
### ab-projects
- Defect: `# Edmonton + Calgary Project-Depth Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### bc-ab-commercial-honing
- Defect: `# BC + AB Commercial Office — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-commercial
- Defect: `# BC + AB Commercial Office Towers — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-hospitals-honing
- Defect: `# BC + AB Hospital Construction — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-hospitals
- Defect: `# BC + AB Hospital Construction — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered batch with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-postsecondary-honing
- Defect: `# BC + AB Post-Secondary — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-postsecondary
- Defect: `# BC + AB Post-Secondary — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-primes
- Defect: `# BC + AB Prime Consultant Identification — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No explicit ``Do NOT call Workflow or Agent tools`` prohibition found.`
- Must become: Add `Do NOT call Workflow or Agent tools.` to the execution rules.

### bc-ab-recreational-honing
- Defect: `# BC + AB Recreational Centres — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-recreational
- Defect: `# BC + AB Recreational Centres — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered batch with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-residential-honing
- Defect: `# BC + AB Residential / Condo — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-residential
- Defect: `# BC + AB Residential / Condo Towers — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-schools-honing
- Defect: `# BC + AB Schools — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-ab-schools
- Defect: `# BC + AB School Construction — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-housing-honing
- Defect: `# BC Housing Projects — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### bc-housing
- Defect: `# BC Housing — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### contact-enrichment
- Defect: `Write SUMMARY after last item, check for next batch.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### contact-finder
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### defense-military-honing
- Defect: `# Defense / Military — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### defense-military
- Defect: `# Defense / Military Construction — Deep Research + DISCOVERY`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### ellisdon-deep-dive
- Defect: `# EllisDon Corporation — Deep Dive Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### engagement-plans
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### firstpass-buyers
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### firstpass-competitors
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### firstpass-developers
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### firstpass-us-orgs
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### gap-fill-orgs
- Defect: `# IDENTITY RULES — READ FIRST, NON-NEGOTIABLE`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### graph-completion-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### graph-completion
- Defect: `# KOR Graph Completion — Pursuit Link Resolution`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-architects-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-architects-tail
- Defect: `# KOR Org Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-buyers-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-buyers
- Defect: `# KOR Org Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-competitors-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-competitors
- Defect: `# KOR Org Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-developers-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-developers
- Defect: `# KOR Org Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-gcs-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-gcs
- Defect: `# KOR Org Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-korclients-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-orgs
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### honing-projects
- Defect: `# KOR Project Honing — Deep Second-Pass Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### honing-us-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### indigenous-projects-honing
- Defect: `# Indigenous Projects — Verification + Pursuit Play Honing`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### indigenous-projects
- Defect: `# Indigenous / First Nations Projects — Deep Research`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### okanagan-orgs
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### okanagan-people
- Defect: `# KOR Person-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered one with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### okanagan-projects
- Defect: `# All-Province Project Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered one with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### org-name-repair
- Defect: `# KOR Org Name-Repair Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### orgs-architect-scout
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### orgs-buyers
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### orgs-gcs-partners
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### orgs-trip
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### orgs
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### people
- Defect: `# KOR Person-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### projects
- Defect: `# All-Province Project Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### proponents
- Defect: `# Proponent Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### us-projects-honing
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### us-projects
- Defect: `# US West Coast Projects — First-Pass Research Drain (us-projects)`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `After the last item, write ``outputs/SUMMARY-batch-NNN.txt``: completed /`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No explicit ``Do NOT call Workflow or Agent tools`` prohibition found.`
- Must become: Add `Do NOT call Workflow or Agent tools.` to the execution rules.

### vanisland-orgs
- Defect: `# KOR Org-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### vanisland-people
- Defect: `# KOR Person-Refresh Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered one with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### vanisland-projects
- Defect: `# All-Province Project Drain — Terminal Sonnet Mission`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.
- Defect: `lowest-numbered one with no matching ``outputs/SUMMARY-batch-NNN.txt``.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### verdict-stamp
- Defect: `# KOR Verdict Stamp — Classify Existing Research (LIGHT pass)`
- Must become: Start the file with `# OUTPUT REQUIREMENTS — READ FIRST, NON-NEGOTIABLE` before any mission text.

### verify-flags
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.
- Defect: `No per-item tool budget or time limit found.`
- Must become: Add a bail-out rule: max tool-call budget or wall-clock time limit per item, then skip/move on.

### vip-ab-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-architects-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-developers-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-gc-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-island-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-okanagan-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

### vip-van-deep
- Defect: `No explicit instruction to re-scan ``inputs/`` after each SUMMARY and continue.`
- Must become: After writing `outputs/SUMMARY-batch-NNN.txt`, re-scan `inputs/` / list `inputs/batch-*.json` again and continue with the next un-summarized batch until none remain.

## PASSes
- None

