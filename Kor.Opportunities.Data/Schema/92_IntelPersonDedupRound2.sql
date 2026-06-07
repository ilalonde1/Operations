SET XACT_ABORT ON;
GO

-- IntelPerson dedup round 2 — additional nickname-variant near-dups
-- surfaced after ingesting the Edmonton/Calgary trip-prep batch (200
-- new names brought the next layer of variant pairs into view). Same
-- conservative bar as migration 91: only obvious nickname/middle-initial
-- variants of the same person, false-positive candidates skipped.

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    (6114, 945),    -- "Geoff Hepworth"            -> "Geoffrey Hepworth"
    (5477, 865),    -- "James Huemoeller"          -> "James F Huemoeller"
    (582, 1739),    -- "Walt Ingram"               -> "Walter R. Ingram"
    (5300, 3513),   -- "Chris Kailing"             -> "Christopher Kailing"
    (246, 3489),    -- "Jim Kenyon"                -> "Jim C. Kenyon"
    (3016, 1423),   -- "Jeff Klapstein"            -> "Jeffrey Klapstein"
    (6200, 2106),   -- "Art Kohanik"               -> "Arthur Kohanik"
    (275, 272),     -- "Walt Koppelaar"            -> "Walter Koppelaar"
    (182, 6957),    -- "Rob Lange"                 -> "Robert Lange"
    (6161, 1082),   -- "John Markulin" (KOR partner JM) -> "John A. Markulin"
    (26, 3705),     -- "Christopher Martin"        -> "Christopher C. Martin"
    (5494, 874);    -- "Scott Mitchell"            -> "Scott Christopher Mitchell"

-- Affiliations: drop collisions, then repoint
DELETE pa
FROM opportunities.IntelPersonAffiliation pa
INNER JOIN @pairs p ON p.LoserId = pa.IntelPersonId
WHERE EXISTS (
    SELECT 1 FROM opportunities.IntelPersonAffiliation pa2
    WHERE pa2.IntelPersonId = p.SurvivorId AND pa2.CanonicalOrgId = pa.CanonicalOrgId
);
PRINT 'Loser affiliations dropped (collisions): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE pa SET pa.IntelPersonId = p.SurvivorId
FROM opportunities.IntelPersonAffiliation pa
INNER JOIN @pairs p ON p.LoserId = pa.IntelPersonId;
PRINT 'Affiliations repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE p
FROM opportunities.IntelPerson p
INNER JOIN @pairs pr ON pr.LoserId = p.Id;
PRINT 'Loser IntelPerson rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 92 IntelPerson dedup round 2 complete.';

COMMIT TRAN;
GO
