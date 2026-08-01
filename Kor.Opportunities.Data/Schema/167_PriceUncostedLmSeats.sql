USE [KorOpportunitiesDb];
GO

/* =====================================================================
   167 — Price the Lower Mainland uncosted open seats (modeled estimate).
   ---------------------------------------------------------------------
   394 LM open seats carry no EstimatedCostCad. This stores a MODELED value
   in a NEW, SEPARATE column (ModeledCostCad) via sector-median imputation,
   so the verified EstimatedCostCad / the $63.5B headline stay untouched.
   A seat's planning value = COALESCE(EstimatedCostCad, ModeledCostCad);
   ModeledCostCad IS NOT NULL flags "modeled, not actual".

   Bucket medians (CAD) derived from the 336 costed LM open seats this cycle:
     K12 57M · Healthcare 286M · PostSec 388M · Civic 103M · Residential 38M
     Other 80M  (Other's raw 3-sample median was a $400M artifact — set to a
     conservative building median instead).
   Sector labels are messy (School/education/K-12 etc.) so they're normalized
   into 6 buckets by the same CASE used to derive the medians.
   ===================================================================== */

IF COL_LENGTH('opportunities.MajorProjectsInventory','ModeledCostCad') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD ModeledCostCad DECIMAL(18,2) NULL;
GO

UPDATE m
SET m.ModeledCostCad =
      CASE
        WHEN LOWER(ISNULL(m.Sector,'')) LIKE '%hospital%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%health%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%care%' THEN 286000000
        WHEN LOWER(ISNULL(m.Sector,'')) LIKE '%post-sec%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%academic%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%university%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%studenthousing%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%lab%' THEN 388000000
        WHEN LOWER(ISNULL(m.Sector,'')) LIKE '%school%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%k-12%' OR LOWER(ISNULL(m.Sector,'')) = 'education' OR LOWER(ISNULL(m.Sector,'')) LIKE '%seismic%' THEN 57000000
        WHEN LOWER(ISNULL(m.Sector,'')) LIKE '%residential%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%mixed-use%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%housing%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%condo%' THEN 38000000
        WHEN LOWER(ISNULL(m.Sector,'')) LIKE '%civic%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%institution%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%recreation%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%librar%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%cultural%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%fire%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%police%' OR LOWER(ISNULL(m.Sector,'')) LIKE '%transit%' THEN 103000000
        ELSE 80000000
      END,
    m.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId
WHERE m.Province='BC' AND m.RegionName='Lower Mainland' AND m.RetiredAtUtc IS NULL
  AND se.Id IS NULL AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL
  AND m.EstimatedCostCad IS NULL
  AND m.ModeledCostCad IS NULL;
GO
