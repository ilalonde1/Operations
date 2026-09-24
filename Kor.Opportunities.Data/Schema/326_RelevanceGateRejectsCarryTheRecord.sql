-- Migration 326 (2026-09-23): the reject log stops throwing the record away.
--
-- Until now a gate rejection kept four strings — source, title, buyer, url —
-- and discarded everything else the provider had already parsed: the file
-- number, the filing date, the description, the address. The candidate was
-- fetched, mapped and then reduced to a sentence.
--
-- That is lossy for every source, and it showed up as soon as anyone asked a
-- question of the dropped data. 2,410 municipal TREE PERMITS are sitting in
-- this table right now — 2,067 from Saanich alone — and each one is a civic
-- address where somebody has permission to remove a tree. They are only usable
-- at all because Saanich happens to put the address inside the title. Victoria
-- and Courtenay put the permit number there instead, and Coquitlam puts
-- neither.
--
-- ⚠ THIS IS NOT ABOUT TREES. The same loss applies to everything the gate
--    drops. The reject log exists so a vocabulary gap can be reviewed later,
--    and a review cannot tell a stale posting from a live one without a date,
--    or find the source record again without its reference.
--
-- Every column added here is NULLABLE. The 100k+ existing rows keep their
-- shape; they simply have nothing in the new columns, which is honest — that
-- data was never captured for them and cannot be reconstructed.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

IF COL_LENGTH('opportunities.RelevanceGateRejects', 'ExternalReference') IS NULL
    ALTER TABLE opportunities.RelevanceGateRejects ADD ExternalReference nvarchar(200) NULL;
GO

IF COL_LENGTH('opportunities.RelevanceGateRejects', 'Location') IS NULL
    ALTER TABLE opportunities.RelevanceGateRejects ADD [Location] nvarchar(500) NULL;
GO

IF COL_LENGTH('opportunities.RelevanceGateRejects', 'Description') IS NULL
    ALTER TABLE opportunities.RelevanceGateRejects ADD [Description] nvarchar(4000) NULL;
GO

IF COL_LENGTH('opportunities.RelevanceGateRejects', 'PostedDateUtc') IS NULL
    ALTER TABLE opportunities.RelevanceGateRejects ADD PostedDateUtc datetimeoffset NULL;
GO

IF COL_LENGTH('opportunities.RelevanceGateRejects', 'ProjectCity') IS NULL
    ALTER TABLE opportunities.RelevanceGateRejects ADD ProjectCity nvarchar(200) NULL;
GO

-- Reading this table by what the work IS, rather than by which source served
-- it, is now the common query — "every tree permit still live", "everything
-- dropped this week from any municipal feed". Both filter on the date first.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_RelevanceGateRejects_LastRejected'
                 AND object_id = OBJECT_ID('opportunities.RelevanceGateRejects'))
BEGIN
    CREATE INDEX IX_RelevanceGateRejects_LastRejected
        ON opportunities.RelevanceGateRejects (LastRejectedAtUtc DESC)
        INCLUDE (SourceName, Title, Buyer, Url, RejectReason);
END
GO

SELECT 'columns now on RelevanceGateRejects' AS Section;
SELECT c.name AS ColumnName, t.name AS TypeName, c.is_nullable AS IsNullable
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('opportunities.RelevanceGateRejects')
ORDER BY c.column_id;

SELECT 'rows held, and how many already carry a reference' AS Section;
SELECT COUNT(*) AS Rows,
       SUM(CASE WHEN ExternalReference IS NULL THEN 0 ELSE 1 END) AS WithReference
FROM opportunities.RelevanceGateRejects;
GO
