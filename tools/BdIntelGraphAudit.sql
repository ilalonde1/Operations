/*
BD intelligence graph adversarial audit.

Run against KorOpportunitiesDb. Read-only: this script creates temp tables only.
It intentionally reports FK-only coverage separately from normalized intel coverage
so sparse denormalized MajorProjectsInventory role columns do not masquerade as
the whole graph.
*/

SET NOCOUNT ON;

IF DB_NAME() <> N'KorOpportunitiesDb'
BEGIN
    PRINT N'Warning: current database is ' + DB_NAME() + N'; expected KorOpportunitiesDb.';
END;

IF SCHEMA_ID(N'opportunities') IS NULL
BEGIN
    THROW 50000, 'Schema opportunities was not found.', 1;
END;

DROP TABLE IF EXISTS #ActiveMpi;
DROP TABLE IF EXISTS #MpiNorm;
DROP TABLE IF EXISTS #HighValueOrg;
DROP TABLE IF EXISTS #MpiRoleCells;
DROP TABLE IF EXISTS #IntelWorkRoleCells;
DROP TABLE IF EXISTS #IntelWorkMpiCandidates;
DROP TABLE IF EXISTS #IntelWorkNewCells;

SELECT
    m.Id,
    m.ProjectName,
    m.Province,
    m.RegionName,
    m.MunicipalityName,
    m.Sector,
    m.SubSector,
    m.Stage,
    m.ProjectStage,
    m.EstimatedCostCad,
    m.ProponentCanonicalOrgId,
    m.ProponentName,
    m.ArchitectCanonicalOrgId,
    m.ArchitectName,
    m.StructuralEngineerCanonicalOrgId,
    m.StructuralEngineerName,
    m.GeneralContractorCanonicalOrgId,
    m.GeneralContractorName,
    m.LastSeenAtUtc,
    m.UpdatedAtUtc
INTO #ActiveMpi
FROM opportunities.MajorProjectsInventory AS m
WHERE m.RetiredAtUtc IS NULL;

SELECT
    m.Id,
    LOWER(LTRIM(RTRIM(m.ProjectName))) AS LowerProjectName,
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)), N' ', N''), N'-', N''), N'.', N''), N',', N''), N'''', N'')) AS RoughNormalizedProjectName
INTO #MpiNorm
FROM #ActiveMpi AS m;

SELECT
    co.Id AS CanonicalOrgId,
    co.DisplayName,
    co.Kind,
    co.ClendorClientId,
    ISNULL(co.KorProjectsCount, 0) AS KorProjectsCount,
    co.LastKorProjectAtUtc,
    CAST(CASE WHEN co.ClendorClientId IS NOT NULL OR ISNULL(co.KorProjectsCount, 0) > 0 OR co.LastKorProjectAtUtc IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsKorClientOrKnownKorRelationship,
    CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM #ActiveMpi AS m
        WHERE co.Id IN (m.ProponentCanonicalOrgId, m.ArchitectCanonicalOrgId, m.StructuralEngineerCanonicalOrgId, m.GeneralContractorCanonicalOrgId)
    ) THEN 1 ELSE 0 END AS bit) AS IsOnActiveMpiProject,
    CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM opportunities.KorPursuits AS kp
        WHERE co.Id IN (kp.BuyerCanonicalOrgId, kp.LostToCanonicalOrgId)
    ) THEN 1 ELSE 0 END AS bit) AS IsInKorPursuits
INTO #HighValueOrg
FROM opportunities.CanonicalOrg AS co
WHERE co.RetiredAtUtc IS NULL
  AND (
      co.ClendorClientId IS NOT NULL
      OR ISNULL(co.KorProjectsCount, 0) > 0
      OR co.LastKorProjectAtUtc IS NOT NULL
      OR EXISTS (
          SELECT 1
          FROM #ActiveMpi AS m
          WHERE co.Id IN (m.ProponentCanonicalOrgId, m.ArchitectCanonicalOrgId, m.StructuralEngineerCanonicalOrgId, m.GeneralContractorCanonicalOrgId)
      )
  );

SELECT
    m.Id AS MpiId,
    v.RoleClass,
    v.CanonicalOrgId,
    v.DisplayName
INTO #MpiRoleCells
FROM #ActiveMpi AS m
CROSS APPLY (VALUES
    (N'Proponent',          m.ProponentCanonicalOrgId,          m.ProponentName),
    (N'Architect',          m.ArchitectCanonicalOrgId,          m.ArchitectName),
    (N'StructuralEngineer', m.StructuralEngineerCanonicalOrgId, m.StructuralEngineerName),
    (N'GeneralContractor',  m.GeneralContractorCanonicalOrgId,  m.GeneralContractorName)
) AS v(RoleClass, CanonicalOrgId, DisplayName);

SELECT
    iw.Id AS IntelWorkId,
    iw.CanonicalOrgId,
    co.DisplayName AS OrgName,
    co.Kind AS OrgKind,
    iw.ProjectName,
    iw.NormalizedProjectName,
    iw.Role,
    CASE
        WHEN iw.Role IS NULL THEN N'Unknown'
        WHEN LOWER(iw.Role) LIKE N'%struct%' OR LOWER(iw.Role) IN (N'se', N'eor', N'engineer of record') THEN N'StructuralEngineer'
        WHEN LOWER(iw.Role) LIKE N'%architect%' THEN N'Architect'
        WHEN LOWER(iw.Role) LIKE N'%general contract%' OR LOWER(iw.Role) = N'gc' OR LOWER(iw.Role) LIKE N'%construction manager%' THEN N'GeneralContractor'
        WHEN LOWER(iw.Role) LIKE N'%owner%' OR LOWER(iw.Role) LIKE N'%developer%' OR LOWER(iw.Role) LIKE N'%proponent%' OR LOWER(iw.Role) LIKE N'%client%' THEN N'Proponent'
        ELSE N'Other'
    END AS RoleClass,
    iw.MajorProjectsInventoryId,
    iw.LastSeenAtUtc,
    iw.SourceProviderName,
    iw.SourceConfidence
INTO #IntelWorkRoleCells
FROM opportunities.IntelWork AS iw
JOIN opportunities.CanonicalOrg AS co
    ON co.Id = iw.CanonicalOrgId
   AND co.RetiredAtUtc IS NULL
WHERE iw.RetiredAtUtc IS NULL;

SELECT DISTINCT
    COALESCE(d.Id, n.Id) AS MpiId,
    iw.RoleClass,
    iw.CanonicalOrgId,
    iw.IntelWorkId,
    CASE WHEN d.Id IS NOT NULL THEN N'DirectMajorProjectsInventoryId' ELSE N'NameMatchCandidate' END AS LinkMode
INTO #IntelWorkMpiCandidates
FROM #IntelWorkRoleCells AS iw
LEFT JOIN #ActiveMpi AS d
    ON d.Id = iw.MajorProjectsInventoryId
LEFT JOIN #MpiNorm AS n
    ON iw.MajorProjectsInventoryId IS NULL
   AND (
       n.LowerProjectName = LOWER(LTRIM(RTRIM(iw.ProjectName)))
       OR n.RoughNormalizedProjectName = iw.NormalizedProjectName
   )
WHERE iw.RoleClass IN (N'Proponent', N'Architect', N'StructuralEngineer', N'GeneralContractor')
  AND COALESCE(d.Id, n.Id) IS NOT NULL;

SELECT DISTINCT
    c.MpiId,
    c.RoleClass,
    c.CanonicalOrgId,
    c.LinkMode
INTO #IntelWorkNewCells
FROM #IntelWorkMpiCandidates AS c
JOIN #ActiveMpi AS m
    ON m.Id = c.MpiId
WHERE
    (c.RoleClass = N'Proponent'          AND m.ProponentCanonicalOrgId IS NULL)
 OR (c.RoleClass = N'Architect'          AND m.ArchitectCanonicalOrgId IS NULL)
 OR (c.RoleClass = N'StructuralEngineer' AND m.StructuralEngineerCanonicalOrgId IS NULL)
 OR (c.RoleClass = N'GeneralContractor'  AND m.GeneralContractorCanonicalOrgId IS NULL);

PRINT N'1. Dossier/narrative coverage by org kind. Healthy: target org kinds have high CanonicalOrgEnrichment and IntelNarrative coverage; gap: many high-value rows with zero enrichment or zero narrative.';
SELECT
    co.Kind,
    COUNT(*) AS LiveOrgs,
    SUM(CASE WHEN e.HasEnrichment IS NOT NULL THEN 1 ELSE 0 END) AS OrgsWithAnyEnrichment,
    SUM(CASE WHEN n.HasNarrative IS NOT NULL THEN 1 ELSE 0 END) AS OrgsWithLiveNarrative,
    CAST(100.0 * SUM(CASE WHEN e.HasEnrichment IS NOT NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS EnrichmentPct,
    CAST(100.0 * SUM(CASE WHEN n.HasNarrative IS NOT NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS NarrativePct
FROM opportunities.CanonicalOrg AS co
OUTER APPLY (
    SELECT TOP (1) 1 AS HasEnrichment
    FROM opportunities.CanonicalOrgEnrichment AS e
    WHERE e.CanonicalOrgId = co.Id
      AND e.Status IN (N'ok', N'Manual')
      AND e.ResultJson IS NOT NULL
) AS e
OUTER APPLY (
    SELECT TOP (1) 1 AS HasNarrative
    FROM opportunities.IntelNarrative AS n
    WHERE n.CanonicalOrgId = co.Id
      AND n.RetiredAtUtc IS NULL
) AS n
WHERE co.RetiredAtUtc IS NULL
GROUP BY co.Kind
ORDER BY LiveOrgs DESC, co.Kind;

PRINT N'2. People-affiliation coverage for high-value orgs. Healthy: high-value owners/architects/competitors/GCs have current affiliations; gap: high-value orgs with zero people.';
SELECT
    h.Kind,
    COUNT(*) AS HighValueOrgs,
    SUM(CASE WHEN pa.PeopleCount > 0 THEN 1 ELSE 0 END) AS HighValueOrgsWithPeople,
    SUM(CASE WHEN pa.EmailablePeopleCount > 0 THEN 1 ELSE 0 END) AS HighValueOrgsWithEmailablePeople,
    SUM(pa.PeopleCount) AS PeopleAffiliations,
    SUM(pa.EmailablePeopleCount) AS EmailablePeopleAffiliations,
    CAST(100.0 * SUM(CASE WHEN pa.PeopleCount > 0 THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS PeopleCoveragePct
FROM #HighValueOrg AS h
OUTER APPLY (
    SELECT
        COUNT(DISTINCT p.Id) AS PeopleCount,
        COUNT(DISTINCT CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)), N'') IS NOT NULL THEN p.Id END) AS EmailablePeopleCount
    FROM opportunities.IntelPersonAffiliation AS a
    JOIN opportunities.IntelPerson AS p
        ON p.Id = a.IntelPersonId
       AND p.RetiredAtUtc IS NULL
    WHERE a.CanonicalOrgId = h.CanonicalOrgId
      AND a.RetiredAtUtc IS NULL
      AND a.IsCurrent = 1
) AS pa
GROUP BY h.Kind
ORDER BY HighValueOrgs DESC, h.Kind;

PRINT N'3. Firm portfolio teaming history from IntelWork. Healthy: substantial role-labeled work history by role, not just MPI FKs; gap: mostly Unknown/Other roles or low counts for core roles.';
SELECT
    RoleClass,
    COUNT(*) AS LiveIntelWorkEdges,
    COUNT(DISTINCT CanonicalOrgId) AS DistinctOrgs,
    COUNT(DISTINCT NormalizedProjectName) AS DistinctProjectsByName,
    MIN(LastSeenAtUtc) AS OldestLastSeenAtUtc,
    MAX(LastSeenAtUtc) AS NewestLastSeenAtUtc
FROM #IntelWorkRoleCells
GROUP BY RoleClass
ORDER BY LiveIntelWorkEdges DESC, RoleClass;

PRINT N'4. Portfolio teaming pairs from IntelWork shared project names. Healthy: enough pair density to infer repeat collaborators; gap: isolated work rows with few shared-project pairs.';
SELECT TOP (50)
    a.CanonicalOrgId AS OrgAId,
    MIN(a.OrgName) AS OrgA,
    b.CanonicalOrgId AS OrgBId,
    MIN(b.OrgName) AS OrgB,
    COUNT(DISTINCT a.NormalizedProjectName) AS SharedIntelProjects
FROM #IntelWorkRoleCells AS a
JOIN #IntelWorkRoleCells AS b
    ON b.NormalizedProjectName = a.NormalizedProjectName
   AND b.CanonicalOrgId > a.CanonicalOrgId
WHERE a.NormalizedProjectName IS NOT NULL
  AND a.NormalizedProjectName <> N''
GROUP BY a.CanonicalOrgId, b.CanonicalOrgId
HAVING COUNT(DISTINCT a.NormalizedProjectName) >= 2
ORDER BY SharedIntelProjects DESC, OrgA, OrgB;

PRINT N'5a. ACTIVE-pipeline team coverage from MajorProjectsInventory FK columns. Healthy: owner/architect high, SE/GC adequate for downstream displacement; gap: sparse SE/GC FKs.';
SELECT
    RoleClass,
    COUNT(*) AS ActiveProjects,
    SUM(CASE WHEN CanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END) AS ProjectsWithCanonicalFk,
    SUM(CASE WHEN CanonicalOrgId IS NULL AND NULLIF(LTRIM(RTRIM(DisplayName)), N'') IS NOT NULL THEN 1 ELSE 0 END) AS ProjectsWithNameOnlyNoFk,
    CAST(100.0 * SUM(CASE WHEN CanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS CanonicalFkCoveragePct
FROM #MpiRoleCells
GROUP BY RoleClass
ORDER BY CASE RoleClass WHEN N'Proponent' THEN 1 WHEN N'Architect' THEN 2 WHEN N'StructuralEngineer' THEN 3 WHEN N'GeneralContractor' THEN 4 ELSE 5 END;

PRINT N'5b. ACTIVE-pipeline team coverage from IntelWork linked or name-matched to active MPI. Healthy: material additional role cells beyond FK columns; gap: IntelWork not project-linked, or no new role cells.';
SELECT
    RoleClass,
    LinkMode,
    COUNT(DISTINCT CONCAT(MpiId, N'|', RoleClass, N'|', CanonicalOrgId)) AS IntelWorkProjectRoleOrgEdges,
    COUNT(DISTINCT CONCAT(MpiId, N'|', RoleClass)) AS IntelWorkProjectRoleCells
FROM #IntelWorkMpiCandidates
GROUP BY RoleClass, LinkMode
ORDER BY RoleClass, LinkMode;

PRINT N'5c. New active-project role cells IntelWork would fill where MPI FK is NULL. Healthy if this is large relative to FK gaps; gap/diminishing-return if tiny.';
SELECT
    RoleClass,
    LinkMode,
    COUNT(DISTINCT CONCAT(MpiId, N'|', RoleClass)) AS NewlyFillableRoleCells,
    COUNT(DISTINCT CONCAT(MpiId, N'|', RoleClass, N'|', CanonicalOrgId)) AS CandidateOrgAssignments
FROM #IntelWorkNewCells
GROUP BY RoleClass, LinkMode
ORDER BY RoleClass, LinkMode;

PRINT N'6. Signals/actions/risks/narratives per high-value org. Healthy: high-value orgs have recent signals and open actions; gap: many high-value orgs with no signals/actions.';
SELECT
    h.Kind,
    COUNT(*) AS HighValueOrgs,
    SUM(CASE WHEN s.SignalCount > 0 THEN 1 ELSE 0 END) AS OrgsWithSignals,
    SUM(CASE WHEN a.OpenActionCount > 0 THEN 1 ELSE 0 END) AS OrgsWithOpenActions,
    SUM(CASE WHEN r.RiskCount > 0 THEN 1 ELSE 0 END) AS OrgsWithRisks,
    SUM(CASE WHEN n.NarrativeCount > 0 THEN 1 ELSE 0 END) AS OrgsWithNarratives,
    SUM(s.SignalCount) AS Signals,
    SUM(a.OpenActionCount) AS OpenActions,
    SUM(r.RiskCount) AS Risks,
    SUM(n.NarrativeCount) AS Narratives
FROM #HighValueOrg AS h
OUTER APPLY (SELECT COUNT(*) AS SignalCount FROM opportunities.IntelSignal AS x WHERE x.CanonicalOrgId = h.CanonicalOrgId AND x.RetiredAtUtc IS NULL) AS s
OUTER APPLY (SELECT COUNT(*) AS OpenActionCount FROM opportunities.IntelAction AS x WHERE x.CanonicalOrgId = h.CanonicalOrgId AND x.RetiredAtUtc IS NULL AND x.Status = N'Open') AS a
OUTER APPLY (SELECT COUNT(*) AS RiskCount FROM opportunities.IntelRisk AS x WHERE x.CanonicalOrgId = h.CanonicalOrgId AND x.RetiredAtUtc IS NULL) AS r
OUTER APPLY (SELECT COUNT(*) AS NarrativeCount FROM opportunities.IntelNarrative AS x WHERE x.CanonicalOrgId = h.CanonicalOrgId AND x.RetiredAtUtc IS NULL) AS n
GROUP BY h.Kind
ORDER BY HighValueOrgs DESC, h.Kind;

PRINT N'7. KOR/Deltek relationship history. Healthy: high-value orgs with Deltek ids and/or KorProjectsCount; gap: active project orgs with neither.';
SELECT
    Kind,
    COUNT(*) AS HighValueOrgs,
    SUM(CASE WHEN ClendorClientId IS NOT NULL THEN 1 ELSE 0 END) AS WithClendorClientId,
    SUM(CASE WHEN KorProjectsCount > 0 THEN 1 ELSE 0 END) AS WithKorProjectsCount,
    SUM(CASE WHEN LastKorProjectAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS WithLastKorProjectDate,
    SUM(CASE WHEN IsOnActiveMpiProject = 1 AND ClendorClientId IS NULL AND KorProjectsCount = 0 AND LastKorProjectAtUtc IS NULL THEN 1 ELSE 0 END) AS ActiveProjectOrgsNoKorDeltekHistory
FROM #HighValueOrg
GROUP BY Kind
ORDER BY HighValueOrgs DESC, Kind;

PRINT N'8. Architect-SE displacement map. Healthy: many architect-incumbent SE pairs and architect briefs; gap: FK-only pair map sparse or briefs missing for top architects.';
SELECT
    COUNT(*) AS ArchitectIncumbentSePairs,
    COUNT(DISTINCT ArchitectOrgId) AS ArchitectsWithIncumbentSe,
    COUNT(DISTINCT SeOrgId) AS CompetitorSeOrgs,
    SUM(CASE WHEN KorSharedProjectsWithArchitect > 0 THEN 1 ELSE 0 END) AS PairsWhereKorHasFootIn
FROM opportunities.vArchitectIncumbentSE;

SELECT
    COUNT(*) AS ArchitectDisplacementBriefRows,
    SUM(CASE WHEN KorPriority = N'high' THEN 1 ELSE 0 END) AS HighPriorityBriefs,
    MIN(GeneratedAtUtc) AS OldestGeneratedAtUtc,
    MAX(GeneratedAtUtc) AS NewestGeneratedAtUtc
FROM opportunities.ArchitectDisplacementBriefs;

PRINT N'9. Freshness/recency. Healthy: LastRefresh/LastSeen distributed recently for active target providers; gap: stale or never-refreshed enrichment.';
SELECT
    ProviderName,
    COUNT(*) AS Rows,
    SUM(CASE WHEN Status IN (N'ok', N'Manual') THEN 1 ELSE 0 END) AS OkOrManualRows,
    MIN(LastRefreshAtUtc) AS OldestLastRefreshAtUtc,
    MAX(LastRefreshAtUtc) AS NewestLastRefreshAtUtc,
    SUM(CASE WHEN LastRefreshAtUtc IS NULL THEN 1 ELSE 0 END) AS NeverRefreshedRows,
    SUM(CASE WHEN LastRefreshAtUtc < DATEADD(DAY, -90, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END) AS OlderThan90Days
FROM opportunities.CanonicalOrgEnrichment
GROUP BY ProviderName
ORDER BY Rows DESC, ProviderName;

SELECT
    N'IntelPersonAffiliation' AS IntelTable,
    MIN(LastSeenAtUtc) AS OldestLastSeenAtUtc,
    MAX(LastSeenAtUtc) AS NewestLastSeenAtUtc,
    COUNT(*) AS LiveRows
FROM opportunities.IntelPersonAffiliation WHERE RetiredAtUtc IS NULL
UNION ALL
SELECT N'IntelSignal', MIN(LastSeenAtUtc), MAX(LastSeenAtUtc), COUNT(*) FROM opportunities.IntelSignal WHERE RetiredAtUtc IS NULL
UNION ALL
SELECT N'IntelAction', MIN(LastSeenAtUtc), MAX(LastSeenAtUtc), COUNT(*) FROM opportunities.IntelAction WHERE RetiredAtUtc IS NULL
UNION ALL
SELECT N'IntelWork', MIN(LastSeenAtUtc), MAX(LastSeenAtUtc), COUNT(*) FROM opportunities.IntelWork WHERE RetiredAtUtc IS NULL
UNION ALL
SELECT N'IntelRisk', MIN(LastSeenAtUtc), MAX(LastSeenAtUtc), COUNT(*) FROM opportunities.IntelRisk WHERE RetiredAtUtc IS NULL
UNION ALL
SELECT N'IntelNarrative', MIN(LastSeenAtUtc), MAX(LastSeenAtUtc), COUNT(*) FROM opportunities.IntelNarrative WHERE RetiredAtUtc IS NULL;

PRINT N'10. Denormalization trap detector. These compare sparse MPI role FKs to normalized IntelWork/project-intel evidence.';
SELECT
    N'MPI.StructuralEngineerCanonicalOrgId FK coverage' AS Metric,
    COUNT(*) AS ActiveProjects,
    SUM(CASE WHEN StructuralEngineerCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END) AS ProjectsWithSeFk,
    SUM(CASE WHEN StructuralEngineerCanonicalOrgId IS NULL THEN 1 ELSE 0 END) AS ProjectsMissingSeFk,
    CAST(100.0 * SUM(CASE WHEN StructuralEngineerCanonicalOrgId IS NOT NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5,1)) AS SeFkCoveragePct
FROM #ActiveMpi;

SELECT
    N'IntelWork structural edges (not necessarily MPI-linked)' AS Metric,
    COUNT(*) AS LiveStructuralIntelWorkEdges,
    COUNT(DISTINCT CanonicalOrgId) AS DistinctStructuralOrgs,
    COUNT(DISTINCT NormalizedProjectName) AS DistinctStructuralProjectNames
FROM #IntelWorkRoleCells
WHERE RoleClass = N'StructuralEngineer';

SELECT
    N'Project-intel key people on active MPI' AS Metric,
    COUNT(*) AS LiveProjectKeyPeople,
    COUNT(DISTINCT MajorProjectsInventoryId) AS ActiveProjectsWithKeyPeople,
    COUNT(DISTINCT CanonicalOrgId) AS DistinctLinkedOrgs
FROM opportunities.IntelProjectKeyPerson AS p
WHERE p.RetiredAtUtc IS NULL
  AND EXISTS (SELECT 1 FROM #ActiveMpi AS m WHERE m.Id = p.MajorProjectsInventoryId);

PRINT N'11. Recent-claim verification. Verdicts are computed from this database; MISLEADING means numerically plausible but FK-only/partial as a graph-completeness claim.';
DECLARE @ActiveProjects int = (SELECT COUNT(*) FROM #ActiveMpi);
DECLARE @FkSeProjects int = (SELECT COUNT(*) FROM #ActiveMpi WHERE StructuralEngineerCanonicalOrgId IS NOT NULL);
DECLARE @IntelWorkEdges int = (SELECT COUNT(*) FROM #IntelWorkRoleCells);
DECLARE @IntelWorkSeEdges int = (SELECT COUNT(*) FROM #IntelWorkRoleCells WHERE RoleClass = N'StructuralEngineer');
DECLARE @NewRoleCells int = (SELECT COUNT(DISTINCT CONCAT(MpiId, N'|', RoleClass)) FROM #IntelWorkNewCells WHERE RoleClass IN (N'Architect', N'StructuralEngineer', N'GeneralContractor'));
DECLARE @HvOrgs int = (SELECT COUNT(*) FROM #HighValueOrg);
DECLARE @HvOrgsWithPeople int = (
    SELECT COUNT(*)
    FROM #HighValueOrg AS h
    WHERE EXISTS (
        SELECT 1
        FROM opportunities.IntelPersonAffiliation AS a
        JOIN opportunities.IntelPerson AS p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
        WHERE a.CanonicalOrgId = h.CanonicalOrgId
          AND a.RetiredAtUtc IS NULL
          AND a.IsCurrent = 1
    )
);

SELECT
    Claim,
    Observed,
    Verdict
FROM (VALUES
    (
        N'(i) only ~83 of 2,410 active projects have an SE named',
        CONCAT(N'active=', @ActiveProjects, N'; SE FK=', @FkSeProjects, N'; FK pct=', CAST(100.0 * @FkSeProjects / NULLIF(@ActiveProjects, 0) AS decimal(5,1)), N'%'),
        CASE
            WHEN @ActiveProjects BETWEEN 2350 AND 2470 AND @FkSeProjects BETWEEN 75 AND 90
                THEN N'MISLEADING: numerically close for MPI FK-only coverage, not the full intelligence graph'
            ELSE N'FALSE for current DB as stated; also MISLEADING if used as full-graph coverage'
        END
    ),
    (
        N'(ii) IntelWork holds ~15,668 teaming edges incl. ~287 SE-role',
        CONCAT(N'IntelWork live=', @IntelWorkEdges, N'; SE-role=', @IntelWorkSeEdges),
        CASE
            WHEN @IntelWorkEdges BETWEEN 14885 AND 16451 AND @IntelWorkSeEdges BETWEEN 273 AND 301
                THEN N'TRUE within +/-5%'
            ELSE N'FALSE outside +/-5%'
        END
    ),
    (
        N'(iii) linking IntelWork to active MPI would newly fill only ~11 role cells',
        CONCAT(N'new Architect/SE/GC role cells=', @NewRoleCells),
        CASE
            WHEN @NewRoleCells BETWEEN 8 AND 14 THEN N'TRUE within loose range'
            WHEN @NewRoleCells < 25 THEN N'MISLEADING: small, but depends on exact/name-match policy'
            ELSE N'FALSE: materially more than ~11'
        END
    ),
    (
        N'(iv) people coverage on high-value orgs is ~73% (857/1178)',
        CONCAT(N'high-value orgs=', @HvOrgs, N'; with current people=', @HvOrgsWithPeople, N'; pct=', CAST(100.0 * @HvOrgsWithPeople / NULLIF(@HvOrgs, 0) AS decimal(5,1)), N'%'),
        CASE
            WHEN @HvOrgs BETWEEN 1120 AND 1235 AND @HvOrgsWithPeople BETWEEN 815 AND 900
                THEN N'TRUE within +/-5%'
            ELSE N'FALSE outside +/-5%'
        END
    ),
    (
        N'(v) OrgDossierViewModel surfaces people/signals/works/actions/risks/narratives + Deltek history',
        N'Code claim; see OrgDossierViewModel LoadAsync/ApplyIntel and OrgDossierView.xaml bindings',
        N'TRUE by code audit; caveat: major-project footprint roles in the dossier are FK-only'
    )
) AS x(Claim, Observed, Verdict);

PRINT N'12. Rows behind claim (iii), for manual inspection.';
SELECT TOP (200)
    nc.MpiId,
    m.ProjectName,
    nc.RoleClass,
    nc.CanonicalOrgId,
    co.DisplayName,
    nc.LinkMode
FROM #IntelWorkNewCells AS nc
JOIN #ActiveMpi AS m ON m.Id = nc.MpiId
JOIN opportunities.CanonicalOrg AS co ON co.Id = nc.CanonicalOrgId
WHERE nc.RoleClass IN (N'Architect', N'StructuralEngineer', N'GeneralContractor')
ORDER BY nc.RoleClass, m.ProjectName, co.DisplayName;
