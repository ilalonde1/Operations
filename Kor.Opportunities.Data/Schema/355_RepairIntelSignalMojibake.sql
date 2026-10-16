/*
    KorOpportunitiesDb migration 355: repair intel-signal text that was UTF-8 read as code page 1252.

    Measured 2026-10-15 on the live store: 496 opportunities.IntelSignal subjects across 435 orgs,
    first seen 2026-06-07, last 2026-10-01, carry the "â€”" (em dash), "â€™" (apostrophe) and "Ã©"
    (accents) shapes. The bytes were written correctly as UTF-8 by the research agents and then
    decoded once as code page 1252 somewhere in the research-import pipeline before reaching the
    table; the C# importer itself reads UTF-8, so the step is upstream of it and still to be found.
    This migration repairs the stored text. It does not fix the pipeline.

    Technique: the stored characters, cast to VARCHAR in a code-page-1252 binary collation, give
    back the original UTF-8 bytes. Those bytes go into a UTF-8 collated column AS VARBINARY, which
    copies bytes; a VARCHAR assigned there is converted character by character and changes nothing.
    The column then reads as the intended characters and is cast to NVARCHAR to store.
    A row is repaired only when
      (a) its text matches one of the shapes above,
      (b) the cast to code page 1252 is lossless (no character outside that code page, so no '?'), and
      (c) the repaired text contains no U+FFFD, tested with CHARINDEX in a BIN2 collation. A genuine
          "â", as in the Cree Mâmawatoskêwak, fails this test and is left alone.
    ⛔ Never test U+FFFD with LIKE '%' + NCHAR(65533) + '%': it is TRUE FOR EVERY STRING here, so the
       first version of this file classed all 496 rows as genuine accents and repaired none.
    Dry run 2026-10-15: 496 candidate subjects, 0 not lossless, 4 left alone, 492 repairable.
    Idempotent: a repaired row no longer matches. Subject and Detail are both treated.

    Run PART A first and look at the sample. Then PART B. Then PART C.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

-- ===== PART A: preview, no writes =====
DECLARE @dash NVARCHAR(2) = NCHAR(226) + NCHAR(8364), @acc NVARCHAR(1) = NCHAR(195), @a NVARCHAR(1) = NCHAR(226), @bad NVARCHAR(1) = NCHAR(65533);
DECLARE @p TABLE (Id BIGINT PRIMARY KEY, Subject8 VARCHAR(MAX) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL, Detail8 VARCHAR(MAX) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL);
INSERT INTO @p (Id, Subject8, Detail8)
SELECT s.Id,
       CASE WHEN (s.Subject LIKE N'%' + @dash + N'%' OR s.Subject LIKE N'%' + @acc + N'%' OR s.Subject LIKE N'%' + @a + N'%')
             AND CAST((CAST(s.Subject AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS NVARCHAR(MAX)) = s.Subject
            THEN CAST((CAST(s.Subject AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS VARBINARY(MAX)) END,
       CASE WHEN s.Detail IS NOT NULL
             AND (s.Detail LIKE N'%' + @dash + N'%' OR s.Detail LIKE N'%' + @acc + N'%' OR s.Detail LIKE N'%' + @a + N'%')
             AND CAST((CAST(s.Detail AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS NVARCHAR(MAX)) = s.Detail
            THEN CAST((CAST(s.Detail AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS VARBINARY(MAX)) END
FROM opportunities.IntelSignal s
WHERE s.Subject LIKE N'%' + @dash + N'%' OR s.Subject LIKE N'%' + @acc + N'%' OR s.Subject LIKE N'%' + @a + N'%'
   OR s.Detail  LIKE N'%' + @dash + N'%' OR s.Detail  LIKE N'%' + @acc + N'%' OR s.Detail  LIKE N'%' + @a + N'%';

SELECT COUNT(*) AS candidate_rows,
       SUM(CASE WHEN Subject8 IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(Subject8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0 THEN 1 ELSE 0 END) AS subjects_repairable,
       SUM(CASE WHEN Detail8  IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(Detail8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0 THEN 1 ELSE 0 END) AS details_repairable,
       SUM(CASE WHEN Subject8 IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(Subject8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) > 0 THEN 1 ELSE 0 END) AS subjects_left_alone_genuine_accent
FROM @p;

SELECT TOP 20 p.Id, LEFT(s.Subject, 70) AS subject_now, LEFT(CAST(p.Subject8 AS NVARCHAR(MAX)), 70) AS subject_repaired,
       CASE WHEN CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(p.Subject8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) > 0 THEN 'LEFT ALONE' ELSE 'repair' END AS verdict
FROM @p p JOIN opportunities.IntelSignal s ON s.Id = p.Id
WHERE p.Subject8 IS NOT NULL
ORDER BY p.Id;
GO

-- ===== PART B: apply, in one transaction =====
DECLARE @dash NVARCHAR(2) = NCHAR(226) + NCHAR(8364), @acc NVARCHAR(1) = NCHAR(195), @a NVARCHAR(1) = NCHAR(226), @bad NVARCHAR(1) = NCHAR(65533);
DECLARE @f TABLE (Id BIGINT PRIMARY KEY, Subject8 VARCHAR(MAX) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL, Detail8 VARCHAR(MAX) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL);
INSERT INTO @f (Id, Subject8, Detail8)
SELECT s.Id,
       CASE WHEN (s.Subject LIKE N'%' + @dash + N'%' OR s.Subject LIKE N'%' + @acc + N'%' OR s.Subject LIKE N'%' + @a + N'%')
             AND CAST((CAST(s.Subject AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS NVARCHAR(MAX)) = s.Subject
            THEN CAST((CAST(s.Subject AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS VARBINARY(MAX)) END,
       CASE WHEN s.Detail IS NOT NULL
             AND (s.Detail LIKE N'%' + @dash + N'%' OR s.Detail LIKE N'%' + @acc + N'%' OR s.Detail LIKE N'%' + @a + N'%')
             AND CAST((CAST(s.Detail AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS NVARCHAR(MAX)) = s.Detail
            THEN CAST((CAST(s.Detail AS VARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) AS VARBINARY(MAX)) END
FROM opportunities.IntelSignal s
WHERE s.Subject LIKE N'%' + @dash + N'%' OR s.Subject LIKE N'%' + @acc + N'%' OR s.Subject LIKE N'%' + @a + N'%'
   OR s.Detail  LIKE N'%' + @dash + N'%' OR s.Detail  LIKE N'%' + @acc + N'%' OR s.Detail  LIKE N'%' + @a + N'%';

BEGIN TRANSACTION;
UPDATE s
   SET Subject = CASE WHEN f.Subject8 IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(f.Subject8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0
                      THEN CAST(f.Subject8 AS NVARCHAR(MAX)) ELSE s.Subject END,
       Detail  = CASE WHEN f.Detail8 IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(f.Detail8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0
                      THEN CAST(f.Detail8 AS NVARCHAR(MAX)) ELSE s.Detail END
  FROM opportunities.IntelSignal s
  JOIN @f f ON f.Id = s.Id
 WHERE (f.Subject8 IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(f.Subject8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0)
    OR (f.Detail8  IS NOT NULL AND CHARINDEX(@bad COLLATE Latin1_General_100_BIN2, CAST(f.Detail8 AS NVARCHAR(MAX)) COLLATE Latin1_General_100_BIN2) = 0);
SELECT @@ROWCOUNT AS rows_repaired;
COMMIT TRANSACTION;
GO

-- ===== PART C: what remains (should be only genuine accents, such as Mâmawatoskêwak) =====
DECLARE @dash NVARCHAR(2) = NCHAR(226) + NCHAR(8364), @acc NVARCHAR(1) = NCHAR(195), @a NVARCHAR(1) = NCHAR(226);
SELECT COUNT(*) AS still_matching_subjects FROM opportunities.IntelSignal
WHERE Subject LIKE N'%' + @dash + N'%' OR Subject LIKE N'%' + @acc + N'%' OR Subject LIKE N'%' + @a + N'%';
SELECT TOP 10 Id, LEFT(Subject, 80) AS subject FROM opportunities.IntelSignal
WHERE Subject LIKE N'%' + @dash + N'%' OR Subject LIKE N'%' + @acc + N'%' OR Subject LIKE N'%' + @a + N'%' ORDER BY Id;
GO
