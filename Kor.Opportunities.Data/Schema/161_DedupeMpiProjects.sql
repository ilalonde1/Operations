USE [KorOpportunitiesDb];
GO

/* =====================================================================
   161 — Dedupe 15 unambiguous duplicate MPI project rows (reviewed).
   ---------------------------------------------------------------------
   Same project ingested twice. Survivor = richer/staged row; before retiring the
   loser, COALESCE any role link / stage / cost the survivor lacks from the loser
   (no data loss). Losers soft-retired.

   DELIBERATELY EXCLUDED (not dups — verified): 9855 Austin Rd Towers 5&6 vs 7&8
   (distinct towers); SFU Shrum Biology vs Physics (distinct buildings); the four
   "Condominium Development" + two "Residential Condominium" rows (generic names on
   DIFFERENT projects); Richmond Hospital Phase 2 vs Phase 2&3 (marquee, ambiguous).
   ===================================================================== */

IF OBJECT_ID('tempdb..#pp') IS NOT NULL DROP TABLE #pp;
CREATE TABLE #pp (Survivor BIGINT, Loser BIGINT);
INSERT INTO #pp VALUES
 (4612,4462),(4992,5495),(4524,5081),(6550,3986),(6551,3988),(4983,5082),(7013,6451),
 (4519,3128),(3174,3981),(6585,3147),(2898,2753),(5078,4944),(3204,3992),(4603,4447),(6882,3887);

/* Copy any link/stage/cost the survivor lacks from the loser. */
UPDATE s SET
  s.StructuralEngineerCanonicalOrgId = COALESCE(s.StructuralEngineerCanonicalOrgId, l.StructuralEngineerCanonicalOrgId),
  s.ArchitectCanonicalOrgId          = COALESCE(s.ArchitectCanonicalOrgId, l.ArchitectCanonicalOrgId),
  s.GeneralContractorCanonicalOrgId  = COALESCE(s.GeneralContractorCanonicalOrgId, l.GeneralContractorCanonicalOrgId),
  s.ProponentCanonicalOrgId          = COALESCE(s.ProponentCanonicalOrgId, l.ProponentCanonicalOrgId),
  s.StructuralEngineerName           = COALESCE(NULLIF(LTRIM(RTRIM(s.StructuralEngineerName)),''), l.StructuralEngineerName),
  s.ArchitectName                    = COALESCE(NULLIF(LTRIM(RTRIM(s.ArchitectName)),''), l.ArchitectName),
  s.GeneralContractorName            = COALESCE(NULLIF(LTRIM(RTRIM(s.GeneralContractorName)),''), l.GeneralContractorName),
  s.ProjectStage                     = COALESCE(NULLIF(LTRIM(RTRIM(s.ProjectStage)),''), l.ProjectStage),
  s.EstimatedCostCad                 = COALESCE(s.EstimatedCostCad, l.EstimatedCostCad),
  s.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.MajorProjectsInventory s
JOIN #pp p ON p.Survivor = s.Id
JOIN opportunities.MajorProjectsInventory l ON l.Id = p.Loser;

/* Retire the losers. */
UPDATE l SET l.RetiredAtUtc = SYSDATETIMEOFFSET(),
       l.RetiredReason = N'Duplicate project — merged into ' + CAST(p.Survivor AS NVARCHAR(20)) + N' (migration 161)',
       l.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.MajorProjectsInventory l
JOIN #pp p ON p.Loser = l.Id
WHERE l.RetiredAtUtc IS NULL;

DROP TABLE #pp;
GO
