/*
    Kor.OpportunitiesDb migration 14.
    Adds vendor ownership + parent-company columns. Idempotent.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorOwnershipStatus'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorOwnershipStatus nvarchar(50)  NULL,  -- 'private' | 'public' | 'employee-owned' | 'subsidiary' | 'unknown'
            AgentVendorParentCompany   nvarchar(300) NULL;  -- non-null when subsidiary
END;
GO

PRINT 'Migration 14 complete.';
GO
