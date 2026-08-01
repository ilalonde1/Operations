USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 238: PM thin-developer contacts. Generated from
   the docs/enrichment-2026-06-20-pm thin-developers JSON. 31 contacts / 8 orgs.
   Excludes Landmark Premiere (54551, receivership / DO NOT PURSUE). */
DECLARE @Provider nvarchar(60) = N'PmEnrich2026-06-20';
BEGIN TRAN;
DECLARE @orgs TABLE (OrgId bigint);
INSERT INTO @orgs VALUES (53693),(53694),(54002),(54043),(54484),(70564),(76905),(76907);
MERGE opportunities.CanonicalOrgEnrichment AS T USING (SELECT OrgId FROM @orgs) AS S ON T.CanonicalOrgId=S.OrgId AND T.ProviderName=@Provider WHEN NOT MATCHED THEN INSERT (CanonicalOrgId,ProviderName,Status,Attempts,CreatedAtUtc,UpdatedAtUtc) VALUES (S.OrgId,@Provider,N'Manual',0,sysdatetimeoffset(),sysdatetimeoffset());
DECLARE @people TABLE (OrgId bigint, PersonName nvarchar(200), Title nvarchar(200), Email nvarchar(200), Src nvarchar(20), Conf tinyint, Note nvarchar(400));
INSERT INTO @people (OrgId,PersonName,Title,Email,Src,Conf,Note) VALUES
(53694, N'Kirk Fisher', N'Chief Executive Officer', N'kfisher@larkgroup.com', N'PatternInferred', 55, N'CEO since Aug 2023, son of founder Larry Fisher. P.Eng. UBC, MBA SFU. Also co-founder of HealthTech Connex.'),
(53694, N'Jay Whisnand', N'Vice President of Construction', N'jwhisnand@larkgroup.com', N'Hunter', 83, N'VP of Construction. Hunter verified valid 2026-05-30. Primary construction contact.'),
(53694, N'Graydon Halley', N'Project Director', N'ghalley@larkgroup.com', N'Hunter', 93, N'Project Director, management/executive seniority.'),
(53694, N'William Shatford', N'Project Director', N'wshatford@larkgroup.com', N'Hunter', 81, N'Project Director. Hunter verified valid 2026-06-19.'),
(53694, N'Kennedy Bray', N'Development Project Manager', N'kbray@larkgroup.com', N'Hunter', 93, N'Development Project Manager.'),
(54043, N'Ralph Berezan', N'Chief Executive Officer', N'rberezan@berezan.ca', N'PatternInferred', 55, N'CEO and owner. Email pattern {f}{last}@berezan.ca inferred from Hunter pattern and ZoomInfo source (r***@berezan.ca partial match). Family-owned firm established 1971.'),
(54043, N'Alison Wallace', N'Regional Manager', N'awallace@berezan.ca', N'Hunter', 94, N'Regional Manager, BC Interior (Sahali Centre Mall, Kamloops). Phone: +1 403 508 5016.'),
(54043, N'Len Robinson', N'Regional Manager', N'lrobinson@berezan.ca', N'Hunter', 93, N'Regional Manager, Interior BC. Phone: +1 250 374 3033.'),
(54484, N'Babak Nikbakhtan', N'Managing Director', N'babak@lexigroup.com', N'Hunter', 99, N'Managing Director. Hunter shows email as invalid as of 2026-06-16 -- treat with caution. Key principal.'),
(54484, N'Baha Naemi', N'Principal / Co-Leader', N'baha@lexigroup.com', N'PatternInferred', 55, N'Co-leader per company website. Email pattern is {first}@lexigroup.com (Hunter accept_all domain). farid@lexigroup.com verified valid -- pattern consistent.'),
(54484, N'Behzad Beheshti', N'Principal / Co-Leader', N'behzad@lexigroup.com', N'PatternInferred', 55, N'Co-leader per company website. Also referenced as Behzad Foroutan in some sources. Pattern {first}@lexigroup.com.'),
(54484, N'Farid Ghasemi', N'Senior Project Manager / BIM Lead', N'farid@lexigroup.com', N'Hunter', 98, N'Senior Project Manager / BIM Lead (Design-Build). Hunter verified valid 2026-06-16.'),
(54002, N'Kenneth Mariash', N'Founder / Principal', NULL, NULL, NULL, N'Founder of Focus Equities / Bayview Properties. No email found in Hunter (focusequities.com returns 0 emails). Contact via focusequities.com website form.'),
(54002, N'Patricia Mariash', N'Partner / Principal', NULL, NULL, NULL, N'Partner, 18+ year involvement. No email found in Hunter.'),
(76905, N'Jesse Blout', N'Founding Partner', N'jblout@stradasf.com', N'PatternInferred', 55, N'Founding Partner. Email pattern {f}{last}@stradasf.com per Hunter. No direct entry for Blout in Hunter results.'),
(76905, N'Michael Cohen', N'Founding Partner', N'mcohen@stradasf.com', N'PatternInferred', 55, N'Founding Partner. Pattern inferred.'),
(76905, N'Steven Danforth', N'Senior Vice President', N'sdanforth@stradasf.com', N'Hunter', 98, N'Senior VP. Hunter verified valid 2026-06-01.'),
(76905, N'Nabil Nazir', N'Construction Manager', N'nnazir@stradasf.com', N'Hunter', 96, N'Construction Manager. Hunter verified valid 2026-06-13.'),
(76905, N'Jake Kalmanovitz', N'Senior Construction Manager', N'jkalmanovitz@stradasf.com', N'Hunter', 97, N'Senior Construction Manager. Hunter verified valid 2026-06-13.'),
(76905, N'William Goodman', N'Principal', N'wgoodman@stradasf.com', N'Hunter', 98, N'Principal. Hunter verified valid 2026-05-25. Phone: +1 314 276 0707.'),
(70564, N'Lisa Lock', N'Chief Executive Officer', N'llock@stobergroup.com', N'Hunter', 96, N'CEO since Nov 2023. Previously COO. Background at Concert Properties and Mission Group Enterprises. Hunter verified valid 2026-06-14.'),
(70564, N'JoAnne Adamson', N'Director of Development', N'jadamson@stobergroup.com', N'Hunter', 99, N'Director of Development. Hunter verified valid 2026-05-27. Key development decision-maker.'),
(70564, N'Carolyn Stober', N'Co-Owner / Director', N'cstober@stobergroup.com', N'PatternInferred', 55, N'Co-owner (second generation, with Ken Stober). Pattern {f}{last}@stobergroup.com inferred from Hunter.'),
(70564, N'Ken Stober', N'Co-Owner / Director', N'kstober@stobergroup.com', N'PatternInferred', 55, N'Co-owner (second generation family, with Carolyn Stober). Pattern inferred.'),
(70564, N'Josiah Siemens', N'Chief Financial Officer', N'jsiemens@stobergroup.com', N'Hunter', 98, N'CFO.'),
(70564, N'Joe Shaw', N'Project Manager', N'jshaw@stobergroup.com', N'Hunter', 95, N'Project Manager. Hunter verified valid 2026-05-25.'),
(53693, N'Satnam Shoker', N'President', NULL, NULL, NULL, N'President of Sunmark Developments. No personal email found in Hunter (only generic info@). Try info@sunmarkdevelopments.com for initial outreach.'),
(76907, N'Polo Munoz', N'Director of Housing Development', N'pmunoz@midpen-housing.org', N'Hunter', 94, N'Director of Housing Development. Hunter accept_all domain. Primary development decision-maker.'),
(76907, N'Nesreen Kawar', N'Director of Housing Development', N'nkawar@midpen-housing.org', N'PatternInferred', 55, N'Director of Housing Development per midpen-housing.org leadership page. Pattern {f}{last}@midpen-housing.org from Hunter.'),
(76907, N'Mollie Naber', N'Vice President, Housing Development Strategy', N'mnaber@midpen-housing.org', N'PatternInferred', 55, N'VP Housing Development Strategy per midpen-housing.org. Pattern inferred.'),
(76907, N'Lisa Howlett', N'Associate Director of Development', N'lhowlett@midpen-housing.org', N'PatternInferred', 55, N'Associate Director of Development per midpen-housing.org. Pattern inferred.');
;WITH src AS (SELECT p.OrgId,p.PersonName,p.Title,p.Email,p.Src,p.Conf,p.Note,LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPerson AS T USING src AS S ON T.NaturalKey=S.NK WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=sysdatetimeoffset(),Corroborations=T.Corroborations+1,UpdatedAtUtc=sysdatetimeoffset(),Email=COALESCE(T.Email,S.Email),EmailSource=COALESCE(T.EmailSource,S.Src),EmailConfidence=COALESCE(T.EmailConfidence,S.Conf),Notes=COALESCE(T.Notes,S.Note)
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,DisplayName,NormalizedName,Corroborations,Email,EmailSource,EmailConfidence,EmailCheckedAtUtc,Notes) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonName,S.Lowered,1,S.Email,S.Src,S.Conf,CASE WHEN S.Email IS NULL THEN NULL ELSE sysdatetimeoffset() END,S.Note);
;WITH aff AS (SELECT ip.Id AS PersonId,p.OrgId,p.Title,e.Id AS EnrId,CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(CONCAT(CAST(ip.Id AS varchar(20)),'|',CAST(p.OrgId AS varchar(20)),'|',REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')) AS VARCHAR(8000))),2) AS NK FROM @people p JOIN opportunities.IntelPerson ip ON ip.NaturalKey=CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS VARCHAR(8000))),2) JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId=p.OrgId AND e.ProviderName=@Provider)
MERGE opportunities.IntelPersonAffiliation AS T USING aff AS S ON T.IntelPersonId=S.PersonId AND T.CanonicalOrgId=S.OrgId WHEN MATCHED THEN UPDATE SET Title=COALESCE(T.Title,S.Title),IsCurrent=1,LastSeenAtUtc=sysdatetimeoffset(),UpdatedAtUtc=sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT (SourceProviderName,SourceEnrichmentId,SourceConfidence,NaturalKey,FirstSeenAtUtc,LastSeenAtUtc,CreatedAtUtc,UpdatedAtUtc,IntelPersonId,CanonicalOrgId,Title,IsCurrent) VALUES (@Provider,S.EnrId,N'Medium',S.NK,sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),sysdatetimeoffset(),S.PersonId,S.OrgId,S.Title,1);
PRINT 'Migration 238: PM thin-developer contacts ingested (31 / 8 orgs).';
COMMIT TRAN;
GO

