USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 218 (data cleanup): the SF Socrata funnel was configured with
  projectNameColumn=description, so SF permit rows were named by their raw
  lowercase permit text ("to erect 60 stories, 4 basement..."). Recompute
  ProjectName from the address components in RawJson (street_number + street_name
  + street_suffix); preserve the original description text in ScheduleNotes if not
  already there. (Provider + source-config fixed separately so future ingests
  name by address — clean at source.)
*/

BEGIN TRAN;

;WITH sf AS (
  SELECT Id, ProjectName AS OldName, ScheduleNotes,
    LTRIM(RTRIM(
      COALESCE(JSON_VALUE(RawJson,'$.street_number'),N'') + N' ' +
      COALESCE(JSON_VALUE(RawJson,'$.street_name'),N'') + N' ' +
      COALESCE(JSON_VALUE(RawJson,'$.street_suffix'),N''))) AS Addr
  FROM opportunities.MajorProjectsInventory
  WHERE Province=N'CA' AND SourceKey LIKE N'sf:%'
)
UPDATE m
SET ProjectName = LEFT(sf.Addr, 500),
    -- keep the descriptive permit text discoverable in notes
    ScheduleNotes = LEFT(COALESCE(m.ScheduleNotes, sf.OldName), 1000),
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
JOIN sf ON sf.Id = m.Id
WHERE LEN(REPLACE(sf.Addr,N' ',N'')) >= 3        -- only when a real address exists
  AND m.ProjectName <> sf.Addr;
PRINT CONCAT('Migration 218: SF project names recomputed from address: ', @@ROWCOUNT);

COMMIT TRAN;
GO
