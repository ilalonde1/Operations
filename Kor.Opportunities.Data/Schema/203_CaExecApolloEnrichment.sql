USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 203: Apollo enrichment for the developer decision-makers ingested in
  migration 202. 10 org-verified, email_status=verified matches. Email COALESCEd
  (never clobber); affiliation Title overwritten with Apollo's (more current than
  the research title). Keyed by NaturalKey = SHA1(full-stripped name).
*/

BEGIN TRAN;

DECLARE @work TABLE (PersonName nvarchar(200), OrgId bigint, Email nvarchar(200), Title nvarchar(200), LinkedIn nvarchar(500));
INSERT INTO @work VALUES
 (N'Tom Warren',      68644, N'twarren@hollandpartnergroup.com',                N'President',                                                  NULL),
 (N'Greg Thomas',     68644, N'gthomas@hollandpartnergroup.com',                N'President, Holland Construction, Inc.',                      NULL),
 (N'Ann Silverberg',  68645, N'asilverberg@related.com',                        N'President & CEO, Related California and Northwest Affordable',NULL),
 (N'Phoebe Yee',      68645, N'pyee@related.com',                               N'Executive Vice President',                                  NULL),
 (N'Adam Mayer',      68641, N'amayer@carmelpartners.com',                      N'Vice President, Development',                                NULL),
 (N'Warner Thomas',   68855, N'warner.thomas@sutterhealth.org',                 N'President & CEO',                                            NULL),
 (N'Adrian Foley',    53589, N'adrian.foley@brookfieldrp.com',                  N'President & CEO',                                            NULL),
 (N'Josh Roden',      53589, N'josh.roden@brookfieldpropertiesdevelopment.com', N'President, NorCal Land & Housing',                          NULL),
 (N'Nicole Burdette', 53589, N'nicole.burdette@brookfieldrp.com',               N'Regional President, US Land (CA & AZ)',                      NULL),
 (N'Bruce Menin',     68642, N'bam@crescentheights.com',                        N'Principal',                                                 NULL);

;WITH w AS (
  SELECT *,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      AS VARCHAR(8000))),2) AS NK
  FROM @work
)
UPDATE p
SET Email           = COALESCE(p.Email, w.Email),
    EmailSource     = COALESCE(p.EmailSource, N'asis'),
    EmailConfidence = COALESCE(p.EmailConfidence, 85),
    EmailCheckedAtUtc = sysdatetimeoffset(),
    LinkedinUrl     = COALESCE(p.LinkedinUrl, w.LinkedIn),
    LastSeenAtUtc   = sysdatetimeoffset(),
    UpdatedAtUtc    = sysdatetimeoffset()
FROM opportunities.IntelPerson p JOIN w ON w.NK = p.NaturalKey;
PRINT CONCAT('Execs email-enriched: ', @@ROWCOUNT);

;WITH w AS (
  SELECT *,
    CONVERT(CHAR(40),HASHBYTES('SHA1',CAST(
      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LOWER(LTRIM(RTRIM(PersonName))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','')
      AS VARCHAR(8000))),2) AS NK
  FROM @work
)
UPDATE a
SET Title = w.Title, LastSeenAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
JOIN w ON w.NK = p.NaturalKey AND a.CanonicalOrgId = w.OrgId;
PRINT CONCAT('Exec affiliation titles updated: ', @@ROWCOUNT);

COMMIT TRAN;
GO
