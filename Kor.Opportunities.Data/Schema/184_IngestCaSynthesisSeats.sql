/*
184_IngestCaSynthesisSeats.sql
WHAT: Land the verified 2026-06 California synthesis project-seats as MajorProjectsInventory
      rows (Province='CA', SourceKey namespace 'ca-research-2026-06:*' for traceability +
      future dedup against the CA funnel providers).
WHY:  Stage 2 of decomposing the CA synthesis. Carries the SE-seat attribution (the #1 data
      gap) + developer/architect links so the app can brief on these seats.
HOW:  Dup-safe MERGE on the unique key (Province, SourceKey). SE org NULL = genuinely OPEN
      seat (red-team-verified). Org IDs resolved/created in migration 183 + the 2026-06-18 audit.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

DECLARE @p TABLE (
  SourceKey nvarchar(200), ProjectName nvarchar(500), Sector nvarchar(100),
  Stage nvarchar(50), ProponentName nvarchar(500), ProponentOrgId bigint,
  ArchitectName nvarchar(500), ArchitectOrgId bigint,
  SeName nvarchar(500), SeOrgId bigint,
  Municipality nvarchar(200), Region nvarchar(200), CostText nvarchar(200),
  ProjDesc nvarchar(max), SchedNotes nvarchar(1000));

INSERT INTO @p VALUES
 (N'ca-research-2026-06:amara-bay', N'Amara Bay (Chula Vista Bayfront)', N'Multifamily / Mixed-Use',
  N'Entitlement', N'Pacifica Companies', 76953, N'AVRP Studios', 75176, NULL, NULL,
  N'Chula Vista', N'San Diego', N'~$1B, ~1,500u + hotel',
  N'OPEN SE seat. Pacifica selects the SE (not AVRP). Watch/position play — re-acquire the current Pacifica entitlements lead (Jack Straw departed). [ca-research-2026-06]',
  N'Public infrastructure under construction; tower SE selection likely 2027+.'),
 (N'ca-research-2026-06:1st-and-island', N'1st & Island', N'Multifamily High-rise',
  N'Paused', N'Bosa Development', 69677, N'James KM Cheng Architects', NULL, N'Glotman Simpson Consulting Engineers', 38926,
  N'San Diego', N'San Diego', N'~35-40 story',
  N'SE = Glotman (probable, Bosa default) but KOR is a 20yr Bosa CO-INCUMBENT — lead with KOR''s own Bosa history. Paused (Nat Bosa "handcuffed" Mar 2025). [ca-research-2026-06]',
  N'Paused pre-permit; restart + SE lock 2027-28.'),
 (N'ca-research-2026-06:fox-hills-uplander', N'5757 Uplander Way (Fox Hills)', N'Multifamily',
  N'Pre-construction', N'Link Logistics Real Estate', 76954, N'Killefer Flammang Architects', 76955, NULL, NULL,
  N'Culver City', N'Los Angeles', N'1,077u',
  N'OPEN — KOR''s ONE genuinely-open LA architect seat (red-team-verified). Reach Link Logistics + KFA now; 6-12 month window. [ca-research-2026-06]',
  N'Entitled Nov 2025; construction Q1 2027 earliest.'),
 (N'ca-research-2026-06:stevens-creek-sj', N'3896 Stevens Creek Blvd', N'Multifamily',
  N'Pre-permit', N'Holland Partner Group', 68644, N'TCA Architects', 76956, NULL, NULL,
  N'San Jose', N'Bay Area', N'532u, two 8-story',
  N'#1 NORCAL SEAT — OPEN. Decision-maker John Wayland (Holland Exec MD, Dev NorCal). Warm off KOR''s SD Holland relationship; incumbent Nelson is displaceable. Enter via Holland or TCA. [ca-research-2026-06]',
  N'Approved Mar 2025; SE locks at building-permit prep — window now to ~Q3 2026.'),
 (N'ca-research-2026-06:calle-de-luna', N'2200 Calle De Luna', N'Multifamily',
  N'Preliminary', N'Holland Partner Group', 68644, NULL, NULL, NULL, NULL,
  N'Santa Clara', N'Bay Area', N'301u',
  N'Second open Holland seat — same John Wayland channel. [ca-research-2026-06]',
  N'Preliminary plans Jun 2025; SE ~6-18 months out.'),
 (N'ca-research-2026-06:mission-college-2518', N'2518 Mission College Blvd (Irvine Company)', N'Multifamily',
  N'Pre-permit', N'Irvine Company LLC', 74271, N'MVE + Partners', 76952, NULL, NULL,
  N'Santa Clara', N'Bay Area', N'1,792u, 5 podium bldgs',
  N'BIGGEST live NorCal seat — OPEN. Irvine Company''s internal dev team picks the SE (MVE only recommends). Use Matthew McLarand for a warm intro to the Irvine Co. project lead. [ca-research-2026-06]',
  N'Approved Mar 2025; window now to ~Q1 2027.'),
 (N'ca-research-2026-06:gateway-crossing', N'Gateway Crossing (Orlo + Alba)', N'Multifamily TOD',
  N'Completed', N'Holland Partner Group', 68644, N'MVE + Partners', 76952, N'Nelson Structural Engineers', 76950,
  N'Santa Clara', N'Bay Area', N'725u, $290M',
  N'Reference project (NOT a target): Nelson is SE, brought FBA Inc. as co-SE = the capacity tell behind KOR''s "dedicated NorCal partner" pitch. [ca-research-2026-06]',
  N'Opened Sept 2025.'),
 (N'ca-research-2026-06:300-s-first-sj', N'300 S First St', N'Multifamily High-rise',
  N'Proposed', N'Westbank', NULL, N'Steinberg Hart', NULL, N'Glotman Simpson Consulting Engineers', 38926,
  N'San Jose', N'Bay Area', N'1,147u, 3 towers',
  N'Westbank San Jose campus is Glotman-locked (confirmed on Orchard/Fountain Alley/Energy Hub; probable here) — flag-plant only. [ca-research-2026-06]',
  N'Under review; no approval yet.'),
 (N'ca-research-2026-06:oc-vibe', N'OC Vibe Residential', N'Multifamily',
  N'In development', N'Samueli / OC Sports & Entertainment', NULL, N'MVE + Partners', 76952, NULL, NULL,
  N'Anaheim', N'Orange County', N'~1,960-2,250u',
  N'OPEN — OC''s biggest open seat. SE TBD; entry via Pieter Berger (MVE Irvine Principal). [ca-research-2026-06]',
  N'~2029 delivery; SE not yet selected.'),
 (N'ca-research-2026-06:bolsa-pacific', N'Bolsa Pacific', N'Multifamily',
  N'Under construction', N'Shopoff Realty Investments', NULL, N'AO (Architects Orange)', NULL, NULL, NULL,
  N'Westminster', N'Orange County', N'2,250u',
  N'OPEN/TBD — broke ground Apr 2026. Warm RC Alley (AO). [ca-research-2026-06]',
  N'Broke ground Apr 2026; SE TBD.'),
 (N'ca-research-2026-06:ritz-carlton-newport', N'Ritz-Carlton Residences Newport Beach', N'Luxury High-rise',
  N'Pre-construction', NULL, NULL, N'MVE + Partners', 76952, N'Glotman Simpson Consulting Engineers', 38926,
  N'Newport Beach', N'Orange County', N'159u, 22-story',
  N'Glotman holds MVE''s OC flagship — the #1 OC displacement target via the Matthew McLarand relationship. [ca-research-2026-06]',
  N'Pre-construction; groundbreaking imminent.');

MERGE opportunities.MajorProjectsInventory WITH (HOLDLOCK) AS T
USING (SELECT * FROM @p) AS S
  ON T.Province = N'CA' AND T.SourceKey = S.SourceKey
WHEN MATCHED THEN UPDATE SET
  ProjectName = S.ProjectName, Sector = S.Sector, Stage = S.Stage, ProjectStage = S.Stage,
  ProponentName = S.ProponentName, ProponentCanonicalOrgId = S.ProponentOrgId,
  ArchitectName = S.ArchitectName, ArchitectCanonicalOrgId = S.ArchitectOrgId,
  StructuralEngineerName = S.SeName, StructuralEngineerCanonicalOrgId = S.SeOrgId,
  MunicipalityName = S.Municipality, RegionName = S.Region, EstimatedCostText = S.CostText,
  ProjectDescription = S.ProjDesc, ScheduleNotes = S.SchedNotes, LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
  (Province, SourceKey, ProjectName, Sector, Stage, ProjectStage, ProponentName, ProponentCanonicalOrgId,
   ArchitectName, ArchitectCanonicalOrgId, StructuralEngineerName, StructuralEngineerCanonicalOrgId,
   MunicipalityName, RegionName, EstimatedCostText, ProjectDescription, ScheduleNotes,
   FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc)
  VALUES
  (N'CA', S.SourceKey, S.ProjectName, S.Sector, S.Stage, S.Stage, S.ProponentName, S.ProponentOrgId,
   S.ArchitectName, S.ArchitectOrgId, S.SeName, S.SeOrgId,
   S.Municipality, S.Region, S.CostText, S.ProjDesc, S.SchedNotes, @now, @now, @now);

-- Fix the one pre-existing researched row: Related Bristol (Id 3639) was mislabeled region=Los Angeles
UPDATE opportunities.MajorProjectsInventory
  SET RegionName = N'Orange County', MunicipalityName = COALESCE(MunicipalityName, N'Santa Ana'),
      ScheduleNotes = COALESCE(ScheduleNotes, N'~3,750u; SE TBD. Region corrected from LA. [ca-research-2026-06]'),
      UpdatedAtUtc = @now
  WHERE Id = 3639 AND Province = N'CA' AND ProjectName LIKE N'%Related Bristol%';

COMMIT;

SELECT Id, LEFT(ProjectName,38) AS Proj, RegionName AS Region, Stage,
       ProponentCanonicalOrgId AS DevId, ArchitectCanonicalOrgId AS ArchId,
       StructuralEngineerCanonicalOrgId AS SeId, StructuralEngineerName AS SE
FROM opportunities.MajorProjectsInventory
WHERE Province = N'CA' AND SourceKey LIKE N'ca-research-2026-06:%'
ORDER BY Region, Proj;
