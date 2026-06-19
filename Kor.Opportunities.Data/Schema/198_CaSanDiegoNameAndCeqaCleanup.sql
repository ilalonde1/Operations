USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO

-- City of San Diego: PROJECT_TITLE is a generic system string
-- ("Actively Managed-Express-Building Construction:..."); the street address
-- is the clean, human-readable project identity. The descriptive scope
-- ("707 BROADWAY APARTMENTS") is already preserved in ScheduleNotes.
UPDATE opportunities.OpportunitySources
SET
    ConfigJson = N'{"kind":"csv","sourceKeyPrefix":"sdcity","municipality":"San Diego","county":"San Diego County","minUnits":"20","minValuation":"2000000","permitColumn":"APPROVAL_ID","projectNameColumn":"GIS_ADDRESS","descriptionColumn":"PROJECT_SCOPE","typeColumn":"APPROVAL_TYPE","valuationColumn":"APPROVAL_VALUATION","unitsColumn":"APPROVAL_DU_NET_CHANGE","storiesColumn":"APPROVAL_STORIES","addressColumn":"GIS_ADDRESS","stageColumn":"APPROVAL_STATUS","filedDateColumn":"APPROVAL_CREATE_DATE"}',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSanDiego';
GO

-- Purge pre-fix CEQAnet artifacts whose ProjectName is a bare SCH# (no letters).
-- The provider now rejects numeric-only titles, so these can never recur.
DELETE FROM opportunities.MajorProjectsInventory
WHERE Province = N'CA'
  AND SourceKey LIKE N'ceqa:%'
  AND ProjectName NOT LIKE N'%[A-Za-z]%';
GO

PRINT 'Migration 198: CA_SocrataSanDiego name -> GIS_ADDRESS; purged numeric-title CEQA rows.';
GO
