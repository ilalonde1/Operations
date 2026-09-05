-- Migration 314 (2026-09-05): collapse control-character whitespace in live
-- CanonicalOrg.DisplayName values so the strict computed NormalizedName key can
-- match the one-line form on future intake.
--
-- If the cleaned NormalizedName already belongs to another live row, this
-- migration must skip the row. That case is a canonical-org merge, not a safe
-- rename, and must go through tools/BdCanonicalDedup --pairs.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @candidates TABLE
(
    Id bigint NOT NULL PRIMARY KEY,
    CleanDisplayName nvarchar(300) NOT NULL
);

INSERT INTO @candidates (Id, CleanDisplayName)
SELECT co.Id,
       LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(
           co.DisplayName,
           CHAR(13), N' '),
           CHAR(10), N' '),
           CHAR(9), N' '),
           NCHAR(160), N' ')))
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND (co.DisplayName LIKE '%' + CHAR(13) + '%'
       OR co.DisplayName LIKE '%' + CHAR(10) + '%'
       OR co.DisplayName LIKE '%' + CHAR(9) + '%'
       OR co.DisplayName LIKE N'%' + NCHAR(160) + N'%');

WHILE EXISTS (SELECT 1 FROM @candidates WHERE CleanDisplayName LIKE N'%  %')
BEGIN
    UPDATE @candidates
    SET CleanDisplayName = REPLACE(CleanDisplayName, N'  ', N' ')
    WHERE CleanDisplayName LIKE N'%  %';
END;

UPDATE co
SET DisplayName = c.CleanDisplayName,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
JOIN @candidates c ON c.Id = co.Id
CROSS APPLY
(
    SELECT CAST(LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        c.CleanDisplayName,
        ' ',''), '.',''), ',',''), '''',''), '-',''), '&',''), '/',''), '(',''), ')',''), '+',''))
        AS nvarchar(300)) AS CleanNormalizedName
) n
WHERE co.DisplayName <> c.CleanDisplayName
  AND NOT EXISTS
  (
      SELECT 1
      FROM opportunities.CanonicalOrg other
      WHERE other.Id <> co.Id
        AND other.RetiredAtUtc IS NULL
        AND other.NormalizedName = n.CleanNormalizedName
  );

PRINT CONCAT('Migration 314 complete. CanonicalOrg display names updated: ', @@ROWCOUNT, '.');
GO
