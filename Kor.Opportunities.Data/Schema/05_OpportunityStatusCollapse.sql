/*
    Kor.OpportunitiesDb - migration 05.
    Collapses opportunities.Opportunities.Status from 9 to 5 values:
        Reviewing (2)    -> New (1)
        Qualified (3)    -> New (1)
        NoBid (8)        -> Lost (7) + WonLostOutcome=NoBid (3) + reason backfilled
        Withdrawn (9)    -> Lost (7) + WonLostOutcome=Withdrawn (4) + reason backfilled

    Identified (1) keeps value 1, renamed "New" in code.
    ProposalSubmitted (5) keeps value 5, renamed "Submitted" in code.

    Idempotent: rows already at terminal values are skipped by the WHERE clauses.
    Safe to re-run.
*/

-- Reviewing (2) and Qualified (3) -> New (1).
-- Lifecycle timestamps (ReviewingSinceUtc, QualifiedAtUtc) preserved for historic record.
UPDATE opportunities.Opportunities
   SET Status = 1
 WHERE Status IN (2, 3);

-- NoBid (8) -> Lost (7). Preserve the distinction in WonLostOutcome.
UPDATE opportunities.Opportunities
   SET Status         = 7,
       WonLostOutcome = COALESCE(WonLostOutcome, 3),  -- 3 = NoBid in WonLostOutcome
       OutcomeReason  = COALESCE(NULLIF(LTRIM(RTRIM(OutcomeReason)), ''), N'NoBid (auto-migrated 2026-05-15)')
 WHERE Status = 8;

-- Withdrawn (9) -> Lost (7). Preserve the distinction in WonLostOutcome.
UPDATE opportunities.Opportunities
   SET Status         = 7,
       WonLostOutcome = COALESCE(WonLostOutcome, 4),  -- 4 = Withdrawn in WonLostOutcome
       OutcomeReason  = COALESCE(NULLIF(LTRIM(RTRIM(OutcomeReason)), ''), N'Withdrawn (auto-migrated 2026-05-15)')
 WHERE Status = 9;
