USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 216: record KOR Structural's actual California track record (verified
  from korstructural.com portfolio, 2026-06-18). KOR is NOT a SD challenger — it is
  an established San Diego high-rise SE with ~10 delivered towers (+ Irvine + LA).
  Deep relationships: JWDA (6), Greystar (3), Bosa (3). These are KOR-SEOR
  reference projects = the qualifications behind every CA pursuit. The Lindley and
  1011 Union are already recorded (migrations 206/211); this adds the other 10.
  Also: 525 Olive = "6th & Olive" on KOR's portfolio (VERIFIED KOR SEOR), so it is
  a KOR credit, not an open seat. The Torrey + Park & Market are NOT KOR (no
  credit anywhere) — they remain genuine pursuits.
*/

BEGIN TRAN;

MERGE opportunities.MajorProjectsInventory AS T
USING (VALUES
 (N'korref:kor-grande',        N'The Grande San Diego (40-story twin towers)',      N'San Diego', N'Bosa Development', 69677, N'Perkins & Company', CAST(NULL AS bigint), N'KOR SE of record. ACI Multi-Family award; landmark triangular bayfront towers.'),
 (N'korref:kor-bayside',       N'Bayside (37-story, 232 units)',                     N'San Diego', N'Bosa Development', 69677, N'Amanat Architect', NULL, N'KOR SE of record. 9 perimeter townhouses.'),
 (N'korref:kor-simone',        N'1401 Union St / Simone (37-story)',                 N'San Diego', N'Trammell Crow Residential', NULL, N'JWDA', 48, N'KOR SE of record. Performance-Based Seismic Design; foundation bridges 20-ft sewer easement; 2024 Golden Nugget.'),
 (N'korref:kor-800-broadway',  N'800 Broadway (40-story, 389 units)',                N'San Diego', N'CA Ventures', NULL, N'JWDA', 48, N'KOR SE of record. PT flat slabs; Alternative Means & Methods seismic approval.'),
 (N'korref:kor-6th-olive',     N'6th & Olive / 525 Olive (21-story, 204 units)',     N'San Diego', N'Greystar', 55110, N'JWDA', 48, N'KOR SE of record (VERIFIED; =525 Olive). JV with St. Paul''s Cathedral; SDBJ Best Multi-Family.'),
 (N'korref:kor-belmont-lj',    N'Belmont Village La Jolla (17-story senior, 180 units)', N'San Diego', N'Greystar Development', 55110, N'JWDA', 48, N'KOR SE of record. Senior residence, 209k sf.'),
 (N'korref:kor-the-fellow',    N'The Fellow (19-story, 130 units)',                  N'San Diego', N'Cast Development', NULL, N'Trachtenberg Architects', NULL, N'KOR SE of record. 13% affordable.'),
 (N'korref:kor-the-quince',    N'The Quince (17-story, 260 units)',                  N'San Diego', N'Cast Development', NULL, N'Works Progress Architecture', NULL, N'KOR SE of record. Irving Gill heritage reference.'),
 (N'korref:kor-marquee-irvine',N'Marquee at Park Place (2x 18-story, 232 units)',    N'Irvine',    N'Bosa Development', 69677, N'Chris Dikeakos Architects', 7, N'KOR SE of record. Orange County''s first condo high-rise in decades.'),
 (N'korref:kor-hartford-la',   N'Hartford Apartments (7-story, 218 units)',          N'Los Angeles', N'Intergulf Development Group', NULL, N'IBI Group Architects', NULL, N'KOR SE of record. Wood-frame on concrete; Westlake.')
) AS S(SourceKey, ProjectName, City, PropName, PropId, ArchName, ArchId, Notes)
ON T.Province=N'CA' AND T.SourceKey=S.SourceKey
WHEN NOT MATCHED THEN
  INSERT (Province, SourceKey, ProjectName, Sector, Stage, ProponentName, ProponentCanonicalOrgId, ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, ScheduleNotes, SourceUrl, RawJson)
  VALUES (N'CA', S.SourceKey, S.ProjectName, N'Residential High-Rise', N'Built - KOR SE of record', S.PropName, S.PropId, S.ArchName, S.ArchId, S.City, S.Notes, N'https://www.korstructural.com/projects/', N'{"source":"kor-portfolio-2026-06-18","korRole":"SE of record"}');

PRINT 'Migration 216: KOR California portfolio (10 reference projects) recorded.';
COMMIT TRAN;
GO
