-- 347: rewrite developer-facing dismissal reasons in words the reader can use.
-- Supplied measurements, 2026-10-14: rules 1/2/3 match 421/1/20 rows respectively.
-- These counts were not remeasured while authoring this file.
-- Idempotent on the VALUE: each UPDATE matches only the old wording; a second
-- apply changes zero rows. No persistent writes occur in report-only mode.
-- Undo: DismissalReasonRewrite347 retains the old and new reason for every row.
-- Its Rollback extended property holds the restoring UPDATE, guarded against
-- overwriting reasons edited after this migration. The log is retained on undo.
-- Missing Apply fails closed in SQLCMD; there is deliberately no SQLCMD default.
-- HOW TO RUN (Claude; not executed by this brief)
--   report only: sqlcmd ... -i 347_PlainDismissalReasons.sql -v Apply=0
--   apply:       sqlcmd ... -i 347_PlainDismissalReasons.sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;

DROP TABLE IF EXISTS #DismissalRewrites347;

SELECT Id AS OpportunityId, 1 AS RuleNumber, DismissedReason AS OldReason,
       CAST(N'Duplicate of ' + SUBSTRING(DismissedReason, 13, CHARINDEX(N';', DismissedReason) - 13)
            + N', the record kept. Its postings and history were merged there.' AS nvarchar(max)) AS NewReason
INTO #DismissalRewrites347
FROM opportunities.Opportunities
WHERE DismissedBy = N'BdOpportunityDedup'
  AND DismissedReason LIKE N'Same RFP as %; held separately because OpportunityKey carries the source prefix. Observations and child rows merged onto that record.'
UNION ALL
SELECT Id, 2, DismissedReason,
       N'Duplicate of APCALLBU-AB-2026-06377 (West Campus Recreation IPD, structural consultant), the record kept. Its postings, deadline and buyer were merged there.'
FROM opportunities.Opportunities
WHERE DismissedBy = N'migration-327'
  AND OpportunityKey = N'BIDSTEND-2026-RFP-IP-061'
  AND DismissedReason LIKE N'Same RFP as APCALLBU-AB-2026-06377 (%'
UNION ALL
SELECT Id, 3, DismissedReason,
       N'Duplicate of the same Vancouver application under its DP file number. A second record was created on 2026-09-10 when the ingest changed how it keys applications.'
FROM opportunities.Opportunities
WHERE DismissedBy = N'migration-321'
  AND DismissedReason LIKE N'Duplicate of the same application under its DP file number; created when the OpportunityKey derivation changed%';

SELECT 'would change' AS Report, rules.RuleNumber, COUNT(f.OpportunityId) AS Rows_
FROM (VALUES (1), (2), (3)) rules(RuleNumber)
LEFT JOIN #DismissalRewrites347 f ON f.RuleNumber = rules.RuleNumber
GROUP BY rules.RuleNumber
ORDER BY rules.RuleNumber;

SELECT 'still containing OpportunityKey (before)' AS Report, COUNT(*) AS Rows_
FROM opportunities.Opportunities
WHERE DismissedReason LIKE N'%OpportunityKey%';

IF @DryRun = 1
BEGIN
    PRINT 'Apply=0 - nothing was changed.';
    RETURN;
END;

BEGIN TRANSACTION;

IF OBJECT_ID(N'opportunities.DismissalReasonRewrite347', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DismissalReasonRewrite347
    (
        OpportunityId bigint PRIMARY KEY,
        OldReason nvarchar(max),
        NewReason nvarchar(max),
        RewrittenAtUtc datetimeoffset NOT NULL
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'opportunities.DismissalReasonRewrite347')
      AND minor_id = 0 AND name = N'Rollback')
BEGIN
    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'UPDATE o SET DismissedReason = r.OldReason FROM opportunities.Opportunities o JOIN opportunities.DismissalReasonRewrite347 r ON r.OpportunityId = o.Id WHERE o.DismissedReason COLLATE Latin1_General_100_BIN2 = r.NewReason COLLATE Latin1_General_100_BIN2;',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE', @level1name = N'DismissalReasonRewrite347';
END;

-- A rollback/reapply may reuse the original log, but must never overwrite it
-- with a different reason for the same record.
IF EXISTS (
    SELECT 1
    FROM #DismissalRewrites347 f
    JOIN opportunities.DismissalReasonRewrite347 r ON r.OpportunityId = f.OpportunityId
    WHERE r.OldReason IS NULL OR r.NewReason IS NULL
       OR r.OldReason COLLATE Latin1_General_100_BIN2 <> f.OldReason COLLATE Latin1_General_100_BIN2
       OR r.NewReason COLLATE Latin1_General_100_BIN2 <> f.NewReason COLLATE Latin1_General_100_BIN2)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50347, '347 aborted: an existing undo row holds a different reason. Nothing committed.', 1;
END;

INSERT INTO opportunities.DismissalReasonRewrite347 (OpportunityId, OldReason, NewReason, RewrittenAtUtc)
SELECT f.OpportunityId, f.OldReason, f.NewReason, SYSDATETIMEOFFSET()
FROM #DismissalRewrites347 f
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.DismissalReasonRewrite347 r WHERE r.OpportunityId = f.OpportunityId);

UPDATE o
SET DismissedReason = f.NewReason
FROM opportunities.Opportunities o
JOIN #DismissalRewrites347 f ON f.OpportunityId = o.Id AND f.RuleNumber = 1
WHERE o.DismissedBy = N'BdOpportunityDedup'
  AND o.DismissedReason LIKE N'Same RFP as %; held separately because OpportunityKey carries the source prefix. Observations and child rows merged onto that record.'
  AND o.DismissedReason COLLATE Latin1_General_100_BIN2 = f.OldReason COLLATE Latin1_General_100_BIN2;
DECLARE @rule1 int = @@ROWCOUNT;

UPDATE o
SET DismissedReason = f.NewReason
FROM opportunities.Opportunities o
JOIN #DismissalRewrites347 f ON f.OpportunityId = o.Id AND f.RuleNumber = 2
WHERE o.DismissedBy = N'migration-327'
  AND o.OpportunityKey = N'BIDSTEND-2026-RFP-IP-061'
  AND o.DismissedReason LIKE N'Same RFP as APCALLBU-AB-2026-06377 (%'
  AND o.DismissedReason COLLATE Latin1_General_100_BIN2 = f.OldReason COLLATE Latin1_General_100_BIN2;
DECLARE @rule2 int = @@ROWCOUNT;

UPDATE o
SET DismissedReason = f.NewReason
FROM opportunities.Opportunities o
JOIN #DismissalRewrites347 f ON f.OpportunityId = o.Id AND f.RuleNumber = 3
WHERE o.DismissedBy = N'migration-321'
  AND o.DismissedReason LIKE N'Duplicate of the same application under its DP file number; created when the OpportunityKey derivation changed%'
  AND o.DismissedReason COLLATE Latin1_General_100_BIN2 = f.OldReason COLLATE Latin1_General_100_BIN2;
DECLARE @rule3 int = @@ROWCOUNT;

IF @rule1 + @rule2 + @rule3 <> (SELECT COUNT(*) FROM #DismissalRewrites347)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50348, '347 aborted: reasons changed since the report; undo and update counts differ. Nothing committed.', 1;
END;

COMMIT TRANSACTION;

SELECT 'changed' AS Report, rules.RuleNumber, rules.Rows_
FROM (VALUES (1, @rule1), (2, @rule2), (3, @rule3)) rules(RuleNumber, Rows_)
ORDER BY rules.RuleNumber;

SELECT 'still containing OpportunityKey (after; expected 0)' AS Report, COUNT(*) AS Rows_
FROM opportunities.Opportunities
WHERE DismissedReason LIKE N'%OpportunityKey%';
