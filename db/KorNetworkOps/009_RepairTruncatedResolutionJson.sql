/*
009_RepairTruncatedResolutionJson.sql

Repairs FindingResolutions rows whose JSON was cut short. Run as sa in SSMS on KOR-APP01\SQLEXPRESS after 008. Idempotent:
a second run finds nothing to repair. No schema change, no logins change.

Why: until service 0.17.2 the list of fixes run while a finding was open was saved as Truncate(Serialize(list), 400). On
2026-10-02, after a night of update installs made those lists long, the cut went through a "\u" escape and appended "…",
leaving invalid JSON in ActionIdsJson. GET /api/resolutions -- which every NetworkOps device window loads -- answered 500
("'0xE2' is not a hex digit following '\u'"), so "History failed to load" on every machine. 0.17.2 never truncates JSON
(it keeps the newest whole items that fit) and reads a bad row as empty; this puts the bad rows right. The lost tail of
each list cannot be recovered: the fixes themselves are all still in NetworkOps.Actions.

What changes: an unreadable ActionIdsJson or ChangedFactsJson becomes NULL (read as "no detail"). Nothing else.
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
GO

DECLARE @actions int, @facts int;

UPDATE NetworkOps.FindingResolutions SET ActionIdsJson = NULL
WHERE ActionIdsJson IS NOT NULL AND ISJSON(ActionIdsJson) = 0;
SET @actions = @@ROWCOUNT;

UPDATE NetworkOps.FindingResolutions SET ChangedFactsJson = NULL
WHERE ChangedFactsJson IS NOT NULL AND ISJSON(ChangedFactsJson) = 0;
SET @facts = @@ROWCOUNT;

PRINT CONCAT('009: repaired ', @actions, ' ActionIdsJson and ', @facts, ' ChangedFactsJson value(s).');

-- Read back: nothing unreadable may remain.
SELECT COUNT(*) AS StillUnreadable FROM NetworkOps.FindingResolutions
WHERE (ActionIdsJson IS NOT NULL AND ISJSON(ActionIdsJson) = 0) OR (ChangedFactsJson IS NOT NULL AND ISJSON(ChangedFactsJson) = 0);
GO
SET NOEXEC OFF;
GO
