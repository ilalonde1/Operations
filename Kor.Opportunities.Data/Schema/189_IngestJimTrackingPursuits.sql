/*
189_IngestJimTrackingPursuits.sql
WHAT: Land the still-live / high-value CA pursuits from Jim's tracking log as MPI rows,
      linked to the warm developer/architect orgs + (where pursuing) an OPEN SE seat.
WHY:  Stage 3 of the warm-path decomposition — connects the warm contacts to concrete
      pursuits so briefs can show "warm contact + the project + the play."
HOW:  Dup-safe MERGE on (Province, SourceKey ns 'jim-tracking-2026-06:*'). Most logged
      projects are dead/historical and intentionally NOT ingested; these are the live or
      relationship-significant few. Stage reflects honest status.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @p TABLE (SourceKey nvarchar(200), ProjectName nvarchar(500), Sector nvarchar(100), Stage nvarchar(50),
  ProponentName nvarchar(500), ProponentOrgId bigint, ArchitectName nvarchar(500), ArchitectOrgId bigint,
  Municipality nvarchar(200), Region nvarchar(200), ProjDesc nvarchar(max), SchedNotes nvarchar(1000));
INSERT INTO @p VALUES
 (N'jim-tracking-2026-06:kettner-and-ash', N'Kettner & Ash', N'Multifamily High-rise', N'Active Lead',
  N'Kennedy Wilson', 70869, NULL, NULL, N'San Diego', N'San Diego',
  N'"Mirror of the Lindley." KW contact Andrew Spousta; $700k proposal; KOR pursuing (KOR was SEoR on the Lindley). [jim-tracking-2026-06]', N'Live 2026 — lunch arranged in SD.'),
 (N'jim-tracking-2026-06:7th-and-a', N'7th & A Tower (San Diego)', N'Multifamily High-rise', N'Pursuit',
  N'AAA Management', 76959, N'Carrier Johnson+Culture', 112, N'San Diego', N'San Diego',
  N'KOR got onto the project team via owner-rep Rosalie Merks (AAA) + Claudia Escala (Carrier Johnson). [jim-tracking-2026-06]', N'Historical pursuit; relationship-significant.'),
 (N'jim-tracking-2026-06:fifth-avenue-landing', N'Fifth Avenue Landing', N'Hotel / Mixed-Use', N'Pursuit',
  N'Robert Greene Company', 76966, N'Gensler', 68631, N'San Diego', N'San Diego',
  N'Gensler (Kevin Heinley) listed KOR as PREFERRED structural consultant; owner Robert Greene. [jim-tracking-2026-06]', N'Owner pacing entitlements; KOR preferred SE.'),
 (N'jim-tracking-2026-06:candlestick-point', N'Candlestick Point', N'Master Plan', N'Early / Masterplan',
  NULL, NULL, N'IBI Group Architects', 75864, N'San Francisco', N'Bay Area',
  N'700-acre / ~10,000-unit SF masterplan (old Candlestick Park). IBI / Stuart Jones. [jim-tracking-2026-06]', N'Large long-horizon masterplan.'),
 (N'jim-tracking-2026-06:6th-and-olive', N'6th & Olive', N'Multifamily High-rise', N'Pursuit',
  N'Greystar', 70737, N'JWDA Inc.', 48, N'San Diego', N'San Diego',
  N'15-story rental tower, Hillcrest. Greystar + JWDA (Joe Wong); KOR did concept work. [jim-tracking-2026-06]', N'JWDA-channel SD pursuit.');
MERGE opportunities.MajorProjectsInventory WITH (HOLDLOCK) AS T
USING (SELECT * FROM @p) AS S ON T.Province = N'CA' AND T.SourceKey = S.SourceKey
WHEN MATCHED THEN UPDATE SET ProjectName=S.ProjectName, Sector=S.Sector, Stage=S.Stage, ProjectStage=S.Stage,
  ProponentName=S.ProponentName, ProponentCanonicalOrgId=S.ProponentOrgId, ArchitectName=S.ArchitectName,
  ArchitectCanonicalOrgId=S.ArchitectOrgId, MunicipalityName=S.Municipality, RegionName=S.Region,
  ProjectDescription=S.ProjDesc, ScheduleNotes=S.SchedNotes, LastSeenAtUtc=@now, UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProjectStage, ProponentName,
  ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, RegionName,
  ProjectDescription, ScheduleNotes, FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc)
  VALUES (N'CA', S.SourceKey, S.ProjectName, S.Sector, S.Stage, S.Stage, S.ProponentName, S.ProponentOrgId,
  S.ArchitectName, S.ArchitectOrgId, S.Municipality, S.Region, S.ProjDesc, S.SchedNotes, @now, @now, @now);
COMMIT;
SELECT Id, LEFT(ProjectName,32) AS Proj, RegionName AS Region, Stage, ProponentCanonicalOrgId AS DevId, ArchitectCanonicalOrgId AS ArchId
FROM opportunities.MajorProjectsInventory WHERE Province=N'CA' AND SourceKey LIKE N'jim-tracking-2026-06:%' ORDER BY Proj;
