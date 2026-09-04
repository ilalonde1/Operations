-- Migration 309 (2026-09-04): the mid-Island applicant firms that were new to the
-- Brain, from the Campbell River / Courtenay / Comox feeds wired in 308.
--
-- Most of the significant mid-Island firms were ALREADY held, several as clients:
-- NSDA Architects (CL00495, 28 KOR jobs), Pennyfarthing (12), D Akers Property
-- Solutions (6), Zemcore (3), Three Dog Ventures (3), Ridge North America (2),
-- Radcliffe (1), Gibbins Road Holdings (1), plus WestUrban, Seymour Pacific,
-- Crowne Pacific, Royop, McElhanney, Ian Moxon, MacDonald Hagarty and JM
-- Architecture as non-clients. Only the twelve below were missing.
--
-- ⛔ FuzzyNormalizedName is left NULL ON PURPOSE. Migration 307 hand-computed it,
--    kept the legal suffix ('hutchinsoncontractingltd' where the real normalizer
--    returns 'hutchinsoncontracting'), and minted four duplicates because the
--    write-time gate could not see the rows. The repair path is: insert with no
--    key, run tools/BdIntegrityCheck, and take ExpectedFuzzy from its own
--    org_fuzzy_key_stale CSV — the tool's computation, not a guess. Do NOT run
--    --backfill-fuzzy-key; it rewrites all 893k rows and undoes the deliberate
--    'mcw' override on 71528.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @new TABLE (Nm nvarchar(200), Kind nvarchar(50), Dom nvarchar(200), Note nvarchar(1000));
INSERT INTO @new (Nm, Kind, Dom, Note) VALUES
 (N'WestUrban Developments Ltd', N'Developer', NULL,
  N'Campbell River: 3 apartment buildings, 200 units, with a height variance from 10m to 16m — the largest single application on the mid-Island file. Also a commercial office space application with parking variances.'),
 (N'Bakerview Building Design', N'Designer', NULL,
  N'Campbell River: 11 townhome units with a 1.5m front-yard variance.'),
 (N'IGV Housing', N'Developer', NULL,
  N'Campbell River: 9 dwelling units — one fourplex and one fiveplex. Missing-middle builder.'),
 (N'JRT Architecture', N'Architect', NULL,
  N'Campbell River: minor development permit, foreshore / Great Blue Heron / steep slope. Principal Joyce Troost.'),
 (N'Storey Creek Golf and Recreation Society', N'Buyer', NULL,
  N'Campbell River: new 3,200 sq ft restaurant facility, form and character plus a streamside application. An owner buying design directly.'),
 (N'Kyline Construction', N'GC', NULL,
  N'Campbell River: ancillary dwelling unit above a shop. Trades as 0762123 BC Ltd.'),
 (N'Nikola Properties Ltd.', N'Developer', NULL, N'Campbell River development applicant.'),
 (N'Parkway Properties Ltd.', N'Developer', NULL,
  N'Campbell River: rezoning of 351 Arizona Drive from R-1 to R-M1.'),
 (N'Highland Engineering Services Ltd', N'Competitor', NULL,
  N'Campbell River development applicant. Local engineering practice — confirm discipline before treating as a structural competitor.'),
 (N'Big Island Construction Ltd.', N'GC', NULL, N'Campbell River. Contact Brad Callander.'),
 (N'Tobem Projects', N'Developer', NULL,
  N'KOR client history: 4330 Island Hwy, South Courtenay (01704-01, Aug 2024).'),
 (N'Comact Canada', N'Buyer', NULL,
  N'KOR client history: 2860 Victoria Street, Chemainus (01738-01, May 2025). Industrial — sawmill and wood-processing equipment.');

INSERT INTO opportunities.CanonicalOrg
    (DisplayName, Kind, WebsiteDomain, CreatedAtUtc, UpdatedAtUtc)
SELECT n.Nm, n.Kind, n.Dom, sysdatetimeoffset(), sysdatetimeoffset()
FROM @new n
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.CanonicalOrg co
    WHERE co.RetiredAtUtc IS NULL AND co.DisplayName = n.Nm);

-- Provenance for each, as a typed fact.
INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(20), co.Id) + '|MarketFocus|midisland-2026-09-04'), 2),
       co.Id, N'MarketFocus', n.Note,
       N'https://gisportal.campbellriver.ca/arcgis/rest/services/AllDevelopmentApplications/FeatureServer/0',
       N'Mid-Island permit feeds wired 2026-09-04 (migration 308)',
       CAST('2026-09-04' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-04'
FROM @new n
JOIN opportunities.CanonicalOrg co ON co.DisplayName = n.Nm AND co.RetiredAtUtc IS NULL
WHERE NOT EXISTS (
    SELECT 1 FROM opportunities.OrgFact f
    WHERE f.CanonicalOrgId = co.Id AND f.FactType = N'MarketFocus'
      AND f.CreatedBy = N'BrainDecompose-2026-09-04' AND f.RetiredAtUtc IS NULL);

SELECT 'inserted' AS Section;
SELECT co.Id, co.Kind, co.DisplayName, ISNULL(NULLIF(co.FuzzyNormalizedName,''),'(NULL - repair from the report)') AS FuzzyKey
FROM opportunities.CanonicalOrg co
JOIN @new n ON n.Nm = co.DisplayName
WHERE co.RetiredAtUtc IS NULL
ORDER BY co.DisplayName;
GO
