USE [KorOpportunitiesDb];
GO

/* =====================================================================
   158 — BD leverage views: the proprietary synthesis layer, computed live.
   ---------------------------------------------------------------------
   Turns the hand-built "beyond-platinum" analysis into permanent, queryable
   views so every regional report (and the future dashboard / one-pager) gets:
     vOrgWarmPath       — KOR's relationship distance to any org (Deltek client?
                          prior KOR projects? contacts? emailable?) + a score.
     vOpenSeatPursuit   — active projects with NO structural engineer committed,
                          joined to the architect/owner/GC warm-path = ranked seats.
     vDeltekCrossSell   — KOR's Deltek clients with active projects KOR is NOT on.
     vSeismicProject    — BC seismic projects flagged, with open-seat marker.
   KOR self-anchor = 38918. Idempotent (CREATE OR ALTER).
   ===================================================================== */
GO

CREATE OR ALTER VIEW opportunities.vOrgWarmPath AS
WITH base AS (
  SELECT co.Id AS CanonicalOrgId, co.DisplayName, co.Kind,
    CASE WHEN co.ClendorClientId IS NOT NULL THEN 1 ELSE 0 END AS IsDeltekClient,
    ISNULL(co.KorProjectsCount, 0) AS KorProjectsCount,
    (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
       JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
       WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NULL) AS Contacts,
    (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
       JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
       WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NULL
         AND NULLIF(LTRIM(RTRIM(p.Email)),'') IS NOT NULL) AS EmailableContacts
  FROM opportunities.CanonicalOrg co
  WHERE co.RetiredAtUtc IS NULL)
SELECT base.*,
  (IsDeltekClient * 3
   + CASE WHEN KorProjectsCount > 0 THEN 2 ELSE 0 END
   + CASE WHEN Contacts > 0 THEN 1 ELSE 0 END
   + CASE WHEN EmailableContacts > 0 THEN 1 ELSE 0 END) AS WarmPathScore
FROM base;
GO

CREATE OR ALTER VIEW opportunities.vDeltekCrossSell AS
SELECT owner.Id AS ClientOrgId, owner.DisplayName AS Client,
  ISNULL(owner.KorProjectsCount, 0) AS PriorKorProjects,
  m.Province, m.RegionName,
  COUNT(*) AS ActiveProjectsNotKor,
  CAST(SUM(m.EstimatedCostCad) / 1000000.0 AS decimal(14,1)) AS ActiveEstM
FROM opportunities.CanonicalOrg owner
JOIN opportunities.MajorProjectsInventory m ON m.ProponentCanonicalOrgId = owner.Id
WHERE owner.ClendorClientId IS NOT NULL AND owner.RetiredAtUtc IS NULL
  AND m.RetiredAtUtc IS NULL
  AND (m.StructuralEngineerCanonicalOrgId IS NULL OR m.StructuralEngineerCanonicalOrgId <> 38918)
GROUP BY owner.Id, owner.DisplayName, owner.KorProjectsCount, m.Province, m.RegionName;
GO

CREATE OR ALTER VIEW opportunities.vSeismicProject AS
SELECT m.Id AS MpiId, m.Province, m.RegionName, m.MunicipalityName, m.ProjectName,
  m.EstimatedCostCad, m.ProjectStage, m.ProponentCanonicalOrgId,
  COALESCE(ow.DisplayName, NULLIF(m.ProponentName,'')) AS Owner,
  COALESCE(se.DisplayName, NULLIF(m.StructuralEngineerName,'')) AS SE,
  CASE WHEN m.StructuralEngineerCanonicalOrgId IS NULL
            AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL
       THEN 1 ELSE 0 END AS IsOpenSeat
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId
LEFT JOIN opportunities.CanonicalOrg ow ON ow.Id = m.ProponentCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND ( m.ProjectName LIKE '%seismic%' OR m.ProjectDescription LIKE '%seismic%'
     OR m.ProjectName LIKE '%SMP%'
     OR (m.ProjectName LIKE '%eplacement%' AND m.Sector LIKE '%chool%') );
GO

CREATE OR ALTER VIEW opportunities.vOpenSeatPursuit AS
SELECT m.Id AS MpiId, m.Province, m.RegionName, m.MunicipalityName, m.ProjectName,
  m.EstimatedCostCad, m.ProjectStage,
  COALESCE(ar.DisplayName, NULLIF(m.ArchitectName,'')) AS Architect,
  COALESCE(gc.DisplayName, NULLIF(m.GeneralContractorName,'')) AS GC,
  COALESCE(ow.DisplayName, NULLIF(m.ProponentName,'')) AS Owner,
  awp.WarmPathScore AS ArchitectWarmScore,
  gwp.WarmPathScore AS GcWarmScore,
  owp.WarmPathScore AS OwnerWarmScore,
  (ISNULL(awp.WarmPathScore,0) + ISNULL(gwp.WarmPathScore,0) + ISNULL(owp.WarmPathScore,0)) AS TeamWarmScore,
  CASE WHEN awp.IsDeltekClient = 1 OR gwp.IsDeltekClient = 1 OR owp.IsDeltekClient = 1 THEN 1 ELSE 0 END AS TeamIncludesKorClient
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.CanonicalOrg ar ON ar.Id = m.ArchitectCanonicalOrgId
LEFT JOIN opportunities.CanonicalOrg gc ON gc.Id = m.GeneralContractorCanonicalOrgId
LEFT JOIN opportunities.CanonicalOrg ow ON ow.Id = m.ProponentCanonicalOrgId
LEFT JOIN opportunities.vOrgWarmPath awp ON awp.CanonicalOrgId = m.ArchitectCanonicalOrgId
LEFT JOIN opportunities.vOrgWarmPath gwp ON gwp.CanonicalOrgId = m.GeneralContractorCanonicalOrgId
LEFT JOIN opportunities.vOrgWarmPath owp ON owp.CanonicalOrgId = m.ProponentCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.StructuralEngineerCanonicalOrgId IS NULL
  AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL;
GO
