USE [KorOpportunitiesDb];
GO

/* =====================================================================
   181 — Retire the residual Sector='Industrial' out-of-lane projects
   that migration 180's name-match missed because they carry a building
   word ("...Export FACILITY", "...TERMINAL", "Grinding FACILITY") or a
   generic name with no extraction keyword (Aley, Fording River).

   These are aggregate/quarry, biofuel/biocarbon, energy/LPG export,
   grinding, and port-terminal projects — not building-SE seats. The
   StructuralRelevanceGate was extended in the same change to deny these
   classes (energy export / lpg / aggregate / quarry / biofuel /
   biocarbon / grinding facility / export terminal/facility) so they
   stop re-ingesting.

   KEEP (in-lane, do NOT retire):
     - "Plant and Animal Health Centre Replacement Project" — a health
       centre building (gate keeps it on "health centre").
     - "Aspen Point Program" — ambiguous; leave for research, not purged.

   Expected: 14 rows retired (16 Industrial − 2 keepers).
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = @now,
    RetiredReason = N'Out-of-lane (Industrial: aggregate/quarry/biofuel/energy-export/grinding/port-terminal — not a building SE seat) — migration 181',
    UpdatedAtUtc = @now
WHERE RetiredAtUtc IS NULL
  AND Sector = N'Industrial'
  AND ProjectName NOT LIKE N'%Health Centre%'   -- keep the health-centre building
  AND ProjectName NOT LIKE N'%Aspen Point%';    -- ambiguous: leave for research
GO
