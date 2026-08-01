/* Connection-scoped like every other migration — no USE.
   APPLIED to KOR-APP01 KorOpportunitiesDb 2026-07-11. */

/* =====================================================================
   280 — HistoricalOpportunities canonical-org FK columns.
   ---------------------------------------------------------------------
   The historical archive is a scheduled, ongoing ingest whose org strings
   (BuyerName, AwardedToOrganization) had NO canonical FK columns at all —
   the only intake path that structurally could not resolve. Adding the two
   FKs and registering them in CanonicalColumnRegistry means the weekly
   backfill wheel links the whole 9,884-row archive automatically; no code
   change to the enrichment path is required for linking to begin.
   ===================================================================== */

IF COL_LENGTH('opportunities.HistoricalOpportunities', 'BuyerCanonicalOrgId') IS NULL
BEGIN
    ALTER TABLE opportunities.HistoricalOpportunities
        ADD BuyerCanonicalOrgId bigint NULL
            CONSTRAINT FK_HistOpp_BuyerCanonicalOrg
            REFERENCES opportunities.CanonicalOrg (Id),
            AwardedToCanonicalOrgId bigint NULL
            CONSTRAINT FK_HistOpp_AwardedToCanonicalOrg
            REFERENCES opportunities.CanonicalOrg (Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('opportunities.HistoricalOpportunities')
                 AND name = 'IX_HistOpp_BuyerCanonicalOrgId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_HistOpp_BuyerCanonicalOrgId
        ON opportunities.HistoricalOpportunities (BuyerCanonicalOrgId)
        WHERE BuyerCanonicalOrgId IS NOT NULL;
    CREATE NONCLUSTERED INDEX IX_HistOpp_AwardedToCanonicalOrgId
        ON opportunities.HistoricalOpportunities (AwardedToCanonicalOrgId)
        WHERE AwardedToCanonicalOrgId IS NOT NULL;
END;
