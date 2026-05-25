USE [KorOpportunitiesDb];
GO

DECLARE @GcId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'GovCanada_ProactiveDisclosure');

IF @GcId IS NULL
BEGIN
    PRINT 'Migration 35: GovCanada_ProactiveDisclosure source missing - nothing to update.';
    RETURN;
END;

/* Update the existing source: flip SourceType from GenericCsvAward (16) to
   GenericJsonAward (17), point BaseUrl at the CKAN datastore_search API,
   re-enable. */
UPDATE opportunities.OpportunitySources
SET    SourceType = 17,  -- GenericJsonAward
       BaseUrl    = N'https://open.canada.ca/data/api/3/action/datastore_search?resource_id=fac950c0-00d5-4ec1-a4d3-9cbebf98a305',
       IsEnabled  = 1,
       UpdatedAtUtc = sysdatetimeoffset()
WHERE  Id = @GcId;

/* Wipe stale mappings for this source before inserting the live JSON config. */
DELETE FROM opportunities.OpportunitySourceMappings
WHERE  OpportunitySourceId = @GcId
  AND  ([Key] LIKE N'csv.%' OR [Key] LIKE N'json.%' OR [Key] LIKE N'header.%');

INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
SELECT @GcId, k, v, sysdatetimeoffset() FROM (VALUES
    -- Item extraction
    (N'json.itemsPath',                N'result.records'),
    (N'json.totalPath',                N'result.total'),
    (N'json.pageSize',                 N'32000'),
    -- Field mappings
    (N'json.externalRefPath',          N'reference_number'),
    (N'json.titlePath',                N'description_en'),
    (N'json.awardingOrgPath',          N'owner_org_title'),
    (N'json.awardedToPath',            N'vendor_name'),
    (N'json.contractValuePath',        N'contract_value'),
    (N'json.contractDatePath',         N'contract_date'),
    (N'json.contractCurrencyOverride', N'CAD'),
    (N'json.sourceUrlOverride',        N'https://search.open.canada.ca/contracts/'),
    (N'json.contractNumberPath',       N'procurement_id'),
    (N'json.solicitationTypePath',     N'solicitation_procedure'),
    -- Safety cap per cron tick. CKAN q full-text search is disabled for this >100k row dataset.
    (N'json.maxRowsPerRun',            N'100000'),
    -- WAF bypass headers (verified working 2026-05-24)
    (N'header.userAgent',              N'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36'),
    (N'header.accept',                 N'application/json')
) AS m(k, v);

PRINT 'Migration 35: GovCanada_ProactiveDisclosure switched to GenericJsonAward + CKAN API.';
GO
