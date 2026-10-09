-- Display-only metadata for the kind-based BD hub. No provider reads bd.kind.
-- One-time reviewed inventory, 2026-10-09. The UI never matches source names.
-- New mixed-transport sources without metadata remain Other / unclassified.
-- Existing operator choices win; this seed is safe to rerun.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @kinds TABLE (Name nvarchar(200) PRIMARY KEY, Kind nvarchar(30));
INSERT @kinds VALUES
('Calgary_LandUseRedesignation','application'),
('Nanaimo_WhatsBuilding','application'),
('Parksville_DevelopmentApplications','application'),
('CoV_AwardedContracts','award'),
('CanadaBuys','bid'),('CanadaBuysNew','bid'),('LACity_RAMP_OpenBids','bid'),
('BCGovNewsRss','other'),('BdAlertsMailbox','other'),
('CivicInfoBC_All','bid'),('CivicInfoBC_Construction','bid'),
('Bonfire_AHS','bid'),('Bonfire_AthabascaU','bid'),('Bonfire_BCIT','bid'),
('Bonfire_BCLC','bid'),('Bonfire_BCTransit','bid'),('Bonfire_CbeAb','bid'),
('Bonfire_CVRD','bid'),('Bonfire_Epsb','bid'),('Bonfire_FNHA','bid'),
('Bonfire_FraserHealth','bid'),('Bonfire_ICBC','bid'),('Bonfire_IslandHealth','bid'),
('Bonfire_Kamloops','bid'),('Bonfire_Kelowna','bid'),('Bonfire_KPU','bid'),
('Bonfire_MacEwanU','bid'),('Bonfire_Mission','bid'),('Bonfire_MountRoyalU','bid'),
('Bonfire_NAIT','bid'),('Bonfire_Nelson','bid'),('Bonfire_NorthCowichan','bid'),
('Bonfire_Penticton','bid'),('Bonfire_PHSA','bid'),('Bonfire_PrrdBc','bid'),
('Bonfire_Saanich','bid'),('Bonfire_SAIT','bid'),('Bonfire_StrathconaCty','bid'),
('Bonfire_TravelAlberta','bid'),('Bonfire_UAlberta','bid'),('Bonfire_UBC','bid'),
('Bonfire_UFV','bid'),('Bonfire_UVic','bid'),('Bonfire_VCH','bid'),('Bonfire_Victoria','bid');

INSERT opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson)
SELECT s.Id, 'bd.kind', k.Kind
FROM opportunities.OpportunitySources s JOIN @kinds k ON k.Name = s.Name
WHERE s.SourceType IN (1,2,3,5,99)
AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings m
                WHERE m.OpportunitySourceId = s.Id AND m.[Key] = 'bd.kind');
COMMIT;
