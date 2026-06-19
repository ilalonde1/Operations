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
         N'https://data.sfgov.org/resource/k2ra-p3nq.json',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"socrata","sourceKeyPrefix":"sf","municipality":"San Francisco","county":"San Francisco County","limit":"500","minUnits":"20","minValuation":"2000000","where":"(estimated_cost >= 2000000 OR proposed_units >= 20) AND (lower(description) like ''%apartment%'' OR lower(description) like ''%residential%'' OR lower(description) like ''%dwelling%'' OR lower(description) like ''%condo%'' OR lower(description) like ''%mixed%'' OR lower(description) like ''%hotel%'' OR lower(description) like ''%commercial%'' OR lower(description) like ''%office%'' OR lower(description) like ''%retail%'')","permitColumn":"permit_number","projectNameColumn":"description","descriptionColumn":"description","addressColumn":"address","typeColumn":"permit_type","valuationColumn":"estimated_cost","unitsColumn":"proposed_units","filedDateColumn":"filed_date","stageColumn":"status"}');
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
         N'https://data.sandiegocounty.gov/resource/dyzh-7eat.json',
         1,
         86400,
         300,
         sysdatetimeoffset(),
         sysdatetimeoffset(),
         0,
         N'{"kind":"socrata","sourceKeyPrefix":"sdcounty","county":"San Diego County","limit":"500","minUnits":"20","minValuation":"2000000","where":"(valuation >= 2000000 OR project_value >= 2000000 OR units >= 20) AND (lower(description) like ''%apartment%'' OR lower(description) like ''%residential%'' OR lower(description) like ''%dwelling%'' OR lower(description) like ''%condo%'' OR lower(description) like ''%mixed%'' OR lower(description) like ''%hotel%'' OR lower(description) like ''%commercial%'' OR lower(description) like ''%office%'' OR lower(description) like ''%retail%'')","permitColumn":"permit_number","projectNameColumn":"project_name","descriptionColumn":"description","addressColumn":"address","typeColumn":"permit_type","valuationColumn":"valuation","unitsColumn":"units","filedDateColumn":"applied_date","stageColumn":"status"}');
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
         N'{"kind":"ckan","sourceKeyPrefix":"sanjose","municipality":"San Jose","county":"Santa Clara County","limit":"500","minUnits":"20","minValuation":"2000000","ckanPackageSearchUrl":"https://data.sanjoseca.gov/api/3/action/package_search","ckanPackageQuery":"building permits","ckanQuery":"apartment residential dwelling condo mixed-use hotel commercial office retail","permitColumn":"permit_number","projectNameColumn":"project_name","descriptionColumn":"description","addressColumn":"address","typeColumn":"permit_type","valuationColumn":"valuation","unitsColumn":"units","filedDateColumn":"application_date","stageColumn":"status"}');
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
