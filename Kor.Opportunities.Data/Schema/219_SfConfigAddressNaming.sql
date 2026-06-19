USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO

/*
  Migration 219: stop the SF funnel from naming projects by raw permit text.
  Remove projectNameColumn=description; add addressColumns so the provider names
  SF rows by composite street address (street_number + street_name + street_suffix).
  APPLY ONLY AFTER the CaSocrataMajorProjectsInventoryProvider composite-address
  change is deployed (see docs/codex-sf-address-naming.md) — otherwise the old
  provider falls back to the description-lead on the next ingest.
  Uses JSON_MODIFY to preserve the rest of ConfigJson (incl. the $where filter).
*/

UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(
                   JSON_MODIFY(ConfigJson, '$.projectNameColumn', NULL),
                   '$.addressColumns', 'street_number,street_name,street_suffix'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSF';
GO

PRINT 'Migration 219: CA_SocrataSF now names by composite street address (apply post-deploy).';
GO
