/* Kor.OpportunitiesDb migration: adds buyer-side contact fields to opportunities.Opportunities and seeds the BD Outreach source. */
IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'DeltekContactId')
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD DeltekContactId nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'BuyerContactName')
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD BuyerContactName nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'BuyerContactEmail')
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD BuyerContactEmail nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'BuyerContactPhone')
BEGIN
    ALTER TABLE opportunities.Opportunities
        ADD BuyerContactPhone nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
      AND name = N'IX_Opp_Opp_DeltekContactId')
BEGIN
    CREATE INDEX IX_Opp_Opp_DeltekContactId
        ON opportunities.Opportunities (DeltekContactId)
        WHERE DeltekContactId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = 'BD Outreach')
BEGIN
    INSERT INTO opportunities.OpportunitySources (Name, SourceType, BaseUrl, IsEnabled)
    VALUES (N'BD Outreach', 7, N'manual:bd-outreach', 1);
END;
GO

PRINT '04_OpportunityClientContact.sql complete.';
