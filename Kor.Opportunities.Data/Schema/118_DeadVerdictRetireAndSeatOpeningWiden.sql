-- 118_DeadVerdictRetireAndSeatOpeningWiden.sql
-- BD-Audit-2026-06-09 M4 + migrations-finding (KorSeatOpening).
--
-- Part A: KorSeatOpening was nvarchar(500) but m109 established it as an
-- append-log (single appends up to ~350 chars); one more append on an
-- already-annotated row raises error 2628 and rolls back whatever
-- migration touches it. Widen to nvarchar(max).
--
-- Part B: 221 ACTIVE MPIs carry a honing verdict of DEAD ("fully awarded
-- or unviable" — the Yurkovich-bar prompts require a named incumbent +
-- source URL before a DEAD call), but nothing actioned the verdict; the
-- most expensive human-curated signal the drains produce sat inert.
-- Retire them (archive-not-delete, reason cites the verdict) and retire
-- their active intel children with them (children retire with parent on
-- a lifecycle retire — same rule as m115).
-- NOTE: m109's three ProponentName/FK "divergence" rows (3255/5180/5184)
-- were verified 2026-06-09: each FK points at a sensible primary entity
-- within the joint name string (Kosapsum / CVRD / City of Courtenay) —
-- no fix needed, recorded here so the audit trail closes.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Part A — own batch (schema change).
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('opportunities.MajorProjectsInventory')
             AND name = 'KorSeatOpening' AND max_length <> -1)
BEGIN
    ALTER TABLE opportunities.MajorProjectsInventory ALTER COLUMN KorSeatOpening nvarchar(max) NULL;
    PRINT 'KorSeatOpening widened to nvarchar(max).';
END
ELSE
    PRINT 'KorSeatOpening already nvarchar(max); skipped.';
GO

-- Part B
BEGIN TRAN;

DECLARE @DeadMpis TABLE (Id bigint PRIMARY KEY);
INSERT INTO @DeadMpis (Id)
SELECT m.Id
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  -- Replay-safety retrofit (audit 2026-07-01): never retire an MPI a pursuit
  -- is linked to (single chokepoint — the child retires below key off @DeadMpis).
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l
                  WHERE l.MajorProjectsInventoryId = m.Id)
  AND COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'),
               JSON_VALUE(e.ResultJson, '$.verdict')) = N'DEAD';
PRINT 'DEAD-verdict active MPIs found: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE m SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: honing verdict DEAD — fully awarded or unviable per Sonnet honing (BD-Audit-2026-06-09 M4)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
JOIN @DeadMpis d ON d.Id = m.Id;
PRINT 'MPIs retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Children retire with the parent (lifecycle rule, m115).
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: parent MPI retired (honing verdict DEAD)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectAction x JOIN @DeadMpis d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectAction retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: parent MPI retired (honing verdict DEAD)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectSignal x JOIN @DeadMpis d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectSignal retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: parent MPI retired (honing verdict DEAD)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectRisk x JOIN @DeadMpis d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectRisk retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: parent MPI retired (honing verdict DEAD)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProjectKeyPerson x JOIN @DeadMpis d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProjectKeyPerson retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm118: parent MPI retired (honing verdict DEAD)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelProject x JOIN @DeadMpis d ON d.Id = x.MajorProjectsInventoryId
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelProject retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm118 committed.';
GO

-- Verify: no DEAD-verdict MPI may remain active; no active intel on retired.
SELECT COUNT(*) AS DeadStillActive
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'),
               JSON_VALUE(e.ResultJson, '$.verdict')) = N'DEAD';
SELECT COUNT(*) AS ActiveIntelOnRetired
FROM (
    SELECT MajorProjectsInventoryId AS MpiId FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectRisk WHERE RetiredAtUtc IS NULL
    UNION ALL SELECT MajorProjectsInventoryId FROM opportunities.IntelProjectKeyPerson WHERE RetiredAtUtc IS NULL
) i
JOIN opportunities.MajorProjectsInventory m ON m.Id = i.MpiId
WHERE m.RetiredAtUtc IS NOT NULL;
GO
