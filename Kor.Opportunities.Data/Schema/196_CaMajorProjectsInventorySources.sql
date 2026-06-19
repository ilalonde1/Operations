USE [KorOpportunitiesDb];
GO

IF COL_LENGTH(N'opportunities.OpportunitySources', N'ConfigJson') IS NULL
BEGIN
    THROW 51000, 'Migration 196 requires opportunities.OpportunitySources.ConfigJson from migration 62.', 1;
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'CA_SocrataSF')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical, ConfigJson)
    VALUES
        (NEWID(),
         N'CA_SocrataSF',
         18,
         N'https://data.sfgov.org/resource/i98e-djp9.json',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"socrata","sourceKeyPrefix":"sf","municipality":"San Francisco","county":"San Francisco County","limit":"500","minUnits":"20","minValuation":"2000000","where":"proposed_units::number >= 20 OR estimated_cost::number >= 2000000","permitColumn":"permit_number","projectNameColumn":"description","descriptionColumn":"description","addressColumn":"street_name","typeColumn":"permit_type_definition","valuationColumn":"estimated_cost","unitsColumn":"proposed_units","filedDateColumn":"filed_date","stageColumn":"status"}');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'CA_SocrataSanDiego')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical, ConfigJson)
    VALUES
        (NEWID(),
         N'CA_SocrataSanDiego',
         18,
         N'https://seshat.datasd.org/development_permits/approvals_active_datasd.csv',
         0,   -- DISABLED: City of San Diego is CSV-only (no Socrata API); needs a CSV provider (fast-follow). County dyzh-7eat was wrong jurisdiction.
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"csv","status":"disabled-pending-csv-provider","sourceKeyPrefix":"sdcity","municipality":"San Diego","county":"San Diego County","minUnits":"20","minValuation":"2000000","permitColumn":"APPROVAL_ID","projectNameColumn":"PROJECT_TITLE","descriptionColumn":"PROJECT_SCOPE","valuationColumn":"APPROVAL_VALUATION","unitsColumn":"APPROVAL_DU_NET_CHANGE","addressColumn":"GIS_ADDRESS","stageColumn":"APPROVAL_STATUS","filedDateColumn":"APPROVAL_CREATE_DATE"}');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'CA_SanJoseCkan')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical, ConfigJson)
    VALUES
        (NEWID(),
         N'CA_SanJoseCkan',
         18,
         N'https://data.sanjoseca.gov/api/3/action/datastore_search',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"ckan","sourceKeyPrefix":"sanjose","municipality":"San Jose","county":"Santa Clara County","limit":"500","minUnits":"20","minValuation":"2000000","resourceId":"761b7ae8-3be1-4ad6-923d-c7af6404a904","ckanQuery":"new construction","permitColumn":"FOLDERNUMBER","projectNameColumn":"FOLDERNAME","descriptionColumn":"WORKDESCRIPTION","typeColumn":"FOLDERDESC","valuationColumn":"PERMITVALUATION","unitsColumn":"DWELLINGUNITS","stageColumn":"Status"}');
END;
GO

IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'CA_CEQAnet')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical, ConfigJson)
    VALUES
        (NEWID(),
         N'CA_CEQAnet',
         18,
         N'https://ceqanet.lci.ca.gov/Search/Recent',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"html","paceMilliseconds":"750"}');
END;
GO

PRINT 'Migration 196: California Major Projects Inventory sources seeded.';
GO
