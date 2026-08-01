USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 227: clean two family-dedup survivors that kept a JV-ish DisplayName
  (they absorbed "X + Y" JV-string variants). Rename to the lead firm; the
  partners remain their own canonical orgs.
*/
BEGIN TRAN;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Saucier + Perrotte Architectes',
  Notes=COALESCE(Notes,N'Teamed with Stantec on linked projects.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=68818;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'MJMA Architects',
  Notes=COALESCE(Notes,N'Teamed with Group2 / Acton Ostry on linked projects.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=76157;
PRINT 'Migration 227: family-dedup survivor renames applied.';
COMMIT TRAN;
GO
