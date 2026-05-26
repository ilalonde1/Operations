USE [KorOpportunitiesDb];
GO

/* =====================================================================
   Fix: City of Vancouver awarded-contracts opps had the bid_number
   leaking into ProjectProvince AND ProjectCity (e.g. 'PS20200123').
   ---------------------------------------------------------------------
   Root cause (fixed in code, Round 40): GenericJsonOpportunityProvider
   resolved a null/unconfigured field path to the whole item object and
   extracted a garbage string. CoV had no json.cityPath/provincePath.

   This migration:
     1) Adds json.provinceOverride='BC' + json.cityOverride='Vancouver'
        so re-ingestion tags CoV correctly (CoV is a single jurisdiction).
     2) Scrubs the existing leaked values on COVAWARD rows.

   After applying, re-run PrimeRfpClassifierBackfill so PrimeKorLocationMatch
   is recomputed from the corrected province (BC matches IsKorLocationMatch).
   ===================================================================== */

DECLARE @CovId uniqueidentifier =
    (SELECT Id FROM opportunities.OpportunitySources WHERE Name = N'CoV_AwardedContracts');

IF @CovId IS NOT NULL
BEGIN
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    SELECT @CovId, k, v, sysdatetimeoffset() FROM (VALUES
        (N'json.provinceOverride', N'BC'),
        (N'json.cityOverride',     N'Vancouver')
    ) AS m(k, v)
    WHERE NOT EXISTS (
        SELECT 1 FROM opportunities.OpportunitySourceMappings
        WHERE OpportunitySourceId = @CovId AND [Key] = m.k);
END;
GO

UPDATE opportunities.Opportunities
SET ProjectProvince = N'BC',
    ProjectCity     = N'Vancouver',
    UpdatedAtUtc    = sysdatetimeoffset()
WHERE OpportunityKey LIKE 'COVAWARD-%';
GO

PRINT 'Migration 45: CoV province/city override added + leaked values scrubbed.';
GO
