/*
    Kor.OpportunitiesDb - Phase 4C migration.
    Adds opportunities.IngestionTriggers used by the WPF "Run Now" button.

    The Worker hosts an IngestionTriggerPoller BackgroundService that pulls
    rows from this table on a short cadence (default 30s) and dispatches
    them through the same IIngestionService that handles scheduled runs.

    Idempotent and self-healing - re-runs cleanly even if a partial
    stub of IngestionTriggers existed beforehand. Each block guards on the
    specific column / index / constraint it manages so a half-built table
    gets completed instead of silently skipped.
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'opportunities')
BEGIN
    EXEC ('CREATE SCHEMA opportunities AUTHORIZATION dbo;');
END;
GO

IF OBJECT_ID('opportunities.IngestionTriggers', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IngestionTriggers
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_IngestionTriggers_Id DEFAULT (NEWSEQUENTIALID()),
        OpportunitySourceId UNIQUEIDENTIFIER NOT NULL,
        Status          NVARCHAR(32)     NOT NULL CONSTRAINT DF_IngestionTriggers_Status DEFAULT ('Pending'),
        RequestedBy     NVARCHAR(150)    NOT NULL,
        RequestedAtUtc  DATETIMEOFFSET   NOT NULL CONSTRAINT DF_IngestionTriggers_RequestedAt DEFAULT (sysdatetimeoffset()),
        ClaimedAtUtc    DATETIMEOFFSET   NULL,
        ClaimedBy       NVARCHAR(200)    NULL,
        CompletedAtUtc  DATETIMEOFFSET   NULL,
        IngestionRunId  UNIQUEIDENTIFIER NULL,
        ErrorSummary    NVARCHAR(2000)   NULL,
        CONSTRAINT PK_IngestionTriggers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_IngestionTriggers_Source FOREIGN KEY (OpportunitySourceId)
            REFERENCES opportunities.OpportunitySources(Id),
        CONSTRAINT CK_IngestionTriggers_Status
            CHECK (Status IN ('Pending', 'InProgress', 'Completed', 'Failed', 'Cancelled'))
    );
END;
GO

-- Self-healing column adds: if a stub of the table existed before this script
-- ran (e.g. an aborted earlier migration attempt), fill in any missing columns
-- without dropping data. Each ALTER is independently guarded.
IF COL_LENGTH('opportunities.IngestionTriggers', 'ClaimedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD ClaimedAtUtc DATETIMEOFFSET NULL;
END;
GO

IF COL_LENGTH('opportunities.IngestionTriggers', 'ClaimedBy') IS NULL
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD ClaimedBy NVARCHAR(200) NULL;
END;
GO

IF COL_LENGTH('opportunities.IngestionTriggers', 'CompletedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD CompletedAtUtc DATETIMEOFFSET NULL;
END;
GO

IF COL_LENGTH('opportunities.IngestionTriggers', 'IngestionRunId') IS NULL
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD IngestionRunId UNIQUEIDENTIFIER NULL;
END;
GO

IF COL_LENGTH('opportunities.IngestionTriggers', 'ErrorSummary') IS NULL
BEGIN
    ALTER TABLE opportunities.IngestionTriggers ADD ErrorSummary NVARCHAR(2000) NULL;
END;
GO

-- Lets the poller find the next claimable row in O(1).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IngestionTriggers_PendingByRequested' AND object_id = OBJECT_ID('opportunities.IngestionTriggers'))
BEGIN
    CREATE INDEX IX_IngestionTriggers_PendingByRequested
        ON opportunities.IngestionTriggers (Status, RequestedAtUtc)
        INCLUDE (OpportunitySourceId, RequestedBy)
        WHERE Status = 'Pending';
END;
GO

GRANT SELECT, INSERT, UPDATE ON opportunities.IngestionTriggers TO opportunities_app;
GO
