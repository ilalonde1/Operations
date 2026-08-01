/*
    Kor.OpportunitiesDb migration 272 — widen CrmEngagementStageHistory.ByStaffId
    nvarchar(20) -> nvarchar(150).

    Audit 2026-07-01 M2: migration 270 widened every staff-identity column to 150
    because the app identity is a ~26-char UPN, but this table (created by 267
    with the same nvarchar(20) sizing) was skipped — the first writer stamping a
    UPN would hit "String or binary data would be truncated". Writers are being
    wired in the same change set (grab creates the initial Drafting row; stage
    changes record transitions), so widen first.

    Idempotent (guarded on the current length). No index references ByStaffId.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA = 'opportunities' AND TABLE_NAME = 'CrmEngagementStageHistory'
             AND COLUMN_NAME = 'ByStaffId' AND CHARACTER_MAXIMUM_LENGTH = 20)
    ALTER TABLE opportunities.CrmEngagementStageHistory ALTER COLUMN ByStaffId nvarchar(150) NULL;
GO

PRINT 'Migration 272 complete.';
GO
