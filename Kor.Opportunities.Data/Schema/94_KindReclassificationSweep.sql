SET XACT_ABORT ON;
GO

-- Systematic Kind reclassification sweep. Audit found that 55 high-win
-- Vendors had wins >= 100, many of which are clearly Competitor (large
-- engineering firms) or GC (civil contractors) or Buyer (universities)
-- but were classified Vendor by legacy import.
--
-- Plus the audit-found Hatch + Mosaic Homes Kind mismatches.
-- Plus Brookfield is a real Developer / GC (not just FM).
-- Plus Pomerleau-style civil GCs misclassified.

BEGIN TRAN;

DECLARE @reclassifications TABLE (Id BIGINT PRIMARY KEY, NewKind NVARCHAR(40) NOT NULL);
INSERT INTO @reclassifications VALUES
    -- Vendor -> Competitor (large engineering/AEC firms)
    (15558, N'Competitor'),  -- SNC Lavalin Inc. (461 wins)
    (40886, N'Competitor'),  -- CBCL LIMITED (274)
    (6231,  N'Competitor'),  -- Dillon Consulting Limited (217)
    (16692, N'Competitor'),  -- Tetra Tech Canada Inc (180)
    (7254,  N'Competitor'),  -- EXP Services Inc (142)
    (4851,  N'Competitor'),  -- CIMA Canada Inc (139)
    (42183, N'Competitor'),  -- ARCADIS CANADA INC. (108)
    (46865, N'Competitor'),  -- J.L. RICHARDS & ASSOCIATES (101)
    (8742,  N'Competitor'),  -- Hatch Ltd (audit-found miss from m88)

    -- Vendor -> GC (civil / paving / excavation)
    (40755, N'GC'),          -- SUTHERLAND EXCAVATING LTD. (436)
    (3561,  N'GC'),          -- BORDER PAVING LTD (205)
    (12757, N'GC'),          -- O'Hanlon Paving Ltd (114)
    (1832,  N'GC'),          -- Aecon Transportation West Ltd (140)
    (15900, N'GC'),          -- Standard General Inc (110)
    (50759, N'GC'),          -- ACADIAN DREDGING LTD (113)
    (18199, N'GC'),          -- WEST CAN SEAL COATING INC (103)
    (46584, N'GC'),          -- Cas-Ell Structures Ltd (193)

    -- Vendor -> Buyer (institutional)
    (39012, N'Buyer'),       -- Carleton University (102)

    -- Buyer -> Developer (audit-found)
    (69746, N'Developer'),   -- Mosaic Homes (audit-found miss from m89)

    -- Architect -> Competitor (Stantec mis-tagged Architect for structural work)
    -- Skip — leave Stantec Architecture rows as-is until clearer signal

    -- Brookfield Global Integrated (FM arm) -> stays Vendor (FM is FM, not GC/Dev)
    -- Black & McDonald stays Vendor (MEP service contractor borderline, keep Vendor for now)

    -- IT / Staffing / Consulting / Equipment OEMs stay Vendor (correct):
    -- PWC, Deloitte, KPMG, MNP, E&Y, QMR, Altis, S.i. Systems, Siemens,
    -- Brandt, Finning, BELL CANADA, WPP, Mindwire, CORADIX, Calian,
    -- Compugen, Modis, etc.

    -- Aliases for Aecon GC parent (need separate dedup pass later for full Aecon variants)
    -- 1832 Aecon Transportation West -> GC for now, dedup to Aecon parent in future
    (-1, N'_sentinel');

DELETE FROM @reclassifications WHERE Id = -1;

UPDATE co SET co.Kind = r.NewKind
FROM opportunities.CanonicalOrg co INNER JOIN @reclassifications r ON r.Id = co.Id;
PRINT 'Kind reclassifications applied: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Summary by new Kind
SELECT 'After reclassify' AS Stat, r.NewKind, COUNT(*) AS Cnt
FROM @reclassifications r
GROUP BY r.NewKind
ORDER BY Cnt DESC;

PRINT 'Migration 94 Kind reclassification sweep complete.';

COMMIT TRAN;
GO
