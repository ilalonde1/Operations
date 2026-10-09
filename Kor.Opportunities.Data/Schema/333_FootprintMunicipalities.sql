-- Migration 333 (2026-10-08): the in-footprint municipality roster, so coverage is a
-- number the weekly audit PRINTS rather than a number a document remembers.
--
-- WHY. `docs/KOR-EarlySignal-CoverageGap-2026-09-10.md` measured the gap properly on
-- 10 September: 56 in-footprint municipalities, 13 with an application feed, Metro
-- Vancouver 4 of 21, Fraser Valley 0 of 6, 2,583,534 of 3,973,480 people living where we
-- hold nothing. It promoted three tools into `tools/BdSourceCoverage/`. And then nothing
-- ran them, so on 2026-10-08 the same gap was measured again from scratch -- Ian: "I'm
-- POSITIVE we looked at this - we HAD to have? If not it was a major miss obviously."
--
-- He was right, and the lesson is not "read harder". A gap that lives only in a document
-- gets rediscovered; a gap in the weekly report gets closed. §K of DataHealthAuditJob
-- reads this table.
--
-- ⛔ THE HELD SET IS NOT STORED HERE, DELIBERATELY. `coverage_gap.py` hardcodes
--    HAVE_APPLICATIONS as a python literal copied out of a live query, which is exactly
--    why its 11 became stale. §K derives "held" by joining this roster to
--    opportunities.OpportunitySources at report time. The only thing stored here is the
--    roster and the census population -- facts that do not change weekly.
--
-- WHAT THE ROSTER IS: the four regions KOR sells into, from the provincial dataset
-- WHSE_LEGAL_ADMIN_BOUNDARIES.ABMS_MUNICIPALITIES_SP (160 BC municipalities, filtered to
-- footprint). Populations are 2021 census. Carried over from coverage_gap.py, which is
-- where they were entered and checked.
--
-- ⚠ ONE CORRECTION made in transit: coverage_gap.py has "Central Saatnich". It is
--    Central Saanich. The typo was harmless there because nothing matched on it; it would
--    not be harmless in a join.
--
-- WHAT THIS TABLE DOES NOT COVER, stated so nobody reads the name and stops looking:
--   * It is not every BC municipality -- 56 of 160, by design. Out-of-footprint gaps are
--     not gaps.
--   * Population is a 2021 snapshot and is used ONLY to rank the gap list. It is not
--     market size, and a big population does not mean a structural opportunity.
--   * A regional district is not a municipality and is not in here. The RDN feed covers
--     ELECTORAL AREAS; it is real coverage of places that have no municipal feed because
--     they have no municipality, and §K will not credit it to anyone.
--   * "Held" means a source row exists and is enabled. It says nothing about whether that
--     source is PRODUCING -- that is what §I and §J are for. A municipality can be held
--     and silent, and §K will still count it.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID('opportunities.FootprintMunicipalities', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.FootprintMunicipalities
    (
        Id              int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FootprintMunicipalities PRIMARY KEY,
        Region          nvarchar(40)  NOT NULL,
        Name            nvarchar(80)  NOT NULL
            CONSTRAINT UQ_FootprintMunicipalities_Name UNIQUE,
        -- How a source row for this municipality is named: the part of
        -- OpportunitySources.Name before the first underscore. Computed so it cannot
        -- drift away from Name. 'Maple Ridge' -> 'MapleRidge';
        -- 'Langley (Township)' -> 'LangleyTownship'.
        -- CAST is not decoration: without it the expression infers nvarchar(4000) and the
        -- index below warns "maximum length of 8000 bytes" and can fail on a long value.
        SourceNamePrefix AS (CAST(REPLACE(REPLACE(REPLACE(Name, N' ', N''), N'(', N''), N')', N'')
                                  AS nvarchar(80)))
            PERSISTED NOT NULL,
        Population2021  int           NOT NULL,
        Notes           nvarchar(200) NULL,
        CreatedAtUtc    datetimeoffset NOT NULL
            CONSTRAINT DF_FootprintMunicipalities_Created DEFAULT sysdatetimeoffset()
    );

    CREATE INDEX IX_FootprintMunicipalities_Prefix
        ON opportunities.FootprintMunicipalities (SourceNamePrefix);
END;
GO

-- Repair path for a table created before the CAST was added: the first run of this
-- migration built SourceNamePrefix as nvarchar(4000) and SQL Server warned that the index
-- exceeded the 1700-byte key limit. Re-point the column at an 80-char cast. Idempotent --
-- a correctly typed column skips this entirely.
IF EXISTS (SELECT 1 FROM sys.columns c
           WHERE c.object_id = OBJECT_ID('opportunities.FootprintMunicipalities')
             AND c.name = 'SourceNamePrefix'
             AND c.max_length <> 160)   -- nvarchar(80) = 160 bytes
BEGIN
    DROP INDEX IX_FootprintMunicipalities_Prefix ON opportunities.FootprintMunicipalities;
    ALTER TABLE opportunities.FootprintMunicipalities DROP COLUMN SourceNamePrefix;
    ALTER TABLE opportunities.FootprintMunicipalities
        ADD SourceNamePrefix AS (CAST(REPLACE(REPLACE(REPLACE(Name, N' ', N''), N'(', N''), N')', N'')
                                      AS nvarchar(80))) PERSISTED NOT NULL;
    CREATE INDEX IX_FootprintMunicipalities_Prefix
        ON opportunities.FootprintMunicipalities (SourceNamePrefix);
END;
GO

MERGE opportunities.FootprintMunicipalities AS t
USING (VALUES
    -- Metro Vancouver (21)
    (N'Metro Vancouver', N'Vancouver',                   662248, NULL),
    (N'Metro Vancouver', N'Surrey',                      568322, NULL),
    (N'Metro Vancouver', N'Burnaby',                     249125, N'2026-09: only ArcGIS hit was a Development Permit AREA overlay'),
    (N'Metro Vancouver', N'Richmond',                    209937, N'2026-09: not found by item search; publishes an applications map, needs a deeper probe'),
    (N'Metro Vancouver', N'Coquitlam',                   148625, NULL),
    (N'Metro Vancouver', N'Langley (Township)',          132603, NULL),
    (N'Metro Vancouver', N'Delta',                       108455, N'maps.delta.ca has Building Permits (issued = late signal)'),
    (N'Metro Vancouver', N'Maple Ridge',                  90990, NULL),
    (N'Metro Vancouver', N'North Vancouver (District)',   88168, NULL),
    (N'Metro Vancouver', N'New Westminster',              78916, N'2026-09: not found by item search; needs a deeper probe'),
    (N'Metro Vancouver', N'Port Coquitlam',               61498, NULL),
    (N'Metro Vancouver', N'North Vancouver (City)',       58120, NULL),
    (N'Metro Vancouver', N'West Vancouver',               44122, NULL),
    (N'Metro Vancouver', N'Port Moody',                   33535, N'EngagementHQ engage.portmoody.ca verified 2026-10-08: 35 of 66 application-shaped, 20 live'),
    (N'Metro Vancouver', N'Langley (City)',               28963, NULL),
    (N'Metro Vancouver', N'White Rock',                   21939, NULL),
    (N'Metro Vancouver', N'Pitt Meadows',                 19146, NULL),
    (N'Metro Vancouver', N'Bowen Island',                  4256, NULL),
    (N'Metro Vancouver', N'Anmore',                        2356, NULL),
    (N'Metro Vancouver', N'Lions Bay',                     1334, NULL),
    (N'Metro Vancouver', N'Belcarra',                        692, NULL),
    -- Fraser Valley (6)
    (N'Fraser Valley',   N'Abbotsford',                  153524, N'ArcGIS org ZYlQy38aWlfDG1Qh; its DP layer was rejected as a zoning overlay'),
    (N'Fraser Valley',   N'Chilliwack',                   93203, NULL),
    (N'Fraser Valley',   N'Mission',                      41519, NULL),
    (N'Fraser Valley',   N'Hope',                          6686, NULL),
    (N'Fraser Valley',   N'Kent',                          6502, NULL),
    (N'Fraser Valley',   N'Harrison Hot Springs',          1717, NULL),
    -- Vancouver Island (21)
    (N'Vancouver Island', N'Nanaimo',                     99863, N'Whats Building page (SourceType 2), not a full application feed'),
    (N'Vancouver Island', N'Saanich',                    117735, NULL),
    (N'Vancouver Island', N'Victoria',                    91867, NULL),
    (N'Vancouver Island', N'Langford',                    46584, N'prices its permits but names no applicant'),
    (N'Vancouver Island', N'Campbell River',              35519, N'names the applicant on 35 of 36; publishes no dates'),
    (N'Vancouver Island', N'North Cowichan',              31990, N'has a database, exposes no feed; needs a human look'),
    (N'Vancouver Island', N'Courtenay',                   28420, NULL),
    (N'Vancouver Island', N'Colwood',                     18961, NULL),
    (N'Vancouver Island', N'Port Alberni',                18259, NULL),
    (N'Vancouver Island', N'Oak Bay',                     17990, NULL),
    (N'Vancouver Island', N'Esquimalt',                   17533, NULL),
    (N'Vancouver Island', N'Central Saanich',             17385, N'coverage_gap.py spelled this "Central Saatnich"'),
    (N'Vancouver Island', N'Sooke',                       15086, NULL),
    (N'Vancouver Island', N'Comox',                       14806, N'publishes no dates'),
    (N'Vancouver Island', N'Powell River',                13943, NULL),
    (N'Vancouver Island', N'Parksville',                  13642, N'quarterly PDF, names the applicant per row; source row is SourceType 99 and DISABLED'),
    (N'Vancouver Island', N'Sidney',                      12318, NULL),
    (N'Vancouver Island', N'North Saanich',               12094, NULL),
    (N'Vancouver Island', N'View Royal',                  11575, N'Tempest source row DISABLED: its BaseUrl is a SOFT 404 and no instance was found 2026-10-08'),
    (N'Vancouver Island', N'Qualicum Beach',               9111, NULL),
    (N'Vancouver Island', N'Duncan',                       5047, NULL),
    -- Okanagan / Interior (8)
    (N'Okanagan / Interior', N'Kelowna',                 144576, N'SETTLED 2026-09: no public development-application layer; its hits are zoning overlays and road-use permits. Do not re-hunt on ArcGIS.'),
    (N'Okanagan / Interior', N'Kamloops',                 97902, N'found by tools/ArcGisFingerprint; layer cannot page (arcgis.supportsPagination)'),
    (N'Okanagan / Interior', N'Vernon',                    44519, NULL),
    (N'Okanagan / Interior', N'Penticton',                 36885, NULL),
    (N'Okanagan / Interior', N'West Kelowna',              36078, NULL),
    (N'Okanagan / Interior', N'Salmon Arm',                19432, NULL),
    (N'Okanagan / Interior', N'Lake Country',              15817, NULL),
    (N'Okanagan / Interior', N'Summerland',                12042, NULL)
) AS s(Region, Name, Population2021, Notes)
ON t.Name = s.Name
WHEN MATCHED AND (t.Region <> s.Region
                  OR t.Population2021 <> s.Population2021
                  OR ISNULL(t.Notes, N'') <> ISNULL(s.Notes, N''))
    THEN UPDATE SET Region = s.Region, Population2021 = s.Population2021, Notes = s.Notes
WHEN NOT MATCHED THEN
    INSERT (Region, Name, Population2021, Notes)
    VALUES (s.Region, s.Name, s.Population2021, s.Notes);
GO

SELECT 'roster seeded' AS Section,
       COUNT_BIG(*)                 AS Municipalities,
       SUM(CAST(Population2021 AS bigint)) AS FootprintPopulation
FROM opportunities.FootprintMunicipalities;

SELECT 'by region' AS Section, Region, COUNT_BIG(*) AS Municipalities,
       SUM(CAST(Population2021 AS bigint)) AS Population
FROM opportunities.FootprintMunicipalities
GROUP BY Region ORDER BY SUM(CAST(Population2021 AS bigint)) DESC;

-- The number §K will print, computed here so the migration proves its own join.
SELECT 'coverage as section K will compute it' AS Section,
       m.Region,
       COUNT_BIG(*) AS Municipalities,
       SUM(CASE WHEN h.Prefix IS NOT NULL THEN 1 ELSE 0 END) AS Held,
       SUM(CASE WHEN h.Prefix IS NULL THEN CAST(m.Population2021 AS bigint) ELSE 0 END) AS PopulationWithNoFeed
FROM opportunities.FootprintMunicipalities m
OUTER APPLY (
    SELECT TOP 1 LEFT(s.Name, CHARINDEX(N'_', s.Name + N'_') - 1) AS Prefix
    FROM opportunities.OpportunitySources s
    WHERE s.IsEnabled = 1
      AND s.SourceType IN (2, 20, 21, 22)
      AND LEFT(s.Name, CHARINDEX(N'_', s.Name + N'_') - 1) = m.SourceNamePrefix
) h
GROUP BY m.Region
ORDER BY SUM(CASE WHEN h.Prefix IS NULL THEN CAST(m.Population2021 AS bigint) ELSE 0 END) DESC;
GO
