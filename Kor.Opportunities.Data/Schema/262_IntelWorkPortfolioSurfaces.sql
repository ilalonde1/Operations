USE [KorOpportunitiesDb];
GO

/* =====================================================================
   262 — Add normalized IntelWork portfolio edges to BD aggregate surfaces.
   ---------------------------------------------------------------------
   Keeps the existing MPI FK displacement-map rows as Source = 'Pipeline'
   and additively contributes IntelWork shared-project rows as Source =
   'Portfolio'. Consumers can aggregate across Source, but existing column
   names/types are preserved and Source is appended.
   ===================================================================== */

CREATE OR ALTER VIEW opportunities.vArchitectIncumbentSE AS
WITH PipelinePairs AS (
    SELECT arch.Id AS ArchitectOrgId, arch.DisplayName AS ArchitectName,
           se.Id   AS SeOrgId,        se.DisplayName   AS SeName,
           COUNT(*) AS SharedProjects, MAX(m.StartYear) AS LatestStartYear
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg arch ON arch.Id = m.ArchitectCanonicalOrgId AND arch.RetiredAtUtc IS NULL
    JOIN opportunities.CanonicalOrg se   ON se.Id   = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL
      AND arch.Id <> se.Id
      AND se.Kind <> N'KorStructural'   -- competitor SEs only (displacement targets)
    GROUP BY arch.Id, arch.DisplayName, se.Id, se.DisplayName
),
PortfolioArchitectWork AS (
    SELECT DISTINCT
           arch.Id AS ArchitectOrgId,
           arch.DisplayName AS ArchitectName,
           LOWER(LTRIM(RTRIM(iw.NormalizedProjectName))) AS NormalizedProjectName,
           TRY_CONVERT(smallint, NULLIF(LTRIM(RTRIM(iw.YearApprox)), N'')) AS YearApprox
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg arch ON arch.Id = iw.CanonicalOrgId AND arch.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL
      AND iw.NormalizedProjectName IS NOT NULL
      AND LTRIM(RTRIM(iw.NormalizedProjectName)) <> N''
      AND LOWER(ISNULL(iw.Role, N'')) LIKE N'%architect%'
),
PortfolioSeWork AS (
    SELECT DISTINCT
           se.Id AS SeOrgId,
           se.DisplayName AS SeName,
           LOWER(LTRIM(RTRIM(iw.NormalizedProjectName))) AS NormalizedProjectName,
           TRY_CONVERT(smallint, NULLIF(LTRIM(RTRIM(iw.YearApprox)), N'')) AS YearApprox
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg se ON se.Id = iw.CanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL
      AND iw.NormalizedProjectName IS NOT NULL
      AND LTRIM(RTRIM(iw.NormalizedProjectName)) <> N''
      AND (LOWER(ISNULL(iw.Role, N'')) LIKE N'%structural%' OR LOWER(ISNULL(iw.Role, N'')) LIKE N'%engineer%')
      AND se.Kind <> N'KorStructural'
),
PortfolioPairs AS (
    SELECT a.ArchitectOrgId, a.ArchitectName,
           s.SeOrgId,        s.SeName,
           COUNT(DISTINCT a.NormalizedProjectName) AS SharedProjects,
           MAX(COALESCE(a.YearApprox, s.YearApprox)) AS LatestStartYear
    FROM PortfolioArchitectWork a
    JOIN PortfolioSeWork s ON s.NormalizedProjectName = a.NormalizedProjectName
    WHERE a.ArchitectOrgId <> s.SeOrgId
    GROUP BY a.ArchitectOrgId, a.ArchitectName, s.SeOrgId, s.SeName
),
UnifiedPairs AS (
    SELECT ArchitectOrgId, ArchitectName, SeOrgId, SeName,
           SharedProjects, LatestStartYear,
           CAST(N'Pipeline' AS nvarchar(20)) AS Source
    FROM PipelinePairs
    UNION ALL
    SELECT ArchitectOrgId, ArchitectName, SeOrgId, SeName,
           SharedProjects, LatestStartYear,
           CAST(N'Portfolio' AS nvarchar(20)) AS Source
    FROM PortfolioPairs
),
KorFootIn AS (
    SELECT m.ArchitectCanonicalOrgId AS ArchitectOrgId, COUNT(*) AS KorSharedProjects
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL AND se.Kind = N'KorStructural' AND m.ArchitectCanonicalOrgId IS NOT NULL
    GROUP BY m.ArchitectCanonicalOrgId
)
SELECT p.ArchitectOrgId, p.ArchitectName, p.SeOrgId, p.SeName,
       p.SharedProjects, p.LatestStartYear,
       RANK() OVER (PARTITION BY p.ArchitectOrgId ORDER BY p.SharedProjects DESC, p.LatestStartYear DESC, p.Source) AS SeRankForArchitect,
       ISNULL(k.KorSharedProjects, 0) AS KorSharedProjectsWithArchitect,
       p.Source
FROM UnifiedPairs p
LEFT JOIN KorFootIn k ON k.ArchitectOrgId = p.ArchitectOrgId;
GO

CREATE OR ALTER VIEW opportunities.vGcIncumbentSE AS
WITH PipelinePairs AS (
    SELECT gc.Id AS GcOrgId, gc.DisplayName AS GcName,
           se.Id AS SeOrgId, se.DisplayName AS SeName,
           COUNT(*) AS SharedProjects, MAX(m.StartYear) AS LatestStartYear
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg gc ON gc.Id = m.GeneralContractorCanonicalOrgId AND gc.RetiredAtUtc IS NULL
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL
      AND gc.Id <> se.Id
      AND se.Kind <> N'KorStructural'
    GROUP BY gc.Id, gc.DisplayName, se.Id, se.DisplayName
),
PortfolioGcWork AS (
    SELECT DISTINCT
           gc.Id AS GcOrgId,
           gc.DisplayName AS GcName,
           LOWER(LTRIM(RTRIM(iw.NormalizedProjectName))) AS NormalizedProjectName,
           TRY_CONVERT(smallint, NULLIF(LTRIM(RTRIM(iw.YearApprox)), N'')) AS YearApprox
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg gc ON gc.Id = iw.CanonicalOrgId AND gc.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL
      AND iw.NormalizedProjectName IS NOT NULL
      AND LTRIM(RTRIM(iw.NormalizedProjectName)) <> N''
      AND (LOWER(ISNULL(iw.Role, N'')) LIKE N'%contractor%' OR UPPER(LTRIM(RTRIM(ISNULL(iw.Role, N'')))) = N'GC')
),
PortfolioSeWork AS (
    SELECT DISTINCT
           se.Id AS SeOrgId,
           se.DisplayName AS SeName,
           LOWER(LTRIM(RTRIM(iw.NormalizedProjectName))) AS NormalizedProjectName,
           TRY_CONVERT(smallint, NULLIF(LTRIM(RTRIM(iw.YearApprox)), N'')) AS YearApprox
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg se ON se.Id = iw.CanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL
      AND iw.NormalizedProjectName IS NOT NULL
      AND LTRIM(RTRIM(iw.NormalizedProjectName)) <> N''
      AND (LOWER(ISNULL(iw.Role, N'')) LIKE N'%structural%' OR LOWER(ISNULL(iw.Role, N'')) LIKE N'%engineer%')
      AND se.Kind <> N'KorStructural'
),
PortfolioPairs AS (
    SELECT g.GcOrgId, g.GcName,
           s.SeOrgId, s.SeName,
           COUNT(DISTINCT g.NormalizedProjectName) AS SharedProjects,
           MAX(COALESCE(g.YearApprox, s.YearApprox)) AS LatestStartYear
    FROM PortfolioGcWork g
    JOIN PortfolioSeWork s ON s.NormalizedProjectName = g.NormalizedProjectName
    WHERE g.GcOrgId <> s.SeOrgId
    GROUP BY g.GcOrgId, g.GcName, s.SeOrgId, s.SeName
),
UnifiedPairs AS (
    SELECT GcOrgId, GcName, SeOrgId, SeName,
           SharedProjects, LatestStartYear,
           CAST(N'Pipeline' AS nvarchar(20)) AS Source
    FROM PipelinePairs
    UNION ALL
    SELECT GcOrgId, GcName, SeOrgId, SeName,
           SharedProjects, LatestStartYear,
           CAST(N'Portfolio' AS nvarchar(20)) AS Source
    FROM PortfolioPairs
),
KorFootIn AS (
    SELECT m.GeneralContractorCanonicalOrgId AS GcOrgId, COUNT(*) AS KorSharedProjects
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.CanonicalOrg se ON se.Id = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
    WHERE m.RetiredAtUtc IS NULL AND se.Kind = N'KorStructural' AND m.GeneralContractorCanonicalOrgId IS NOT NULL
    GROUP BY m.GeneralContractorCanonicalOrgId
)
SELECT p.GcOrgId, p.GcName, p.SeOrgId, p.SeName,
       p.SharedProjects, p.LatestStartYear,
       RANK() OVER (PARTITION BY p.GcOrgId ORDER BY p.SharedProjects DESC, p.LatestStartYear DESC, p.Source) AS SeRankForGc,
       ISNULL(k.KorSharedProjects, 0) AS KorSharedProjectsWithGc,
       p.Source
FROM UnifiedPairs p
LEFT JOIN KorFootIn k ON k.GcOrgId = p.GcOrgId;
GO
