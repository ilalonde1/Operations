/*
    Kor.OpportunitiesDb migration 268.

    Phase-2 backfill: seed KOR's historical win record into the CRM.

    The only source of win outcomes is the one-time Deltek.CustomProposal import
    in opportunities.KorPursuits (the recurring Deltek PR sync records ZERO wins —
    see docs/BD-Pursuit-Lifecycle-Design-2026-06-25.md section 3). Under Model B the
    CRM (CrmEngagements) owns win/loss, so these 177 historical wins are migrated in
    as closed-won engagements.

    Idempotent: keyed on (ExternalSource, ExternalSourceKey), guarded by NOT EXISTS
    and backed by the UX_CrmEngagements_ExternalKey unique index (migration 267).
    Re-running yields no duplicates. Reversible:
        DELETE FROM opportunities.CrmEngagements WHERE ExternalSource = N'Deltek.CustomProposal';

    Scope = Stage='Won' only (per design doc). Broadening to Submitted/Lost is a
    separate future decision and would be its own migration.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

INSERT INTO opportunities.CrmEngagements
(
    OpportunityId, Stage, OwnerStaffId, ProposedFee,
    OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
    CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy,
    BuyerCanonicalOrgId, WonLostOutcome, OutcomeReason,
    ExternalSource, ExternalSourceKey, Notes
)
SELECT
    NULL,                                                   -- OpportunityId (historical, no live RFP)
    6,                                                      -- Stage = Won
    NULL,                                                   -- OwnerStaffId (BD name kept in Notes; not a staff code)
    kp.BidFee,                                              -- ProposedFee
    CAST(COALESCE(kp.PursuitOpenedDate, kp.AwardDate, CAST(kp.CreatedAtUtc AS date)) AS DATETIMEOFFSET), -- OpenedAtUtc (NOT NULL)
    CAST(kp.AwardDate AS DATETIMEOFFSET),                   -- ClosedAtUtc
    N'Backfilled from Deltek CustomProposal (historical win).', -- OutcomeNotes
    SYSDATETIMEOFFSET(),                                    -- CreatedAtUtc
    N'migration-268-winbackfill',                           -- CreatedBy
    SYSDATETIMEOFFSET(),                                    -- UpdatedAtUtc
    N'migration-268-winbackfill',                           -- UpdatedBy
    kp.BuyerCanonicalOrgId,                                 -- BuyerCanonicalOrgId (may be NULL: 2 of 177)
    1,                                                      -- WonLostOutcome = Won
    kp.ClosedReason,                                        -- OutcomeReason
    N'Deltek.CustomProposal',                               -- ExternalSource (idempotency)
    kp.ExternalSourceKey,                                   -- ExternalSourceKey (idempotency)
    LEFT(CONCAT(kp.Title,
         CASE WHEN NULLIF(LTRIM(RTRIM(kp.KorBusinessDeveloper)), N'') IS NOT NULL
              THEN N' | BD: ' + kp.KorBusinessDeveloper ELSE N'' END), 4000) -- Notes (project + BD)
FROM opportunities.KorPursuits kp
WHERE kp.ExternalSource = N'Deltek.CustomProposal'
  AND kp.Stage = N'Won'
  AND kp.ExternalSourceKey IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM opportunities.CrmEngagements e
      WHERE e.ExternalSource = N'Deltek.CustomProposal'
        AND e.ExternalSourceKey = kp.ExternalSourceKey);
GO

PRINT 'Migration 268 complete.';
GO
