/*
187_CreateJimTrackingOrgs.sql
WHAT: Create the canonical orgs surfaced in Jim's BD tracking files (2018+2026) that are
      genuinely absent (coverage + fuzzy-recheck 2026-06-18).
WHY:  Stage 1 of decomposing the warm-path matrix. These orgs anchor the 38 net-new contacts
      (migration 188).
HOW:  Dup-safe IF NOT EXISTS on the computed NormalizedName literal (per 120/183 idiom).
      Reused (NOT created): Carrier Johnson+Culture(112), Gensler(68631), Carmel(68641),
      Suffolk Construction(68628), CDA Architects(76660), IBI Group Architects(75864),
      Forge Properties(53735). Skipped: Bailey Metals (steel-product vendor, not a BD target).
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'martinezcutri' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Architect', N'Martinez + Cutri', N'San Diego architect. Warm KOR contacts Tony Cutri + Joe Martinez (8th & Broadway, 6th & A, 4th & C, Stationer Tower LA). [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'aaamanagement' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'AAA Management', N'SD owner/development manager. Rosalie Merks = owner rep on 7th & A (got KOR onto the project team). [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'tannerhecht' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Tanner Hecht', N'SD developer. Contacts Jim Tanner + Chris Binger (4400 Palm, Bahia Tower, 1850 5th Ave). [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'margroup' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'MAR Group', N'SD developer/owner (owner Juan Pablo Mariscal). Joe Werner = KOR contact on 13th & E (240ft tower); also appears as "nVision Design Dev." [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'narven' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Narven', N'Owner-rep. Donald Kramer (Fifth & Ash Suites) — met in Joe Wong''s office; high confidence in KOR/GS. [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'gafcon' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'GC', N'Gafcon', N'SD program/construction management firm. Yahudi "Gaf" Gaffen (Seaport San Diego); Gafcon naming KOR as SE on Shoreline Gateway team. [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'groupdelta' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Vendor', N'Group Delta', N'Geotechnical engineering firm (allied consultant). Rob Stroop — teamed w/ KOR on Shoreline Gateway SD. [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'largo' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Largo', N'Developer/GC (Largo Construction). Jim Strelow — two-tower project + Long Beach work. [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'robertgreenecompany' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Robert Greene Company', N'LA/SD developer. Robert Greene = owner of Fifth Avenue Landing (Gensler architect). [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'shopoff' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Shopoff', N'Shopoff Realty Investments (also OC: Bolsa Pacific 2,250u). Comslab/woodframe CA residential interest. [jim-tracking-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'envisionengineering' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Competitor', N'Envision Engineering', N'Engineering firm (allied/competitor). Alex Barajas — met via Jim. [jim-tracking-2026-06]', @now, @now);

COMMIT;

SELECT Id, Kind, DisplayName FROM opportunities.CanonicalOrg
WHERE DisplayName IN (N'Martinez + Cutri',N'AAA Management',N'Tanner Hecht',N'MAR Group',N'Narven',N'Gafcon',N'Group Delta',N'Largo',N'Robert Greene Company',N'Shopoff',N'Envision Engineering')
ORDER BY Kind, DisplayName;
