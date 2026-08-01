/*
    Kor.OpportunitiesDb migration 15.
    Adds vendor branch-locations + certifications columns (JSON arrays). Idempotent.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorLocations'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorLocations      nvarchar(max) NULL, -- JSON array of city/region strings (additional offices beyond HQ)
            AgentVendorCertifications nvarchar(max) NULL; -- JSON array of cert/registration strings (P.Eng, ISO 9001, LEED, etc.)
END;
GO

PRINT 'Migration 15 complete.';
GO
