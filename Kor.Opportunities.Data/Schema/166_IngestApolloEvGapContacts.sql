USE [KorOpportunitiesDb];
GO

/* =====================================================================
   166 — Ingest Apollo contacts filling coverage gaps on the top EV
   target list (building-sector pursuits that had 0 emailable contacts).
   Same contract as migration 162 (see [[reference_intelperson_ingest_contract]]):
   SHA1 NaturalKeys via HASHBYTES, upsert on unique key, COALESCE email.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @provider NVARCHAR(120) = N'ApolloEvGaps-2026-06-16';
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

DECLARE @c TABLE (OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200),
  NormName NVARCHAR(200), NormTitle NVARCHAR(200), PKey CHAR(40), AffKey CHAR(40), PersonId BIGINT);
INSERT INTO @c (OrgId, Person, Title, Email) VALUES
 (18859,N'Jenny Tough',N'General Manager, Engineering and Capital Projects',N'jenny.tough@mission.ca'),
 (18859,N'Tracy Kyle',N'Director of Engineering & Public Works',N'tkyle@mission.ca'),
 (38975,N'Jerry Foster',N'Principal',N'jerry.foster@zgf.com'),
 (38975,N'Ryan Velasco',N'Principal',N'ryan.velasco@zgf.com'),
 (74202,N'Dayle Krahn',N'Vice President, Property Maintenance & Development',N'dkrahn@baptisthousing.org');

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
USING (SELECT c.PKey, MIN(c.Person) Person, MIN(c.NormName) NormName, MIN(c.Email) Email, MIN(e.EnrId) EnrId
       FROM @c c JOIN @enr e ON e.OrgId=c.OrgId GROUP BY c.PKey) AS S
   ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET
   Email = COALESCE(T.Email, S.Email),
   EmailSource = COALESCE(T.EmailSource, CASE WHEN T.Email IS NULL THEN N'asis' ELSE T.EmailSource END),
   EmailConfidence = CASE WHEN T.Email IS NULL THEN 70 ELSE T.EmailConfidence END,
   LastSeenAtUtc=@now, UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName, Email, EmailSource, EmailConfidence)
   VALUES (@provider, S.EnrId, N'High', S.PKey, @now, @now, S.Person, S.NormName, S.Email, N'asis', 70);

UPDATE c SET c.PersonId = p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;
UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(CAST(PersonId AS VARCHAR(20))+'|'+CAST(OrgId AS VARCHAR(20))+'|'+NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, c.Title, e.EnrId FROM @c c JOIN @enr e ON e.OrgId=c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc=@now, UpdatedAtUtc=@now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES (@provider, S.EnrId, N'High', S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);
GO
