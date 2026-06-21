USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 255: BD audit close-out 2026-06-20 M1.
  Re-anchor active opportunities.IntelPerson.NaturalKey from name-only to the
  identity-anchor contract used by IntelNaturalKey.ComputePerson:
  [[reference_intelperson_ingest_contract]]
    1. trimmed/lower email
    2. trimmed/lower LinkedinUrl
    3. normalize(DisplayName) + '|org:' + lowest current affiliation org id
    4. normalize(DisplayName)

  DRY-RUN counts are printed before changes. New active-row collisions are
  disambiguated deterministically by hashing the same key material with
  '|id:<IntelPerson.Id>' appended for non-survivors. Rows that would still
  collide with an existing NaturalKey are left unchanged and counted.

  IntelPersonAffiliation keys are intentionally untouched: that NaturalKey is
  personId-based (personId|orgId|normalized-title), so changing IntelPerson's
  NaturalKey does not change the affiliation identity anchor.
*/
BEGIN TRAN;

IF OBJECT_ID(N'tempdb..#Candidate') IS NOT NULL DROP TABLE #Candidate;
IF OBJECT_ID(N'tempdb..#Desired') IS NOT NULL DROP TABLE #Desired;
IF OBJECT_ID(N'tempdb..#Resolved') IS NOT NULL DROP TABLE #Resolved;
IF OBJECT_ID(N'tempdb..#Final') IS NOT NULL DROP TABLE #Final;

;WITH PrimaryAffiliation AS
(
    SELECT IntelPersonId, MIN(CanonicalOrgId) AS PrimaryCurrentAffiliationOrgId
    FROM opportunities.IntelPersonAffiliation
    WHERE RetiredAtUtc IS NULL
      AND IsCurrent = 1
    GROUP BY IntelPersonId
),
Normalized AS
(
    SELECT
        p.Id,
        p.NaturalKey AS OldNaturalKey,
        NormalizedName =
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                LOWER(LTRIM(RTRIM(COALESCE(p.DisplayName, N'')))),
                N' ', N''), N'.', N''), N',', N''), N'''', N''), N'-', N''), N'&', N''), N'/', N''), N'(', N''), N')', N''), N'+', N''),
        EmailKey = LOWER(LTRIM(RTRIM(COALESCE(p.Email, N'')))),
        LinkedinKey = LOWER(LTRIM(RTRIM(COALESCE(p.LinkedinUrl, N'')))),
        pa.PrimaryCurrentAffiliationOrgId
    FROM opportunities.IntelPerson p
    LEFT JOIN PrimaryAffiliation pa ON pa.IntelPersonId = p.Id
    WHERE p.RetiredAtUtc IS NULL
)
SELECT
    Id,
    OldNaturalKey,
    KeyMaterial = CAST(
        CASE
            WHEN EmailKey <> N'' THEN EmailKey
            WHEN LinkedinKey <> N'' THEN LinkedinKey
            WHEN NormalizedName <> N'' AND PrimaryCurrentAffiliationOrgId IS NOT NULL
                THEN NormalizedName + N'|org:' + CONVERT(nvarchar(20), PrimaryCurrentAffiliationOrgId)
            ELSE NormalizedName
        END AS nvarchar(4000))
INTO #Candidate
FROM Normalized;

SELECT
    Id,
    OldNaturalKey,
    KeyMaterial,
    DesiredNaturalKey = CONVERT(char(40), HASHBYTES('SHA1', CAST(KeyMaterial AS varchar(8000))), 2)
INTO #Desired
FROM #Candidate;

DECLARE @ActiveRows int = (SELECT COUNT(*) FROM #Desired);
DECLARE @PlainChangedRows int = (SELECT COUNT(*) FROM #Desired WHERE OldNaturalKey <> DesiredNaturalKey);
DECLARE @NewCollisionGroups int =
(
    SELECT COUNT(*)
    FROM
    (
        SELECT DesiredNaturalKey
        FROM #Desired
        GROUP BY DesiredNaturalKey
        HAVING COUNT(*) > 1
    ) c
);
DECLARE @NewCollisionRows int =
(
    SELECT COUNT(*)
    FROM #Desired d
    WHERE EXISTS
    (
        SELECT 1
        FROM #Desired d2
        WHERE d2.DesiredNaturalKey = d.DesiredNaturalKey
          AND d2.Id <> d.Id
    )
);

PRINT 'Migration 255 dry-run: active IntelPerson rows scanned: ' + CONVERT(varchar(20), @ActiveRows);
PRINT 'Migration 255 dry-run: active rows whose plain new key differs from current key: ' + CONVERT(varchar(20), @PlainChangedRows);
PRINT 'Migration 255 dry-run: NEW active-row collision groups on plain new key: ' + CONVERT(varchar(20), @NewCollisionGroups);
PRINT 'Migration 255 dry-run: active rows involved in NEW plain-key collisions: ' + CONVERT(varchar(20), @NewCollisionRows);

;WITH Ranked AS
(
    SELECT
        d.Id,
        d.OldNaturalKey,
        d.KeyMaterial,
        d.DesiredNaturalKey,
        HasRetiredOwner = CASE WHEN EXISTS
        (
            SELECT 1
            FROM opportunities.IntelPerson p
            WHERE p.Id <> d.Id
              AND p.RetiredAtUtc IS NOT NULL
              AND p.NaturalKey = d.DesiredNaturalKey
        ) THEN 1 ELSE 0 END,
        RowNumber = ROW_NUMBER() OVER
        (
            PARTITION BY d.DesiredNaturalKey
            ORDER BY CASE WHEN d.OldNaturalKey = d.DesiredNaturalKey THEN 0 ELSE 1 END, d.Id
        )
    FROM #Desired d
)
SELECT
    Id,
    OldNaturalKey,
    DesiredNaturalKey,
    NewNaturalKey = CONVERT(char(40), HASHBYTES('SHA1', CAST(
        CASE
            WHEN HasRetiredOwner = 0 AND RowNumber = 1 THEN KeyMaterial
            ELSE KeyMaterial + N'|id:' + CONVERT(nvarchar(20), Id)
        END AS varchar(8000))), 2),
    TempNaturalKey = CONVERT(char(40), HASHBYTES('SHA1', CAST(
        N'm255-intelperson-temp|id:' + CONVERT(nvarchar(20), Id)
        AS varchar(8000))), 2)
INTO #Resolved
FROM Ranked;

;WITH Checked AS
(
    SELECT
        r.*,
        NewKeyCount = COUNT(*) OVER (PARTITION BY r.NewNaturalKey),
        TempKeyCount = COUNT(*) OVER (PARTITION BY r.TempNaturalKey),
        HasExistingNewKeyConflict = CASE WHEN EXISTS
        (
            SELECT 1
            FROM opportunities.IntelPerson p
            WHERE p.Id <> r.Id
              AND p.NaturalKey = r.NewNaturalKey
              AND NOT EXISTS (SELECT 1 FROM #Resolved x WHERE x.Id = p.Id)
        ) THEN 1 ELSE 0 END,
        HasExistingTempKeyConflict = CASE WHEN EXISTS
        (
            SELECT 1
            FROM opportunities.IntelPerson p
            WHERE p.Id <> r.Id
              AND p.NaturalKey = r.TempNaturalKey
        ) THEN 1 ELSE 0 END
    FROM #Resolved r
)
SELECT
    Id,
    OldNaturalKey,
    DesiredNaturalKey,
    NewNaturalKey,
    TempNaturalKey,
    UpdateNaturalKey = CASE
        WHEN NewKeyCount > 1
          OR TempKeyCount > 1
          OR HasExistingNewKeyConflict = 1
          OR HasExistingTempKeyConflict = 1
            THEN OldNaturalKey
        ELSE NewNaturalKey
    END,
    Unsafe = CASE
        WHEN NewKeyCount > 1
          OR TempKeyCount > 1
          OR HasExistingNewKeyConflict = 1
          OR HasExistingTempKeyConflict = 1
            THEN 1
        ELSE 0
    END
INTO #Final
FROM Checked;

DECLARE @DisambiguatedRows int =
(
    SELECT COUNT(*)
    FROM #Final
    WHERE DesiredNaturalKey <> NewNaturalKey
      AND Unsafe = 0
);
DECLARE @UnsafeRows int = (SELECT COUNT(*) FROM #Final WHERE Unsafe = 1);
DECLARE @RowsToUpdate int = (SELECT COUNT(*) FROM #Final WHERE Unsafe = 0 AND OldNaturalKey <> UpdateNaturalKey);

PRINT 'Migration 255 dry-run: collision-safe disambiguated active rows using |id:<Id>: ' + CONVERT(varchar(20), @DisambiguatedRows);
PRINT 'Migration 255 dry-run: rows left on old key because final key was unsafe: ' + CONVERT(varchar(20), @UnsafeRows);
PRINT 'Migration 255 dry-run: active IntelPerson rows that will be updated: ' + CONVERT(varchar(20), @RowsToUpdate);
PRINT 'Migration 255 note: IntelPersonAffiliation NaturalKey is personId-based and is not updated.';

UPDATE p
SET NaturalKey = f.TempNaturalKey,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p
JOIN #Final f ON f.Id = p.Id
WHERE f.Unsafe = 0
  AND f.OldNaturalKey <> f.UpdateNaturalKey;
PRINT 'Migration 255 temp-key move rows: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE p
SET NaturalKey = f.UpdateNaturalKey,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPerson p
JOIN #Final f ON f.Id = p.Id
WHERE f.Unsafe = 0
  AND f.OldNaturalKey <> f.UpdateNaturalKey;
PRINT 'Migration 255 final NaturalKey rows updated: ' + CONVERT(varchar(20), @@ROWCOUNT);

IF EXISTS
(
    SELECT 1
    FROM opportunities.IntelPerson
    GROUP BY NaturalKey
    HAVING COUNT(*) > 1
)
BEGIN
    THROW 51255, 'Migration 255 verification failed: duplicate IntelPerson.NaturalKey detected.', 1;
END;

PRINT 'Migration 255 complete: IntelPerson identity-anchor NaturalKey backfill applied safely.';
COMMIT TRAN;
GO
