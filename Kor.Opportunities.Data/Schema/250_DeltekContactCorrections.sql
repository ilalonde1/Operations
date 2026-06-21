USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 250: senior-review corrections to migration 248 (Deltek-resolved contacts).
   (1) RBC Holdings (22291): "R. Pabla" (person 14142) was a name SYNTHESIZED from an
       email local-part on an unnamed "AP" Deltek contact - that is inference, not data.
       Retire the person + affiliation; preserve the billing email as an org note instead.
   (2) Nexus (75123): "Awtar (Aman) Madan" (person 14144) is DF Architecture staff
       (aman@dfarchitecture.ca) - the project architect, NOT Nexus staff. Re-point his
       affiliation to DF Architecture (24) where he actually works; keep a note on Nexus.
   Idempotent. */
BEGIN TRAN;

-- (1) Retire the synthesized RBC "R. Pabla" affiliation + person
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = COALESCE(RetiredAtUtc, sysdatetimeoffset()),
    RetiredReason = N'Synthesized name from email local-part (no real person in Deltek); demoted to org billing note (mig 250).',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 21083 AND RetiredAtUtc IS NULL;

UPDATE opportunities.IntelPerson
SET RetiredAtUtc = COALESCE(RetiredAtUtc, sysdatetimeoffset()),
    RetiredReason = N'Name synthesized from email (r.pabla@hotmail.com) on an unnamed AP contact - not a verified person (mig 250).',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 14142 AND RetiredAtUtc IS NULL;

-- preserve the billing fact on the org (no fabricated person)
UPDATE opportunities.CanonicalOrg
SET Notes = LTRIM(ISNULL(Notes, N'') + N' Billing/AP contact (Deltek): r.pabla@hotmail.com / jspabla62@hotmail.com (Pabla family principals; no named individual on file).'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 22291 AND (Notes IS NULL OR Notes NOT LIKE N'%r.pabla@hotmail.com%');

-- (2) Re-point Aman Madan from Nexus (75123) to his real firm DF Architecture (24)
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = COALESCE(RetiredAtUtc, sysdatetimeoffset()),
    RetiredReason = N'Mis-attributed: subject is DF Architecture staff (project architect), not Nexus staff. Re-pointed to org 24 (mig 250).',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 21085 AND RetiredAtUtc IS NULL;

-- ensure an enrichment row exists on DF Architecture (24) to anchor the corrected affiliation
DECLARE @Provider nvarchar(60) = N'DeltekCrmFix2026-06-20';
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT CAST(24 AS bigint) AS OrgId) AS S
  ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @Provider
WHEN NOT MATCHED THEN
  INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());
DECLARE @EnrId bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 24 AND ProviderName = @Provider);

-- NaturalKey matches the generator scheme: SHA1('<personId>|<orgId>|<normalized-title>')
DECLARE @nk CHAR(40) = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(CONCAT(N'14144', N'|', N'24', N'|', N'architect') AS VARCHAR(8000))), 2);
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation WHERE IntelPersonId = 14144 AND CanonicalOrgId = 24 AND RetiredAtUtc IS NULL)
BEGIN
  INSERT INTO opportunities.IntelPersonAffiliation
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent, Notes)
  VALUES
    (@Provider, @EnrId, N'High', @nk, sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), 14144, 24, N'Architect', 1,
     N'KOR project architect (Deltek); originally recorded against Nexus pursuit, corrected to DF Architecture (mig 250).');
END

-- note on Nexus that the project architect is DF Architecture
UPDATE opportunities.CanonicalOrg
SET Notes = LTRIM(ISNULL(Notes, N'') + N' Project architect on KOR pursuit: DF Architecture (Aman Madan, #24).'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 75123 AND (Notes IS NULL OR Notes NOT LIKE N'%#24%');

PRINT 'Migration 250: RBC Pabla de-fabricated; Aman Madan re-pointed Nexus -> DF Architecture (24).';
COMMIT TRAN;
GO
