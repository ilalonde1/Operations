USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
/* Migration 244: configure CA_SocrataSF to compose the rich name at ingest
   (provider ComposeRichName): append municipality + descriptor so NEW rows are
   born as "<address>, San Francisco - <stories>-storey <use> (<units> units)".
   Requires the provider build with ComposeRichName deployed. */
UPDATE opportunities.OpportunitySources
SET ConfigJson = JSON_MODIFY(
                   JSON_MODIFY(ConfigJson, '$.nameAppendMunicipality', 'true'),
                   '$.descriptorParts', 'number_of_proposed_stories={}-storey;proposed_use={};proposed_units=({} units)'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'CA_SocrataSF';
GO
PRINT 'Migration 244: CA_SocrataSF rich-name config set.';
GO
