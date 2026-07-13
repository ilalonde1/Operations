/* Connection-scoped like every other migration — no USE.
   Apply with sqlcmd -I (QUOTED_IDENTIFIER ON). */

/* =====================================================================
   285 — MajorProjectsInventory.SeatWindow: how soon the SE seat opens.
   ---------------------------------------------------------------------
   The channel research knows WHEN the structural seat is up ("now — team
   forming", "2026", "2027+"), but the weekly attack sheet only scored
   channel/sector/value, so a distant 2027+ master-plan could rank beside a
   pitch-now opening. SeatWindow makes urgency first-class so "now" plays
   lead. Distinct from SeatStatus (open/filled/locked = availability) and from
   the schedule text (construction dates, not the SE procurement window).
   Values: 'now' | '2026' | '2027+'  (paused/on-hold rows are excluded from
   the sheet via SeatStatus, so they need no window). NULL = not yet researched.

   NOTE: vw_ActionableProjects / vw_ActionableOpportunities are SELECT * over
   the base tables, so their column lists are frozen at creation — adding a
   base column requires sp_refreshsqlmodule for the views to expose it (this
   is the refresh promised in migration 284's header).
   ===================================================================== */

IF COL_LENGTH('opportunities.MajorProjectsInventory', 'SeatWindow') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD SeatWindow nvarchar(20) NULL;
GO

EXEC sp_refreshsqlmodule 'opportunities.vw_ActionableProjects';
EXEC sp_refreshsqlmodule 'opportunities.vw_ActionableOpportunities';
GO
