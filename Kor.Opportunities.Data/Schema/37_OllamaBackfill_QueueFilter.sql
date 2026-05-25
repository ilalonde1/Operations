USE [KorOpportunitiesDb];
GO

-- Round 21: filter Ollama backfill queue
-- 1) Exclude APC historical + SamGov + sub-$150K awards from AI enrichment
--    by bumping AgentEnrichmentAttempts to the max (3), which removes them
--    from the pending queue without losing canonical-org linkage data.

-- VERIFICATION FIRST (must show in SSMS output, then run the UPDATEs)
PRINT '=== Source names matching exclusion patterns ===';
SELECT Id, Name, SourceType, IsEnabled
FROM   opportunities.OpportunitySources
WHERE  Name LIKE N'APC%' OR Name LIKE N'SamGov%'
ORDER  BY Name;

-- Mark APC historical awards as skipped
-- (APC live ingestion source stays in queue - only the bulk historical is noise)
UPDATE opportunities.OpportunityAwards
SET    AgentEnrichmentAttempts = 3,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  AgentEnrichedAtUtc IS NULL
  AND  ISNULL(AgentEnrichmentAttempts, 0) < 3
  AND  OpportunitySourceId IN (
        SELECT Id FROM opportunities.OpportunitySources
        WHERE Name LIKE N'APC%Historical%'
           OR Name LIKE N'APC_Historical%'
  );
PRINT N'APC historical awards marked skipped: ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- Mark SamGov awards as skipped
UPDATE opportunities.OpportunityAwards
SET    AgentEnrichmentAttempts = 3,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  AgentEnrichedAtUtc IS NULL
  AND  ISNULL(AgentEnrichmentAttempts, 0) < 3
  AND  OpportunitySourceId IN (
        SELECT Id FROM opportunities.OpportunitySources
        WHERE Name LIKE N'SamGov%'
  );
PRINT N'SamGov awards marked skipped: ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- Mark sub-$150K awards as skipped (regardless of source)
UPDATE opportunities.OpportunityAwards
SET    AgentEnrichmentAttempts = 3,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  AgentEnrichedAtUtc IS NULL
  AND  ISNULL(AgentEnrichmentAttempts, 0) < 3
  AND  ISNULL(ContractValue, 0) < 150000;
PRINT N'Sub-$150K awards marked skipped: ' + CAST(@@ROWCOUNT AS nvarchar(20));

PRINT N'=== Round 21 noise filter applied ===';

-- Post-check: remaining queue by source
SELECT TOP 30
       s.Name AS Source,
       COUNT(*) AS PendingEnrich,
       FORMAT(AVG(CAST(ISNULL(a.ContractValue, 0) AS bigint)), 'C0', 'en-CA') AS AvgValue
FROM   opportunities.OpportunityAwards a
JOIN   opportunities.OpportunitySources s ON s.Id = a.OpportunitySourceId
WHERE  a.AgentEnrichedAtUtc IS NULL
  AND  ISNULL(a.AgentEnrichmentAttempts, 0) < 3
GROUP  BY s.Name
ORDER  BY COUNT(*) DESC;
GO
