/*
    Kor.OpportunitiesDb migration 61.
    Adds canonical organization linkage for opportunities.OpportunityBids
    bidder firms. Idempotent: re-runs cleanly. Safe to apply via SSMS with
    the target database selected in the dropdown.
*/
IF COL_LENGTH('opportunities.OpportunityBids', 'BidderCanonicalOrgId') IS NULL
BEGIN
    ALTER TABLE opportunities.OpportunityBids
        ADD BidderCanonicalOrgId BIGINT NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityBids', 'U') IS NOT NULL
   AND OBJECT_ID(N'opportunities.CanonicalOrg', 'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = N'FK_OpportunityBids_CanonicalOrg'
         AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityBids')
   )
BEGIN
    ALTER TABLE opportunities.OpportunityBids
        ADD CONSTRAINT FK_OpportunityBids_CanonicalOrg
            FOREIGN KEY (BidderCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
            ON DELETE SET NULL;
END;
GO

IF OBJECT_ID(N'opportunities.OpportunityBids', 'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_OpportunityBids_BidderCanonicalOrgId'
         AND object_id = OBJECT_ID(N'opportunities.OpportunityBids')
   )
BEGIN
    CREATE INDEX IX_OpportunityBids_BidderCanonicalOrgId
        ON opportunities.OpportunityBids (BidderCanonicalOrgId);
END;
GO
