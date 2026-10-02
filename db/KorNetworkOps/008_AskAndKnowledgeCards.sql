/*
008_AskAndKnowledgeCards.sql

Asking Claude anything about a machine or the network, and banking what the session learns so it reaches every later
prompt about a machine it applies to. Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 007. Idempotent. No logins
change (schema-wide grants in 001/002).

What changes:
  PromptRuns.Question   an "ask" run's question, in the person's own words (a device or finding run leaves it NULL).
  KnowledgeCards        one row per card a session proposes with its report: title, which machines it applies to
                        (Core/Prompts/KnowledgeCards: any | app:<name> | model:<text> | gpu:<text> | kind:<kind> |
                        device:<name> | finding:<rule>), symptom, cause, how to check, fix. Status proposed -> accepted
                        or rejected by Ian (the same Accept/Reject as the run's learning); only accepted cards go into
                        prompts. Nothing edits a card's text after it is written: a better card replaces it (retired).
*/

USE KorNetworkOps;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @here sysname = DB_NAME();
IF @here <> N'KorNetworkOps'
BEGIN
    RAISERROR('Not in KorNetworkOps (in %s). Nothing below will run.', 16, 1, @here);
    SET NOEXEC ON;
END;
IF OBJECT_ID(N'NetworkOps.PromptRuns', N'U') IS NULL
BEGIN
    RAISERROR('Run 007 first: NetworkOps.PromptRuns does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'NetworkOps.PromptRuns', N'Question') IS NULL
        ALTER TABLE NetworkOps.PromptRuns ADD Question nvarchar(2000) NULL;

    IF OBJECT_ID(N'NetworkOps.KnowledgeCards', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.KnowledgeCards
        (
            CardId          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_KnowledgeCards PRIMARY KEY,
            Title           nvarchar(200)   NOT NULL,
            AppliesTo       nvarchar(400)   NOT NULL,
            Symptom         nvarchar(2000)  NOT NULL,
            Cause           nvarchar(2000)  NULL,
            HowToCheck      nvarchar(2000)  NULL,
            Fix             nvarchar(2000)  NULL,
            Tags            nvarchar(400)   NULL,
            SourceRunId     bigint          NULL CONSTRAINT FK_NetworkOps_KnowledgeCards_Run REFERENCES NetworkOps.PromptRuns(RunId),
            SourceDeviceId  int             NULL CONSTRAINT FK_NetworkOps_KnowledgeCards_Device REFERENCES NetworkOps.Devices(DeviceId),
            Status          varchar(16)     NOT NULL CONSTRAINT DF_NetworkOps_KnowledgeCards_Status DEFAULT ('proposed'),   -- proposed | accepted | rejected | retired
            CreatedUtc      datetime2(0)    NOT NULL CONSTRAINT DF_NetworkOps_KnowledgeCards_Created DEFAULT (SYSUTCDATETIME()),
            DecidedUtc      datetime2(0)    NULL,
            DecidedBy       nvarchar(128)   NULL
        );
        CREATE INDEX IX_NetworkOps_KnowledgeCards_Status ON NetworkOps.KnowledgeCards(Status, CreatedUtc DESC);
        CREATE UNIQUE INDEX UX_NetworkOps_KnowledgeCards_Run ON NetworkOps.KnowledgeCards(SourceRunId) WHERE SourceRunId IS NOT NULL;
    END;

    COMMIT;
    SELECT 'KnowledgeCards' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.KnowledgeCards;
    SELECT CASE WHEN COL_LENGTH(N'NetworkOps.PromptRuns', N'Question') IS NULL THEN 'MISSING' ELSE 'present' END AS [PromptRuns.Question];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
