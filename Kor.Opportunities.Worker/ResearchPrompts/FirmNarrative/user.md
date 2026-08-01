Research {ORG_DISPLAY_NAME} (kind: {ORG_KIND}) for KOR Structural's BD platform. Today is {TODAY_UTC}.

You are doing a DELTA REFRESH, not from-scratch research. Here is what KOR's platform already knows about this organization:

CURRENT_KNOWLEDGE_BEGIN
{CURRENT_INTEL_JSON}
CURRENT_KNOWLEDGE_END

Your job:
1. CONFIRM what's still true (re-list existing facts that web search verifies).
2. UPDATE what's changed (someone departed, project advanced, capacity shifted, leadership rotated).
3. ADD what's new (people / signals / works / actions / risks not in CURRENT_KNOWLEDGE).
4. FLAG what's no longer true (a person showing as IsCurrent who has actually left  emit a corrective signal).

Use web_search liberally for recent news (last 12 months), leadership rosters, capital plan updates, new procurements. Don't repeat searches you can answer from CURRENT_KNOWLEDGE alone.

Focus on:
1. Current decision-makers for structural engineering selection (VP Facilities, Project Director, CPO Capital, etc.)
2. Recent leadership changes, hiring signals, office moves, M&A activity in the last 18 months
3. Active or recently announced capital projects / pipeline (names, values, sectors, structural partners if known)
4. KOR-specific pursuit angle: where can KOR position to win structural work with this org? Timing window? Incumbent structural firms that could be displaced?
5. Confirmed, changed, or newly relevant facts from CURRENT_KNOWLEDGE.
6. ALWAYS include narratives  at minimum a "Current" narrative paragraph (2-4 sentences) summarizing where the org is right now, and an "Action" narrative paragraph (2-4 sentences) summarizing what KOR should do this quarter. Optionally add "History" or "Summary" narratives if you have context that doesn't fit elsewhere.
7. Flag at least one risk if any are evident  capacity strain on the org's incumbent structural partner, key-person dependency (single point of failure), ownership uncertainty (acquisition rumors, succession), or an exploitable competitor weakness. Leave risks empty ONLY if truly nothing applies.

## HARD RULES for `actions` (do not violate — violations are rejected at ingest)

1. **Never recommend registering on or monitoring a procurement portal.** KOR's BD platform ALREADY ingests 100+ procurement feeds — BC Bid, MERX (paid, via mailbox), bids&tenders (~25 BC/AB municipalities) + their award feeds, Bonfire, APC / Alberta Purchasing Connection, CivicInfo, CanadaBuys, SEAO, BC Gov news, and the AB/BC Major Projects inventories. Feed access is solved. Do NOT emit any action that says "register on / monitor / set alerts on / watch" BC Bid, MERX, APC, Bonfire, bids&tenders, CivicInfo, CanadaBuys, SEAO, Buyandsell, or any tender portal. That is wrong and useless for KOR.
2. **Every action must be one of:** (a) a RELATIONSHIP play — a named architect / GC / developer / decision-maker for KOR to approach (KOR wins as the architect's structural sub, before the RFP); (b) a CREDENTIAL / positioning move (e.g. build a first healthcare credit, post-disaster classification); or (c) a SPECIFIC named live opportunity with a project name + RFP/RFPQ number. If you have none of these, emit NO action rather than portal-registration filler.
3. **Captive-structural gate (the "Yurkovich rule").** On an Alliance / P3 / DBFOM / design-build project where a GC + architect have already been selected, the structural sub is locked CAPTIVE at team selection and is simply never announced publicly. "No structural engineer is publicly named" is NOT evidence the seat is open. Do NOT recommend pursuing such a seat; mark it as locked/MONITOR and say who the design-builder is.

Output ONLY the JSON object per the system prompt schema. No prose, no markdown fences, no backticks.
