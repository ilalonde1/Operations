USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 228: repoint the 44 live MajorProjectsInventory role-FKs that still
  point to JV-string orgs retired (without MPI repoint) by older Jun 12/16 passes.
  Apply LeadOperator retroactively: point each to the lead operator's live
  canonical org, dropping funders/agents. Where the true lead has no canonical
  org (Recovery Alberta, Hoy Creek, Ayrshire, Lulu Island Energy, RayCam), fall
  back to the funder/owner/partner that exists; where no resolvable lead at all,
  set NULL (the ProponentName text field retains the original string).
*/
BEGIN TRAN;

DECLARE @map TABLE (Arch bigint, Lead bigint, Role char(4));
INSERT INTO @map VALUES
 -- Architect role
 (72339,6154,'Arch'),   -- Dialog + GEC -> DIALOG
 (72394,54753,'Arch'),  -- Diamond Schmitt / Connect -> Diamond Schmitt
 (72085,69765,'Arch'),  -- KPMB / Hindle / Tawaw -> KPMB Architects
 (72194,70621,'Arch'),  -- Musson Cattell Mackey / PFS -> MCMP
 -- Proponent role
 (72438,476,'Prop'),    -- Alberta Infrastructure / AHS -> AHS
 (72434,53255,'Prop'),  -- Alberta Infrastructure / Recovery Alberta -> Alberta Infrastructure
 (72086,72092,'Prop'),  -- Assisted Living Alberta / Multiple -> Assisted Living Alberta
 (72056,53614,'Prop'),  -- Ayrshire / CMLC -> CMLC (Ayrshire has no org)
 (72264,38939,'Prop'),  -- BC Housing / Hoy Creek -> BC Housing
 (72255,38939,'Prop'),  -- BC Housing / Province / Surrey -> BC Housing
 (72246,38943,'Prop'),  -- Bosa / Chunghwa -> Bosa Properties
 (72095,903,'Prop'),    -- Bow Valley College / CMLC -> Bow Valley College
 (72064,53614,'Prop'),  -- CMLC / City -> CMLC
 (72093,612,'Prop'),    -- Calgary Public Library / City -> Calgary Public Library
 (72156,394,'Prop'),    -- City of Richmond / Lulu Island -> City of Richmond
 (72144,771,'Prop'),    -- Surrey / Province / SFU -> Simon Fraser University
 (72155,38939,'Prop'),  -- Vancouver / BC Housing / RayCam -> BC Housing
 (72150,28259,'Prop'),  -- Vancouver / Park Board -> Vancouver Board of Parks
 (72083,70853,'Prop'),  -- Deveraux / Lansdowne -> Deveraux Group
 (74194,54624,'Prop'),  -- Diverse Properties / Seraphim -> Diverse Properties
 (72461,880,'Prop'),    -- Fraser Health / Province -> Fraser Health
 (72049,18867,'Prop'),  -- Government of Alberta -> Government of Alberta
 (72051,18867,'Prop'),  -- Government of Alberta -> Government of Alberta
 (72077,476,'Prop'),    -- Government of Alberta / AHS -> AHS
 (72075,476,'Prop'),    -- Government of Alberta / AHS / Children -> AHS
 (72396,54977,'Prop'),  -- Interior Health / BC Cancer -> Interior Health
 (72428,54977,'Prop'),  -- Interior Health / North Okanagan -> Interior Health
 (72218,54982,'Prop'),  -- MST Partnership -> MST Development
 (72220,54982,'Prop'),  -- MST Partnership / Aquilini -> MST Development
 (72089,53119,'Prop'),  -- Multiple Developers / City of Calgary -> City of Calgary
 (72243,54985,'Prop'),  -- Musqueam / Townline / YMCA -> Musqueam Capital Corp
 (72446,53255,'Prop'),  -- Recovery Alberta / Alberta Infra -> Alberta Infrastructure
 (72213,71489,'Prop'),  -- Squamish Nation / OPTrust -> Squamish Nation
 (72058,53277,'Prop'),  -- Trico Communities / CMLC -> Trico Homes
 (72096,53260,'Prop'),  -- Various Developers / Calgary Airport -> Calgary Airport Authority
 (72040,76045,'Prop'),  -- YMCA Calgary / Library / City -> YMCA Calgary
 (72091,76045,'Prop');  -- YMCA Calgary / City -> YMCA Calgary

UPDATE m SET m.ArchitectCanonicalOrgId = x.Lead, m.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
JOIN @map x ON x.Role='Arch' AND m.ArchitectCanonicalOrgId = x.Arch
WHERE m.RetiredAtUtc IS NULL;

UPDATE m SET m.ProponentCanonicalOrgId = x.Lead, m.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectsInventory m
JOIN @map x ON x.Role='Prop' AND m.ProponentCanonicalOrgId = x.Arch
WHERE m.RetiredAtUtc IS NULL;

-- No resolvable lead (both parties have no canonical org): clear the FK (text field retains the name)
UPDATE opportunities.MajorProjectsInventory
   SET ProponentCanonicalOrgId = NULL, UpdatedAtUtc = sysdatetimeoffset()
 WHERE ProponentCanonicalOrgId = 72059 AND RetiredAtUtc IS NULL;  -- Ayrshire / Copenhagen

PRINT 'Migration 228: repointed archived JV-string MPI role-FKs to lead operators.';
COMMIT TRAN;
GO
