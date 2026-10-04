/*
010_KnowledgeCardPlainAndAmends.sql

A knowledge card now carries a PLAIN-ENGLISH explanation -- what the card means and what approving it will do,
for the person approving it (Ian), who should never have to read the technical card to know what he is signing off.
The session writes it with its report; the Prompt Library shows it above the technical card.

A card may also AMEND an earlier one: a corrected or widened version (e.g. the fan-profile fix widened to P360 and
P3 Tower models, with the machine list filled in). It is still a PROPOSED card Ian approves -- nothing edits a card's
text in place. On acceptance of an amendment, the card it amends is RETIRED (the existing replace-by-retire path).

Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 008. Idempotent. No logins change (schema-wide grants in 001/002).

What changes:
  NetworkOps.KnowledgeCards.Plain          plain-English "what this is / what approving it does" (shown first at approval).
  NetworkOps.KnowledgeCards.AmendsCardId   the card this one supersedes, or NULL. Accepting an amendment retires it.
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
IF OBJECT_ID(N'NetworkOps.KnowledgeCards', N'U') IS NULL
BEGIN
    RAISERROR('Run 008 first: NetworkOps.KnowledgeCards does not exist.', 16, 1);
    SET NOEXEC ON;
END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'NetworkOps.KnowledgeCards', N'Plain') IS NULL
        ALTER TABLE NetworkOps.KnowledgeCards ADD Plain nvarchar(2000) NULL;

    IF COL_LENGTH(N'NetworkOps.KnowledgeCards', N'AmendsCardId') IS NULL
        ALTER TABLE NetworkOps.KnowledgeCards ADD AmendsCardId bigint NULL
            CONSTRAINT FK_NetworkOps_KnowledgeCards_Amends REFERENCES NetworkOps.KnowledgeCards(CardId);

    COMMIT;
    SELECT
        CASE WHEN COL_LENGTH(N'NetworkOps.KnowledgeCards', N'Plain')        IS NULL THEN 'MISSING' ELSE 'present' END AS [KnowledgeCards.Plain],
        CASE WHEN COL_LENGTH(N'NetworkOps.KnowledgeCards', N'AmendsCardId') IS NULL THEN 'MISSING' ELSE 'present' END AS [KnowledgeCards.AmendsCardId];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
