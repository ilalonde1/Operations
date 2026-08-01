USE [KorOpportunitiesDb];
GO

/* =====================================================================
   178 — Retire the "Highstreet Calgary" MPI artifact. Research (2026-06-17)
   could not verify this $400M Shape Properties project in any public
   registry, news, or planning database; the Alberta Major Projects record
   it came from 404s and redirects to an unrelated "West Mixed Use" entry.
   It was the #1 Calgary warm seat by value — a phantom inflating the funnel.
   Retire it. (Distinct from the real "Highstreet at Cornerstone" / Anthem.)
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = @now, RetiredReason = N'Unverifiable phantom record (broken AB Major Projects source) — migration 178', UpdatedAtUtc = @now
WHERE Province = 'AB' AND RetiredAtUtc IS NULL
  AND LTRIM(RTRIM(ProjectName)) = N'Highstreet Calgary';
GO
