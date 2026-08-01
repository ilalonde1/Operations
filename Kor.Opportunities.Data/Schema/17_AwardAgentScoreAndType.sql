/*
    Kor.OpportunitiesDb migration 17.
    Adds vendor KOR-overlap-score (0-10) + contract project-type columns. Idempotent.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentKorOverlapScore'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentKorOverlapScore     tinyint      NULL, -- 0-10: how directly the vendor competes with KOR's services
            AgentContractProjectType nvarchar(80) NULL; -- short categorization, e.g. "structural design", "facility maintenance"
END;
GO

PRINT 'Migration 17 complete.';
GO
