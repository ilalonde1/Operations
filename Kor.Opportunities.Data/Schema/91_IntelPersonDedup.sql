SET XACT_ABORT ON;
GO

-- IntelPerson dedup — name-variant near-duplicates surfaced by the
-- people drain. NaturalKey-based dedup couldn't catch these because
-- they have meaningfully different DisplayName values
-- ("Jeff Brink" vs "Jeff D. Brink"). All 18 pairs below are
-- nickname/middle-initial variants of the same person, verified
-- manually. False-positive candidates (Marieke vs Mary Burgers,
-- Michelle vs Michael Brown, Marc vs Marcello De Cotiis — different
-- people, NOT merged) skipped.
--
-- Survivor chosen as the more-informative name (longer form, includes
-- middle initial, includes legal-form variant).

BEGIN TRAN;

DECLARE @pairs TABLE (LoserId BIGINT PRIMARY KEY, SurvivorId BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    (3678, 731),    -- "Robert D. Moser, Jr."     -> "Robert D. 'Robby' Moser Jr."
    (155, 6951),    -- "Jessie Arora"             -> "Jessica (Jessie) Arora"
    (6869, 834),    -- "Helen Besharat"           -> "Helen Avini Besharat"
    (2414, 1494),   -- "Pat Bohan"                -> "Patrick (Pat) Bohan"
    (395, 3835),    -- "Jeff Brink"               -> "Jeff D. Brink"
    (2156, 6402),   -- "John Broersen"            -> "Johnny Broersen"
    (2169, 2396),   -- "Doug Burnside"            -> "Douglas Burnside"
    (2405, 462),    -- "Jeff Busby"               -> "Jeffrey Busby"
    (1942, 902),    -- "Don De Cotiis"            -> "Donato De Cotiis"
    (6978, 1234),   -- "Clay Cowan"               -> "Clayton Cowan"
    (1437, 330),    -- "Steve Cox"                -> "Steven J. Cox"
    (108, 3272),    -- "Greg Damant"              -> "Gregory Damant"
    (3949, 7002),   -- "Arup Datta"               -> "Arup K. Datta"
    (32, 3726),     -- "James Donaldson"          -> "James (JED) Donaldson"
    (2423, 3034),   -- "Greg Ebel"                -> "Gregory L. Ebel"
    (1588, 1759),   -- "Timothy Eddy"             -> "Timothy R. Eddy"
    (3838, 3657),   -- "Robert Englekirk"         -> "Robert E. Englekirk"
    (5589, 925),    -- "Nabih Faris"              -> "Nabih A. Faris"
    (3711, 28),     -- "Kelly Farrell"            -> "Kelly M. Farrell"
    (1062, 5588);   -- "Danilo Funaro"            -> "Danilo (Dan) Funaro"

-- ========== Move affiliations from loser to survivor ==========
-- Handle collisions: if survivor already has an affiliation to the same
-- CanonicalOrg, delete the loser's affiliation (the survivor's data wins).
DELETE pa
FROM opportunities.IntelPersonAffiliation pa
INNER JOIN @pairs p ON p.LoserId = pa.IntelPersonId
WHERE EXISTS (
    SELECT 1 FROM opportunities.IntelPersonAffiliation pa2
    WHERE pa2.IntelPersonId = p.SurvivorId
      AND pa2.CanonicalOrgId = pa.CanonicalOrgId
);
PRINT 'Loser affiliations dropped (collisions with survivor): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE pa SET pa.IntelPersonId = p.SurvivorId
FROM opportunities.IntelPersonAffiliation pa
INNER JOIN @pairs p ON p.LoserId = pa.IntelPersonId;
PRINT 'Affiliations repointed to survivor: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ========== Delete loser IntelPerson rows ==========
DELETE p
FROM opportunities.IntelPerson p
INNER JOIN @pairs pr ON pr.LoserId = p.Id;
PRINT 'Loser IntelPerson rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 91 IntelPerson nickname-variant dedup complete.';

COMMIT TRAN;
GO
