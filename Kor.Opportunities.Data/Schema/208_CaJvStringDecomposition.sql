USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 208: decompose CA JV-string / junk org names (part 1 of 2).
  - Rename JV-strings to their lead entity where no clean canonical exists yet
    (the JV partner is noted; projects attribute to the operating lead).
  - Retire pure-junk placeholder "orgs" and null their MajorProjectsInventory
    canonical links (the project row + its free-text proponent are kept).
  Part 2 (dedup run) merges the JV-strings whose lead already exists as a
  canonical (Hines, Tishman Speyer, BRIDGE Housing Corp, Strada, SKK, DGS, SJSU,
  Steinberg Hart, Trammell Crow) — done by BdCanonicalDedup with allowlist.
*/

BEGIN TRAN;

-- Rename-in-place to the lead (no existing clean canonical for these leads).
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Abode Services', Kind=N'Developer', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=76927;  -- was "Abode Services / Community Development Partners" (nonprofit affordable JV; Abode = lead)
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Wexford Science + Technology', Kind=N'Developer', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=76871;  -- was "UC Davis / Wexford Science + Technology" (Aggie Square; Wexford = developer, UC Davis = owner)
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Lawrence Berkeley National Laboratory', Kind=N'Buyer', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=76934;  -- was "UC / Lawrence Berkeley National Laboratory"
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Jamison Properties', Kind=N'Developer', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=74231;  -- was "Hankey Investment Company / Jamison Properties" (LA JV; Jamison = operating developer)
UPDATE opportunities.CanonicalOrg SET DisplayName=N'San Francisco Mayor''s Office of Housing & Community Development', Kind=N'Buyer', UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=76929;  -- was "SFMOH / nonprofit developer"

-- Retire pure-junk placeholders + null their MPI canonical links (keep project rows + free-text proponent).
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId=NULL, UpdatedAtUtc=sysdatetimeoffset()
WHERE ProponentCanonicalOrgId IN (76940,76931,76937,76990,77005);
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId=NULL, UpdatedAtUtc=sysdatetimeoffset()
WHERE ArchitectCanonicalOrgId IN (76940,76931,76937,76990,77005);
UPDATE opportunities.CanonicalOrg SET RetiredAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id IN
 (76940,  -- Developer (unnamed per source)
  76931,  -- Nonprofit developer
  76937,  -- Multiple private owners / City of San Francisco mandate
  76990,  -- ANTHONY CORRALES AND MARICELDA CORRALES (individuals)
  77005); -- HILL/HO/PANG/NGUENPHUC/PANG  CHAD NGUYEN (surname list)

PRINT 'Migration 208: JV-strings renamed to leads; junk placeholders retired.';
COMMIT TRAN;
GO
