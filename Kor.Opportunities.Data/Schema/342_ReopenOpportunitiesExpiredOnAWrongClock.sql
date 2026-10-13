-- 342: reopen opportunities that were AUTO-EXPIRED on a deadline that was wrong.
--
-- DataRetirementJob expires on `SubmissionDeadlineUtc < SYSDATETIMEOFFSET()`.
-- While deadlines held a source's local wall clock relabelled as UTC (339/340)
-- or a date-only value pinned to midnight (341), that comparison fired EARLY --
-- by 4-8 hours for a named-zone deadline, and by most of a day for a date-only
-- one. Opportunities therefore vanished from the pipeline before they closed.
--
-- With the clocks corrected, any opportunity whose deadline is now in the FUTURE
-- but which carries the auto-expiry signature was expired on a false premise and
-- is reopened here.
--
-- ⛔ WHY THIS CANNOT UNDO A HUMAN DECISION. The reversal requires the exact
-- signature DataRetirementJob writes -- Status 7, WonLostOutcome 3 AND the
-- literal OutcomeReason it sets -- plus an unowned row, which is a precondition
-- of that job firing at all. A human no-bid or loss carries a different reason
-- (or an owner), so it cannot match. Status is a judgement about a pursuit, so
-- the bar for touching it is deliberately higher than for a timestamp.
--
-- Idempotent: a reopened row no longer carries the signature.
--
-- HOW TO RUN
--   report only:  sqlcmd ... -i 342_...sql -v Apply=0
--   apply:        sqlcmd ... -i 342_...sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;
DECLARE @autoReason nvarchar(500) = N'Auto-expired: submission deadline passed (no bid).';

IF OBJECT_ID('opportunities.OpportunityReopenLog') IS NULL
BEGIN
    CREATE TABLE opportunities.OpportunityReopenLog (
        Id                 bigint IDENTITY(1,1) PRIMARY KEY,
        OpportunityId      bigint         NOT NULL,
        OldStatus          int            NOT NULL,
        OldWonLostOutcome  int            NULL,
        OldOutcomeReason   nvarchar(500)  NULL,
        OldOutcomeAtUtc    datetimeoffset NULL,
        DeadlineUtc        datetimeoffset NULL,
        ReopenedAtUtc      datetimeoffset NOT NULL DEFAULT sysdatetimeoffset(),
        MigrationName      nvarchar(100)  NOT NULL
    );

    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'UPDATE o SET o.Status = l.OldStatus, o.WonLostOutcome = l.OldWonLostOutcome, o.OutcomeReason = l.OldOutcomeReason, o.OutcomeAtUtc = l.OldOutcomeAtUtc FROM opportunities.Opportunities o JOIN opportunities.OpportunityReopenLog l ON l.OpportunityId = o.Id WHERE l.MigrationName = N''342_ReopenOpportunitiesExpiredOnAWrongClock'';',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE',  @level1name = N'OpportunityReopenLog';
END;

IF OBJECT_ID('tempdb..#reopen') IS NOT NULL DROP TABLE #reopen;

SELECT o.Id AS OpportunityId, o.Status, o.WonLostOutcome, o.OutcomeReason,
       o.OutcomeAtUtc, o.SubmissionDeadlineUtc
INTO #reopen
FROM opportunities.Opportunities o
WHERE o.SubmissionDeadlineUtc IS NOT NULL
  AND o.SubmissionDeadlineUtc > sysdatetimeoffset()   -- has NOT actually closed
  AND o.Status = 7
  AND o.WonLostOutcome = 3
  AND o.OutcomeReason = @autoReason                   -- the job's own wording
  AND o.OwnerStaffId IS NULL                          -- as the job required
  AND o.DismissedAtUtc IS NULL;                       -- never revive a dismissal

SELECT 'to reopen' AS Report, COUNT(*) AS Rows_ FROM #reopen;
SELECT OpportunityId,
       CONVERT(varchar(19), SubmissionDeadlineUtc, 120) AS DeadlineUtc,
       CONVERT(varchar(19), OutcomeAtUtc, 120) AS WasExpiredAt
FROM #reopen ORDER BY SubmissionDeadlineUtc;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed.';
    RETURN;
END;

BEGIN TRANSACTION;

    INSERT INTO opportunities.OpportunityReopenLog
        (OpportunityId, OldStatus, OldWonLostOutcome, OldOutcomeReason,
         OldOutcomeAtUtc, DeadlineUtc, MigrationName)
    SELECT r.OpportunityId, r.Status, r.WonLostOutcome, r.OutcomeReason,
           r.OutcomeAtUtc, r.SubmissionDeadlineUtc,
           N'342_ReopenOpportunitiesExpiredOnAWrongClock'
    FROM #reopen r
    JOIN opportunities.Opportunities o ON o.Id = r.OpportunityId AND o.Status = 7;
    DECLARE @logged int = @@ROWCOUNT;

    UPDATE o
       SET o.Status         = 1,     -- New
           o.WonLostOutcome = NULL,
           o.OutcomeReason  = NULL,
           o.OutcomeAtUtc   = NULL,
           o.UpdatedAtUtc   = sysdatetimeoffset(),
           o.UpdatedBy      = N'342_ReopenOpportunitiesExpiredOnAWrongClock'
    FROM opportunities.Opportunities o
    JOIN #reopen r ON r.OpportunityId = o.Id
    WHERE o.Status = 7 AND o.WonLostOutcome = 3 AND o.OutcomeReason = @autoReason;
    DECLARE @reopened int = @@ROWCOUNT;

    IF @logged <> @reopened
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'342 aborted: logged %d but reopened %d. Nothing committed.', 16, 1, @logged, @reopened);
        RETURN;
    END;

COMMIT TRANSACTION;

SELECT 'reopened' AS Report, @reopened AS Rows_;
