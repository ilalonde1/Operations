USE [KorOpportunitiesDb];
GO

/* =====================================================================
   170 — Apply VERIFIED corrections from the 2026-06-16 Project-Reverify
   Sonnet pass (all source-cited; see session log). Each UPDATE is name-
   guarded so it only touches the intended row.
     A. Real budgets replace the sector-median model (clear ModeledCostCad).
     B. Structural engineer now known -> not an open seat (set SE name).
     C. Built/complete -> retire (nightly retirement job parity).
     D. BC Budget Feb-2026 LTC pause -> mark stage On-Hold.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* A. real budgets (verified) */
UPDATE opportunities.MajorProjectsInventory
  SET EstimatedCostCad = 80000000, ModeledCostCad = NULL, UpdatedAtUtc=@now
  WHERE Id=5583 AND ProjectName LIKE '%ANSO%' AND RetiredAtUtc IS NULL;
UPDATE opportunities.MajorProjectsInventory
  SET EstimatedCostCad = 25464000, ModeledCostCad = NULL, UpdatedAtUtc=@now
  WHERE Id=5584 AND ProjectName LIKE '%Kenny%' AND RetiredAtUtc IS NULL;
UPDATE opportunities.MajorProjectsInventory
  SET EstimatedCostCad = 211000000, ModeledCostCad = NULL, UpdatedAtUtc=@now
  WHERE Id=5065 AND ProjectName LIKE '%Cottage-Worthington%' AND RetiredAtUtc IS NULL;

/* B. structural engineer now known (RJC) -> seat not open */
UPDATE opportunities.MajorProjectsInventory
  SET StructuralEngineerName = N'RJC Engineers', UpdatedAtUtc=@now
  WHERE Id=5584 AND ProjectName LIKE '%Kenny%' AND RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(StructuralEngineerName)),'') IS NULL;
UPDATE opportunities.MajorProjectsInventory
  SET StructuralEngineerName = N'RJC Engineers', UpdatedAtUtc=@now
  WHERE Id=6889 AND ProjectName LIKE '%Gateway North%' AND RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(StructuralEngineerName)),'') IS NULL;

/* C. built/complete (opened Sept 2025) -> retire */
UPDATE opportunities.MajorProjectsInventory
  SET RetiredAtUtc=@now, RetiredReason=N'Built/complete — opened Sep 2025 (reverify migration 170)', UpdatedAtUtc=@now
  WHERE Id=6906 AND ProjectName LIKE '%Gibson%' AND RetiredAtUtc IS NULL;
UPDATE opportunities.MajorProjectsInventory
  SET RetiredAtUtc=@now, RetiredReason=N'Built/complete — opened Sep 2025 (reverify migration 170)', UpdatedAtUtc=@now
  WHERE Id=6907 AND ProjectName LIKE '%Gathering House%' AND RetiredAtUtc IS NULL;

/* D. BC Budget Feb-2026 LTC pause -> On-Hold */
UPDATE opportunities.MajorProjectsInventory
  SET ProjectStage = N'On-Hold (BC Budget 2026 pause)', UpdatedAtUtc=@now
  WHERE Id IN (3148,3908,6587,5301,5065) AND RetiredAtUtc IS NULL
    AND (ProjectName LIKE '%Long-Term Care%' OR ProjectName LIKE '%Long Term Care%' OR ProjectName LIKE '%Cottage-Worthington%' OR ProjectName LIKE '%Squamish%' OR ProjectName LIKE '%Clyde%');
GO
