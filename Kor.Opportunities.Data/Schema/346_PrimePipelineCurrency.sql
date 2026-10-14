-- 346: preserve opportunity currency in PrimePipeline; never alias USD as CAD.
-- No FX conversion is performed. EstimatedValueCad contains explicitly CAD values
-- only; EstimatedValue and EstimatedValueCurrency retain the original units. MPI
-- EstimatedCostCad is already denominated in CAD. Missing opportunity currency is
-- unknown, not an implicit CAD default. The dashboard withholds monetary totals
-- and market-value bars when a valued row has non-CAD or unknown currency.
--
-- Supplied measurement, 2026-10-13: 389 of 389 valued opportunities are CAD.
-- No live measurement was performed while authoring this migration.
-- Apply this additive view migration before deploying the updated reader.
-- Existing consumers keep their column names/order and receive NULL for non-CAD.
-- Reapplying is idempotent: CREATE OR ALTER installs the same definition.
-- Report-only unless Apply is exactly 1; no persistent writes precede the guard.
-- As in 343-345, omitting the sqlcmd variable fails closed before execution.
-- Do not set a SQLCMD default in this file: it overrides the operator's -v value.
-- HOW TO RUN (Ian only; not executed by this brief)
--   report only: sqlcmd ... -i 346_PrimePipelineCurrency.sql -v Apply=0
--   apply:       sqlcmd ... -i 346_PrimePipelineCurrency.sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;

SELECT 'current definition' AS Report,
       OBJECT_DEFINITION(OBJECT_ID(N'opportunities.PrimePipeline')) AS ViewDefinition;
SELECT 'opportunity currency (no conversion)' AS Report,
       NULLIF(UPPER(LTRIM(RTRIM(EstimatedValueCurrency))), N'') AS Currency,
       COUNT(*) AS ValuedRows
FROM opportunities.Opportunities
WHERE EstimatedValue IS NOT NULL
GROUP BY NULLIF(UPPER(LTRIM(RTRIM(EstimatedValueCurrency))), N'');

IF @DryRun = 1
BEGIN
    PRINT 'Apply=0 - nothing was changed. PrimePipeline would preserve original currency and expose CAD values only in EstimatedValueCad.';
    RETURN;
END;

BEGIN TRANSACTION;
EXEC sys.sp_executesql N'CREATE OR ALTER VIEW opportunities.PrimePipeline AS
SELECT
    CAST(''Open RFP'' AS nvarchar(20))            AS PipelineType,
    CAST(o.Id AS nvarchar(40))                   AS SourceRef,
    CAST(o.Name AS nvarchar(500))                AS ProjectName,
    CAST(o.BuyerName AS nvarchar(500))           AS BuyerOrOwner,
    CAST(o.PrimeProjectSector AS nvarchar(120))  AS Sector,
    CAST(o.Status AS nvarchar(120))              AS Stage,
    CAST(CASE WHEN UPPER(LTRIM(RTRIM(o.EstimatedValueCurrency))) = N''CAD''
              THEN o.EstimatedValue ELSE NULL END AS decimal(18,2)) AS EstimatedValueCad,
    CAST(o.ProjectProvince AS nvarchar(40))      AS Province,
    CAST(o.ProjectCity AS nvarchar(200))         AS City,
    CAST(NULL AS nvarchar(500))                  AS ArchitectName,
    CAST(NULL AS nvarchar(1000))                 AS SourceUrl,
    CAST(o.EstimatedValue AS decimal(18,2)) AS EstimatedValue,
    CAST(NULLIF(UPPER(LTRIM(RTRIM(o.EstimatedValueCurrency))), N'''') AS nvarchar(10)) AS EstimatedValueCurrency
FROM opportunities.Opportunities o
WHERE o.IsPrimeConsultantRfp = 1
  AND o.Status NOT IN (6, 7)
  AND o.DismissedAtUtc IS NULL
  AND (o.SubmissionDeadlineUtc IS NULL OR o.SubmissionDeadlineUtc >= SYSDATETIMEOFFSET())
UNION ALL
SELECT
    CAST(''Pipeline Project'' AS nvarchar(20)),
    CAST(m.Id AS nvarchar(40)),
    CAST(m.ProjectName AS nvarchar(500)),
    CAST(m.ProponentName AS nvarchar(500)),
    CAST(m.Sector AS nvarchar(120)),
    CAST(m.Stage AS nvarchar(120)),
    CAST(m.EstimatedCostCad AS decimal(18,2)),
    CAST(m.Province AS nvarchar(40)),
    CAST(m.MunicipalityName AS nvarchar(200)),
    CAST(m.ArchitectName AS nvarchar(500)),
    CAST(m.SourceUrl AS nvarchar(1000)),
    CAST(m.EstimatedCostCad AS decimal(18,2)),
    CAST(N''CAD'' AS nvarchar(10))
FROM opportunities.MajorProjectsInventory m
WHERE m.Province IN (''BC'',''AB'',''CA'',''WA'',''OR'')
  AND m.RetiredAtUtc IS NULL
  AND (
        m.Sector LIKE ''%school%'' OR m.Sector LIKE ''%hospital%'' OR m.Sector LIKE ''%health%''
     OR m.Sector LIKE ''%recreation%'' OR m.Sector LIKE ''%civic%'' OR m.Sector LIKE ''%cultural%''
     OR m.Sector LIKE ''%universit%'' OR m.Sector LIKE ''%college%'' OR m.Sector LIKE ''%library%''
     OR m.Sector LIKE ''%communit%'' OR m.Sector LIKE ''%education%'' OR m.Sector LIKE ''%housing%''
     OR m.Sector LIKE ''%institution%'' OR m.Sector LIKE ''%care%''
     OR m.Sector IN (''Civic'',''Tourism / Recreation'',''Government'',''Mixed-use'')
      )
  AND (m.Stage IS NULL OR NOT (
        m.Stage LIKE ''%complet%'' OR m.Stage LIKE ''construction%'' OR m.Stage LIKE ''%under construction%''
     OR m.Stage LIKE ''%construction started%'' OR m.Stage LIKE ''%in construction%''
     OR m.Stage LIKE ''%construction phase%'' OR m.Stage LIKE ''%in-service%'' OR m.Stage LIKE ''%in service%''
     OR m.Stage LIKE ''%operating%'' OR m.Stage LIKE ''%occupancy%'' OR m.Stage LIKE ''%built%''
     OR m.Stage LIKE ''%in progress%'' OR m.Stage LIKE ''%underway%'' OR m.Stage LIKE ''%demolition%''
      ));';
COMMIT TRANSACTION;

SELECT 'after definition' AS Report,
       OBJECT_DEFINITION(OBJECT_ID(N'opportunities.PrimePipeline')) AS ViewDefinition;
