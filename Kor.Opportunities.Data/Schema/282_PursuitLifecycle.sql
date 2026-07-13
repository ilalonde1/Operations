/* Connection-scoped like every other migration — no USE.
   Apply with sqlcmd -I (QUOTED_IDENTIFIER ON — see migration 277's lesson). */

/* =====================================================================
   282 — Pursuit lifecycle: human ownership + "not for us" as first-class,
         auditable state; ONE actionable predicate for every surface.
   ---------------------------------------------------------------------
   Problem: MajorProjectsInventory rows (the weekly-attack-sheet / prime-
   target surface) had no human lifecycle — nobody could own a play or
   remove a bad one, and every consumer (sheet, boards, digests, reports)
   re-derived its own idea of "actionable" inline, which is exactly the
   drift class audit-v2 kept finding.

   Design:
   - OWNERSHIP (MPI): OwnerStaffId/OwnedAtUtc — mirrors Opportunities'
     existing Bazaar grab. Owned rows leave the shared pool.
   - DISMISSAL (both tables): DismissedAtUtc/By/Reason — a HUMAN judgment,
     deliberately separate from the system's RetiredAtUtc staleness reaper
     so "machine aged it out" and "a person said no" stay distinguishable.
     Never a delete; admin surfaces read the base table.
   - AUDIT: OpportunityAssignmentLog gains a nullable MpiId so both
     entities share one assignment/audit stream (Action literals:
     MpiOwn / MpiRelease / MpiDismiss / MpiRestore / MpiReap,
     OppDismiss / OppRestore — alongside the existing 'Grab').
   - VIEWS: vw_ActionableProjects / vw_ActionableOpportunities are THE
     actionable predicate. Consumers select from the views; doctrine test
     D11 fails the build if a job re-derives lifecycle columns inline.
     NOTE: views are SELECT * — if a future migration adds base-table
     columns, refresh with EXEC sp_refreshsqlmodule on both views.
   ===================================================================== */

/* ---- 1. MajorProjectsInventory: ownership + dismissal ---------------- */
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'OwnerStaffId') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD OwnerStaffId nvarchar(300) NULL;
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'OwnedAtUtc') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD OwnedAtUtc datetimeoffset NULL;
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'DismissedAtUtc') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD DismissedAtUtc datetimeoffset NULL;
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'DismissedBy') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD DismissedBy nvarchar(300) NULL;
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'DismissedReason') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD DismissedReason nvarchar(1000) NULL;
GO

/* ---- 2. Opportunities: dismissal (ownership already exists) ---------- */
IF COL_LENGTH('opportunities.Opportunities', 'DismissedAtUtc') IS NULL
    ALTER TABLE opportunities.Opportunities ADD DismissedAtUtc datetimeoffset NULL;
IF COL_LENGTH('opportunities.Opportunities', 'DismissedBy') IS NULL
    ALTER TABLE opportunities.Opportunities ADD DismissedBy nvarchar(300) NULL;
IF COL_LENGTH('opportunities.Opportunities', 'DismissedReason') IS NULL
    ALTER TABLE opportunities.Opportunities ADD DismissedReason nvarchar(1000) NULL;
GO

/* ---- 3. Shared audit stream: log rows can reference an MPI ----------- */
IF COL_LENGTH('opportunities.OpportunityAssignmentLog', 'MpiId') IS NULL
    ALTER TABLE opportunities.OpportunityAssignmentLog ADD MpiId bigint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OpportunityAssignmentLog_MpiId'
                 AND object_id = OBJECT_ID('opportunities.OpportunityAssignmentLog'))
    CREATE NONCLUSTERED INDEX IX_OpportunityAssignmentLog_MpiId
        ON opportunities.OpportunityAssignmentLog (MpiId)
        WHERE MpiId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_MajorProjectsInventory_OwnerStaffId'
                 AND object_id = OBJECT_ID('opportunities.MajorProjectsInventory'))
    CREATE NONCLUSTERED INDEX IX_MajorProjectsInventory_OwnerStaffId
        ON opportunities.MajorProjectsInventory (OwnerStaffId)
        INCLUDE (OwnedAtUtc)
        WHERE OwnerStaffId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_MajorProjectsInventory_DismissedAtUtc'
                 AND object_id = OBJECT_ID('opportunities.MajorProjectsInventory'))
    CREATE NONCLUSTERED INDEX IX_MajorProjectsInventory_DismissedAtUtc
        ON opportunities.MajorProjectsInventory (DismissedAtUtc)
        WHERE DismissedAtUtc IS NOT NULL;
GO

/* ---- 4. THE actionable predicate (one definition, every surface) ----- */
CREATE OR ALTER VIEW opportunities.vw_ActionableProjects
AS
/* Human-actionable prime-target pool. A row leaves this view when the
   system retires it, a person dismisses it, a person owns it, or the SE
   seat is known filled/locked. Freshness windows stay per-surface knobs
   (e.g. the weekly sheet's 45-day rule) — they are NOT lifecycle. */
SELECT m.*
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL
  AND m.DismissedAtUtc IS NULL
  AND m.OwnerStaffId IS NULL
  AND ISNULL(m.SeatStatus, N'') NOT IN (N'filled', N'locked');
GO

CREATE OR ALTER VIEW opportunities.vw_ActionableOpportunities
AS
/* The grabbable tender pool (what the Grab Opportunities board shows).
   Status 1 = New (OpportunityEnums.cs, values stable on disk). */
SELECT o.*
FROM opportunities.Opportunities o
WHERE o.Status = 1
  AND o.OwnerStaffId IS NULL
  AND o.DismissedAtUtc IS NULL;
GO
