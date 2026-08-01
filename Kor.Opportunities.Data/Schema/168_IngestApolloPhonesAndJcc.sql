USE [KorOpportunitiesDb];
GO

/* =====================================================================
   168 — Apollo direct-dial phones for top decision-makers + 2 new JCC
   contacts. Phones captured via a transient webhook (Apollo delivers
   phone reveals asynchronously). Same IntelPerson contract as migration 162.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @provider NVARCHAR(120) = N'ApolloEvGaps-2026-06-16';
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* A) direct dials onto existing persons (match by email; never clobber) */
;WITH ph(Email,Phone) AS (
  SELECT * FROM (VALUES
   (N'betina.albornoz@fraserhealth.ca',N'+1 604-364-5730'),
   (N'riley_d@surreyschools.ca',N'+1 778-772-2287'),
   (N'denise.brown@ubc.ca',N'+1 604-638-3374'),
   (N'jenny.tough@mission.ca',N'+1 604-378-2948'),
   (N'jerry.foster@zgf.com',N'+1 202-257-2464'),
   (N'macci@parkin.ca',N'+1 416-420-1789'),
   (N'podegaard@mcmparchitects.com',N'+1 604-376-2181'),
   (N'richard.gage@vch.ca',N'+1 778-872-2087')) v(Email,Phone))
UPDATE p SET p.Phone = COALESCE(p.Phone, ph.Phone), p.UpdatedAtUtc = @now
FROM opportunities.IntelPerson p JOIN ph ON p.Email = ph.Email WHERE p.RetiredAtUtc IS NULL;

/* B) ingest 2 new JCC of Greater Vancouver contacts (org 74198) with email + phone */
DECLARE @c TABLE (OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200), Phone NVARCHAR(50),
  NormName NVARCHAR(200), NormTitle NVARCHAR(200), PKey CHAR(40), AffKey CHAR(40), PersonId BIGINT);
INSERT INTO @c (OrgId, Person, Title, Email, Phone) VALUES
 (74198,N'Betty Hum',N'Director of Development',N'betty@jccgv.bc.ca',N'+1 604-782-4317'),
 (74198,N'Eldad Goldfarb',N'Executive Director',N'eldad@jccgv.bc.ca',N'+1 604-617-0609');
UPDATE @c SET
  NormName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Person))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+',''),
  NormTitle = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','');
UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(NormName AS VARCHAR(8000))), 2);

DECLARE @enr TABLE (EnrId BIGINT, OrgId BIGINT);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @c) AS S ON 1=0
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now)
OUTPUT inserted.Id, inserted.CanonicalOrgId INTO @enr (EnrId, OrgId);

MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT c.PKey, MIN(c.Person) Person, MIN(c.NormName) NormName, MIN(c.Email) Email, MIN(c.Phone) Phone, MIN(e.EnrId) EnrId
       FROM @c c JOIN @enr e ON e.OrgId=c.OrgId GROUP BY c.PKey) AS S
   ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET Email=COALESCE(T.Email,S.Email), Phone=COALESCE(T.Phone,S.Phone),
   EmailSource=COALESCE(T.EmailSource, CASE WHEN T.Email IS NULL THEN N'asis' ELSE T.EmailSource END),
   EmailConfidence=CASE WHEN T.Email IS NULL THEN 70 ELSE T.EmailConfidence END, LastSeenAtUtc=@now, UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName, Email, Phone, EmailSource, EmailConfidence)
   VALUES (@provider, S.EnrId, N'High', S.PKey, @now, @now, S.Person, S.NormName, S.Email, S.Phone, N'asis', 70);

UPDATE c SET c.PersonId=p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey=c.PKey;
UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(CAST(PersonId AS VARCHAR(20))+'|'+CAST(OrgId AS VARCHAR(20))+'|'+NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, c.Title, e.EnrId FROM @c c JOIN @enr e ON e.OrgId=c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=@now, UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES (@provider, S.EnrId, N'High', S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);
GO
