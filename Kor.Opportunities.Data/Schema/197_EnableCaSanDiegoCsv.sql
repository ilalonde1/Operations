USE [KorOpportunitiesDb];
GO

UPDATE opportunities.OpportunitySources
SET
    BaseUrl = N'https://seshat.datasd.org/development_permits/approvals_created_2026_datasd.csv',
    IsEnabled = 1,
    UpdatedAtUtc = sysdatetimeoffset(),
    ConfigJson = N'{"kind":"csv","sourceKeyPrefix":"sdcity","municipality":"San Diego","county":"San Diego County","minUnits":"20","minValuation":"2000000","permitColumn":"APPROVAL_ID","projectNameColumn":"PROJECT_TITLE","descriptionColumn":"PROJECT_SCOPE","typeColumn":"APPROVAL_TYPE","valuationColumn":"APPROVAL_VALUATION","unitsColumn":"APPROVAL_DU_NET_CHANGE","storiesColumn":"APPROVAL_STORIES","addressColumn":"GIS_ADDRESS","stageColumn":"APPROVAL_STATUS","filedDateColumn":"APPROVAL_CREATE_DATE"}'
WHERE Name = N'CA_SocrataSanDiego';
GO

PRINT 'Migration 197: CA_SocrataSanDiego retargeted to City of San Diego 2026 approvals CSV.';
GO
