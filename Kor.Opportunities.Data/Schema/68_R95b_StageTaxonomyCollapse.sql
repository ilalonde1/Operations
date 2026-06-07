SET XACT_ABORT ON;
GO

IF COL_LENGTH('opportunities.MajorProjectsInventory', 'KorPipelineTag') IS NULL
BEGIN
    ALTER TABLE opportunities.MajorProjectsInventory
        ADD KorPipelineTag nvarchar(80) NULL;
END;
GO

BEGIN TRAN;

DECLARE @pipelineTagsPopulated int;
DECLARE @projectStagesNormalized int;
DECLARE @unmappedStages int;

UPDATE opportunities.MajorProjectsInventory
SET KorPipelineTag = ProjectStage,
    ProjectStage = NULL
WHERE ProjectStage IS NOT NULL
  AND LOWER(ProjectStage) IN (
      N'usmarketresearch',
      N'albertamarketresearch',
      N'lowermainlandpairing',
      N'islandokanaganpairing',
      N'edmontonpairing',
      N'islandokanaganecosystem',
      N'institutionalpipeline',
      N'structural-pipeline',
      N'ownerpipeline',
      N'seismic',
      N'facilityrenewal',
      N'competitor-projects',
      N'indigenous-projects',
      N'project-teams',
      N'intelteamawards',
      N'indigenous'
  );
SET @pipelineTagsPopulated = @@ROWCOUNT;
PRINT 'KorPipelineTag populated from ProjectStage: ' + CONVERT(varchar(20), @pipelineTagsPopulated) + ' rows';

;WITH StageMap AS (
    SELECT
        Id,
        ProjectStage,
        CASE
            WHEN LOWER(ProjectStage) IN (N'capitalplan', N'publiccapitalplan', N'years 4-5 priority') THEN N'CapitalPlan'
            WHEN LOWER(ProjectStage) IN (N'planning', N'planned') THEN N'Planned'
            WHEN LOWER(ProjectStage) IN (N'pre-design', N'concept', N'preliminary/feasibility') THEN N'Concept'
            WHEN LOWER(ProjectStage) IN (N'design', N'design-rfp-open') THEN N'Design'
            WHEN LOWER(ProjectStage) IN (N'consultation/approvals', N'permitting') THEN N'Permitting'
            WHEN LOWER(ProjectStage) IN (N'procurement', N'pretender', N'tender/preconstruction', N'rfp-issued') THEN N'Procurement'
            WHEN LOWER(ProjectStage) IN (N'approved', N'approved-funded', N'announced-funded', N'capital-approved', N'funding-approved') THEN N'Approved'
            WHEN LOWER(ProjectStage) = N'announced' THEN N'Announced'
            WHEN LOWER(ProjectStage) = N'under construction' THEN N'Construction'
            ELSE NULL
        END AS CanonicalStage
    FROM opportunities.MajorProjectsInventory
    WHERE ProjectStage IS NOT NULL
)
-- Case-sensitive comparison so pure case-variants ('design' vs 'Design')
-- DO get normalized. Default DB collation is CI_AS, which would treat
-- them as equal and skip the update.
UPDATE StageMap
SET ProjectStage = CanonicalStage
WHERE CanonicalStage IS NOT NULL
  AND ProjectStage COLLATE SQL_Latin1_General_CP1_CS_AS <> CanonicalStage;
SET @projectStagesNormalized = @@ROWCOUNT;
PRINT 'ProjectStage normalized: ' + CONVERT(varchar(20), @projectStagesNormalized) + ' rows';

SELECT DISTINCT ProjectStage
INTO #UnmappedStages
FROM opportunities.MajorProjectsInventory
WHERE ProjectStage IS NOT NULL
  AND ProjectStage NOT IN (
      N'CapitalPlan',
      N'Planned',
      N'Concept',
      N'Design',
      N'Permitting',
      N'Procurement',
      N'Approved',
      N'Announced',
      N'Construction'
  );

SELECT @unmappedStages = COUNT(*) FROM #UnmappedStages;
PRINT 'Unmapped ProjectStage values remaining: ' + CONVERT(varchar(20), @unmappedStages);

SELECT ProjectStage
FROM #UnmappedStages
ORDER BY ProjectStage;

COMMIT TRAN;

PRINT 'Migration 68 R95b stage-taxonomy collapse complete.';
GO
