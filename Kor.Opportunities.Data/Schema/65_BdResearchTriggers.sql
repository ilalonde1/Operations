/*
    Kor.OpportunitiesDb - R84c migration.
    Adds opportunities.BdResearchTriggers used by the Org Dossier
    "Refresh now" button.

    The Worker hosts a BdResearchTriggerPollerBackgroundService that pulls
    rows from this table on the same short cadence as IngestionTriggers and
    dispatches them through BdResearchExecutorService.ExecuteOneAsync.

    Idempotent and self-healing - re-runs cleanly even if a partial
    stub of BdResearchTriggers existed beforehand. Each block guards on the
    specific column / index / constraint it manages so a half-built table
    gets completed instead of silently skipped.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC ('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

IF OBJECT_ID('opportunities.BdResearchTriggers', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdResearchTriggers
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_BdResearchTriggers_Id DEFAULT (NEWSEQUENTIALID()),
        CanonicalOrgId  BIGINT           NOT NULL,
        ProviderName    NVARCHAR(64)     NOT NULL,
        Status          NVARCHAR(32)     NOT NULL CONSTRAINT DF_BdResearchTriggers_Status DEFAULT ('Pending'),
        RequestedBy     NVARCHAR(150)    NOT NULL,
        RequestedAtUtc  DATETIMEOFFSET   NOT NULL CONSTRAINT DF_BdResearchTriggers_RequestedAt DEFAULT (sysdatetimeoffset()),
        ClaimedAtUtc    DATETIMEOFFSET   NULL,
        ClaimedBy       NVARCHAR(200)    NULL,
        ClaimToken      UNIQUEIDENTIFIER NULL,
        ReclaimedCount  INT              NOT NULL CONSTRAINT DF_BdResearchTriggers_ReclaimedCount DEFAULT (0),
        CompletedAtUtc  DATETIMEOFFSET   NULL,
        ErrorSummary    NVARCHAR(2000)   NULL,
        InputTokens     BIGINT           NULL,
        OutputTokens    BIGINT           NULL,
        CONSTRAINT PK_BdResearchTriggers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_BdResearchTriggers_CanonicalOrg FOREIGN KEY (CanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id),
        CONSTRAINT CK_BdResearchTriggers_Status
            CHECK (Status IN ('Pending', 'InProgress', 'Completed', 'Failed', 'Cancelled'))
    );
END;
GO

-- Self-healing column adds: if a stub of the table existed before this script
-- ran (e.g. an aborted earlier migration attempt), fill in any missing columns
-- without dropping data. Each ALTER is independently guarded.
IF COL_LENGTH('opportunities.BdResearchTriggers', 'CanonicalOrgId') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD CanonicalOrgId BIGINT NOT NULL CONSTRAINT DF_BdResearchTriggers_CanonicalOrgId DEFAULT (0);
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ProviderName') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ProviderName NVARCHAR(64) NOT NULL CONSTRAINT DF_BdResearchTriggers_ProviderName DEFAULT ('FirmNarrative');
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'Status') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD Status NVARCHAR(32) NOT NULL CONSTRAINT DF_BdResearchTriggers_Status DEFAULT ('Pending');
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'RequestedBy') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD RequestedBy NVARCHAR(150) NOT NULL CONSTRAINT DF_BdResearchTriggers_RequestedBy DEFAULT ('unknown');
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'RequestedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD RequestedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdResearchTriggers_RequestedAt DEFAULT (sysdatetimeoffset());
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ClaimedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ClaimedAtUtc DATETIMEOFFSET NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ClaimedBy') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ClaimedBy NVARCHAR(200) NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ClaimToken') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ClaimToken UNIQUEIDENTIFIER NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ReclaimedCount') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ReclaimedCount INT NOT NULL CONSTRAINT DF_BdResearchTriggers_ReclaimedCount DEFAULT (0);
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'CompletedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD CompletedAtUtc DATETIMEOFFSET NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'ErrorSummary') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD ErrorSummary NVARCHAR(2000) NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'InputTokens') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD InputTokens BIGINT NULL;
END;
GO

IF COL_LENGTH('opportunities.BdResearchTriggers', 'OutputTokens') IS NULL
BEGIN
    ALTER TABLE opportunities.BdResearchTriggers ADD OutputTokens BIGINT NULL;
END;
GO

UPDATE opportunities.BdResearchTriggers
SET    ClaimToken = NEWID()
WHERE  Status = N'InProgress'
  AND  ClaimToken IS NULL;
GO

-- Lets the poller find the next claimable row in O(1).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BdResearchTriggers_PendingByRequested' AND object_id = OBJECT_ID('opportunities.BdResearchTriggers'))
BEGIN
    CREATE INDEX IX_BdResearchTriggers_PendingByRequested
        ON opportunities.BdResearchTriggers (Status, RequestedAtUtc)
        INCLUDE (CanonicalOrgId, ProviderName)
        WHERE Status = 'Pending';
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.BdResearchTriggers TO opportunities_app;
GO

PRINT 'Migration 65 BD research triggers complete.';
GO
