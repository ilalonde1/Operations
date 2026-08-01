USE [KorOpportunitiesDb];
GO

/* =====================================================================
   142 — BD "one-of-a-kind" differentiator views (read-only, over existing data).
   ---------------------------------------------------------------------
   1) vArchitectIncumbentSE — the displacement map. For each architect, the
      COMPETITOR structural engineers they pair with (shared active MPI projects),
      ranked, plus how many of that architect's projects already used KOR. The core
      win-path: who is the incumbent SE to displace, and where KOR has a foot in.
   2) vGcIncumbentSE — same for general contractors.
   3) vRegionBdRollup — per canonical region (post-migration-141), project + distinct
      org-by-role counts. The clean regional intelligence slice.
   All filter RetiredAtUtc IS NULL on every joined entity.
   ===================================================================== */

CREATE OR ALTER VIEW opportunities.vArchitectIncumbentSE AS
WITH Pairs AS (
    SELECT arch.Id AS ArchitectOrgId, arch.DisplayName AS ArchitectName,
           se.Id   AS SeOrgId,        se.DisplayName   AS SeName,
           COUNT(*) AS SharedProjects, MAX(m.StartYear) AS LatestStartYear
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg arch ON arch.Id = m.ArchitectCanonicalOrgId AND arch.RetiredAtUtc IS NULL
    JOIN opportunities.CanonicalOrg se   ON se.Id   = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL
      AND se.Kind <> N'KorStructural'   -- competitor SEs only (displacement targets)
    GROUP BY arch.Id, arch.DisplayName, se.Id, se.DisplayName
),
KorFootIn AS (
    SELECT m.ArchitectCanonicalOrgId AS ArchitectOrgId, COUNT(*) AS KorSharedProjects
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId
    WHERE m.RetiredAtUtc IS NULL AND se.Kind = N'KorStructural' AND m.ArchitectCanonicalOrgId IS NOT NULL
    GROUP BY m.ArchitectCanonicalOrgId
)
SELECT p.ArchitectOrgId, p.ArchitectName, p.SeOrgId, p.SeName,
       p.SharedProjects, p.LatestStartYear,
       RANK() OVER (PARTITION BY p.ArchitectOrgId ORDER BY p.SharedProjects DESC, p.LatestStartYear DESC) AS SeRankForArchitect,
       ISNULL(k.KorSharedProjects, 0) AS KorSharedProjectsWithArchitect
FROM Pairs p
LEFT JOIN KorFootIn k ON k.ArchitectOrgId = p.ArchitectOrgId;
GO

CREATE OR ALTER VIEW opportunities.vGcIncumbentSE AS
WITH Pairs AS (
    SELECT gc.Id AS GcOrgId, gc.DisplayName AS GcName,
           se.Id AS SeOrgId, se.DisplayName AS SeName,
           COUNT(*) AS SharedProjects, MAX(m.StartYear) AS LatestStartYear
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg gc ON gc.Id = m.GeneralContractorCanonicalOrgId AND gc.RetiredAtUtc IS NULL
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL AND se.Kind <> N'KorStructural'
    GROUP BY gc.Id, gc.DisplayName, se.Id, se.DisplayName
),
KorFootIn AS (
    SELECT m.GeneralContractorCanonicalOrgId AS GcOrgId, COUNT(*) AS KorSharedProjects
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId
    WHERE m.RetiredAtUtc IS NULL AND se.Kind = N'KorStructural' AND m.GeneralContractorCanonicalOrgId IS NOT NULL
    GROUP BY m.GeneralContractorCanonicalOrgId
)
SELECT p.GcOrgId, p.GcName, p.SeOrgId, p.SeName,
       p.SharedProjects, p.LatestStartYear,
       RANK() OVER (PARTITION BY p.GcOrgId ORDER BY p.SharedProjects DESC, p.LatestStartYear DESC) AS SeRankForGc,
       ISNULL(k.KorSharedProjects, 0) AS KorSharedProjectsWithGc
FROM Pairs p
LEFT JOIN KorFootIn k ON k.GcOrgId = p.GcOrgId;
GO

CREATE OR ALTER VIEW opportunities.vRegionBdRollup AS
SELECT m.Province,
       ISNULL(m.RegionName, N'(unspecified)') AS Region,
       COUNT(*) AS Projects,
       COUNT(DISTINCT m.ArchitectCanonicalOrgId)           AS Architects,
       COUNT(DISTINCT m.StructuralEngineerCanonicalOrgId)  AS StructuralEngineers,
       COUNT(DISTINCT m.GeneralContractorCanonicalOrgId)   AS GeneralContractors,
       COUNT(DISTINCT m.ProponentCanonicalOrgId)           AS Owners
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL AND m.Province IN (N'BC', N'AB')
GROUP BY m.Province, m.RegionName;
GO
