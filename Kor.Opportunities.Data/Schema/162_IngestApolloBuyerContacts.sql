USE [KorOpportunitiesDb];
GO

/* =====================================================================
   162 — Ingest Apollo-sourced buyer-side decision-makers into the
   IntelPerson / IntelPersonAffiliation graph.
   ---------------------------------------------------------------------
   WHY: the 2026-06-16 Apollo passes surfaced the owner-side capital /
   facilities decision-makers behind the open SE seats (health authorities,
   school districts, post-secondary, BC Housing, PCL preconstruction).
   They lived only in report text + temp CSVs, so vOrgWarmPath / future
   reports / the AI layer were blind to them. This persists them properly.

   CONTRACT (mirrors IntelPersistenceService exactly so the app's next
   enrichment MERGEs onto these rows instead of duplicating them):
     - Person NaturalKey = SHA1_HEX_UPPER( normalize(DisplayName) )
     - Affiliation NaturalKey = SHA1_HEX_UPPER( personId|orgId|normalize(Title) )
     - normalize() = lower + strip  space . , ' - & / ( ) +
     - HASHBYTES('SHA1', CAST(.. AS VARCHAR)) == C# Convert.ToHexString(UTF8)
       for ASCII input (proven: 'sandratschauner' -> 914277F4...8ED8B4).
     - Email is set with COALESCE(T.Email, incoming) — NEVER overwrites an
       existing contact email (the email-wipe class of bug cannot recur).
     - 2 Apollo-truncated junk names dropped at source ("Farah N","Harry Cfm").
   Idempotency: persons/affiliations MERGE on their unique NaturalKey.
   ===================================================================== */

SET NOCOUNT ON;
DECLARE @provider NVARCHAR(120) = N'ApolloBuyerSide-2026-06-16';
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

DECLARE @c TABLE (
  OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200),
  NormName NVARCHAR(200), NormTitle NVARCHAR(200), PKey CHAR(40), AffKey CHAR(40), PersonId BIGINT);

INSERT INTO @c (OrgId, Person, Title, Email) VALUES
 (18917,N'John Hood',N'Manager, Capital Projects',N'john.hood@vch.ca'),
 (18917,N'Richard Gage',N'Executive Director & Chief Project Officer, Capital Planning and Projects',N'richard.gage@vch.ca'),
 (880,N'Janet Gaspar',N'Manager, Capital Projects',N'janet.gaspar@fraserhealth.ca'),
 (880,N'Betina Albornoz',N'Chief Project Officer & Executive Director, Major Capital Projects',N'betina.albornoz@fraserhealth.ca'),
 (880,N'Grace Liang',N'Project Manager, Capital Projects',N'grace.liang@fraserhealth.ca'),
 (72211,N'Juan Martinez',N'Senior Director, Major Capital Projects',N'juan.martinez@phsa.ca'),
 (72211,N'Gina Pisoni',N'Senior Director, Delivery Solutions, Capital Projects',N'gina.pisoni@phsa.ca'),
 (76162,N'Arthur Mak',N'Project Manager, Capital',N'amak@vsb.bc.ca'),
 (68851,N'Kris Wilkins',N'Director, Facilities Services',N'kwilkins@sd38.bc.ca'),
 (54964,N'Dave Riley',N'Director, Capital Projects Office',N'riley_d@surreyschools.ca'),
 (54964,N'Beatrice Tincu',N'Finance Manager, Capital Projects',N'tincu_b@surreyschools.ca'),
 (459,N'Ricky Biring',N'Associate Director, Facilities',N'ricky.biring@ubc.ca'),
 (459,N'Natalie Walliser',N'Director, Facilities Planning',N'natalie.walliser@ubc.ca'),
 (459,N'Denise Brown',N'Director, Capital Planning & Development',N'denise.brown@ubc.ca'),
 (771,N'Shelley Rid',N'Associate Director, Facilities & Capital Planning',N'shelley_rid@sfu.ca'),
 (771,N'Mike Devolin',N'Associate Director, Facilities',N'mike_devolin@sfu.ca'),
 (771,N'Sam Dahabieh',N'Director, Facilities Services',N'dahabieh@sfu.ca'),
 (38939,N'Rani Hayden',N'Project Manager',N'rhayden@bchousing.org'),
 (38939,N'Aidan McGrath',N'Project Manager',N'amcgrath@bchousing.org'),
 (54979,N'Dean Anderson',N'Executive Director, Facilities Management',N'dean.anderson@islandhealth.ca'),
 (54979,N'Ron Bouveur',N'Director, Facilities Design and Construction',N'ronald.bouveur@islandhealth.ca'),
 (54979,N'Dave Patten',N'Director, Facilities Design and Construction',N'david.patten@islandhealth.ca'),
 (13243,N'Jon Keaney',N'Director of Preconstruction',N'jkeaney@pcl.com'),
 (13243,N'Ray Mollett',N'Director of Preconstruction',N'rmollett@pcl.com'),
 (69036,N'Stephen Monahan',N'Manager of Major Capital Projects',N'smonahan@sd61.bc.ca'),
 (68990,N'Mhairi Bennett',N'Director of Facilities',N'mbennett@sd62.bc.ca'),
 (70308,N'Rob Lumb',N'Director of Facilities',N'rlumb@saanichschools.ca');

/* normalize name + title (same char-strip set as CanonicalOrgResolver.NormalizeName) */
UPDATE @c SET
  NormName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(Person))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+',''),
  NormTitle = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','');

UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(NormName AS VARCHAR(8000))), 2);

/* one enrichment parent row per distinct org (SourceEnrichmentId FK target) */
DECLARE @enr TABLE (EnrId BIGINT, OrgId BIGINT);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @c) AS S
  ON 1 = 0
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now)
OUTPUT inserted.Id, inserted.CanonicalOrgId INTO @enr (EnrId, OrgId);

/* upsert persons on NaturalKey; never clobber an existing email */
MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT c.PKey, MIN(c.Person) AS Person, MIN(c.NormName) AS NormName,
              MIN(c.Email) AS Email, MIN(e.EnrId) AS EnrId
       FROM @c c JOIN @enr e ON e.OrgId = c.OrgId
       GROUP BY c.PKey) AS S
   ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET
   Email = COALESCE(T.Email, S.Email),
   EmailSource = COALESCE(T.EmailSource, CASE WHEN T.Email IS NULL THEN N'asis' ELSE T.EmailSource END),
   EmailConfidence = CASE WHEN T.Email IS NULL THEN 70 ELSE T.EmailConfidence END,
   LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
    FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName, Email, EmailSource, EmailConfidence)
   VALUES
   (@provider, S.EnrId, N'High', S.PKey,
    @now, @now, S.Person, S.NormName, S.Email, N'asis', 70);

/* resolve person ids + affiliation NaturalKeys */
UPDATE c SET c.PersonId = p.Id
FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;

UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1',
   CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

/* upsert affiliations on NaturalKey */
MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, c.Title, e.EnrId
       FROM @c c JOIN @enr e ON e.OrgId = c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
    FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES
   (@provider, S.EnrId, N'High', S.AffKey,
    @now, @now, S.PersonId, S.OrgId, S.Title, 1);
GO
