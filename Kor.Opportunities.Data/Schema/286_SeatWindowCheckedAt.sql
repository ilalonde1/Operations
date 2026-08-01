/* Connection-scoped; apply with sqlcmd -I (QUOTED_IDENTIFIER ON). */

/* =====================================================================
   286 — MajorProjectsInventory.SeatWindowCheckedAtUtc: when the SE-seat
         timing was last researched. This is the freshness stamp that stops
         the attack sheet trusting an aged 'now' — the sheet honours SeatWindow
         only if it was checked recently; SeatTimingRefreshJob re-checks the
         oldest ones on a small daily budget. Backfill = now for everything
         researched in today's sweep (it IS current as of today).
   ===================================================================== */

IF COL_LENGTH('opportunities.MajorProjectsInventory', 'SeatWindowCheckedAtUtc') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD SeatWindowCheckedAtUtc datetimeoffset NULL;
GO

UPDATE opportunities.MajorProjectsInventory
   SET SeatWindowCheckedAtUtc = SYSDATETIMEOFFSET()
 WHERE SeatWindow IS NOT NULL AND SeatWindowCheckedAtUtc IS NULL;
GO

EXEC sp_refreshsqlmodule 'opportunities.vw_ActionableProjects';
GO
