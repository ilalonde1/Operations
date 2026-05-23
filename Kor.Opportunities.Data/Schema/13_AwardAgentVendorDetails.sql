/*
    Kor.OpportunitiesDb migration 13.
    Adds 6 new agent-extracted vendor-metadata columns to OpportunityAwards.
    Idempotent. Safe to re-run.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorWebsite'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorWebsite         nvarchar(500)  NULL,
            AgentVendorHqLocation      nvarchar(200)  NULL,
            AgentVendorSizeBand        nvarchar(20)   NULL,  -- 'small' | 'mid' | 'large' | 'unknown'
            AgentVendorFoundedYear     int            NULL,
            AgentVendorSpecialties     nvarchar(max)  NULL,  -- JSON array of strings
            AgentVendorLeadership      nvarchar(max)  NULL;  -- JSON array of {name, title}
END;
GO

PRINT 'Migration 13 complete.';
GO
