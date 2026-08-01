USE [KorOpportunitiesDb];
GO

/* =====================================================================
   Source: City of Los Angeles — RAMP Open Bids (Socrata)
   ---------------------------------------------------------------------
   LA is a core KOR market. RAMP is the city's live solicitation feed.
   We pull OPEN bids in the "Personal Services" (consultant / A&E /
   RFQ-PQS) and "Construction" categories — 271 live rows at seed time;
   "Commodity" (sensors, materials) is excluded as noise.

   Ingested via GenericJsonOpportunityProvider (SourceType 2), same as
   CoV_AwardedContracts. Socrata returns a root JSON array, so no
   json.itemsPath. The $select aliases a constant 'CA' as province so
   every row carries a KOR-matchable location (PrimeConsultantClassifier
   .IsKorLocationMatch accepts 'CA'); the nested url object {url:...} is
   resolved by the provider's preferred-key handling.

   No worker redeploy needed — OpportunitySourceCronScheduler auto-queues
   any enabled, non-excluded source on its next tick. Each ingested opp
   is then classified by the live PrimeConsultantClassifier on insert.
   ===================================================================== */
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySources WHERE Name = N'LACity_RAMP_OpenBids')
BEGIN
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
    VALUES
        (NEWID(),
         N'LACity_RAMP_OpenBids',
         2,
         N'https://data.lacity.org/resource/hf3r-utnq.json?$select=rampid,title,department,type,bidpost,closedate,url,%27CA%27%20as%20province&$where=stagename%3D%27Open%27%20AND%20category%20in(%27Personal%20Services%27,%27Construction%27)&$order=bidpost%20DESC&$limit=1000',
         1, 86400, 60, sysdatetimeoffset(), sysdatetimeoffset(), 0);
END;
GO

DECLARE @LaId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'LACity_RAMP_OpenBids');

IF @LaId IS NOT NULL
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    SELECT @LaId, k, v, sysdatetimeoffset() FROM (VALUES
        (N'json.titlePath',              N'title'),
        (N'json.urlPath',                N'url'),          -- nested {url:...} -> provider resolves
        (N'json.buyerPath',              N'department'),
        (N'json.descriptionPath',        N'type'),
        (N'json.postedDatePath',         N'bidpost'),
        (N'json.deadlinePath',           N'closedate'),
        (N'json.provincePath',           N'province'),     -- aliased constant 'CA'
        (N'json.externalReferencePath',  N'rampid')
    ) AS m(k, v)
    WHERE NOT EXISTS (
        SELECT 1 FROM opportunities.OpportunitySourceMappings
        WHERE OpportunitySourceId = @LaId AND [Key] = m.k);
END;
GO

PRINT 'Migration 44: City of LA RAMP Open Bids (Personal Services + Construction) seeded.';
GO
