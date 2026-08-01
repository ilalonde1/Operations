USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 257: BD audit close-out 2026-06-20 M6.
  SF permits sharing the same ProjectName/address are the same building for BD
  purposes. Keep the lowest SourceKey per active address and soft-retire the
  other active SF permits as same-building superseded rows.

  Child IntelProject and signal rows are deliberately not hand-repointed here; the
  survivor MPI row remains active and enrichment can regenerate project intel
  against that survivor.
*/
BEGIN TRAN;

DECLARE @AddressesToCollapse int;
DECLARE @RowsToRetire int;

;WITH AddressGroups AS
(
    SELECT
        AddressKey = LTRIM(RTRIM(ProjectName)),
        SurvivorSourceKey = MIN(SourceKey),
        PermitCount = COUNT(*)
    FROM opportunities.MajorProjectsInventory
    WHERE RetiredAtUtc IS NULL
      AND SourceKey LIKE N'sf:%'
      AND NULLIF(LTRIM(RTRIM(ProjectName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(ProjectName))
    HAVING COUNT(*) > 1
),
Victims AS
(
    SELECT m.Id, g.SurvivorSourceKey
    FROM opportunities.MajorProjectsInventory m
    JOIN AddressGroups g ON g.AddressKey = LTRIM(RTRIM(m.ProjectName))
    WHERE m.RetiredAtUtc IS NULL
      AND m.SourceKey LIKE N'sf:%'
      AND m.SourceKey <> g.SurvivorSourceKey
)
SELECT
    @AddressesToCollapse = (SELECT COUNT(*) FROM AddressGroups),
    @RowsToRetire = (SELECT COUNT(*) FROM Victims);

PRINT 'SF same-address active address groups to collapse: ' + CONVERT(varchar(20), COALESCE(@AddressesToCollapse, 0));
PRINT 'SF same-address active permit rows to soft-retire: ' + CONVERT(varchar(20), COALESCE(@RowsToRetire, 0));

;WITH AddressGroups AS
(
    SELECT
        AddressKey = LTRIM(RTRIM(ProjectName)),
        SurvivorSourceKey = MIN(SourceKey)
    FROM opportunities.MajorProjectsInventory
    WHERE RetiredAtUtc IS NULL
      AND SourceKey LIKE N'sf:%'
      AND NULLIF(LTRIM(RTRIM(ProjectName)), N'') IS NOT NULL
    GROUP BY LTRIM(RTRIM(ProjectName))
    HAVING COUNT(*) > 1
),
Victims AS
(
    SELECT m.Id, g.SurvivorSourceKey
    FROM opportunities.MajorProjectsInventory m
    JOIN AddressGroups g ON g.AddressKey = LTRIM(RTRIM(m.ProjectName))
    WHERE m.RetiredAtUtc IS NULL
      AND m.SourceKey LIKE N'sf:%'
      AND m.SourceKey <> g.SurvivorSourceKey
)
UPDATE m
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'm257: same-building SF permit superseded by survivor ' + v.SurvivorSourceKey,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
JOIN Victims v ON v.Id = m.Id;
PRINT 'SF same-address active permit rows soft-retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 257 complete: SF same-address permits collapsed to lowest SourceKey survivor.';
COMMIT TRAN;
GO
