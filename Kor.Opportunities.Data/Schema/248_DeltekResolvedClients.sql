USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 248: thin-client deep enrichment. Generated from
   docs/enrichment-2026-06-20-thin-clients agent-A/B/C JSON.
   12 contacts / 11 org-meta updates / 11 orgs. */
DECLARE @Provider nvarchar(60) = N'DeltekCrm2026-06-20';
BEGIN TRAN;
DECLARE @meta TABLE (OrgId bigint, Website nvarchar(400), ProfileNote nvarchar(400));
INSERT INTO @meta (OrgId,Website,ProfileNote) VALUES
(230, N'https://www.bdkdevcorp.com', N'Vancouver developer in the Plaza of Nations group. KOR project: 144 West 21st St, North Vancouver (2023). Tel 604-682-0777.'),
(192, N'https://frame.properties', N'Vancouver west-side residential developer; trades as / affiliated with Frame Properties. KOR projects at 2720-2730 West 16th Ave, Vancouver (2023-2025).'),
(132, N'https://townside.ca', N'Surrey land-assembly developer (Guildford), affiliated with Townside Developments. KOR project: 14518-14558 106 Ave land assembly, Surrey (2024).'),
(6, N'https://turngroup.ca', N'Real-estate developer (Calgary HQ, area code 587; also Ottawa pursuits). KOR pursuit: 299 Carling Ave, Ottawa (2026).'),
(18059, N'https://warrenconstruction.ca', N'Commercial/restaurant fit-out general contractor (Calgary, area code 403). KOR project: Popeyes Louisiana Kitchen, Park Royal Mall, West Vancouver (2022).'),
(37977, N'https://jfoconstruction.ca', N'BC general contractor (principal Jeff Owen; JFO = his initials). KOR projects: Arris tower rooftop catwalk, Calgary (2023); 10828 144th St, Surrey.'),
(22291, NULL, N'Private Vancouver developer (Pabla family) - NOT Royal Bank. KOR project: 4112 Heather St, Vancouver (Cambie corridor; formerly 692 West King Edward).'),
(127, N'https://coastdevelop.com', N'Developer. KOR pursued 38140 Third Ave, Squamish BC (2024, LOST). KOR-recorded site coastdevelop.com - confirm BC arm vs Ontario projects shown on that domain.'),
(75123, NULL, N'Surrey commercial developer. KOR pursuit: 9572 120th St, Surrey commercial development (2026). Deltek contact routed via DF Architecture (project architect).'),
(212, NULL, N'Private BC (Vancouver Island) investment/development company; low public profile. KOR projects: 570 Bezanton Way, Colwood BC (foundation/hydro redesign, 2023-2024).'),
(27247, N'https://www.quadrangle.ca', N'Toronto architecture firm (now BDP Quadrangle), area code 416 - not a BC firm. KOR has a contact relationship; no won project.');
UPDATE o SET o.Website=COALESCE(o.Website,m.Website), o.Notes=COALESCE(o.Notes,m.ProfileNote), o.UpdatedAtUtc=sysdatetimeoffset() FROM opportunities.CanonicalOrg o JOIN @meta m ON m.OrgId=o.Id;
DECLARE @orgs TABLE (OrgId bigint);
INSERT INTO @orgs VALUES (6),(127),(132),(192),(212),(230),(18059),(22291),(27247),(37977),(75123);
MERGE opportunities.CanonicalOrgEnrichment AS T USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider WHEN NOT MATCHED THEN INSERT (CanonicalOrgId,ProviderName,Status,Attempts,CreatedAtUtc,UpdatedAtUtc) VALUES (S.OrgId,@Provider,N'Manual',0,sysdatetimeoffset(),sysdatetimeoffset());
DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));
INSERT INTO @people (OrgId,PersonName,Title,Email,Src,Conf,Note) VALUES
(230, N'Daisen Gee-Wing', N'Principal', N'daisen@bdkdevcorp.com', N'Deltek', 90, N'KOR client contact (Deltek). Also bdk@plazaofnations.com; tel 604-682-0777 x240.'),
(192, N'Alexander Ray', N'', N'alex@frame.properties', N'Deltek', 90, N'KOR client contact (Deltek), Frame Properties.'),
(192, N'Hassan Sayed', N'', N'Hassan@frame.properties', N'Deltek', 90, N'KOR client contact (Deltek), Frame Properties. Tel 604-710-3627.'),
(132, N'Jashin Jhand', N'', N'jashin@townside.ca', N'Deltek', 90, N'KOR client contact (Deltek), Townside Developments. Also jashinjhand@gmail.com.'),
(6, N'Leo Jorduela', N'', N'LJorduela@turngroup.ca', N'Deltek', 90, N'KOR client contact (Deltek). Tel 587-328-6303.'),
(18059, N'Chris Warren', N'Principal', N'chris@warrenconstruction.ca', N'Deltek', 90, N'KOR client contact (Deltek). Tel 403-899-1661.'),
(37977, N'Jeff Owen', N'President', N'jeff@jfoconstruction.ca', N'Deltek', 95, N'KOR client contact (Deltek). Tel 778-926-4191.'),
(22291, N'R. Pabla', N'Principal', N'r.pabla@hotmail.com', N'Deltek', 50, N'KOR client AP/principal contact (Deltek). Pabla family. Also jspabla62@hotmail.com.'),
(127, N'Adam Overing', N'', N'ao@coastdevelop.com', N'Deltek', 80, N'KOR client contact (Deltek). Tel 929-895-1776.'),
(75123, N'Awtar (Aman) Madan', N'Contact via DF Architecture', N'aman@dfarchitecture.ca', N'Deltek', 55, N'KOR contact (Deltek) - project architect at DF Architecture; may not be Nexus staff.'),
(212, N'Jordan Mills', N'', N'tr3jordy@gmail.com', N'Deltek', 55, N'KOR client contact (Deltek), Yushi Investments. Tel 250-216-4519 (Vancouver Island).'),
(27247, N'Dorna Ghorashi', N'', N'DGhorashi@quadrangle.ca', N'Deltek', 85, N'KOR contact (Deltek), Quadrangle Toronto office. Tel 416-598-1240.');
;WITH src AS (SELECT p.OrgId,p.PersonName,p.Title,p.Email,p.Src,p.Conf,p.Note,LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPerson AS T USING src AS S ON T.NaturalKey=S.NK WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(),Corroborations=T.Corroborations+1,UpdatedAtUtc=sysdatetimeoffset(),Email=COALESCE(T.Email,S.Email),EmailSource=COALESCE(T.EmailSource,S.Src),EmailConfidence=COALESCE(T.EmailConfidence,S.Conf),Notes=COALESCE(T.Notes,S.Note)
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,DisplayName,NormalizedName,Corroborations,Email,EmailSource,EmailConfidence,EmailCheckedAtUtc,Notes) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonName,S.Lowered,1,S.Email,S.Src,S.Conf,CASE WHEN S.Email IS NULL THEN NULL ELSE sysdatetimeoffset() END,S.Note);
;WITH aff AS (SELECT ip.Id AS PersonId,p.OrgId,p.Title,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2) JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPersonAffiliation AS T USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title),IsCurrent=1,LastSeenAtUtc=sysdatetimeoffset(),UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,IntelPersonId,CanonicalOrgId,Title,IsCurrent) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonId,S.OrgId,S.Title,1);
PRINT 'Migration 248: thin-client enrichment (12 contacts / 11 org-meta).';
COMMIT TRAN;
GO

