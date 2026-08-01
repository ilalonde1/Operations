# Live-Opportunity Enrichment Fix — Implementation Plan
### From the coverage audit (KOR-BD-LiveOpp-Enrichment-Audit-2026-07-11). Scope approved: Everything (T0–T3).

**Working method:** one phase at a time — Codex brief → Ian runs it → Claude verifies against source + builds → commit → next brief. No phase's brief is written until the prior phase's code has landed (verify from source, don't guess ahead).

## Dependency order (why this sequence)
The tiers stack: contact/commodity data has nowhere to persist until the model has columns; the detail-enricher needs those columns + a documents store. So:

**Phase 1 = T1 + T0 feed wins (the enabler).** Add `BuyerContact*` + `CommodityCodes` to `OpportunityCandidate`; add a deterministic `DisciplineClassifier`; stop `IngestionService` hardcoding `Discipline = Unknown`; map the fields CanadaBuys/SAM/LACity already carry. → live opps start getting discipline + contact + value immediately from feed sources.

**Phase 2 = T2 (the real fix).** Generalize APC's `ApcInterestEnrichmentJob` pattern into a live-opp **detail-enricher** covering BC Bid, Bids&Tenders, MERX-DCC, Bonfire — opens each live opp's detail page (reusing the browser pool + the already-built `BcBidInterestProbe` / `BidsAndTendersInterestProbe` login/read logic), harvests scope + discipline + contact + **documents** + plan-takers, upserts idempotently. Add an `opportunities.OpportunityDocuments` table + fetch. Fix the plan-taker no-list re-queue bug.

**Phase 3 = T3 (coverage).** Add Interior Health + other health-authority Bids&Tenders tenants; surface Major-Projects-Inventory value/owner/stage onto the opportunity surface; enable live-opp document download (reuse the historical download service).

## Verified anchors (file:line)
- `OpportunityCandidate` — no Discipline/Contact/Documents slots: `Kor.Opportunities.Core\Ingestion\OpportunityCandidate.cs:14-72`
- Hardcoded `Discipline = Unknown`: `Kor.Opportunities.Data\Ingestion\IngestionService.cs:594`; contact never set: `:578-595`
- Discipline enum (coarse KOR signal): `Kor.Opportunities.Core\Models\OpportunityEnums.cs:38-45` (Unknown/Structural/Inspections/Mixed/OutOfScope)
- Opportunities columns already exist: BuyerContactName / BuyerContactEmail / BuyerContactPhone / Discipline (confirmed in table schema)
- The reusable template: `ApcInterestEnrichmentJob` + `ApcInterestExtractor` (decoupled, idempotent, opens live detail DOM)
- Unwired probe tools ready to reuse: `tools/BcBidInterestProbe`, `tools/BidsAndTendersInterestProbe`
- Plan-taker re-queue bug: `BcBidPlanTakerEnrichmentJob.cs:152-161` (NOT EXISTS re-selects no-list opps forever)

See `Phase1-Codex-Brief.md` for the first brief.
