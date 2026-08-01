USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 213: open-seat pursuit pipeline (research 2026-06-18, two sweeps:
  docs/ca-research-raw/ca-open-seat-sweep + sacramento-eastbay). Net-new CA
  residential/mixed-use projects, pre-construction, with NO public structural
  engineer (open seat). Stored as MPI pursuit rows with developer/architect as
  text + SE-status note + source; canonical org linking deferred until a pursuit
  advances (avoids minting ~40 thin orgs). MERGE on SourceKey = idempotent.
*/

BEGIN TRAN;

DECLARE @p TABLE (SourceKey nvarchar(200), ProjectName nvarchar(500), Stage nvarchar(50), City nvarchar(200), Prop nvarchar(500), Arch nvarchar(500), Notes nvarchar(1000), Url nvarchar(1000));
INSERT INTO @p VALUES
 (N'korref:88-bluxome', N'88 Bluxome St (1,500 units)', N'Plans approved Dec 2025', N'San Francisco', N'Strada Investment Group', N'SCB + Henning Larsen', N'SE OPEN - largest SF pipeline; new design team Dec 2025 => fresh SE selection; high-seismic (KOR lane). Door: SCB (Wrigley/Mozzati). Strada also has 555 Beale.', N'https://sfyimby.com/2025/12/plans-approved-for-58-story-88-bluxome-street-promptly-hits-market-in-san-francisco.html'),
 (N'korref:riverwalk-studio-city', N'Riverwalk at Studio City (814 units)', N'Planning Commission approved Jun 2026', N'Los Angeles', N'Genton Property Group / RC Development', N'MVE + Partners', N'SE OPEN. Architect MVE (in graph). Just cleared planning - procurement window.', N'https://la.urbanize.city/post/big-mixed-use-project-clears-hurdle-12555-ventura-blvd-studio-city'),
 (N'korref:555-beale', N'555 Beale St / Seawall Lot 330 (568 units)', N'Application filed May 2026', N'San Francisco', N'Strada Investment Group', N'Grimshaw + Perry Architects', N'SE OPEN - waterfront seismic (KOR lane). Strada (also 88 Bluxome).', N'https://sfyimby.com/2026/05/formal-application-for-seawall-lot-330-san-francisco.html'),
 (N'korref:american-river-one', N'American River One (826 units, 4 towers)', N'CEQA cleared Jan 2025; to CDs', N'Sacramento', N'IM Investments', N'LPA Design Studios', N'SE OPEN - largest Sac residential advancing to CDs. LPA spans Sac+Oakland.', N'https://sfyimby.com/2025/01/american-river-one-development-continues-to-succeed-against-appeals.html'),
 (N'korref:fourth-central', N'Fourth & Central (1,589 units, ~$2B)', N'Entitlement', N'Los Angeles', N'Continuum Partners', N'Adjaye Associates + Studio One Eleven', N'SE OPEN - DTLA/Arts District mega mixed-use; early entitlement = good timing.', N'https://la.urbanize.city/post/2-billion-fourth-central-development-takes-haircut'),
 (N'korref:bloc-tower', N'The Bloc Tower / 700 S Flower (466 units)', N'Planning approved', N'Los Angeles', N'National Real Estate Advisors', N'Handel Architects', N'SE OPEN - 53-story DTLA. Handel relationship to explore.', N'https://la.urbanize.city/post/53-story-tower-above-blocs-garage-slated-completion-2030'),
 (N'korref:360-5th', N'360 5th St, SoMa (272 units)', N'Building permits filed Dec 2025', N'San Francisco', N'Thompson Builders', N'Handel Architects', N'SE OPEN - construction start mid-2026. Handel.', N'https://sfyimby.com/2025/12/new-building-permits-filed-for-360-5th-street-in-soma-san-francisco.html'),
 (N'korref:6000-hollywood', N'6000 Hollywood Blvd (350 units)', N'Entitlement upheld; GB 2026', N'Los Angeles', N'Hines + Sullivan family', N'OfficeUntitled', N'SE OPEN - groundbreaking 2026.', N'https://la.urbanize.city/post/la-city-council-upholds-approval-high-rise-complex-6000-hollywood-blvd'),
 (N'korref:mandela-station', N'Mandela Station / West Oakland BART (762 units, 31-story)', N'Site prep underway 2026', N'Oakland', N'MacFarlane Partners / SUDA', N'AO (Architects Orange) + JRDV', N'SE OPEN - #1 East Bay open seat: 31-story high-rise, seismic zone, no SE named. GC Hensel Phelps.', N'https://sfyimby.com/2026/02/initial-construction-on-west-oakland-transit-oriented-development-planned-to-start-later-this-year.html'),
 (N'korref:2128-oxford', N'2128 Oxford St / The Hub, Berkeley (433 units)', N'ZAB review Apr 2026', N'Berkeley', N'Core Spaces', N'DLR Group', N'SE OPEN - early-stage student/multifamily.', N'https://sfyimby.com/2026/04/meeting-tomorrow-for-2128-oxford-street-in-downtown-berkeley.html'),
 (N'korref:south-park-towers', N'South Park Towers (250 units + hotel)', N'Entitlement approved', N'Los Angeles', N'Jade Enterprises', N'AC Martin', N'SE OPEN - DTLA. AC Martin in graph.', N'https://la.urbanize.city/post/mack-urbans-new-south-park-towers-revealed'),
 (N'korref:220-w-20th', N'220 W 20th Ave (232 units)', N'Council approved Jun 2026', N'San Mateo', N'Summerhill Apartment Communities', N'BAR Architects', N'SE OPEN - SB330.', N'https://sfyimby.com/'),
 (N'korref:1215-bordeaux', N'1215 Bordeaux Dr (265 units)', N'Final approval Jun 2026', N'Sunnyvale', N'Beam Reach', N'KTGY', N'SE OPEN. Architect KTGY (in graph).', N'https://news.theregistrysf.com/'),
 (N'korref:mclellan-pacific', N'McLellan Apts / 4095 Pacific Blvd (202 units)', N'Permit application Jun 2026', N'San Mateo', N'McLellan Company', N'AO (Architects Orange)', N'SE OPEN.', N'https://therealdeal.com/san-francisco/2026/06/15/mclellan-company-looks-to-build-202-apartments-in-san-mateo/'),
 (N'korref:midway-rising', N'Midway Rising (4,000+ units, ~$2B)', N'Planning approved Sep 2025', N'San Diego', N'Zephyr + Chelsea Investment', N'Safdie Rabines Architects', N'SE OPEN (watch) - master plan; SE distributed across parcels; watch parcel RFPs. Safdie Rabines in graph.', N'https://www.safdierabines.com/midway-rising-moves-forward/'),
 (N'korref:smud-59th', N'SMUD 59th St / Sac59 (900 units)', N'Design development / EIR', N'Sacramento', N'SKK Developments + BlackPine', N'C2K Architecture', N'SE OPEN - SKK is dominant Sac mid-market dev (Sotiris Kolokotronis). Podium wood-frame + seismic fit.', N'https://www.sac59.com/'),
 (N'korref:north-berkeley-bart', N'North Berkeley BART TOD (743 units, 13 bldgs)', N'Funding 2026; GB ~2027', N'Berkeley', N'BRIDGE Housing + AvalonBay', N'David Baker Architects', N'SE OPEN - affordable/wood-frame podium (KOR lane). BRIDGE + David Baker in graph.', N'https://www.berkeleyside.org/2025/12/23/north-berkeley-bart-housing-construction-start-date-2027'),
 (N'korref:grand-gateway-wsac', N'Grand Gateway, West Sacramento (450+ units)', N'EIR H1 2026', N'West Sacramento', N'SKK Developments + UrbanCore', N'TBD', N'SE OPEN - SKK + UrbanCore (Michael Johnson).', N'https://www.cityofwestsacramento.org/government/departments/community-development/planning-division/major-planning-projects'),
 (N'korref:yusi-commons', N'Yusi Commons, North Highlands (370 units affordable)', N'Filed Mar 2026', N'Sacramento', N'Onyx Investment Group', N'TBD', N'SE OPEN - affordable.', N'https://news.theregistrysf.com/onyx-investment-group-files-plans-for-370-unit-affordable-housing-complex-in-sacramentos-north-highlands/'),
 (N'korref:1523-harrison', N'1523 Harrison St (284 units, mass timber)', N'Plans approved Dec 2025; GB 2026', N'Oakland', N'oWOW', N'oWOW (in-house)', N'SE: Glotman Simpson partial (WoodWorks) - oWOW design-build, repeat SE sub. Mass timber + seismic (KOR differentiator). Andy Ball / Jeremy Harris.', N'https://sfyimby.com/2025/12/final-plans-approved-for-the-1523-harrison-street-mass-timber-building-by-owow.html'),
 (N'korref:csus-downtown', N'CSU Sacramento Downtown Campus (~500 student units)', N'Master-planning', N'Sacramento', N'CSU Sacramento / State of CA + Meta', N'TBD', N'SE OPEN - $50M Meta seed Jan 2026; early.', N'https://www.gov.ca.gov/2026/01/29/governor-newsom-sacramento-state-and-meta-advance-major-redevelopment-in-downtown-sacramento-to-create-affordable-housing-for-students/'),
 (N'korref:laconia-berkeley', N'Laconia 23-story tower, Berkeley', N'Proposal', N'Berkeley', N'Laconia Development + Gilbane', N'SDT Architects', N'SE OPEN - student/multifamily, proposal stage.', N'https://news.theregistrysf.com/laconia-development-proposes-23-story-tower-in-berkeley-to-feature-multifamily-or-student-housing/');

MERGE opportunities.MajorProjectsInventory AS T
USING @p AS S
ON T.Province=N'CA' AND T.SourceKey=S.SourceKey
WHEN NOT MATCHED THEN
  INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ArchitectName, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES (N'CA', S.SourceKey, S.ProjectName, N'Residential', S.Stage, S.Prop, S.Arch, S.City, S.Notes, S.Url, N'{"source":"open-seat-sweep-2026-06-18","seStatus":"open"}');

DECLARE @cnt int = (SELECT COUNT(*) FROM @p);
PRINT CONCAT('Migration 213: open-seat pursuit projects upserted (', @cnt, ' candidates).');
COMMIT TRAN;
GO
