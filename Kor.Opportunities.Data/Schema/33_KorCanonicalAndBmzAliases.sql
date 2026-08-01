/*
    Migration 33 — Seed KOR Structural canonical entity + BMZ predecessor aliases.

    Kor.OpportunitiesDb migration 33.

    Seeds a single CanonicalOrg row for KOR Structural Ltd. (the firm formerly known as
    Bryson Markulin Zickmantel Structural Engineers — same BC General Partnership reg.
    FM0678571, BN 867868663, renamed ~2020-2021) and maps every verified BMZ + KOR public
    name variant to it via OrgAlias rows, so historical BMZ-era awards/news auto-link to KOR.

    Pattern: Round 14c UPDATE-then-INSERT with UPDLOCK + HOLDLOCK on the NormalizedName
    range (mirrors SqlCanonicalOrgStore.UpsertCanonicalOrgAsync). Defensive set-based
    NOT EXISTS guard on the OrgAlias seed (respects UX_OrgAlias_RawName_Source). Idempotent.

    NOTES FOR REVIEWER (Ian):
      * Kind = 'Firm' is used per the task spec. This value is NOT yet in the OrgKinds enum
        (Kor.Opportunities.Core/Models/CanonicalOrg.cs: KorClient|Vendor|Buyer|Architect|GC|
        Subcontractor|Unknown). There is no CHECK constraint on CanonicalOrg.Kind, so it is
        DB-valid — but consider adding 'Firm' to OrgKinds if you keep it.
      * DisplayName 'KOR Structural Ltd.' follows the task spec. The BC Registry entity is a
        General Partnership literally named "KOR STRUCTURAL" (no "Ltd."). 'Ltd.' here is the
        branding form. NormalizedName resolves to 'korstructuralltd' either way for matching.
      * Aliases deliberately EXCLUDE bare "KOR" (too generic → false-positive risk) and
        "KORSE" / "KOR S.E." / "KOR Structural Engineering" (searched, no public evidence).
      * NormalizedName below uses the exact 10-character REPLACE chain that defines the
        CanonicalOrg.NormalizedName computed column (migration 22), NOT the resolver's
        [^a-z0-9] regex — they agree for these names, but the column is what we must match.
*/

USE [KorOpportunitiesDb];
GO

SET XACT_ABORT ON;
GO

DECLARE @KorId        bigint;
DECLARE @DisplayName  nvarchar(300) = N'KOR Structural Ltd.';
DECLARE @Kind         nvarchar(40)  = N'Firm';
DECLARE @Notes        nvarchar(max) = N'KOR Structural Ltd. — Vancouver structural engineering firm. '
    + N'Same BC General Partnership (reg. FM0678571, BN 867868663) formerly named '
    + N'"Bryson Markulin Zickmantel Structural Engineers" (BMZ); renamed ~2020-2021. '
    + N'Founders: John Bryson, John A. Markulin, John Zickmantel. Seeded by Migration 33.';

-- Mirror the CanonicalOrg.NormalizedName computed-column expression (migration 22) exactly.
DECLARE @Norm nvarchar(300) = CAST(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    @DisplayName,
    ' ',''), '.',''), ',',''), '''',''), '-',''), '&',''), '/',''), '(',''), ')',''), '+',''))
    AS nvarchar(300));   -- => 'korstructuralltd'

BEGIN TRAN;

-- Round 14c: lock the NormalizedName range so concurrent upserts can't double-insert.
SELECT TOP (1) @KorId = Id
FROM   opportunities.CanonicalOrg WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
WHERE  NormalizedName = @Norm
ORDER BY CASE WHEN ClendorClientId IS NOT NULL THEN 0 ELSE 1 END, Id;

IF @KorId IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, ClendorClientId, Website, Notes)
    VALUES (@Kind, @DisplayName, NULL, N'https://www.korstructural.com/', @Notes);

    SET @KorId = CONVERT(bigint, SCOPE_IDENTITY());
    PRINT CONCAT('Migration 33: inserted CanonicalOrg ', @KorId, ' (', @DisplayName, ').');
END
ELSE
BEGIN
    UPDATE opportunities.CanonicalOrg
    SET    Kind         = COALESCE(@Kind, Kind),
           DisplayName  = @DisplayName,
           Website      = COALESCE(Website, N'https://www.korstructural.com/'),
           Notes        = COALESCE(Notes, @Notes),
           UpdatedAtUtc = sysdatetimeoffset()
    WHERE  Id = @KorId;

    PRINT CONCAT('Migration 33: CanonicalOrg ', @KorId, ' already existed (', @DisplayName, '); updated.');
END;

-- Seed every verified BMZ + KOR public name variant as a Manual alias → @KorId.
-- Defensive NOT EXISTS guard honours UX_OrgAlias_RawName_Source (RawName, Source).
;WITH SeedAliases (RawName) AS (
    -- BMZ-era (pre-rebrand)
    SELECT N'BMZ'                                                          UNION ALL
    SELECT N'BMZ Ltd'                                                      UNION ALL
    SELECT N'BMZ Ltd.'                                                     UNION ALL
    SELECT N'BMZ Structural Engineers'                                     UNION ALL
    SELECT N'BMZSE'                                                        UNION ALL
    SELECT N'Bryson Markulin Zickmantel'                                   UNION ALL
    SELECT N'Bryson Markulin Zickmantel Ltd'                               UNION ALL
    SELECT N'Bryson Markulin Zickmantel Ltd.'                              UNION ALL
    SELECT N'Bryson Markulin Zickmantel Structural Engineers'             UNION ALL
    SELECT N'Bryson Markulin Zickmantel BMZ Structural Engineers'         UNION ALL
    SELECT N'Bryson, Markulin, Zickmantel'                                 UNION ALL
    SELECT N'Bryson Markulin Zickmantel Structural Engineers USA Inc.'    UNION ALL
    -- KOR-era (2020/2021+)
    -- (Default case-insensitive collation collapses "Kor Structural" into "KOR Structural"
    --  for the unique index — list only the canonical-case forms.)
    SELECT N'KOR Structural'                                               UNION ALL
    SELECT N'KOR Structural Ltd'                                           UNION ALL
    SELECT N'KOR Structural Ltd.'                                          UNION ALL
    SELECT N'KOR Structural USA Inc.'
)
INSERT INTO opportunities.OrgAlias
    (RawName, Source, CanonicalOrgId, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes)
SELECT s.RawName, N'Manual', @KorId, 100, N'manual', sysdatetimeoffset(),
       N'Migration 33 — BMZ→KOR name-variant seed'
FROM   SeedAliases s
WHERE  NOT EXISTS (
           SELECT 1 FROM opportunities.OrgAlias a
           WHERE  a.RawName = s.RawName AND a.Source = N'Manual');

PRINT CONCAT('Migration 33: OrgAlias rows inserted this run: ', @@ROWCOUNT, '.');

COMMIT TRAN;
GO

PRINT 'Migration 33 complete.';
GO
