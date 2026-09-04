-- Migration 300 (2026-09-04): for Victoria, the contact is the APPLICANT'S
-- AGENT, not the city planner.
--
-- WHY. Opportunity has ONE BuyerContact* slot and two candidates compete for it:
--   * the ArcGIS layer's CityContact — a City of Victoria planner. A regulator.
--   * the Prospero detail page's Application Contact — the applicant's own
--     agent: the developer, their architect or their planning consultant.
--
-- For BD the second is the lead and the first is not. LiveOppDetailEnrichmentJob
-- persists FILL-ONLY ("SET BuyerContactName=@v WHERE BuyerContactName IS NULL"),
-- so as long as the planner occupies the slot the agent is silently discarded.
--
-- So: stop mapping the city contact for Victoria, and clear the values already
-- written, letting VictoriaProsperoLiveDetailExtractor fill them. Nothing is
-- lost — the city contact is still on every observation's RawJson and can be
-- re-read from the layer at any time.
--
-- ⚠ This is Victoria-specific ON PURPOSE. The arcgis.contact*Field mappings stay
-- in the adapter for cities that have no detail page to enrich from; Coquitlam
-- and Maple Ridge keep theirs.
USE [KorOpportunitiesDb];
GO

-- sqlcmd -i defaults QUOTED_IDENTIFIER OFF, and opportunities.Opportunities
-- carries a computed column, so any UPDATE against it fails with Msg 1934
-- without these. Set them explicitly rather than relying on the client.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

DECLARE @victoria uniqueidentifier;

SELECT @victoria = Id
FROM opportunities.OpportunitySources
WHERE Name = N'Victoria_DevelopmentApplications';

IF @victoria IS NULL
BEGIN
    PRINT 'Migration 300: Victoria_DevelopmentApplications not found; nothing to do.';
    RETURN;
END;

DELETE FROM opportunities.OpportunitySourceMappings
WHERE OpportunitySourceId = @victoria
  AND [Key] IN (N'arcgis.contactNameField',
                N'arcgis.contactEmailField',
                N'arcgis.contactPhoneField');

-- Clear the planner values so the fill-only enrichment can write the agent, and
-- re-open the rows for enrichment.
UPDATE o
SET o.BuyerContactName = NULL,
    o.BuyerContactEmail = NULL,
    o.BuyerContactPhone = NULL,
    o.DetailEnrichedAtUtc = NULL,
    o.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.Opportunities o
WHERE EXISTS (
    SELECT 1
    FROM opportunities.OpportunityObservations ob
    WHERE ob.OpportunityId = o.Id
      AND ob.OpportunitySourceId = @victoria);
GO

PRINT 'Migration 300: Victoria contact slot reserved for the applicant''s agent; rows re-opened for detail enrichment.';
GO
