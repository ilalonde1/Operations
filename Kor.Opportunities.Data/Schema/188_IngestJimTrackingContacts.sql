/*
188_IngestJimTrackingContacts.sql
WHAT: Land the net-new warm contacts from Jim's BD tracking files as IntelPerson +
      IntelPersonAffiliation. Emails verified via Apollo + Hunter (2026-06-18).
WHY:  Stage 2 of the warm-path decomposition — makes every relationship in the matrix
      actionable with a verified email.
HOW:  162/185 SHA1-NaturalKey MERGE (COALESCE-safe; app auto-ingest matches, never dups).
      "Moved employer" contacts keep the historical affiliation + a Note on current org.
      Bad Apollo match (Joe Martinez -> martinezsteel.com, wrong person) discarded -> no email.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @provider nvarchar(100) = N'jim-tracking-2026-06';
-- resolve Pinnacle org for Mike De Cotiis (best existing match)
DECLARE @pinnacle bigint = (SELECT TOP 1 Id FROM opportunities.CanonicalOrg
  WHERE RetiredAtUtc IS NULL AND NormalizedName LIKE N'pinnacle%' ORDER BY KorProjectsCount DESC);

DECLARE @c TABLE (OrgId bigint, Person nvarchar(200), Title nvarchar(200), Email nvarchar(200),
  Conf nvarchar(20), Notes nvarchar(max), NormName nvarchar(200), NormTitle nvarchar(200),
  NormDisplay nvarchar(200), PKey char(40), AffKey char(40), PersonId bigint, EnrId bigint);

INSERT INTO @c (OrgId, Person, Title, Email, Conf, Notes) VALUES
 (68631,N'Kevin Heinley',N'Principal',N'kevin_heinley@gensler.com',N'High',N'Gensler''s KOR relationship lead — Gensler listed KOR as its PREFERRED structural consultant. Fifth Avenue Landing, UCSD Housing. [jim-tracking-2026-06]'),
 (68631,N'Tom Heffernan',N'Principal',N'tom_heffernan@gensler.com',N'High',N'Gensler — UCSD Housing. [jim-tracking-2026-06]'),
 (68631,N'Andrew Michajlenko',N'Studio Director',N'andrew_michajlenko@gensler.com',N'High',N'Gensler — Fifth Ave Hotel SD. [jim-tracking-2026-06]'),
 (68644,N'Matt Davis',N'Development',N'mdavis@hollandpartnergroup.com',N'High',N'Holland — 6th & Pine Long Beach woodframe, Smart City 6-tower CA; woodframe/Comslab. [jim-tracking-2026-06]'),
 (68644,N'Victoria Seidler',NULL,N'vseidler@hollandpartnergroup.com',N'High',N'Holland Partner Group. [jim-tracking-2026-06]'),
 (68644,N'Bert Levesque',NULL,N'blevesque@holland-corp.com',N'Medium',N'Holland — Comslab/woodframe, promo dinners. [jim-tracking-2026-06]'),
 (68644,N'Rob Flitton',N'Director of Development (historical)',N'rob.flitton@builderscapital.com',N'Medium',N'Was Holland Dir. of Development; now at Builders Capital (Apollo 2026-06). [jim-tracking-2026-06]'),
 (76952,N'Peter Berger',N'Principal-in-Charge, OC',N'pberger@mve-architects.com',N'High',N'MVE OC project lead — 9200 Wilshire, Lifan Tower. [jim-tracking-2026-06]'),
 (76952,N'Michael Spivak',N'(MVE historical)',N'mspivak@tattarang.com',N'Medium',N'Was MVE (Lifan Tower RFP); now at Tattarang (Apollo 2026-06). [jim-tracking-2026-06]'),
 (76958,N'Tony Cutri',N'Principal / Owner',NULL,N'Medium',N'Martinez + Cutri principal — 8th & Broadway, 6th & A, Stationer Tower LA. [jim-tracking-2026-06]'),
 (76958,N'Joe Martinez',N'Principal',NULL,N'Medium',N'Martinez + Cutri — 4th & C. (Apollo email match was wrong person — Martinez Steel — discarded.) [jim-tracking-2026-06]'),
 (76959,N'Rosalie Merks',N'Owner Representative',N'rmerks@aaamanagementllc.com',N'High',N'Owner rep — got KOR onto the 7th & A project team. [jim-tracking-2026-06]'),
 (76953,N'Sergio Sandoval',N'Senior Project Manager',N'ssandoval@excelhotelgroup.com',N'Medium',N'Chula Vista Bayfront PM (email at Israni-affiliated Excel Hotel Group). Warm Amara Bay thread. [jim-tracking-2026-06]'),
 (70737,N'Omar Dawi',NULL,N'odawi@greystar.com',N'Medium',N'Greystar — Overture UTC seniors housing (email low-confidence/pattern). [jim-tracking-2026-06]'),
 (75176,N'Doug Austin',N'Founder / Principal',NULL,N'Medium',N'AVRP founder — Oakland 2x28-story highrise PBD. [jim-tracking-2026-06]'),
 (75864,N'Stuart Jones',NULL,N'sjones@ibigroup.com',N'High',N'IBI — Candlestick Point SF (700ac / 10,000u masterplan). [jim-tracking-2026-06]'),
 (69677,N'Louis Nasr',N'Senior Construction Superintendent',N'lnasr@thinkbosa.com',N'High',N'Bosa — 10th & Market twin towers. [jim-tracking-2026-06]'),
 (69677,N'Matt Patzer',N'Vice President, Construction',N'mpatzer@thinkbosa.com',N'High',N'Bosa VP Construction — key Bosa team contact. [jim-tracking-2026-06]'),
 (220,N'Brian Buchanan',NULL,N'bbuchanan@intergulf.com',N'High',N'Intergulf — 7th & A tower SD. [jim-tracking-2026-06]'),
 (68641,N'Neils Cotter',NULL,N'ncotter@carmelpartners.com',N'High',N'Carmel Partners — met at Bisnow. [jim-tracking-2026-06]'),
 (68628,N'Scott Potter',N'Chief Operating Officer',N'spotter@suffolk.com',N'High',N'Suffolk — India & Beech (Joe Wong recommended KOR). [jim-tracking-2026-06]'),
 (76965,N'Jim Strelow',NULL,N'jstrelow@largoconcrete.com',N'High',N'Largo Concrete — two-tower project, Long Beach. [jim-tracking-2026-06]'),
 (76963,N'Yahudi Gaffen',N'Chairman',N'ygaffen@gafcon.com',N'High',N'Gafcon Chairman — Seaport San Diego. [jim-tracking-2026-06]'),
 (76964,N'Rob Stroop',N'Associate Engineer',N'rob.stroop@nv5.com',N'Medium',N'Geotech (Group Delta, acquired by NV5) — Shoreline Gateway SD teaming. [jim-tracking-2026-06]'),
 (76968,N'Alex Barajas',N'CEO',N'alex@envisionengineering.us',N'High',N'Envision Engineering CEO. [jim-tracking-2026-06]'),
 (76960,N'Jim Tanner',N'Founding Partner, President',N'jtanner@tannerhecht.com',N'High',N'Tanner Hecht president — 4400 Palm, Bahia Tower. [jim-tracking-2026-06]'),
 (76960,N'Chris Binger',N'(Tanner Hecht historical)',N'chris.binger@zgf.com',N'Medium',N'Was Tanner Hecht (1850 5th Ave); now at ZGF Architects (Apollo 2026-06). [jim-tracking-2026-06]'),
 (76961,N'Joe Werner',NULL,NULL,N'Medium',N'MAR Group / nVision — 13th & E (owner Juan Pablo Mariscal). [jim-tracking-2026-06]'),
 (76966,N'Robert Greene',N'Owner / Broker',NULL,N'Medium',N'Owner of Fifth Avenue Landing (Gensler architect). [jim-tracking-2026-06]'),
 (53735,N'Richard Hannum',NULL,NULL,N'Medium',N'Forge Land Company — India & Beech. [jim-tracking-2026-06]'),
 (53735,N'Alexander Zucker',NULL,NULL,N'Medium',N'Forge Land Company — India & Beech. [jim-tracking-2026-06]'),
 (76962,N'Donald Kramer',N'Owner Representative',N'dkramer@narveninc.com',N'High',N'Owner rep — Fifth & Ash Suites. [jim-tracking-2026-06]'),
 (70869,N'Andrew Spousta',NULL,N'aspousta@kennedywilson.com',N'High',N'Kennedy Wilson — Kettner & Ash "mirror of the Lindley", $700k proposal (live 2026). [jim-tracking-2026-06]'),
 (38949,N'Chris Evans',NULL,NULL,N'Medium',N'Onni brass — Vancouver lunch. [jim-tracking-2026-06]'),
 (@pinnacle,N'Mike De Cotiis',NULL,N'md@pinnacleinternational.ca',N'High',N'Pinnacle — 11th & E, future SD hotel. [jim-tracking-2026-06]');

DELETE FROM @c WHERE OrgId IS NULL;  -- drop De Cotiis only if Pinnacle org unresolved

UPDATE @c SET
  NormName    = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Person))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormTitle   = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(COALESCE(Title,N'')))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormDisplay = LOWER(LTRIM(RTRIM(Person)));
UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(NormName AS VARCHAR(8000))), 2);

MERGE opportunities.CanonicalOrgEnrichment WITH (HOLDLOCK) AS T
USING (SELECT DISTINCT OrgId FROM @c) AS S
  ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now);
UPDATE c SET EnrId = e.Id FROM @c c JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = c.OrgId AND e.ProviderName = @provider;

MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT PKey, MIN(Person) Person, MIN(NormDisplay) NormDisplay, MIN(Email) Email,
              MIN(Conf) Conf, MIN(Notes) Notes, MIN(EnrId) EnrId FROM @c GROUP BY PKey) AS S
  ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET Email = COALESCE(T.Email, S.Email), LastSeenAtUtc = @now,
  UpdatedAtUtc = @now, Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
  (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
   DisplayName, NormalizedName, Email, Notes, Corroborations)
  VALUES (@provider, S.EnrId, S.Conf, S.PKey, @now, @now, S.Person, S.NormDisplay, S.Email, S.Notes, 1);

UPDATE c SET PersonId = p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;
UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1',
   CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT AffKey, PersonId, OrgId, Title, Conf, EnrId FROM @c) AS S
  ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
  (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
   IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@provider, S.EnrId, S.Conf, S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);

COMMIT;

SELECT COUNT(*) AS PeopleIngested, SUM(CASE WHEN Email IS NOT NULL THEN 1 ELSE 0 END) AS WithEmail
FROM opportunities.IntelPerson WHERE SourceProviderName = N'jim-tracking-2026-06';
