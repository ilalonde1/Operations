USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 199: extract contacts that the CA ecosystem ingest (migrations
  190-192) concatenated into CanonicalOrg.DisplayName ("Firm  Person"), then
  clean each org name back to the firm only.

  - IntelPerson NaturalKey   = SHA1_HEX_UPPER( fullstrip(DisplayName) )
  - NormalizedName            = lower(trim(DisplayName))   (spaces kept)
  - Affiliation NaturalKey    = SHA1_HEX_UPPER( personId|orgId|'' )  (no title)
  - SourceEnrichmentId (NOT NULL) -> one CanonicalOrgEnrichment parent per org.
  - People are affiliated to the FINAL surviving org (AffOrgId): for the 6 orgs
    that have a clean twin, AffOrgId is the survivor (dedup will merge the loser
    away; the dedup tool does NOT repoint affiliations, so they must land on the
    survivor now). For the rest AffOrgId is the org itself (cleaned in place).

  Run as a dry-run first by setting @Commit = 0 (rolls back after diagnostics).
*/

DECLARE @Commit bit = 1;
DECLARE @Provider nvarchar(60) = N'CaEcosystemContactExtract';

BEGIN TRAN;

DECLARE @people TABLE (LoserOrgId bigint, CleanName nvarchar(200), PersonName nvarchar(200), AffOrgId bigint);
INSERT INTO @people (LoserOrgId, CleanName, PersonName, AffOrgId) VALUES
 -- contaminated orgs that have a clean twin -> affiliate to the survivor
 (77021, N'Holland Partner Group',                N'Kevin Willis',      68644),
 (77026, N'Devcon Construction, Inc.',            N'Anthony Bonasera',  77014),
 (76998, N'Level 10 Construction LP',             N'Tobias Yuen',       76997),
 (77024, N'David Baker Architects',               N'Tom Bliska',        76889),
 (77006, N'Pacific West Builders Inc',            N'Jarrod Short',      77019),
 (77018, N'Danco Builders Northwest',             N'Daniel Johnson',    77002),
 -- contaminated orgs with no twin -> clean in place, affiliate to self
 (77022, N'525 Capitol LP',                       N'Kyle Paine',        77022),
 (77008, N'Architects FORA',                      N'Kate Conley',       77008),
 (77015, N'Arup',                                 N'Nicole Olaes',      77015),
 (77023, N'Delmas Pro LLC',                       N'Terrence Chen',     77023),
 (77007, N'Green Valley Corp',                    N'James Shydlowski',  77007),
 (76993, N'Hanover RS Construction',              N'Arturo Ramirez',    76993),
 (77010, N'Holland Construction Inc',             N'Kevin Wills',       77010),
 (76994, N'Iron Construction, Inc.',              N'Matthew Edwards',   76994),
 (76991, N'J3C Group, LLC',                       N'Derric Allen',      76991),
 (76999, N'Jemcor Construction Partners, Inc.',   N'Jonathan Emami',    76999),
 (77009, N'JTM Construction Group Inc.',          N'Doug Consigny',     77009),
 (77025, N'Nibbi Bros. Associates, Inc.',         N'Robert Nibbi',      77025),
 (77017, N'Oarcon Inc',                           N'Vince O''Driscoll', 77017),
 (76992, N'Premier Design & Build Group LLC',     N'Matt Lindsay',      76992),
 (76995, N'Roem Builders Inc',                    N'Robert Emami',      76995),
 (77012, N'Swenson',                              N'Mark Pilarczyk',    77012),
 (77020, N'TMHCO LLC',                            N'Tyler Raineri',     77020),
 (76996, N'Truebeck Construction, Inc.',          N'Andrew Valdez',     76996),
 (77013, N'UC Madera Owner LLC',                  N'Erik Hayden',       77013),
 (77000, N'ARCO Murray',                          N'Colin Epperson',    77000),
 (77001, N'ARCO National Holdings, Inc.',         N'Chris Wilson',      77001);

-- 1) Clean the contaminated org names (NormalizedName is computed; recomputes).
UPDATE o
SET DisplayName = p.CleanName, UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg o
JOIN @people p ON p.LoserOrgId = o.Id
WHERE o.DisplayName <> p.CleanName;
PRINT CONCAT('Org names cleaned: ', @@ROWCOUNT);

-- 1b) Non-person "et al" dup of ARCO Murray National Norcal (cleaned for dedup).
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'ARCO Murray National Norcal, LLC', UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 77004 AND DisplayName <> N'ARCO Murray National Norcal, LLC';

-- 2) Ensure one enrichment parent per surviving org.
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT AffOrgId FROM @people) AS S
   ON T.CanonicalOrgId = S.AffOrgId AND T.ProviderName = @Provider
WHEN NOT MATCHED THEN
  INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.AffOrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

-- 3) Upsert people (NaturalKey = SHA1 of the full-stripped lowercase name).
;WITH src AS (
  SELECT
    p.PersonName,
    LOWER(LTRIM(RTRIM(p.PersonName))) AS Lowered,
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
      LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip,
    e.Id AS EnrichmentId
  FROM @people p
  CROSS APPLY (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = p.AffOrgId AND ProviderName = @Provider) e
)
MERGE opportunities.IntelPerson AS T
USING (SELECT DISTINCT PersonName, Lowered, Strip,
              CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(Strip AS VARCHAR(8000))), 2) AS NK,
              MIN(EnrichmentId) AS EnrichmentId
       FROM src GROUP BY PersonName, Lowered, Strip) AS S
   ON T.NaturalKey = S.NK
WHEN MATCHED THEN
  UPDATE SET LastSeenAtUtc = sysdatetimeoffset(), Corroborations = T.Corroborations + 1, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
          FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
          DisplayName, NormalizedName, Corroborations)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK,
          sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(),
          S.PersonName, S.Lowered, 1);
PRINT CONCAT('IntelPerson rows affected: ', @@ROWCOUNT);

-- 4) Upsert affiliations to the surviving org (Title unknown -> empty).
;WITH aff AS (
  SELECT
    ip.Id AS PersonId,
    p.AffOrgId AS OrgId,
    e.Id AS EnrichmentId
  FROM @people p
  CROSS APPLY (SELECT
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(p.PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','') AS Strip) s
  JOIN opportunities.IntelPerson ip ON ip.NaturalKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(s.Strip AS VARCHAR(8000))), 2)
  JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = p.AffOrgId AND e.ProviderName = @Provider
)
MERGE opportunities.IntelPersonAffiliation AS T
USING (SELECT DISTINCT PersonId, OrgId, EnrichmentId,
              CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
                CONCAT(CAST(PersonId AS varchar(20)), '|', CAST(OrgId AS varchar(20)), '|') AS VARCHAR(8000))), 2) AS NK
       FROM aff) AS S
   ON T.NaturalKey = S.NK
WHEN MATCHED THEN
  UPDATE SET LastSeenAtUtc = sysdatetimeoffset(), IsCurrent = 1, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
          FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
          IntelPersonId, CanonicalOrgId, IsCurrent)
  VALUES (@Provider, S.EnrichmentId, N'Medium', S.NK,
          sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset(),
          S.PersonId, S.OrgId, 1);
PRINT CONCAT('IntelPersonAffiliation rows affected: ', @@ROWCOUNT);

-- Diagnostics
SELECT p.AffOrgId, o.DisplayName AS Org, ip.DisplayName AS Person, ip.NaturalKey, a.IsCurrent
FROM @people p
JOIN opportunities.CanonicalOrg o ON o.Id = p.AffOrgId
JOIN opportunities.IntelPerson ip ON ip.NormalizedName = LOWER(LTRIM(RTRIM(p.PersonName)))
LEFT JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = ip.Id AND a.CanonicalOrgId = p.AffOrgId
ORDER BY o.DisplayName;

IF @Commit = 1
BEGIN
    COMMIT TRAN;
    PRINT 'Migration 199 COMMITTED.';
END
ELSE
BEGIN
    ROLLBACK TRAN;
    PRINT 'Migration 199 DRY-RUN rolled back.';
END
GO
