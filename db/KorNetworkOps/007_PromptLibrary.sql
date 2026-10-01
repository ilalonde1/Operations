/*
007_PromptLibrary.sql

The Prompt Library: every Claude prompt the Command Center hands out is generated from the live database at that
moment (nothing is stored as a finished prompt), and recorded here as a run so the session's outcome comes back to it.
Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 006. Idempotent. No logins change (schema-wide grants in 001/002).

What the table is for:
  PromptRuns   one row per prompt opened: what it was about (a tool, a device, a finding), who opened it, the SHA-256
               of exactly what was handed out, and the SHA-256 of the one-time token the session reports back with.
               The outcome (solved or not, what was done, what was learned) is written by that report; a proposed
               learning waits in LearnedText for Ian's approval -- nothing edits Knowledge by itself. Once accepted,
               it is put into every later prompt about the same kind of problem: that is how the library learns.
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
IF OBJECT_ID(N'NetworkOps.MeshNodes', N'U') IS NULL
BEGIN
    RAISERROR('Run 006 first: NetworkOps.MeshNodes does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'NetworkOps.PromptRuns', N'U') IS NULL
    BEGIN
        CREATE TABLE NetworkOps.PromptRuns
        (
            RunId           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_NetworkOps_PromptRuns PRIMARY KEY,
            Kind            varchar(16)     NOT NULL,   -- tool | device | finding
            Subject         nvarchar(200)   NOT NULL,   -- the tool id, the device name, or "device: rule"
            DeviceId        int             NULL CONSTRAINT FK_NetworkOps_PromptRuns_Devices REFERENCES NetworkOps.Devices(DeviceId),
            FindingId       bigint          NULL,
            RuleKey         varchar(100)    NULL,       -- the finding's rule: accepted learnings reach every later prompt for its family
            CreatedBy       nvarchar(128)   NOT NULL,
            CreatedUtc      datetime2(0)    NOT NULL CONSTRAINT DF_NetworkOps_PromptRuns_Created DEFAULT (SYSUTCDATETIME()),
            PromptSha256    binary(32)      NOT NULL,
            TokenSha256     binary(32)      NOT NULL,
            OutcomeUtc      datetime2(0)    NULL,
            Outcome         varchar(16)     NULL,       -- solved | partly | not-solved | no-action
            Summary         nvarchar(2000)  NULL,       -- what was wrong and what was done
            LearnedText     nvarchar(2000)  NULL,       -- a proposed Knowledge change, for Ian to accept or not
            LearnedStatus   varchar(16)     NULL        -- proposed | accepted | rejected
        );
        CREATE INDEX IX_NetworkOps_PromptRuns_Device ON NetworkOps.PromptRuns(DeviceId, CreatedUtc DESC);
    END;

    COMMIT;
    SELECT 'PromptRuns' AS [Table], COUNT(*) AS [Rows] FROM NetworkOps.PromptRuns;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
