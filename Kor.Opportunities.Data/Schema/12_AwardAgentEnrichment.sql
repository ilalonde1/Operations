/*
    Kor.OpportunitiesDb migration 12.
    Adds agent-enrichment columns to opportunities.OpportunityAwards so the
    AwardAgentEnrichmentService can backfill vendor profile, contract context,
    and KOR-competitor classification via Claude + web_search.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'AgentVendorProfile'
                 AND Object_ID = Object_ID(N'opportunities.OpportunityAwards'))
BEGIN
    ALTER TABLE opportunities.OpportunityAwards
        ADD AgentVendorProfile        nvarchar(max)       NULL,
            AgentContractContext      nvarchar(max)       NULL,
            AgentCompetesWithKor      bit                 NULL,
            AgentCompetitionNotes     nvarchar(max)       NULL,
            AgentSourceUrls           nvarchar(max)       NULL,  -- JSON array
            AgentEnrichedAtUtc        datetimeoffset(3)   NULL,
            AgentEnrichmentAttempts   int                 NOT NULL CONSTRAINT DF_OppAwards_AgentAttempts DEFAULT (0),
            AgentLastError            nvarchar(2000)      NULL,
            AgentLastAttemptAtUtc     datetimeoffset(3)   NULL;
END;
GO

-- Pending-enrichment partial index (rows that haven't been touched yet, ordered cheaply)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OppAwards_PendingAgent')
    CREATE INDEX IX_OppAwards_PendingAgent
        ON opportunities.OpportunityAwards (ContractValue DESC, Id)
        INCLUDE (AgentEnrichmentAttempts)
        WHERE AgentEnrichedAtUtc IS NULL;
GO

-- Filter index on the boolean classification so the UI can query "competitors of KOR" fast
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OppAwards_CompetesWithKor')
    CREATE INDEX IX_OppAwards_CompetesWithKor
        ON opportunities.OpportunityAwards (AgentCompetesWithKor)
        WHERE AgentCompetesWithKor = 1;
GO

PRINT 'Migration 12 complete.';
GO
