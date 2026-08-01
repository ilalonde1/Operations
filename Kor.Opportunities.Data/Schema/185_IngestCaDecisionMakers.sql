/*
185_IngestCaDecisionMakers.sql
WHAT: Land the 8 verified CA decision-makers as IntelPerson + IntelPersonAffiliation.
WHY:  Stage 3 of decomposing the CA synthesis — the contact last-mile that makes the
      seats actionable. Emails verified via Hunter 2026-06-18 (7/8); Ryan Bosa email
      pending Apollo (no Hunter data).
HOW:  Follows the proven 162_IngestApolloBuyerContacts pattern. NaturalKey = SHA1 hex
      (upper) of the stripped-normalized name (matches IntelNaturalKey.Compute), so the
      app's IntelPersistenceService.MergePersonAsync will MATCH, never duplicate.
      SourceEnrichmentId requires a CanonicalOrgEnrichment parent (created per org below).
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @provider nvarchar(100) = N'ca-research-2026-06';

DECLARE @c TABLE (
  OrgId bigint, Person nvarchar(200), Title nvarchar(200), Email nvarchar(200),
  Linkedin nvarchar(500), Conf nvarchar(20), Notes nvarchar(max),
  NormName nvarchar(200), NormTitle nvarchar(200), NormDisplay nvarchar(200),
  PKey char(40), AffKey char(40), PersonId bigint, EnrId bigint);

INSERT INTO @c (OrgId, Person, Title, Email, Linkedin, Conf, Notes) VALUES
 (68644, N'John Wayland', N'Executive Managing Director, Development - Northern California', N'jwayland@hollandpartnergroup.com', NULL, N'High', N'#1 NorCal decision-maker: picks the SE on Holland''s 3896 Stevens Creek (San Jose). Email via Hunter (score 80, accept_all). [ca-research-2026-06]'),
 (70869, N'Michael Eadie', N'Head of US Development & Construction', N'meadie@kennedywilson.com', NULL, N'High', N'KW dev/SE selector for the Lindley/KW conversion play. Hunter 95 valid. [ca-research-2026-06]'),
 (70869, N'John McCullough', N'President, Multifamily Development', N'jmccullough@kennedywilson.com', NULL, N'High', N'Ex-Toll; runs KW Multifamily Development; flagged SF as a target. Hunter 97 valid. [ca-research-2026-06]'),
 (75176, N'Pablo Collin', N'Associate Principal', N'pcollin@avrpstudios.com', NULL, N'High', N'Amara Bay architect-side co-vector (AVRP); promoted Sept 2025. Hunter 96 valid. [ca-research-2026-06]'),
 (76952, N'Matthew McLarand', N'President & Director of Design', N'mmclarand@mve-architects.com', NULL, N'High', N'Jim DesRoches'' personal contact — the warm channel into MVE''s OC (OC Vibe, Ritz-Carlton) + NorCal (Irvine Co. 2518 Mission College) seats. Hunter 96 valid. [ca-research-2026-06]'),
 (76952, N'Chase Ronge', N'Principal & Director, San Diego', N'cronge@mve-architects.com', NULL, N'High', N'MVE San Diego office lead (655 Broadway). SD entry for MVE work. Hunter 96 valid. [ca-research-2026-06]'),
 (20, N'Craig Howard', N'Principal (Sacramento)', N'craig@dbrds.com', NULL, N'High', N'DBRDS Sacramento — KOR''s only live NorCal architect relationship (verify-first; thin pipeline). Hunter 83 valid. [ca-research-2026-06]'),
 (69677, N'Ryan Bosa', N'President', NULL, NULL, N'Medium', N'Bosa picks the SE directly. Lead with KOR''s 20yr Bosa co-incumbency (Evolution, Halifax & Willingdon). Email PENDING Apollo (no Hunter data on thinkbosa.com). [ca-research-2026-06]');

-- Normalize: stripped form for keys (lower + strip space . , ' - & / ( ) +); display form keeps spaces
UPDATE @c SET
  NormName    = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Person))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormTitle   = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(COALESCE(Title,N'')))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N''),
  NormDisplay = LOWER(LTRIM(RTRIM(Person)));
UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(NormName AS VARCHAR(8000))), 2);

-- Stage: one CanonicalOrgEnrichment parent per distinct org (UX on CanonicalOrgId+ProviderName)
MERGE opportunities.CanonicalOrgEnrichment WITH (HOLDLOCK) AS T
USING (SELECT DISTINCT OrgId FROM @c) AS S
  ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @provider
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now);
UPDATE c SET EnrId = e.Id
  FROM @c c JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = c.OrgId AND e.ProviderName = @provider;

-- IntelPerson MERGE on NaturalKey (COALESCE email — never null out an existing contact)
MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT PKey, MIN(Person) Person, MIN(NormDisplay) NormDisplay, MIN(Email) Email,
              MIN(Linkedin) Linkedin, MIN(Conf) Conf, MIN(Notes) Notes, MIN(EnrId) EnrId
       FROM @c GROUP BY PKey) AS S
  ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET
  Email = COALESCE(T.Email, S.Email), LinkedinUrl = COALESCE(T.LinkedinUrl, S.Linkedin),
  LastSeenAtUtc = @now, UpdatedAtUtc = @now, Corroborations = T.Corroborations + 1
WHEN NOT MATCHED THEN INSERT
  (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
   DisplayName, NormalizedName, Email, LinkedinUrl, Notes, Corroborations)
  VALUES (@provider, S.EnrId, S.Conf, S.PKey, @now, @now, S.Person, S.NormDisplay, S.Email, S.Linkedin, S.Notes, 1);

UPDATE c SET PersonId = p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;
UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1',
   CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

-- IntelPersonAffiliation MERGE on NaturalKey
MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT AffKey, PersonId, OrgId, Title, Conf, EnrId FROM @c) AS S
  ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
  (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
   IntelPersonId, CanonicalOrgId, Title, IsCurrent)
  VALUES (@provider, S.EnrId, S.Conf, S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);

COMMIT;

SELECT p.Id, p.DisplayName, p.Email, p.SourceConfidence AS Conf, a.Title, a.CanonicalOrgId AS OrgId
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id
WHERE p.SourceProviderName = N'ca-research-2026-06'
ORDER BY p.DisplayName;
