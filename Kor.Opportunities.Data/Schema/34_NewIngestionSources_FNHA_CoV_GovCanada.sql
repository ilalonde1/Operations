USE [KorOpportunitiesDb];
GO

/* === Source 1: First Nations Health Authority - Bonfire/Euna RSS === */
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'Bonfire_FNHA')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(), N'Bonfire_FNHA', 3, N'https://fnha.bonfirehub.ca/opportunities/rss', 1, 7200, 30, sysdatetimeoffset(), sysdatetimeoffset(), 0);
END;
GO

DECLARE @FnhaId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'Bonfire_FNHA');

IF @FnhaId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
                   WHERE OpportunitySourceId = @FnhaId AND [Key] = N'rss.titleFormat')
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@FnhaId, N'rss.titleFormat', N'BonfireReferenceName', sysdatetimeoffset());

    IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
                   WHERE OpportunitySourceId = @FnhaId AND [Key] = N'rss.buyerOverride')
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@FnhaId, N'rss.buyerOverride', N'First Nations Health Authority', sysdatetimeoffset());

    IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings
                   WHERE OpportunitySourceId = @FnhaId AND [Key] = N'rss.buyerFromTitle')
        INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
        VALUES (@FnhaId, N'rss.buyerFromTitle', N'false', sysdatetimeoffset());
END;
GO

PRINT 'Migration 34a: FNHA Bonfire tenant seeded.';
GO

/* === Source 2: City of Vancouver Awarded Contracts === */
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'CoV_AwardedContracts')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(),
         N'CoV_AwardedContracts',
         2,
         N'https://opendata.vancouver.ca/api/explore/v2.1/catalog/datasets/awarded-contracts/records?select=bid_number,bid_type,bid_description,award_date,vendor_name,bid_amount,awarded,%27City%20of%20Vancouver%27%20as%20buyer_name,%27https%3A%2F%2Fopendata.vancouver.ca%2Fexplore%2Fdataset%2Fawarded-contracts%2Ftable%2F%27%20as%20source_url&where=awarded%3D%27Yes%27&limit=100',
         1, 86400, 60, sysdatetimeoffset(), sysdatetimeoffset(), 0);
END;
GO

DECLARE @CovAwId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'CoV_AwardedContracts');

IF @CovAwId IS NOT NULL
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    SELECT @CovAwId, k, v, sysdatetimeoffset() FROM (VALUES
        (N'json.itemsPath',              N'results'),
        (N'json.titlePath',              N'bid_description'),
        (N'json.descriptionPath',        N'bid_type'),
        (N'json.urlPath',                N'source_url'),
        (N'json.externalReferencePath',  N'bid_number'),
        (N'json.buyerPath',              N'buyer_name'),
        (N'json.postedDatePath',         N'award_date'),
        (N'json.estimatedValuePath',     N'bid_amount')
    ) AS m(k, v)
    WHERE NOT EXISTS (
        SELECT 1 FROM opportunities.OpportunitySourceMappings
        WHERE OpportunitySourceId = @CovAwId AND [Key] = m.k);
END;
GO

PRINT 'Migration 34b: City of Vancouver Awarded Contracts seeded.';
GO

/* === Source 3: Government of Canada Proactive Disclosure of Contracts (>$10k) === */
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'GovCanada_ProactiveDisclosure')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(),
         N'GovCanada_ProactiveDisclosure',
         16,
         N'https://open.canada.ca/data/dataset/d8f85d91-7dec-4fd1-8055-483b77225d8b/resource/fac950c0-00d5-4ec1-a4d3-9cbebf98a305/download/contracts.csv',
         1, 604800, 300, sysdatetimeoffset(), sysdatetimeoffset(), 1);
END;
GO

DECLARE @GcId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'GovCanada_ProactiveDisclosure');

IF @GcId IS NOT NULL
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    SELECT @GcId, k, v, sysdatetimeoffset() FROM (VALUES
        (N'csv.externalRefPath',        N'reference_number'),
        (N'csv.titlePath',              N'description_en'),
        (N'csv.awardingOrgOverride',    N'Government of Canada'),
        (N'csv.awardedToPath',          N'vendor_name'),
        (N'csv.contractValuePath',      N'contract_value'),
        (N'csv.contractCurrencyPath',   N'CAD'),
        (N'csv.awardedAtPath',          N'contract_date'),
        (N'csv.sourceUrlPath',          N'https://search.open.canada.ca/contracts/'),
        (N'csv.contractNumberPath',     N'procurement_id'),
        (N'csv.solicitationTypePath',   N'solicitation_procedure'),
        (N'csv.filterColumn',           N'commodity_code'),
        (N'csv.filterRegex',            N'^23\d{4}$')
    ) AS m(k, v)
    WHERE NOT EXISTS (
        SELECT 1 FROM opportunities.OpportunitySourceMappings
        WHERE OpportunitySourceId = @GcId AND [Key] = m.k);
END;
GO

PRINT 'Migration 34c: Gov Canada Proactive Disclosure seeded.';
GO
