/*
    Kor.OpportunitiesDb migration 16.
    Adds vendor recent-news + LinkedIn-URL columns. Idempotent.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorRecentNews'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorRecentNews  nvarchar(max) NULL, -- JSON array of {headline,url,date} objects (last ~12 months)
            AgentVendorLinkedInUrl nvarchar(500) NULL; -- Company LinkedIn page URL
END;
GO

PRINT 'Migration 16 complete.';
GO
