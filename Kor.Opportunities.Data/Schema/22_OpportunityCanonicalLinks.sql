/*
    Kor.OpportunitiesDb migration 22.
    Adds canonical-org FK columns on OpportunityAwards + Opportunities,
    and a normalized-name persisted computed column + index on CanonicalOrg.
    Idempotent. Uses GO-separated batches so column references compile.
*/

-- ============================================================
-- CanonicalOrg.NormalizedName + index
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'NormalizedName'
                 AND Object_ID = Object_ID(N'opportunities.CanonicalOrg'))
BEGIN
    ALTER TABLE opportunities.CanonicalOrg ADD NormalizedName AS (
        CAST(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            DisplayName,
            ' ',''), '.',''), ',',''), '''',''), '-',''), '&',''), '/',''), '(',''), ')',''), '+',''))
        AS nvarchar(300))
    ) PERSISTED;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CanonicalOrg_NormalizedName'
                 AND object_id = Object_ID(N'opportunities.CanonicalOrg'))
BEGIN
    CREATE INDEX IX_CanonicalOrg_NormalizedName
        ON opportunities.CanonicalOrg (NormalizedName);
END;
GO

-- ============================================================
-- OpportunityAwards canonical FK columns
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AwardingCanonicalOrgId'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AwardingCanonicalOrgId  bigint NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AwardedToCanonicalOrgId'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AwardedToCanonicalOrgId bigint NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_OpportunityAwards_AwardingCanonical')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD CONSTRAINT FK_OpportunityAwards_AwardingCanonical
            FOREIGN KEY (AwardingCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_OpportunityAwards_AwardedToCanonical')
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD CONSTRAINT FK_OpportunityAwards_AwardedToCanonical
            FOREIGN KEY (AwardedToCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_OpportunityAwards_AwardingCanonical'
                 AND object_id = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    CREATE INDEX IX_OpportunityAwards_AwardingCanonical
        ON opportunities.OpportunityAwards (AwardingCanonicalOrgId)
        WHERE AwardingCanonicalOrgId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_OpportunityAwards_AwardedToCanonical'
                 AND object_id = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    CREATE INDEX IX_OpportunityAwards_AwardedToCanonical
        ON opportunities.OpportunityAwards (AwardedToCanonicalOrgId)
        WHERE AwardedToCanonicalOrgId IS NOT NULL;
END;
GO

-- ============================================================
-- Opportunities (active pipeline) canonical FK
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'BuyerCanonicalOrgId'
                 AND Object_ID = Object_ID(N'opportunities.Opportunities'))
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD BuyerCanonicalOrgId bigint NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_Opportunities_BuyerCanonical')
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD CONSTRAINT FK_Opportunities_BuyerCanonical
            FOREIGN KEY (BuyerCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_Opportunities_BuyerCanonical'
                 AND object_id = Object_ID(N'opportunities.Opportunities'))
BEGIN
    CREATE INDEX IX_Opportunities_BuyerCanonical
        ON opportunities.Opportunities (BuyerCanonicalOrgId)
        WHERE BuyerCanonicalOrgId IS NOT NULL;
END;
GO

PRINT 'Migration 22 complete.';
GO
